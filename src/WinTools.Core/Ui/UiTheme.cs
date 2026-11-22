using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WinTools.Core.Ui;

/// <summary>
/// 全工具箱统一的视觉规范（Windows 11 分层色板）。放在 Core 里，Shell 与所有工具 DLL 都能复用——
/// 新增工具零改宿主，也自动获得一致的视觉与主题切换能力。
/// 颜色以属性暴露：切换主题后所有读取 UiTheme.X 的控件重建即得到新配色。
///
/// 分层模型是纯实心色阶：Window（窗口底）→ Canvas（内容画布）→ Surface（卡片/导航）
/// → Layer（输入框/表头等次级填充），再配 Stroke 描边拉开层级。不用半透明叠加，也不做模拟模糊的投影。
/// </summary>
public static class UiTheme
{
    // ── DPI 缩放 ────────────────────────────────────────────────
    // 源码里的像素值一律按 100% 缩放的"设计尺寸"书写，落到尺寸/坐标/字号前统一乘 S。
    // 进程声明 PerMonitorV2 后 Windows 不再把窗口当位图整体拉伸，界面按物理分辨率原生绘制，
    // 文字边缘和描边才是锐的；S 负责让界面在高缩放屏上保持原来的比例与体量。
    public static float S { get; private set; } = 1f;

    /// <summary>宿主在构造任何界面前调用一次：把系统 DPI 折算成缩放倍数。</summary>
    public static void SetScaleFromDpi(float dpi)
    {
        var s = Math.Max(1f, dpi / 96f);
        if (Math.Abs(s - S) < 0.001f) return;
        S = s;
        // L34：缩放变化后旧字号缓存不再命中，先释放其 GDI 字体句柄再清空。
        lock (_fontAtCache)
        {
            foreach (var font in _fontAtCache.Values) font.Dispose();
            _fontAtCache.Clear();
        }
        RebuildFonts();
    }

    public static int Sx(double logical) => (int)Math.Round(logical * S, MidpointRounding.AwayFromZero);
    public static float Sxf(double logical) => (float)(logical * S);

    // 尺寸/坐标助手：调用点写设计尺寸，缩放只在这一层发生。
    public static Size Sz(int w, int h) => new(Sx(w), Sx(h));
    public static Size Sz(int v) => new(Sx(v), Sx(v));
    public static Point Pt(int x, int y) => new(Sx(x), Sx(y));
    public static Padding Pad(int v) => new(Sx(v));
    public static Padding Pad(int l, int t, int r, int b) => new(Sx(l), Sx(t), Sx(r), Sx(b));
    public static Rectangle Rc(int x, int y, int w, int h) => new(Sx(x), Sx(y), Sx(w), Sx(h));
    public static RectangleF Rcf(double x, double y, double w, double h) =>
        new(Sxf(x), Sxf(y), Sxf(w), Sxf(h));

    /// <summary>描边宽度：1 设计像素在高缩放屏上就该占 2 物理像素，否则线条相对变细、层级拉不开。</summary>
    public static int PenW(int logical = 1) => Math.Max(1, Sx(logical));

    /// <summary>
    /// 贴边直线的坐标：宽度 w 的笔居中放在 edge - w/2，正好盖满边缘内侧 w 个像素行/列。
    /// 直接画在整数边界上会把 1px 线摊到相邻两行各半灰，分隔线看着就发虚。
    /// </summary>
    public static float Edge(double edge, int logicalWidth = 1) => (float)(edge - PenW(logicalWidth) / 2.0);

    // ── 主题状态 ────────────────────────────────────────────────
    public enum ThemeMode { Light, Dark }

    /// <summary>方案 A 是深色优先的设计，默认进深色。</summary>
    public static ThemeMode CurrentTheme { get; private set; } = ThemeMode.Dark;
    public static bool IsDark => CurrentTheme == ThemeMode.Dark;

    /// <summary>主题切换广播；宿主订阅后重建界面。</summary>
    public static event Action? ThemeChanged;

    public static void SetTheme(ThemeMode mode)
    {
        if (CurrentTheme == mode) return;
        CurrentTheme = mode;

        var handlers = ThemeChanged?.GetInvocationList();
        if (handlers is null) return;

        // M5：封送到 UI 线程执行，避免后台线程直接写控件属性。
        // 没有已创建的窗体（启动早期）时同步执行，保持原有调用行为。
        Form? uiForm = null;
        foreach (Form f in Application.OpenForms)
        {
            if (f.IsHandleCreated) { uiForm = f; break; }
        }
        if (uiForm is not null && uiForm.InvokeRequired)
        {
            uiForm.BeginInvoke(new Action(() => Notify(handlers)));
            return;
        }
        Notify(handlers);
    }

    /// <summary>
    /// M4：逐个调用订阅者并隔离异常——多播委托默认在第一个异常处中断，
    /// 会导致后续控件收不到主题更新，甚至把异常冒泡到事件分发处终止进程。
    /// </summary>
    private static void Notify(Delegate[] handlers)
    {
        foreach (var handler in handlers)
        {
            try
            {
                ((Action)handler)();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UiTheme 主题切换订阅者异常: {ex}");
            }
        }
    }

    public static void ToggleTheme() => SetTheme(IsDark ? ThemeMode.Light : ThemeMode.Dark);

    // ── 浅色分层色板 ────────────────────────────────────────────
    private static readonly Color LWindow = Color.FromArgb(0xEC, 0xEF, 0xF5);
    private static readonly Color LBg = Color.FromArgb(0xF5, 0xF7, 0xFB);
    private static readonly Color LPanel = Color.FromArgb(0xFF, 0xFF, 0xFF);
    private static readonly Color LSurface = Color.FromArgb(0xF8, 0xFA, 0xFD);
    private static readonly Color LLayer = Color.FromArgb(0xF2, 0xF4, 0xF9);
    private static readonly Color LFieldBg = Color.FromArgb(0xFA, 0xFB, 0xFE);
    private static readonly Color LBorder = Color.FromArgb(0xE4, 0xE8, 0xF0);
    private static readonly Color LStrokeStrong = Color.FromArgb(0xC9, 0xD1, 0xE0);
    private static readonly Color LText = Color.FromArgb(0x14, 0x18, 0x21);
    private static readonly Color LText2 = Color.FromArgb(0x3C, 0x44, 0x53);
    private static readonly Color LMuted = Color.FromArgb(0x6E, 0x77, 0x88);
    private static readonly Color LHover = Color.FromArgb(0xEB, 0xF0, 0xF9);
    private static readonly Color LActiveBg = Color.FromArgb(0xE3, 0xEF, 0xFC);
    private static readonly Color LTableHead = Color.FromArgb(0xF3, 0xF5, 0xFA);
    private static readonly Color LRowLine = Color.FromArgb(0xEA, 0xEE, 0xF5);
    private static readonly Color LInfoBg = Color.FromArgb(0xF0, 0xF4, 0xFB);
    private static readonly Color LButtonBg = Color.FromArgb(0xFB, 0xFC, 0xFE);
    private static readonly Color LButtonHover = Color.FromArgb(0xF1, 0xF4, 0xFA);
    private static readonly Color LButtonPress = Color.FromArgb(0xE6, 0xEB, 0xF4);
    private static readonly Color LShadow = Color.FromArgb(0x2C, 0x36, 0x4C);

    private static readonly Color LPrimary = Color.FromArgb(0x0E, 0x67, 0xD0);
    private static readonly Color LPrimaryHover = Color.FromArgb(0x2A, 0x7F, 0xDE);
    private static readonly Color LPrimaryPress = Color.FromArgb(0x09, 0x52, 0xA8);
    private static readonly Color LPrimarySoft = Color.FromArgb(0xE6, 0xF0, 0xFC);
    private static readonly Color LSuccess = Color.FromArgb(0x0E, 0x7A, 0x4F);
    private static readonly Color LSuccessBg = Color.FromArgb(0xE3, 0xF6, 0xEC);
    private static readonly Color LWarn = Color.FromArgb(0xA8, 0x6A, 0x0C);
    private static readonly Color LWarnBg = Color.FromArgb(0xFD, 0xF2, 0xDF);
    private static readonly Color LError = Color.FromArgb(0xC0, 0x34, 0x34);
    private static readonly Color LErrorBg = Color.FromArgb(0xFC, 0xEA, 0xEA);

    // ── 暗色分层色板：深海蓝（方案 A）────────────────────────────
    // 全部是实心色：层级靠"底色 → 面板 → 次级面 → 输入位"的明度台阶和 1px 描边拉开，
    // 不叠半透明，也不做模拟模糊的投影，界面因此边缘干净、文字不发虚。
    private static readonly Color DWindow = Color.FromArgb(0x0E, 0x18, 0x26);
    private static readonly Color DBg = Color.FromArgb(0x0B, 0x14, 0x20);
    private static readonly Color DPanel = Color.FromArgb(0x16, 0x22, 0x3A);
    private static readonly Color DSurface = Color.FromArgb(0x1B, 0x29, 0x42);
    private static readonly Color DLayer = Color.FromArgb(0x1F, 0x2E, 0x49);
    private static readonly Color DFieldBg = Color.FromArgb(0x10, 0x1B, 0x2C);
    private static readonly Color DBorder = Color.FromArgb(0x2C, 0x3B, 0x52);
    private static readonly Color DStrokeStrong = Color.FromArgb(0x41, 0x57, 0x7A);
    private static readonly Color DText = Color.FromArgb(0xE8, 0xEF, 0xF9);
    private static readonly Color DText2 = Color.FromArgb(0xA9, 0xB8, 0xCC);
    private static readonly Color DMuted = Color.FromArgb(0x72, 0x86, 0xA0);
    private static readonly Color DHover = Color.FromArgb(0x1E, 0x2E, 0x47);
    private static readonly Color DActiveBg = Color.FromArgb(0x1D, 0x33, 0x54);
    private static readonly Color DTableHead = Color.FromArgb(0x18, 0x26, 0x3C);
    private static readonly Color DRowLine = Color.FromArgb(0x24, 0x33, 0x4A);
    private static readonly Color DInfoBg = Color.FromArgb(0x15, 0x22, 0x36);
    private static readonly Color DButtonBg = Color.FromArgb(0x20, 0x30, 0x4A);
    private static readonly Color DButtonHover = Color.FromArgb(0x28, 0x3C, 0x59);
    private static readonly Color DButtonPress = Color.FromArgb(0x16, 0x23, 0x37);
    private static readonly Color DShadow = Color.FromArgb(0x00, 0x04, 0x0A);

    private static readonly Color DPrimary = Color.FromArgb(0x4C, 0x9A, 0xFF);
    private static readonly Color DPrimaryHover = Color.FromArgb(0x6F, 0xB4, 0xFF);
    private static readonly Color DPrimaryPress = Color.FromArgb(0x2F, 0x6F, 0xED);
    private static readonly Color DPrimarySoft = Color.FromArgb(0x1D, 0x33, 0x54);
    private static readonly Color DSuccess = Color.FromArgb(0x34, 0xD3, 0x99);
    private static readonly Color DSuccessBg = Color.FromArgb(0x11, 0x2A, 0x27);
    private static readonly Color DWarn = Color.FromArgb(0xFB, 0xBF, 0x24);
    private static readonly Color DWarnBg = Color.FromArgb(0x2B, 0x24, 0x14);
    private static readonly Color DError = Color.FromArgb(0xFB, 0x71, 0x85);
    private static readonly Color DErrorBg = Color.FromArgb(0x2E, 0x18, 0x22);

    // ── 对外属性：按当前主题取值 ────────────────────────────────
    /// <summary>窗口最底色（标题栏、导航栏）。</summary>
    public static Color WindowBg => IsDark ? DWindow : LWindow;
    /// <summary>内容画布底色。</summary>
    public static Color Bg => IsDark ? DBg : LBg;
    /// <summary>卡片/浮层表面。</summary>
    public static Color Panel => IsDark ? DPanel : LPanel;
    /// <summary>次级表面（分区底、信息条）。</summary>
    public static Color Surface => IsDark ? DSurface : LSurface;
    /// <summary>三级填充（表头、代码条）。</summary>
    public static Color Layer => IsDark ? DLayer : LLayer;
    /// <summary>描边。</summary>
    public static Color Border => IsDark ? DBorder : LBorder;
    /// <summary>强调描边（输入框默认边框、分隔线）。</summary>
    public static Color Stroke => IsDark ? DStrokeStrong : LStrokeStrong;
    /// <summary>主文本。</summary>
    public static Color Text => IsDark ? DText : LText;
    /// <summary>次文本。</summary>
    public static Color TextSecondary => IsDark ? DText2 : LText2;
    /// <summary>弱化文本。</summary>
    public static Color Muted => IsDark ? DMuted : LMuted;
    public static Color Hover => IsDark ? DHover : LHover;
    public static Color FieldBg => IsDark ? DFieldBg : LFieldBg;
    public static Color InfoBg => IsDark ? DInfoBg : LInfoBg;
    public static Color TableHead => IsDark ? DTableHead : LTableHead;
    public static Color RowLine => IsDark ? DRowLine : LRowLine;
    public static Color ActiveBg => IsDark ? DActiveBg : LActiveBg;
    /// <summary>标准按钮填充。</summary>
    public static Color ButtonBg => IsDark ? DButtonBg : LButtonBg;
    public static Color ButtonHover => IsDark ? DButtonHover : LButtonHover;
    public static Color ButtonPress => IsDark ? DButtonPress : LButtonPress;
    /// <summary>投影基色（配合 alpha 使用）。</summary>
    public static Color Shadow => IsDark ? DShadow : LShadow;

    public static Color Primary => IsDark ? DPrimary : LPrimary;
    public static Color PrimaryHover => IsDark ? DPrimaryHover : LPrimaryHover;
    public static Color PrimaryPress => IsDark ? DPrimaryPress : LPrimaryPress;
    /// <summary>主色淡底，用于选中项、徽章。</summary>
    public static Color PrimarySoft => IsDark ? DPrimarySoft : LPrimarySoft;
    public static Color Success => IsDark ? DSuccess : LSuccess;
    public static Color SuccessBg => IsDark ? DSuccessBg : LSuccessBg;
    public static Color Warn => IsDark ? DWarn : LWarn;
    public static Color WarnBg => IsDark ? DWarnBg : LWarnBg;
    public static Color Error => IsDark ? DError : LError;
    public static Color ErrorBg => IsDark ? DErrorBg : LErrorBg;

    // 兼容旧命名（工具 DLL 与 Shell 中已有引用）
    public static Color ShellBg => WindowBg;
    public static Color PrimarySoftBg => PrimarySoft;

    // ── 强调色：按工具/分类语义取色 ─────────────────────────────
    private static readonly Color[] LightAccents =
    {
        Color.FromArgb(0x63, 0x66, 0xF1),  // 0 靛 · 系统
        Color.FromArgb(0x08, 0x91, 0xB2),  // 1 青 · 校验
        Color.FromArgb(0x7C, 0x3A, 0xED),  // 2 紫 · 结构
        Color.FromArgb(0xD9, 0x77, 0x06),  // 3 琥珀 · 批量
        Color.FromArgb(0x05, 0x96, 0x69),  // 4 绿 · 网络
        Color.FromArgb(0xDB, 0x27, 0x77),  // 5 玫 · 安全
        Color.FromArgb(0x25, 0x63, 0xEB),  // 6 蓝 · 编码
        Color.FromArgb(0x02, 0x84, 0xC7),  // 7 天蓝 · 通用
    };
    private static readonly Color[] DarkAccents =
    {
        Color.FromArgb(0x81, 0x8C, 0xF8),
        Color.FromArgb(0x22, 0xD3, 0xEE),
        Color.FromArgb(0xA7, 0x8B, 0xFA),
        Color.FromArgb(0xF5, 0xA5, 0x24),
        Color.FromArgb(0x34, 0xD3, 0x99),
        Color.FromArgb(0xF4, 0x72, 0xB6),
        Color.FromArgb(0x60, 0xA5, 0xFA),
        Color.FromArgb(0x38, 0xBD, 0xF8),
    };

    public static Color Accent(int index)
    {
        var a = IsDark ? DarkAccents : LightAccents;
        return a[((index % a.Length) + a.Length) % a.Length];
    }

    // ── 字体阶梯（设计像素 × S，全部走 GraphicsUnit.Pixel）──────────
    // 字体在 SetScaleFromDpi 时一次性重建：控件持有的是属性值，界面在启动时构建，
    // 缩放设定一定发生在任何 Font 被取用之前。
    private const string Family = "Microsoft YaHei UI";

    private static Font _caption = null!, _small = null!, _body = null!, _bold = null!,
        _subtitle = null!, _cardTitle = null!, _title = null!, _pageTitle = null!, _hero = null!,
        _mono = null!, _monoBig = null!, _glyph = null!, _glyphSmall = null!;

    public static Font Caption => _caption;
    public static Font Small => _small;
    public static Font Body => _body;
    public static Font Bold => _bold;
    public static Font Subtitle => _subtitle;
    /// <summary>卡片标题专用：比正文重一档，比区块标题轻一档。</summary>
    public static Font CardTitle => _cardTitle;
    public static Font Title => _title;
    public static Font PageTitle => _pageTitle;
    public static Font Hero => _hero;
    public static Font Mono => _mono;
    public static Font MonoBig => _monoBig;
    /// <summary>图标名对应的绘制键，交给 IconFactory 画矢量。</summary>
    public static Font Glyph => _glyph;
    public static Font GlyphSmall => _glyphSmall;

    /// <summary>临时字号（品牌名、分区小标题这类不在阶梯里的字）。</summary>
    // 临时字号也按规格缓存：这些 Font 由 Label 长期持有，每次 new 都占一个 GDI 句柄，
    // 反复切页/切主题会慢慢累积。缩放变化时 key 里的 Scale 变化，自然换一套新字体。
    private static readonly Dictionary<(float, bool, string, float), Font> _fontAtCache = new();
    public static Font FontAt(float px, bool bold = false, string family = Family)
    {
        var key = (px, bold, family, S);
        lock (_fontAtCache)
        {
            if (!_fontAtCache.TryGetValue(key, out var f))
                _fontAtCache[key] = f = new Font(family, px * S,
                    bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
            return f;
        }
    }

    private static Font Px(float px, bool bold = false, string family = Family) =>
        new(family, px * S, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);

    static UiTheme() => RebuildFonts();

    private static void RebuildFonts()
    {
        // L34：重建前释放旧字体句柄（首次调用时字段为 null，跳过）。
        foreach (var old in new[] { _caption, _small, _body, _bold, _subtitle, _cardTitle,
                     _title, _pageTitle, _hero, _mono, _monoBig, _glyph, _glyphSmall })
            old?.Dispose();

        _caption = Px(11F);
        _small = Px(11.5F);
        _body = Px(13F);
        _bold = Px(13F, true);
        _subtitle = Px(15F, true);
        _cardTitle = Px(14F, true);
        _title = Px(18F, true);
        _pageTitle = Px(21F, true);
        _hero = Px(26F, true);
        _mono = Px(12.5F, family: "Consolas");
        _monoBig = Px(16F, family: "Consolas");
        _glyph = Px(15F);
        _glyphSmall = Px(12F);
    }

    // ── 圆角令牌（随 S 缩放）────────────────────────────────────
    public static int RadiusSmall => Sx(4);
    public static int RadiusMedium => Sx(6);
    public static int RadiusLarge => Sx(8);
    public static int RadiusCard => Sx(10);
    public static int RadiusWindow => Sx(12);

    public enum ButtonKind { Primary, Secondary, Danger, Ghost, Subtle, AccentSoft }

    /// <summary>统一按钮：自绘圆角 + 图标可选 + 悬停/按下/聚焦反馈。宽高传设计尺寸。</summary>
    public static FluentButton Button(string text, ButtonKind kind = ButtonKind.Primary, int width = 88, int height = 32)
        => new FluentButton { Text = text, Variant = kind, Size = Sz(width, height) };

    /// <summary>带矢量图标的按钮；图标画在文字左侧。</summary>
    public static FluentButton Button(string text, string icon, ButtonKind kind = ButtonKind.Secondary,
        int width = 110, int height = 32)
        => new FluentButton { Text = text, IconName = icon, Variant = kind, Size = Sz(width, height) };

    /// <summary>仅图标的方形按钮（窗口控制、工具条）。</summary>
    public static FluentButton IconButton(string icon, ButtonKind kind = ButtonKind.Ghost, int size = 32)
        => new FluentButton { IconName = icon, Variant = kind, Size = Sz(size), Text = "" };

    /// <summary>
    /// 统一单行输入框（圆角柔边，聚焦主色描边）。
    /// 高度与标准按钮一致（32），同排才不会一个高一个矮。
    /// MinimumSize 是必需的：单行 TextBox 的 AutoSize 会把高度锁成字体高，
    /// 而 TableLayoutPanel 按 PreferredSize 排布时会把宽度压回默认的 100。
    /// </summary>
    public static FluentInput Input(string placeholder = "", int width = 220)
        => new(placeholder, width);

    /// <summary>统一多行输入框（等宽字体、圆角柔边）。</summary>
    public static TextBox MultilineInput(bool readOnly = false)
    {
        var box = new FluentTextBox
        {
            Multiline = true,
            // 软换行 + 只留纵向滚动条：横向滚动条是一条压在圆角上的原生灰带，比折行难看得多
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            WordWrap = true,
            ReadOnly = readOnly,
        };
        return box;
    }

    /// <summary>
    /// 数据表格视图（圆角柔边 + Win11 表头）。给了宽高就用 MinimumSize 钉住，
    /// 免得 TableLayoutPanel/FlowLayoutPanel 按 PreferredSize 把它压回默认 100x20。
    /// </summary>
    public static ListView List(int width = 0, int height = 0)
    {
        var list = new FluentListView();
        if (width > 0 && height > 0)
        {
            var size = Sz(width, height);
            list.Size = size;
            list.MinimumSize = size;
        }
        return list;
    }

    /// <summary>
    /// 卡片之间 12 设计像素的占位条。Dock 布局按控件自身高度推进，不认 CardPanel 的下边距，
    /// 两张卡直接相邻时两条 1px 描边会糊成一条缝，只能显式撑开。
    /// </summary>
    public static Panel CardGap() => new()
    {
        Dock = DockStyle.Top,
        Height = Sx(12),
        BackColor = Bg,
        Margin = Padding.Empty,
    };

    public static Label FieldLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = Bold,
        ForeColor = TextSecondary,
        Margin = Pad(0, 0, 0, 6),
        BackColor = Color.Transparent,
    };

    public static Label HintLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = Small,
        ForeColor = Muted,
        BackColor = Color.Transparent,
    };

    // ── 颜色工具 ────────────────────────────────────────────────
    /// <summary>颜色混合：t 为上层权重，用于把半透明色调换算成等效不透明色。</summary>
    public static Color Blend(Color over, Color under, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb(
            (int)Math.Round(under.R + (over.R - under.R) * t),
            (int)Math.Round(under.G + (over.G - under.G) * t),
            (int)Math.Round(under.B + (over.B - under.B) * t));
    }

    // ── 绘制助手 ────────────────────────────────────────────────
    /// <summary>
    /// 沿父链取第一层不透明底色。自绘控件铺背景时用这个，而不是直接读 Parent.BackColor：
    /// 中间容器常用 Color.Transparent（A=0），拿它当颜色刷会让双缓冲画布保持全黑。
    /// </summary>
    public static Color Backdrop(Control? c)
    {
        for (; c is not null; c = c.Parent)
            if (c.BackColor.A > 0) return c.BackColor;
        return Bg;
    }

    /// <summary>圆角路径：Region 裁剪与自绘边框共用，保证边缘完全一致。</summary>
    public static GraphicsPath RoundedPath(Rectangle rect, int radius) =>
        RoundedPath(new RectangleF(rect.X, rect.Y, rect.Width, rect.Height), radius);

    /// <summary>浮点版圆角路径：1px 描边要把路径挪到像素中心，只能用这个重载。</summary>
    public static GraphicsPath RoundedPath(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        var d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        if (d <= 0) { path.AddRectangle(rect); return path; }
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>
    /// 替换控件 Region 并释放旧对象（M19）。Control.Region 的 setter 不会释放旧 Region，
    /// 直接覆盖会在每次 Resize 泄漏一个 GDI 句柄，高缩放/频繁停靠时累积。
    /// </summary>
    public static void SetRegion(Control control, GraphicsPath path)
    {
        var old = control.Region;
        control.Region = new Region(path);
        old?.Dispose();
    }

    /// <summary>给控件套圆角区域。</summary>
    public static void Round(Control control, int radius = -1)
    {
        if (radius < 0) radius = RadiusLarge;
        if (control.Width <= 0 || control.Height <= 0) return;
        using var path = RoundedPath(new Rectangle(0, 0, control.Width, control.Height), radius);
        SetRegion(control, path);
    }

    /// <summary>
    /// 沿圆角路径画描边。路径按线宽内缩半个像素，让奇数宽的线正好压在像素列上——
    /// 否则 GDI+ 会把线摊到相邻两列各半灰，圆角卡片外圈看着就是一层发虚的"毛边"。
    /// width 传设计线宽（默认 1），实际宽度随 S 放大。
    /// </summary>
    public static void DrawRoundedBorder(Graphics g, Rectangle rect, int radius, Color color, int width = 1)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var w = PenW(width);
        using var pen = new Pen(color, w);
        var half = w / 2f;
        var inner = new RectangleF(rect.X + half, rect.Y + half,
            Math.Max(0, rect.Width - w), Math.Max(0, rect.Height - w));
        using var path = RoundedPath(inner, Math.Max(0, radius - half));
        g.DrawPath(pen, path);
    }

    /// <summary>填充圆角表面并画 1px 描边：一次画完一张实心卡片，不投影、不高光。</summary>
    public static void FillSurface(Graphics g, Rectangle rect, int radius, Color fill, Color? stroke = null)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = RoundedPath(rect, radius))
        using (var b = new SolidBrush(fill))
            g.FillPath(b, path);
        if (stroke.HasValue)
            DrawRoundedBorder(g, rect, radius, stroke.Value);
    }

    /// <summary>
    /// 工具图标块：强调色淡底渐变 + 同色描边，图形本身用强调色绘制。
    /// 相比纯色块 + 白图形，染色处理在深色底上更耐看，也不会让八个色块互相抢视线。
    /// </summary>
    public static void IconTile(Graphics g, Rectangle rect, Color accent, string icon, float iconScale = 0.56f)
    {
        int radius = Math.Max(Sx(6), rect.Height * 3 / 10);
        var baseFill = IsDark ? Blend(accent, DPanel, 0.22f) : Blend(accent, LPanel, 0.16f);
        var top = Blend(accent, baseFill, 0.30f);
        var bottom = Blend(accent, baseFill, 0.10f);

        using (var path = RoundedPath(rect, radius))
        using (var br = new LinearGradientBrush(new Rectangle(rect.X, rect.Y, rect.Width, Math.Max(2, rect.Height)),
                   top, bottom, LinearGradientMode.Vertical))
        {
            g.SetClip(path);
            g.FillRectangle(br, rect);
            g.ResetClip();
        }
        DrawRoundedBorder(g, rect, radius, Blend(accent, baseFill, 0.55f));

        float pad = rect.Width * (1 - iconScale) / 2f;
        IconFactory.Draw(g, icon, new RectangleF(rect.X + pad, rect.Y + pad,
            rect.Width - pad * 2, rect.Height - pad * 2), accent);
    }

    /// <summary>画矢量图标（转发 IconFactory，自动取当前主题色）。</summary>
    public static void Icon(Graphics g, string name, Rectangle bounds, Color color, float? stroke = null)
        => IconFactory.Draw(g, name, bounds, color, stroke);
}
