using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WinTools.Core;
using WinTools.Core.Ui;
using WinTools.Services.Logging;

namespace WinTools.Shell;

/// <summary>
/// 宿主主窗口（Windows 11 NavigationView 版式）：
/// 自定义标题栏 + 可折叠左侧导航 + 卡片墙首页 + 独立搜索结果页 + 全宽工作台（面包屑）。
/// 视图：Home / Category / Favorites / Recent / Search / Workbench / Settings。
/// 工具仍由 ToolDiscovery 反射发现，本类只消费 ITool 与 ToolMeta，不感知具体工具。
/// </summary>
public sealed partial class MainForm : Form
{
    private const int TitleBarH = 48;
    private const int NavExpanded = 228;
    private const int NavRail = 60;
    private const int PagePad = 28;
    private const int RowH = 36;   // 以下均为 100% 缩放下的设计像素，用 UiTheme.Sx/Pt/Sz/Pad 折算
    private static readonly Color CloseHover = Color.FromArgb(0xC4, 0x2B, 0x2B);
    private static readonly Color ClosePress = Color.FromArgb(0xA3, 0x2D, 0x2D);

    private readonly TextLogger _logger = new();
    private IReadOnlyList<ITool> _tools;
    private readonly IReadOnlyList<ITool> _toolsByGroup;
    private readonly IReadOnlyList<ITool> _toolsByName;
    private bool _sortByName;

    // 外壳
    private readonly Panel _titleBar = new();
    private readonly Panel _nav = new();
    private readonly Panel _navList = new();
    private readonly Panel _navFoot = new();
    private readonly Panel _body = new();
    private readonly Panel _content = new();

    // 标题栏控件
    private readonly TextBox _search = new();
    private Panel _searchBox = new();
    private Label _searchPlaceholder = null!;
    private bool _searchFocused;
    private readonly FluentButton _btnTheme = new();
    private readonly FluentButton _btnCollapse = new();
    private readonly FluentButton _btnMin = new();
    private readonly FluentButton _btnMax = new();
    private readonly FluentButton _btnClose = new();
    private FluentButton[] _chrome = Array.Empty<FluentButton>();
    private Panel? _chromeHost;
    private Label _brandLabel = null!;
    private Chip _verChip = null!;
    private bool _navCollapsed;
    private readonly ToolTip _toastTip = new() { InitialDelay = 60, ReshowDelay = 60, AutoPopDelay = 3600 };
    private readonly ToolTip _navTip = new() { InitialDelay = 200, AutoPopDelay = 2000 };
    private readonly List<Form> _standaloneForms = new();
    private ITool? _currentTool;   // 主题切换后回到工具页，而不是被踢回分类目录

    // 导航
    private readonly List<NavRow> _navRows = new();
    private readonly List<Control> _navEntries = new();
    private readonly List<Label> _navSections = new();

    // 状态
    private readonly HashSet<string> _favorites = new() { "json-formatter", "hash-checker" };
    private readonly List<string> _recent = new();
    private readonly Dictionary<string, Control> _uiCache = new();
    /// <summary>Ctrl+数字 → 工具。卡片上的快捷键徽章不是装饰，按下就得真的打开。</summary>
    private readonly Dictionary<int, ITool> _hotkeys = new();
    private NavRow? _activeNav;
    private FluentScrollPanel? _page;
    private string _keyword = "";
    // 程序性清空搜索框时抑制 TextChanged，避免递归重建整页（M15/L16）。
    private bool _suppressSearchTextChanged;

    public MainForm()
    {
        _toolsByGroup = ToolDiscovery.DiscoverFromDirectory(AppDomain.CurrentDomain.BaseDirectory, _logger)
            .OrderBy(t => ToolMeta.Groups.IndexOf(ToolMeta.For(t).Group))
            .ThenBy(t => t.Name, StringComparer.Ordinal)
            .ToList();
        _toolsByName = _toolsByGroup.OrderBy(t => t.Name, StringComparer.Ordinal).ToList();
        _tools = _toolsByGroup;
        foreach (var t in _tools)
        {
            var sc = ToolMeta.For(t).Shortcut;
            if (sc.Length == 6 && sc.StartsWith("Ctrl+") && char.IsAsciiDigit(sc[5]))
                _hotkeys[sc[5] - '0'] = t;
        }

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        // 工作区是物理像素，窗口体量按设计尺寸（100% 下的 1000~1280 宽）乘缩放后取值
        var work = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1040);
        Size = new Size(Math.Clamp(work.Width - UiTheme.Sx(140), UiTheme.Sx(1000), UiTheme.Sx(1280)),
            Math.Clamp(work.Height - UiTheme.Sx(70), UiTheme.Sx(660), UiTheme.Sx(820)));
        MinimumSize = UiTheme.Sz(960, 640);
        AutoScaleMode = AutoScaleMode.None;
        Font = UiTheme.Body;
        BackColor = UiTheme.WindowBg;
        Text = "WinTools 工具箱";
        KeyPreview = true;

        UiTheme.ThemeChanged += OnThemeChanged;

        BuildShell();
        ShowHome();
    }

    // 浮窗投影：无边框窗口借助 CS_DROPSHADOW 获得悬浮感
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
            return cp;
        }
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        ApplyWindowCorners();
        SetupTray();
        // 后台工具（全局热键等）必须在窗体首帧之前就绪：用户可能一启动就按快捷键。
        StartBackgroundTools();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyWindowCorners();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ApplyWindowCorners();
        if (_btnMax.IconName != null)
            _btnMax.IconName = WindowState == FormWindowState.Maximized ? "restore" : "maximize";
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;

    /// <summary>
    /// 圆角交给 DWM：Win11 会按系统半径抗锯齿裁切，比 Region 逐像素裁出来的阶梯边干净。
    /// 系统不支持（返回非 0）时才退回 Region，保证老系统上仍不是直角。
    /// </summary>
    private void ApplyWindowCorners()
    {
        if (WindowState == FormWindowState.Maximized)
        {
            Region = null;
            return;
        }
        var preference = DwmwcpRound;
        if (Handle != IntPtr.Zero && DwmSetWindowAttribute(Handle, DwmwaWindowCornerPreference, ref preference, 4) == 0)
        {
            if (Region is not null) Region = null;
            return;
        }
        using var path = UiTheme.RoundedPath(new Rectangle(0, 0, Width, Height), UiTheme.RadiusWindow);
        UiTheme.SetRegion(this, path);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        DisposeTrayAndBackground();
        UiTheme.ThemeChanged -= OnThemeChanged;
        foreach (var form in _standaloneForms.ToArray()) form.Close();
        foreach (var ui in _uiCache.Values) ui.Dispose();
        _toastTip.Dispose();
        _navTip.Dispose();
        base.OnFormClosed(e);
    }

    // ── 窗口拖动 ────────────────────────────────────────────────
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 0x0002;

    private void TitleBar_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
    }

    // ── 骨架 ────────────────────────────────────────────────────
    private void BuildShell()
    {
        BuildTitleBar();
        BuildNavigation();

        _content.Dock = DockStyle.Fill;
        _body.Dock = DockStyle.Fill;
        _body.Controls.Add(_content);   // Fill 先加
        _body.Controls.Add(_nav);       // Left 后加

        Controls.Add(_body);
        Controls.Add(_titleBar);       // Top 最后加
        ApplyChromeColors();
    }

    /// <summary>常驻外壳的实心底色：构造时铺一次，切主题时原样重铺。</summary>
    private void ApplyChromeColors()
    {
        var chrome = UiTheme.WindowBg;
        var canvas = UiTheme.Bg;
        BackColor = chrome;
        _titleBar.BackColor = chrome;
        _nav.BackColor = chrome;
        _navList.BackColor = chrome;
        _navFoot.BackColor = chrome;
        _body.BackColor = canvas;
        _content.BackColor = canvas;
    }

    private void BuildTitleBar()
    {
        _titleBar.Controls.Clear();
        _titleBar.Dock = DockStyle.Top;
        _titleBar.Height = UiTheme.Sx(TitleBarH);

        // 品牌区：底色透明交给 _titleBar 绘制，切主题时不会残留旧色
        var brand = new Panel { Dock = DockStyle.Left, Width = UiTheme.Sx(210), BackColor = Color.Transparent };
        var logo = new Panel { Location = UiTheme.Pt(16, 9), Size = UiTheme.Sz(30, 30), BackColor = Color.Transparent };
        logo.Paint += (_, e) =>
        {
            var rect = new Rectangle(0, 0, logo.Width, logo.Height);
            UiTheme.IconTile(e.Graphics, rect, UiTheme.Primary, "logo", 0.68f);
        };
        brand.Controls.Add(logo);
        brand.Controls.Add(_brandLabel = new Label
        {
            Text = "WinTools",
            Location = UiTheme.Pt(54, 4), AutoSize = true,
            Font = UiTheme.FontAt(16F, true),
            ForeColor = UiTheme.Text, BackColor = Color.Transparent
        });
        // 版本徽章左缘与品牌名对齐，顶边落在文字行框之下，避免两个控件互相盖住
        var ver = _verChip = new Chip
        {
            Text = "v" + typeof(MainForm).Assembly.GetName().Version?.ToString(3),
            Fill = UiTheme.Layer,
            ForeColor = UiTheme.Muted,
            BackColor = Color.Transparent,
            Location = UiTheme.Pt(54, 27),
            AutoSize = true
        };
        brand.Controls.Add(ver);
        _titleBar.Controls.Add(brand);

        // 右侧窗口控制 + 主题 + 导航折叠：用固定宽度的停靠容器预留位置，
        // 不要 AutoSize 布局面板（会与自绘按钮的绘制区域打架），也不要裸挂标题栏
        // （会被 Dock=Fill 的搜索宿主盖住）。
        var chromeHost = new Panel
        {
            Dock = DockStyle.Right, Width = UiTheme.Sx(212), BackColor = Color.Transparent
        };
        SetupChrome(_btnTheme, UiTheme.IsDark ? "moon" : "sun", 34);
        _btnTheme.Click += (_, _) => UiTheme.ToggleTheme();
        SetupChrome(_btnCollapse, "panel-left", 34);
        _btnCollapse.Click += (_, _) => ToggleNav();
        SetupChrome(_btnMin, "minimize", 44);
        _btnMin.Click += (_, _) => WindowState = FormWindowState.Minimized;
        SetupChrome(_btnMax, WindowState == FormWindowState.Maximized ? "restore" : "maximize", 44);
        _btnMax.Click += (_, _) => WindowState = WindowState == FormWindowState.Maximized
            ? FormWindowState.Normal : FormWindowState.Maximized;
        SetupChrome(_btnClose, "close", 46, close: true);
        _btnClose.Click += (_, _) => Close();
        _chrome = new FluentButton[] { _btnTheme, _btnCollapse, _btnMin, _btnMax, _btnClose };
        foreach (var b in _chrome) chromeHost.Controls.Add(b);
        _chromeHost = chromeHost;
        LayoutChrome();
        _titleBar.Controls.Add(chromeHost);

        // 居中搜索框
        var host = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        _searchBox = new Panel
        {
            Size = UiTheme.Sz(420, 32),
            BackColor = UiTheme.FieldBg
        };
        _searchBox.Paint += (_, e) =>
        {
            var rect = new Rectangle(0, 0, _searchBox.Width - 1, _searchBox.Height - 1);
            UiTheme.FillSurface(e.Graphics, rect, UiTheme.RadiusMedium, UiTheme.FieldBg,
                _searchFocused ? UiTheme.Primary : UiTheme.Border);
            IconFactory.Draw(e.Graphics, "search", UiTheme.Rcf(10, 8, 16, 16),
                _searchFocused ? UiTheme.Primary : UiTheme.Muted);
        };
        // 系统 cue banner 不跟随自定义主题取色，深色底上会变成几乎看不清的深灰；
        // 占位提示改由同主题色的透明 Label 绘制。
        const string searchHint = "搜索工具或功能，例如「哈希」「编码」  Ctrl K";
        _search.PlaceholderText = "";
        _search.Font = UiTheme.Body;
        _search.BorderStyle = BorderStyle.None;
        _search.BackColor = UiTheme.FieldBg;
        _search.ForeColor = UiTheme.Text;
        // 不用 Dock=Fill：单行 TextBox 的文字按控件顶部排版，撑满 32px 会明显偏上。
        // 改为按字体行高收窄、再在搜索框内几何居中，输入文字和占位提示同处中线。
        _search.Dock = DockStyle.None;
        _search.Margin = Padding.Empty;
        _search.TextChanged += (_, _) =>
        {
            _searchPlaceholder.Visible = _search.Text.Length == 0;
            if (_suppressSearchTextChanged) return; // 程序性清空不触发视图重建
            _keyword = _search.Text.Trim();
            if (_keyword.Length > 0) Search(_keyword); else ShowHome();
        };
        _search.Enter += (_, _) => { _searchFocused = true; _searchBox.Invalidate(); };
        _search.Leave += (_, _) => { _searchFocused = false; _searchBox.Invalidate(); };
        _searchBox.Controls.Add(_search);
        _searchPlaceholder = new Label
        {
            Text = searchHint,
            Font = UiTheme.Body,
            ForeColor = UiTheme.Muted,
            BackColor = Color.Transparent,
            AutoSize = false,
            Enabled = false,   // 不拦截鼠标，点击穿透到下面的输入框
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = Padding.Empty
        };
        _searchBox.Controls.Add(_searchPlaceholder);
        UiTheme.Round(_searchBox, UiTheme.RadiusMedium);
        host.Controls.Add(_searchBox);
        void CenterSearch()
        {
            _searchBox.Left = Math.Max(UiTheme.Sx(10), (host.Width - _searchBox.Width) / 2);
            _searchBox.Top = UiTheme.Sx(8);
            int textH = Math.Min(_searchBox.ClientSize.Height,
                Math.Max(_search.PreferredHeight, UiTheme.Sx(18)));
            int left = UiTheme.Sx(30);   // 左侧留给放大镜图标
            int right = UiTheme.Sx(10);
            _search.SetBounds(left, (_searchBox.ClientSize.Height - textH) / 2,
                Math.Max(UiTheme.Sx(40), _searchBox.ClientSize.Width - left - right), textH);
            int hintX = left + UiTheme.Sx(2);
            _searchPlaceholder.SetBounds(hintX, 0,
                Math.Max(UiTheme.Sx(40), _searchBox.ClientSize.Width - hintX - right),
                _searchBox.ClientSize.Height);
        }
        host.Resize += (_, _) => CenterSearch();
        CenterSearch();
        _titleBar.Controls.Add(host);

        // WinForms Dock 规则：Fill 必须先加，Left/Right 后加才不被遮挡
        _titleBar.Controls.SetChildIndex(host, 0);

        // 无边框窗口必须自己处理拖动；鼠标事件不冒泡，标题栏空白处和品牌区都要挂。
        // 按钮/搜索框是子控件，其 MouseDown 不会传到这里，不会与点击冲突。
        _titleBar.MouseDown += TitleBar_MouseDown;
        brand.MouseDown += TitleBar_MouseDown;
    }

    private void LayoutChrome()
    {
        int x = _chromeHost?.Width - UiTheme.Sx(10) ?? UiTheme.Sx(202);
        // 按钮高度不随宽度变，统一按标题栏几何居中，别让某个数字偏离中线
        int cy = (UiTheme.Sx(TitleBarH) - _chrome[0].Height) / 2;
        for (var i = _chrome.Length - 1; i >= 0; i--)
        {
            var b = _chrome[i];
            b.Left = x - b.Width;
            b.Top = cy;
            x -= b.Width;
        }
    }

    private void SetupChrome(FluentButton btn, string icon, int width, bool close = false)
    {
        btn.IconName = icon;
        btn.Text = "";
        btn.Variant = UiTheme.ButtonKind.Ghost;
        btn.Size = UiTheme.Sz(width, 32);
        btn.Margin = Padding.Empty;
        btn.Radius = UiTheme.RadiusSmall;
        RestyleChrome(btn);
    }

    /// <summary>标题栏按钮配色跟随主题；关闭键固定用红底白字。</summary>
    private void RestyleChrome(FluentButton btn)
    {
        bool close = ReferenceEquals(btn, _btnClose);
        btn.HoverOverride = close ? CloseHover : UiTheme.Hover;
        btn.PressOverride = close ? ClosePress : UiTheme.Border;
        btn.ForegroundOverride = close ? Color.White : UiTheme.TextSecondary;
        btn.Invalidate();
    }

    // ── 导航 ────────────────────────────────────────────────────
    private void BuildNavigation()
    {
        _nav.Controls.Clear();
        _navRows.Clear();
        _navEntries.Clear();
        _nav.Dock = DockStyle.Left;
        _nav.Width = _navCollapsed ? UiTheme.Sx(NavRail) : UiTheme.Sx(NavExpanded);
        // 留 1 设计像素给分隔线，否则 Dock=Fill 的导航列表会把父级画的分隔线盖掉
        _nav.Padding = UiTheme.Pad(0, 0, 1, 0);
        _nav.Paint += (_, e) =>
        {
            var w = UiTheme.PenW();
            using var pen = new Pen(UiTheme.Border, w);
            var x = UiTheme.Edge(_nav.Width, w);
            e.Graphics.DrawLine(pen, x, 0, x, _nav.Height);
        };

        _navList.Dock = DockStyle.Fill;
        _navFoot.Dock = DockStyle.Bottom;
        _navFoot.Height = UiTheme.Sx(116);
        _nav.Controls.Add(_navList);
        _nav.Controls.Add(_navFoot);

        AddSection("浏览");
        AddNav("all", "全部工具", "apps", UiTheme.TextSecondary, _tools.Count, ShowHome);
        AddNav("favorites", "我的收藏", "star", UiTheme.Warn, _favorites.Count, ShowFavorites);
        AddNav("recent", "最近使用", "history", UiTheme.Primary, _recent.Count, ShowRecent);

        AddSection("分类");
        foreach (var g in ToolMeta.GroupTools(_tools).Where(g => g.Any()))
        {
            var name = g.Key;
            AddNav("cat:" + name, name, ToolMeta.GroupIcon(name),
                UiTheme.Accent(ToolMeta.GroupAccent(name)), g.Count(), () => ShowCategory(name));
        }

        AddNav("settings", "设置", "gear", UiTheme.TextSecondary, 0, ShowSettings, foot: true);
        BuildTrayCard();
        LayoutNav();
    }

    private void AddSection(string text)
    {
        var label = new Label
        {
            Text = text, AutoSize = false, Height = UiTheme.Sx(30),
            Font = UiTheme.FontAt(11.5F, true),
            ForeColor = UiTheme.Muted, BackColor = Color.Transparent,
            TextAlign = ContentAlignment.BottomLeft,
            // 左缩进与导航行图标对齐（行起点 8 + 图标起点 14），分组标题才不会比行缩进多两个像素
            Padding = UiTheme.Pad(14, 0, 0, 2)
        };
        _navEntries.Add(label);
        _navSections.Add(label);
        _navList.Controls.Add(label);
    }

    private void AddNav(string key, string title, string icon, Color iconColor, int badge,
        Action onClick, bool foot = false)
    {
        var row = new NavRow
        {
            Key = key,
            Title = title,
            IconName = icon,
            IconColor = iconColor,
            Badge = badge > 0 ? badge.ToString() : "",
            CategoryKey = key.StartsWith("cat:") ? key[4..] : null,
            Collapsed = _navCollapsed,
            RowClick = onClick
        };
        _navRows.Add(row);
        _navEntries.Add(row);
        (foot ? _navFoot : _navList).Controls.Add(row);
    }

    private void BuildTrayCard()
    {
        var tray = new Panel
        {
            Size = UiTheme.Sz(200, 62),
            BackColor = Color.Transparent,
            Location = UiTheme.Pt(12, 44)
        };
        // 文字直接画在面板上：常驻控件不会被视图重建，用子 Label 会把旧主题色留在界面上
        tray.Paint += (_, e) =>
        {
            var g = e.Graphics;
            var rect = new Rectangle(0, 0, tray.Width - 1, tray.Height - 1);
            UiTheme.FillSurface(g, rect, UiTheme.RadiusLarge, UiTheme.Surface, UiTheme.Border);
            // 图标几何中心与首行文字行框中心同高（18），不再各偏各的
            IconFactory.Draw(g, "pin", UiTheme.Rcf(12, 10, 16, 16), UiTheme.Primary);
            TextRenderer.DrawText(g, "常驻托盘", UiTheme.Bold, UiTheme.Rc(34, 9, 160, 18),
                UiTheme.Text, TextFormatFlags.Left | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, "关窗后后台待命", UiTheme.Caption, UiTheme.Rc(34, 29, 170, 15),
                UiTheme.Muted, TextFormatFlags.Left | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, "Ctrl Shift T 唤起", UiTheme.Caption, UiTheme.Rc(34, 44, 170, 15),
                UiTheme.Muted, TextFormatFlags.Left | TextFormatFlags.NoPadding);
        };
        _navFoot.Controls.Add(tray);
        _navFoot.Tag = tray;
    }

    private void LayoutNav()
    {
        int w = _nav.Width - UiTheme.Sx(16), x = UiTheme.Sx(8);
        int y = UiTheme.Sx(10), gap = UiTheme.Sx(2);
        foreach (var c in _navEntries)
        {
            if (c.Parent == _navFoot) continue;
            // 图标栏放不下分组标题，折叠时整条撤掉，避免只剩一个"浏"字
            c.Visible = c is not Label || !_navCollapsed;
            if (!c.Visible) continue;   // 隐藏的分组标题不该继续占垂直空间
            c.Width = w;
            c.Location = new Point(x, y);
            y += c.Height + gap;
        }
        int fy = UiTheme.Sx(6);
        foreach (var c in _navRows.Where(r => r.Parent == _navFoot))
        {
            c.Width = w;
            c.Location = new Point(x, fy);
            fy += c.Height + gap;
        }
        if (_navFoot.Tag is Panel tray)
        {
            // 折叠成图标栏时文字放不下，直接隐藏托盘说明卡
            tray.Visible = !_navCollapsed;
            tray.Width = _nav.Width - UiTheme.Sx(24);
            tray.Location = UiTheme.Pt(12, 44);
            tray.Invalidate();
        }
    }

    private void ToggleNav()
    {
        _navCollapsed = !_navCollapsed;
        _nav.Width = _navCollapsed ? UiTheme.Sx(NavRail) : UiTheme.Sx(NavExpanded);
        foreach (var r in _navRows)
        {
            r.Collapsed = _navCollapsed;
            // 折叠后只剩图标，悬停提示补上被隐藏的名称；展开后撤掉，避免多余气泡。
            _navTip.SetToolTip(r, _navCollapsed ? r.Title : "");
        }
        LayoutNav();
        _nav.Invalidate();
    }

    private void SetActiveNav(string? key)
    {
        foreach (var n in _navRows)
        {
            n.Active = key is not null && n.Key == key;
            n.Invalidate();
        }
        _activeNav = _navRows.FirstOrDefault(n => n.Key == key);
    }

    /// <summary>自绘导航行：圆角状态底 + 矢量图标 + 标题 + 计数徽章 + 激活指示条。</summary>
    private sealed class NavRow : Panel
    {
        private bool _hover;
        public string Key = "";
        public string? CategoryKey;
        public string IconName = "apps";
        public Color IconColor = UiTheme.Muted;
        public string Title = "";
        public string Badge = "";
        public bool Active;
        public bool Collapsed;
        public Action? RowClick;

        public NavRow()
        {
            Height = UiTheme.Sx(RowH);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            MouseEnter += (_, _) => { _hover = true; Invalidate(); };
            MouseLeave += (_, _) => { _hover = false; Invalidate(); };
            Click += (_, _) => RowClick?.Invoke();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(UiTheme.Backdrop(Parent));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // 状态底用控件级圆角令牌，和搜索框/按钮同一档，避免"快一档慢一档"的圆角割裂
            int radius = UiTheme.RadiusMedium;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            if (Active)
                UiTheme.FillSurface(g, rect, radius, UiTheme.ActiveBg,
                    UiTheme.Blend(UiTheme.Primary, UiTheme.ActiveBg, 0.55f));
            else if (_hover) UiTheme.FillSurface(g, rect, radius, UiTheme.Hover, UiTheme.Border);

            if (Active)
            {
                int pillH = UiTheme.Sx(18), pillW = UiTheme.Sx(3);
                var pill = new Rectangle(0, (Height - pillH) / 2, pillW, pillH);
                using var pp = UiTheme.RoundedPath(pill, UiTheme.Sx(2));
                using var pb = new SolidBrush(UiTheme.Primary);
                g.FillPath(pb, pp);
            }

            int iconSize = UiTheme.Sx(18);
            int ix = Collapsed ? Width / 2 - iconSize / 2 : UiTheme.Sx(14);
            IconFactory.Draw(g, IconName, new RectangleF(ix, (Height - iconSize) / 2f, iconSize, iconSize),
                Active ? UiTheme.Primary : IconColor);
            if (Collapsed) return;

            int tx = ix + iconSize + UiTheme.Sx(12);
            int pad = UiTheme.Sx(12);
            int badgeRoom = Badge.Length > 0 ? UiTheme.Sx(36) : 0;
            TextRenderer.DrawText(g, Title, Active ? UiTheme.Bold : UiTheme.Body,
                new Rectangle(tx, 0, Math.Max(UiTheme.Sx(20), Width - tx - pad - badgeRoom), Height),
                Active ? UiTheme.Text : UiTheme.TextSecondary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

            if (Badge.Length == 0) return;
            var ts = TextRenderer.MeasureText(g, Badge, UiTheme.Caption, new Size(0, UiTheme.Sx(20)),
                TextFormatFlags.NoPadding);
            int bh = UiTheme.Sx(19), bw = ts.Width + UiTheme.Sx(10);
            var br = new Rectangle(Width - pad - bw, (Height - bh) / 2, bw, bh);
            var badgeFill = Active ? UiTheme.Primary : UiTheme.Layer;
            // 计数徽章与 Chip 同族，圆角统一取 RadiusSmall，不再各写各的数
            using (var bp = UiTheme.RoundedPath(br, UiTheme.RadiusSmall))
            {
                using (var bb = new SolidBrush(badgeFill))
                    g.FillPath(bb, bp);
                if (!Active)
                {
                    using var hp = new Pen(UiTheme.Border);
                    g.DrawPath(hp, bp);
                }
            }
            TextRenderer.DrawText(g, Badge, UiTheme.Caption, br,
                Active ? Color.White : UiTheme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    // ── 页面基元 ────────────────────────────────────────────────
    private FluentScrollPanel NewPage()
    {
        var scroll = new FluentScrollPanel { Dock = DockStyle.Fill };
        _page = scroll;
        return scroll;
    }

    /// <summary>
    /// 自绘文字用的实心面板：自己铺一层不透明底色。
    /// 子控件用 BackColor=Transparent 时回拷的正是这层纯色，切主题重建视图即可，不会留残影。
    /// </summary>
    private class SolidPanel : Panel
    {
        protected SolidPanel(Color back)
        {
            Margin = Padding.Empty;
            BackColor = back;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
    }

    /// <summary>页面大标题 + 副标题，文字自绘。</summary>
    private sealed class PageHead : SolidPanel
    {
        public string Title = "";
        public string Subtitle = "";
        private readonly FlowLayoutPanel _actions = new()
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            Margin = Padding.Empty,
        };

        public PageHead(params Button[] actions) : base(UiTheme.Bg)
        {
            Height = UiTheme.Sx(84);
            Controls.Add(_actions);
            foreach (var a in actions)
            {
                a.Margin = UiTheme.Pad(0, 0, 8, 0);
                _actions.Controls.Add(a);
            }
            _actions.BackColor = Color.Transparent;
            Resize += (_, _) =>
            {
                _actions.Left = Math.Max(0, Width - _actions.PreferredSize.Width);
                // 按钮在 84 高的页头里几何居中（(84-34)/2），原来顶 22 会浮起半格
                _actions.Top = UiTheme.Sx(25);
                Invalidate();
            };
        }

        /// <summary>文字可用宽度：窄窗口下给右侧操作键留位，长标题/副标题不会印在按钮上。</summary>
        private int TextRight =>
            Math.Max(UiTheme.Sx(120), Width - _actions.PreferredSize.Width - UiTheme.Sx(12));

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            // 主标题与副标题共用左缘 0：副标题此前多缩 2 设计像素，200% 下错 4 个物理点
            TextRenderer.DrawText(g, Title, UiTheme.Hero,
                new Rectangle(0, UiTheme.Sx(8), TextRight, UiTheme.Sx(36)), UiTheme.Text,
                TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, Subtitle, UiTheme.Body,
                new Rectangle(0, UiTheme.Sx(50), TextRight, UiTheme.Sx(22)), UiTheme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>分区标题行：标题 + 计数 + 右侧"查看全部"，全部自绘。</summary>
    private sealed class SectionHead : SolidPanel
    {
        public string Title = "";
        public string Count = "";
        public Action? OnViewAll;
        private Rectangle _linkRect;

        public SectionHead() : base(UiTheme.Bg) => Height = UiTheme.Sx(32);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            TextRenderer.DrawText(g, Title, UiTheme.Subtitle,
                new Rectangle(0, UiTheme.Sx(4), UiTheme.Sx(300), UiTheme.Sx(24)), UiTheme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            int tw = TextRenderer.MeasureText(g, Title, UiTheme.Subtitle, new Size(0, UiTheme.Sx(24)),
                TextFormatFlags.NoPadding).Width;
            // 计数与链接的行框中心都取 16，和标题同一条基准线（原来分别偏 1 像素，200% 下更明显）
            TextRenderer.DrawText(g, Count, UiTheme.Small,
                new Rectangle(tw + UiTheme.Sx(10), UiTheme.Sx(7), UiTheme.Sx(160), UiTheme.Sx(18)), UiTheme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            if (OnViewAll is null) { _linkRect = Rectangle.Empty; return; }
            const string link = "查看全部";
            var size = TextRenderer.MeasureText(g, link, UiTheme.Small, new Size(0, UiTheme.Sx(20)), TextFormatFlags.NoPadding);
            int gap = UiTheme.Sx(20), h = UiTheme.Sx(22);
            _linkRect = new Rectangle(Width - size.Width - gap, UiTheme.Sx(5), size.Width + gap, h);
            TextRenderer.DrawText(g, link, UiTheme.Small,
                new Rectangle(_linkRect.X, _linkRect.Y, size.Width, _linkRect.Height), UiTheme.Primary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            int chev = UiTheme.Sx(13);
            IconFactory.Draw(g, "chevron-right",
                new RectangleF(_linkRect.Right - UiTheme.Sx(15), _linkRect.Y + (_linkRect.Height - chev) / 2f, chev, chev),
                UiTheme.Primary);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && _linkRect.Contains(e.Location)) OnViewAll?.Invoke();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor = _linkRect.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
        }
    }

    private Panel PageHeader(string title, string subtitle, int width, params Button[] actions)
    {
        var head = new PageHead(actions) { Title = title, Subtitle = subtitle, Width = UiTheme.Sx(width) };
        return head;
    }

    private Panel SectionHeading(string text, string badge, Action? onViewAll = null)
    {
        var head = new SectionHead { Title = text, Count = badge, OnViewAll = onViewAll, Width = UiTheme.Sx(800) };
        return head;
    }

    /// <summary>
    /// 等宽列卡片网格。列数由可用宽度推算，卡片拉伸填满所在列，
    /// 因此快速入口条与分类网格共用同一套列线，不会出现"上面小胶囊、下面大卡片"的规格割裂。
    /// </summary>
    private sealed class CardGrid : Panel
    {
        private readonly bool _compact;
        private readonly int _minCell;
        private readonly int _cellH;
        private readonly Action<ITool> _open;
        private readonly Action<ITool, bool> _toggleFav;
        private readonly Func<ITool, bool> _isFavorite;
        private readonly List<ToolCard> _cards = new();

        public CardGrid(bool compact, Action<ITool> open, Action<ITool, bool> toggleFav,
            Func<ITool, bool> isFavorite)
        {
            _compact = compact;
            _open = open;
            _toggleFav = toggleFav;
            _isFavorite = isFavorite;
            _minCell = ToolCard.LargeCardSize.Width;
            _cellH = (_compact ? ToolCard.CompactSurfaceH : ToolCard.LargeSurfaceH)
                     + ToolCard.InsetTop + ToolCard.InsetBottom;
            Margin = Padding.Empty;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = UiTheme.Bg;
        }

        private IReadOnlyList<ITool> _shown = Array.Empty<ITool>();

        public void Apply(int width, IReadOnlyList<ITool> tools)
        {
            if (!_shown.SequenceEqual(tools)) Rebuild(tools);

            int cols = Math.Max(1, width / _minCell);
            int cell = Math.Max(_minCell, width / cols);
            for (var i = 0; i < _cards.Count; i++)
            {
                var card = _cards[i];
                card.Size = new Size(cell, _cellH);
                card.Location = new Point(i % cols * cell, i / cols * _cellH);
                card.Invalidate();
            }
            Width = width;
            Height = (int)Math.Ceiling(_cards.Count / (double)cols) * _cellH;
        }

        private void Rebuild(IReadOnlyList<ITool> tools)
        {
            foreach (var c in _cards) { Controls.Remove(c); c.Dispose(); }
            _cards.Clear();
            foreach (var t in tools)
            {
                var card = new ToolCard(t, _compact, _isFavorite(t), _open, _toggleFav);
                _cards.Add(card);
                Controls.Add(card);
            }
            _shown = tools.ToList();
        }
    }

    private CardGrid BuildCardRow(bool compact) =>
        new(compact, OpenTool, ToggleFavorite, t => _favorites.Contains(t.Id));

    // ── 视图：目录 / 分类 ───────────────────────────────────────
    /// <summary>清空搜索态但不触发 TextChanged 的视图重建（M15/L16）。</summary>
    private void ClearSearch()
    {
        _keyword = "";
        _searchPlaceholder.Visible = true;
        _suppressSearchTextChanged = true;
        try { if (_search.Text.Length > 0) _search.Clear(); }
        finally { _suppressSearchTextChanged = false; }
    }

    private void ShowHome() => ShowDirectory(null, "全部工具", "按分类浏览，或直接搜索想要的功能", "all");

    private void ShowCategory(string category) =>
        ShowDirectory(category, category, $"「{category}」分类下的工具", "cat:" + category);

    private void ShowDirectory(string? category, string heading, string subtitle, string navKey)
    {
        _currentTool = null;
        ClearSearch();
        SetActiveNav(navKey);

        var scroll = NewPage();
        var canvas = scroll.Canvas;
        int y = UiTheme.Sx(16);
        int pad = UiTheme.Sx(PagePad);

        var sortBtn = UiTheme.Button(_sortByName ? "分类排序" : "排序", "sort", UiTheme.ButtonKind.Secondary, 96, 34);
        var addBtn = UiTheme.Button("添加工具", "plus", UiTheme.ButtonKind.Primary, 120, 34);
        addBtn.Click += (_, _) => ShowToast("工具由插件目录自动发现，把 WinTools.Tools.*.dll 放进程序目录即可", addBtn);

        var shown = category is null ? _tools.Count : _tools.Count(t => ToolMeta.For(t).Group == category);
        var head = PageHeader(heading, $"{subtitle} · 共 {shown} 个", 800, sortBtn, addBtn);
        head.Location = new Point(pad, y);
        canvas.Controls.Add(head);
        y += head.Height + UiTheme.Sx(6);

        // 用工厂而不是快照列表：切换排序后 Reflow 会重新取工具并重建卡片。
        var sections = new List<(Panel? head, CardGrid grid, Func<IReadOnlyList<ITool>> getTools)>();

        if (category is null)
        {
            int commonCount = _tools.Count(t => ToolMeta.For(t).IsCommon);
            sections.Add((SectionHeading("快速入口", $"{commonCount} 个常用"),
                BuildCardRow(compact: true),
                () => _tools.Where(t => ToolMeta.For(t).IsCommon).ToList()));
        }

        var groups = ToolMeta.Groups
            .Concat(_tools.Select(t => ToolMeta.For(t).Group)
                .Where(g => !ToolMeta.Groups.Contains(g)).Distinct());
        foreach (var groupKey in groups.Where(g => category is null || g == category))
        {
            var list = _tools.Where(t => ToolMeta.For(t).Group == groupKey).ToList();
            if (list.Count == 0) continue;
            // 分类页的标题栏已经写了分类名和数量，再挂一行同名小节标题纯属重复
            var secHead = category is null
                ? SectionHeading(groupKey, $"{list.Count} 个工具", () => ShowCategory(groupKey))
                : null;
            sections.Add((secHead, BuildCardRow(compact: false),
                () => _tools.Where(t => ToolMeta.For(t).Group == groupKey).ToList()));
        }

        void Reflow()
        {
            int w = Math.Max(UiTheme.Sx(200), scroll.ContentWidth - pad * 2);
            head.Width = w;
            int yy = UiTheme.Sx(16) + head.Height + UiTheme.Sx(6);
            foreach (var (h, r, getTools) in sections)
            {
                if (h is not null)
                {
                    h.Width = w;
                    h.Location = new Point(pad, yy);
                    yy += h.Height + UiTheme.Sx(4);
                }
                // 卡片控件自带 InsetX 留白，网格整体左移一个留白、宽度补齐，
                // 卡片表面才和标题/分区文字同一条左边线（设置页、工具页同基准）
                r.Apply(w + ToolCard.InsetX * 2, getTools());
                r.Location = new Point(pad - ToolCard.InsetX, yy);
                yy += r.Height;
            }
            scroll.RelayoutContent();
        }

        foreach (var (h, r, _) in sections)
        {
            if (h is not null)
            {
                h.Location = new Point(pad, 0);
                canvas.Controls.Add(h);
            }
            r.Location = new Point(pad, 0);
            canvas.Controls.Add(r);
        }

        sortBtn.Click += (_, _) =>
        {
            // 真实切换排序：卡片按新顺序重建，不再只弹一句"已排序"的空提示。
            _sortByName = !_sortByName;
            _tools = _sortByName ? _toolsByName : _toolsByGroup;
            sortBtn.Text = _sortByName ? "分类排序" : "排序";
            Reflow();
            ShowToast(_sortByName ? "已切换为按名称排序" : "已恢复按分类顺序排列", sortBtn);
        };

        canvas.Resize += (_, _) => Reflow();
        Reflow();
        SwapContent(scroll);
    }

    // ── 视图：收藏 / 最近 ───────────────────────────────────────
    private void ShowFavorites()
    {
        var favs = _tools.Where(t => _favorites.Contains(t.Id)).ToList();
        ShowList("我的收藏", favs.Count > 0 ? $"已收藏 {favs.Count} 个工具" : "点击工具卡片右上角的星标即可收藏",
            favs, "favorites");
    }

    private void ShowRecent()
    {
        var recents = _recent.Select(id => _tools.FirstOrDefault(t => t.Id == id))
            .Where(t => t is not null).Cast<ITool>().ToList();
        ShowList("最近使用", recents.Count > 0 ? $"最近打开过 {recents.Count} 个工具" : "打开任意工具后会出现在这里",
            recents, "recent");
    }

    private void ShowList(string heading, string subtitle, IReadOnlyList<ITool> tools, string navKey)
    {
        _currentTool = null;
        ClearSearch();
        SetActiveNav(navKey);

        var scroll = NewPage();
        var head = PageHeader(heading, subtitle, 800);
        head.Location = UiTheme.Pt(PagePad, 16);
        scroll.Canvas.Controls.Add(head);
        var row = BuildCardRow(compact: false);
        scroll.Canvas.Controls.Add(row);

        void Reflow()
        {
            int w = Math.Max(UiTheme.Sx(200), scroll.ContentWidth - UiTheme.Sx(PagePad) * 2);
            head.Width = w;
            // 与首页同一边线基准：卡片表面落在 PagePad，控件本身左移一个 InsetX
            row.Apply(w + ToolCard.InsetX * 2, tools);
            row.Location = new Point(UiTheme.Sx(PagePad) - ToolCard.InsetX,
                UiTheme.Sx(16) + head.Height + UiTheme.Sx(6));
            scroll.RelayoutContent();
        }
        scroll.Canvas.Resize += (_, _) => Reflow();
        Reflow();
        SwapContent(scroll);
    }

    // ── 视图：搜索结果 ──────────────────────────────────────────
    private void Search(string keyword)
    {
        _currentTool = null;
        var matches = _tools.Where(t =>
            t.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            t.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            t.Category.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            ToolMeta.For(t).Group.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            ToolMeta.For(t).Description.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToList();

        SetActiveNav(null);
        var scroll = NewPage();

        var clear = UiTheme.Button("清除", "close", UiTheme.ButtonKind.Secondary, 96, 34);
        clear.Click += (_, _) => _search.Clear();
        var head = PageHeader($"搜索「{keyword}」",
            matches.Count > 0 ? $"在全部 {_tools.Count} 个工具中找到 {matches.Count} 个结果"
                               : "没有匹配的工具，换个关键词试试，例如「哈希」「编码」「端口」",
            800, clear);
        head.Location = UiTheme.Pt(PagePad, 16);
        scroll.Canvas.Controls.Add(head);

        var row = BuildCardRow(compact: false);
        scroll.Canvas.Controls.Add(row);

        void Reflow()
        {
            int w = Math.Max(UiTheme.Sx(200), scroll.ContentWidth - UiTheme.Sx(PagePad) * 2);
            head.Width = w;
            // 搜索结果页与首页/设置页共用同一条卡片左边线
            row.Apply(w + ToolCard.InsetX * 2, matches);
            row.Location = new Point(UiTheme.Sx(PagePad) - ToolCard.InsetX,
                UiTheme.Sx(16) + head.Height + UiTheme.Sx(6));
            scroll.RelayoutContent();
        }
        scroll.Canvas.Resize += (_, _) => Reflow();
        Reflow();
        SwapContent(scroll);
    }

    private void ToggleFavorite(ITool tool, bool fav)
    {
        if (fav) _favorites.Add(tool.Id); else _favorites.Remove(tool.Id);
        var row = _navRows.FirstOrDefault(r => r.Key == "favorites");
        if (row is not null)
        {
            row.Badge = _favorites.Count.ToString();
            row.Invalidate();
        }
    }

    /// <summary>
    /// 工具页顶部细条：返回键 + 「分类 › 工具名」面包屑，右侧挂收藏与独立窗口。
    /// 大标题和说明由工具页自己的 ToolHeader 负责，这里不重复一遍工具名。
    /// </summary>
    private sealed class ToolBar : SolidPanel
    {
        public string Crumb = "";

        public ToolBar() : base(UiTheme.WindowBg)
        {
            Dock = DockStyle.Top;
            Height = UiTheme.Sx(44);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var w = UiTheme.PenW();
            using var pen = new Pen(UiTheme.Border, w);
            var y = UiTheme.Edge(Height, w);
            // 两端收进内容列（PagePad），横线才不会比下面的卡片各长出一截
            int inset = UiTheme.Sx(PagePad);
            e.Graphics.DrawLine(pen, inset, y, Math.Max(inset, Width - inset), y);
            TextRenderer.DrawText(e.Graphics, Crumb, UiTheme.Body,
                new Rectangle(UiTheme.Sx(54), 0, Math.Max(0, Width - UiTheme.Sx(300)), Height), UiTheme.TextSecondary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }

    // ── 视图：工作台 ────────────────────────────────────────────
    private void OpenTool(ITool tool)
    {
        _currentTool = tool;
        var meta = ToolMeta.For(tool);
        SetActiveNav(_navRows.FirstOrDefault(n => n.CategoryKey == meta.Group)?.Key);

        _recent.Remove(tool.Id);
        _recent.Insert(0, tool.Id);
        if (_recent.Count > 8) _recent.RemoveRange(8, _recent.Count - 8);
        if (_navRows.FirstOrDefault(r => r.Key == "recent") is { } rec)
        {
            rec.Badge = Math.Min(_recent.Count, 99).ToString();
            rec.Invalidate();
        }

        try
        {
            // 批量重命名带"撤销上一步"栈：跨打开复用会把上次会话的写操作带回来，
            // 这类有副作用的工具每次新建 UI，离开页面即清理状态。
            var cacheable = tool.Id != "batch-rename";
            if (!cacheable || !_uiCache.TryGetValue(tool.Id, out var ui))
            {
                ui = tool.CreateUI();
                ui.Dock = DockStyle.Fill;
                if (cacheable) _uiCache[tool.Id] = ui;
            }

            var page = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Bg };

            var bar = new ToolBar { Crumb = $"{meta.Group}  ›  {tool.Name}" };
            ApplyHeaderMeta(ui, meta);

            var back = UiTheme.IconButton("back", UiTheme.ButtonKind.Subtle, 32);
            back.Location = UiTheme.Pt(12, 6);
            back.Click += (_, _) => ShowHome();
            bar.Controls.Add(back);

            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Right, AutoSize = true, WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = UiTheme.Pad(0, 5, 12, 0), Margin = Padding.Empty,
                BackColor = Color.Transparent
            };
            var fav = _favorites.Contains(tool.Id);
            var star = UiTheme.Button(fav ? "已收藏" : "收藏", fav ? "star-filled" : "star",
                UiTheme.ButtonKind.Subtle, 104, 34);
            star.ForegroundOverride = fav ? UiTheme.Warn : null;
            star.Click += (_, _) =>
            {
                // 原地切换状态，不再递归 OpenTool 重建整页（会把输入中的内容清掉）
                ToggleFavorite(tool, !_favorites.Contains(tool.Id));
                var nowFav = _favorites.Contains(tool.Id);
                star.Text = nowFav ? "已收藏" : "收藏";
                star.IconName = nowFav ? "star-filled" : "star";
                star.ForegroundOverride = nowFav ? UiTheme.Warn : null;
                star.Invalidate();
            };
            var pop = UiTheme.Button("独立窗口", "popout", UiTheme.ButtonKind.Subtle, 116, 34);
            pop.Click += (_, _) => OpenStandalone(tool);
            actions.Controls.Add(star);
            actions.Controls.Add(pop);
            bar.Controls.Add(actions);

            if (ui.Parent != null) ui.Parent.Controls.Remove(ui);
            // 工具页根面板只当地板，卡片和输入区自己是不透明表面
            ui.BackColor = UiTheme.Bg;
            var host = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.Bg,
                // 工作台内容列与首页/设置页对齐：原来左右各少 6 设计像素，卡片左边线三页不一条
                Padding = UiTheme.Pad(PagePad, 14, PagePad, 14)
            };
            host.Controls.Add(ui);

            page.Controls.Add(host);   // Fill 先加
            page.Controls.Add(bar);    // Top 后加
            SwapContent(page);
            _page = null;
        }
        catch (Exception ex)
        {
            _logger.Error($"打开工具 {tool.Id} 失败: {ex.Message}");
            MessageBox.Show($"无法打开「{tool.Name}」：{ex.Message}", "打开失败",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// 把 ToolMeta 的图标与色相注入工具页自己的 ToolHeader：
    /// 工具只声明 Id，展示信息由宿主统一喂进来，工作台标题块才能和首页卡片同一套视觉。
    /// </summary>
    private static void ApplyHeaderMeta(Control root, ToolMeta.Meta meta)
    {
        if (root is ToolHeader h)
        {
            h.IconName = meta.Icon;
            h.Accent = UiTheme.Accent(meta.AccentIndex);
        }
        foreach (Control c in root.Controls) ApplyHeaderMeta(c, meta);
    }

    private void OpenStandalone(ITool tool)
    {
        var form = new Form
        {
            Text = tool.Name,
            Size = UiTheme.Sz(820, 600),
            MinimumSize = UiTheme.Sz(560, 400),
            StartPosition = FormStartPosition.CenterScreen,
            AutoScaleMode = AutoScaleMode.None,
            Font = UiTheme.Body,
            BackColor = UiTheme.Bg
        };
        Control ui;
        try
        {
            ui = tool.CreateUI();
        }
        catch (Exception ex)
        {
            _logger.Error($"打开独立窗口 {tool.Id} 失败: {ex.Message}");
            form.Dispose();
            MessageBox.Show($"无法打开「{tool.Name}」：{ex.Message}", "打开失败",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        ui.Dock = DockStyle.Fill;
        ui.Padding = UiTheme.Pad(16);
        form.Controls.Add(ui);
        ApplyHeaderMeta(ui, ToolMeta.For(tool));
        form.Show();

        // 独立窗口不会随主窗体重建：切主题时原地换一份新配色的 UI，
        // 关闭时退订并摘掉跟踪，避免静态事件吊住已关闭的窗口。
        void Rebuild()
        {
            Control? next = null;
            try
            {
                next = tool.CreateUI();
                next.Dock = DockStyle.Fill;
                next.Padding = UiTheme.Pad(16);
                ApplyHeaderMeta(next, ToolMeta.For(tool));
            }
            catch (Exception ex)
            {
                // 主题切换遍历所有订阅者：此处的异常绝不能冒泡中断其他窗口的重建。
                _logger.Error($"独立窗口 {tool.Id} 主题重建失败: {ex.Message}");
                next?.Dispose();
                return;
            }
            form.BackColor = UiTheme.Bg;
            form.Controls.Remove(ui);
            ui.Dispose();
            ui = next;
            form.Controls.Add(ui);
        }
        void OnClosed(object? sender, FormClosedEventArgs e)
        {
            UiTheme.ThemeChanged -= Rebuild;
            form.FormClosed -= OnClosed;
            _standaloneForms.Remove(form);
        }
        UiTheme.ThemeChanged += Rebuild;
        form.FormClosed += OnClosed;
        _standaloneForms.Add(form);
    }

    // ── 视图：设置 ──────────────────────────────────────────────
    private void ShowSettings()
    {
        _currentTool = null;
        ClearSearch(); // M15：进设置页清理搜索态，切主题后不会跳回搜索结果页
        SetActiveNav("settings");
        var scroll = NewPage();
        var head = PageHeader("设置", "外观、快捷键与关于信息", 800);
        head.Location = UiTheme.Pt(PagePad, 16);
        scroll.Canvas.Controls.Add(head);

        int pad = UiTheme.Sx(PagePad);
        int y = UiTheme.Sx(16) + head.Height + UiTheme.Sx(8);

        // 表头 40 + 上下留白 14 + 主题块 56：再高就是空一截没有内容的表面
        var appearance = new CardPanel("外观") { Size = UiTheme.Sz(800, 124), Location = new Point(pad, y) };
        appearance.HeaderIcon = "sun";
        appearance.Content.Controls.Add(BuildThemePicker(appearance.Content.Width));
        scroll.Canvas.Controls.Add(appearance);
        y += appearance.Height + UiTheme.Sx(12);

        var keys = new CardPanel("键盘快捷键") { Size = UiTheme.Sz(800, 218), Location = new Point(pad, y) };
        keys.HeaderIcon = "bolt";
        keys.Content.Controls.Add(HintRows(
            ("Ctrl K", "聚焦搜索框，随时开始检索工具"),
            ("Ctrl 1 ~ 6", "直接打开快速入口卡片上标注对应的工具"),
            ("Ctrl Shift A / F / S", "全局截图：区域 / 全屏 / 滚动（屏幕截图页可自定义，窗口关闭后仍生效）"),
            ("Ctrl Shift T", "从托盘唤起主界面"),
            ("Esc", "清空搜索并退出输入焦点")));
        scroll.Canvas.Controls.Add(keys);
        y += keys.Height + UiTheme.Sx(12);

        var about = new CardPanel("关于") { Size = UiTheme.Sz(800, 158), Location = new Point(pad, y) };
        about.HeaderIcon = "info";
        about.Content.Controls.Add(HintRows(
            ("版本", typeof(MainForm).Assembly.GetName().Version?.ToString(3) + " · WinTools 轻量级工具箱"),
            ("已发现工具", $"{_tools.Count} 个（由插件目录自动发现，新增工具无需改宿主）"),
            ("运行方式", "全部处理在本机完成，零联网、普通权限，不写入系统目录")));
        scroll.Canvas.Controls.Add(about);
        y += about.Height;

        void Reflow()
        {
            int w = Math.Max(UiTheme.Sx(240), scroll.ContentWidth - pad * 2);
            head.Width = w;
            appearance.Width = w;
            keys.Width = w;
            about.Width = w;
            scroll.RelayoutContent();
        }
        scroll.Canvas.Resize += (_, _) => Reflow();
        Reflow();
        SwapContent(scroll);
    }

    /// <summary>
    /// 键值说明行。用表格布局而不是把若干 Dock=Top 行依次 Add：
    /// 停靠顺序按 z-order 倒序解析，直接加会把行序排反，最后一行还会被卡片高度截掉。
    /// </summary>
    private static Control HintRows(params (string Key, string Value)[] rows)
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = rows.Length,
            BackColor = UiTheme.Panel,
            Margin = Padding.Empty,
            AutoSize = false
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UiTheme.Sx(150)));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        for (var i = 0; i < rows.Length; i++)
        {
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, UiTheme.Sx(30)));
            grid.Controls.Add(new Label
            {
                Text = rows[i].Key,
                Font = UiTheme.Bold,
                ForeColor = UiTheme.TextSecondary,
                BackColor = UiTheme.Panel,
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill
            }, 0, i);
            grid.Controls.Add(new Label
            {
                Text = rows[i].Value,
                Font = UiTheme.Body,
                ForeColor = UiTheme.Muted,
                BackColor = UiTheme.Panel,
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill
            }, 1, i);
        }
        return grid;
    }

    /// <summary>浅色 / 深色两块可点击预览卡。</summary>
    private Control BuildThemePicker(int width)
    {
        var host = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = UiTheme.Sx(TileH), Width = width,
            FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
            BackColor = UiTheme.Panel
        };
        host.Controls.Add(ThemeTile("浅色", "sun", UiTheme.ThemeMode.Light));
        host.Controls.Add(ThemeTile("深色", "moon", UiTheme.ThemeMode.Dark));
        return host;
    }

    private const int TileH = 56;

    private Control ThemeTile(string title, string icon, UiTheme.ThemeMode mode)
    {
        bool selected = UiTheme.CurrentTheme == mode;
        var tile = new Panel { Size = UiTheme.Sz(196, TileH), Margin = UiTheme.Pad(0, 0, 12, 0), Cursor = Cursors.Hand };
        tile.Paint += (_, e) =>
        {
            var g = e.Graphics;
            var rect = new Rectangle(0, 0, tile.Width - 1, tile.Height - 1);
            UiTheme.FillSurface(g, rect, UiTheme.RadiusLarge, UiTheme.Surface,
                selected ? UiTheme.Primary : UiTheme.Border);
            // 图标、标题、勾选共用一条水平中线：写死顶部偏移会让下半张卡空成一截说不清的留白
            int side = UiTheme.Sx(20);
            IconFactory.Draw(g, icon, new RectangleF(UiTheme.Sx(14), (tile.Height - side) / 2f, side, side),
                selected ? UiTheme.Primary : UiTheme.Muted);
            TextRenderer.DrawText(g, title, selected ? UiTheme.Bold : UiTheme.Body,
                new Rectangle(UiTheme.Sx(42), 0, tile.Width - UiTheme.Sx(84), tile.Height), UiTheme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            if (selected)
            {
                int ck = UiTheme.Sx(18);
                IconFactory.Draw(g, "check",
                    new RectangleF(tile.Width - UiTheme.Sx(14) - ck, (tile.Height - ck) / 2f, ck, ck),
                    UiTheme.Primary);
            }
        };
        tile.Click += (_, _) => UiTheme.SetTheme(mode);
        return tile;
    }

    // ── 主题切换 ────────────────────────────────────────────────
    private void OnThemeChanged()
    {
        _btnTheme.IconName = UiTheme.IsDark ? "moon" : "sun";
        ApplyChromeColors();
        _searchBox.BackColor = UiTheme.FieldBg;
        _searchBox.Invalidate();
        _search.BackColor = UiTheme.FieldBg;
        _search.ForeColor = UiTheme.Text;
        _searchPlaceholder.ForeColor = UiTheme.Muted;

        // 常驻控件不随视图重建，颜色要手动跟着主题走
        _brandLabel.ForeColor = UiTheme.Text;
        _verChip.Fill = UiTheme.Layer;
        _verChip.ForeColor = UiTheme.Muted;
        _verChip.Invalidate();
        foreach (var l in _navSections) l.ForeColor = UiTheme.Muted;
        foreach (var b in _chrome) RestyleChrome(b);
        if (_navFoot.Tag is Panel tray) tray.Invalidate();

        foreach (var r in _navRows)
        {
            // 导航是常驻控件，图标色是创建时快照，切主题必须重新取色
            r.IconColor = NavIconColor(r);
            r.Invalidate();
        }

        // 工具 UI 内部控件颜色是创建时快照，切主题后重建；先摘离页面再释放。
        foreach (var ui in _uiCache.Values.ToArray())
        {
            ui.Parent?.Controls.Remove(ui);
            ui.Dispose();
        }
        _uiCache.Clear();
        if (_currentTool is { } current) OpenTool(current);
        else if (_keyword.Length > 0) Search(_keyword);
        else ReopenCurrentView();
    }

    private static Color NavIconColor(NavRow row) => row.Key switch
    {
        "favorites" => UiTheme.Warn,
        "recent" => UiTheme.Primary,
        _ when row.CategoryKey is { } category => UiTheme.Accent(ToolMeta.GroupAccent(category)),
        _ => UiTheme.TextSecondary,
    };

    /// <summary>主题切换后回到当前导航对应的视图。</summary>
    private void ReopenCurrentView()
    {
        var nav = _activeNav;
        if (nav?.CategoryKey is { } category) ShowCategory(category);
        else switch (nav?.Key)
        {
            case "favorites": ShowFavorites(); break;
            case "recent": ShowRecent(); break;
            case "settings": ShowSettings(); break;
            default: ShowHome(); break;
        }
    }

    // ── 工具方法 ────────────────────────────────────────────────
    private void SwapContent(Control next)
    {
        // 旧页面必须 Dispose：页面里的 FluentButton/ToolCard/滚动容器都订阅了
        // UiTheme.ThemeChanged，只 Clear 不 Dispose 会让静态事件一直吊住整棵控件树。
        foreach (Control old in _content.Controls.Cast<Control>().ToList())
        {
            _content.Controls.Remove(old);
            // 工具 UI 来自缓存，摘出来留给下次打开，不能随旧页面一起释放。
            foreach (var ui in _uiCache.Values.ToArray())
                if (ui.Parent is not null && Contains(old, ui)) ui.Parent.Controls.Remove(ui);
            old.Dispose();
        }
        _content.Controls.Add(next);
        next.Visible = true;

        static bool Contains(Control parent, Control child)
        {
            foreach (Control c in parent.Controls)
                if (ReferenceEquals(c, child) || Contains(c, child)) return true;
            return false;
        }
    }

    private void ShowToast(string message, Control anchor)
    {
        // 复用一个 ToolTip：每次 new 出来的气泡持有原生窗口和计时器，从不释放会一直累积。
        int tw = TextRenderer.MeasureText(message, UiTheme.Body).Width;
        int x = Math.Max(0, anchor.Width / 2 - tw / 2);
        _toastTip.Show(message, anchor, x, anchor.Bottom + UiTheme.Sx(10), 3400);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.K))
        {
            _search.Focus();
            _search.SelectAll();
            return true;
        }
        var code = keyData & Keys.KeyCode;
        if (keyData == (Keys.Control | code) && code is >= Keys.D0 and <= Keys.D9 &&
            _hotkeys.TryGetValue((int)code - (int)Keys.D0, out var hot))
        {
            OpenTool(hot);
            return true;
        }
        if (keyData == Keys.Escape)
        {
            if (_search.Focused)
            {
                _search.Clear();
                ActiveControl = null;
                return true;
            }
            if (_page is { } p) { p.ScrollToTop(); return true; }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
