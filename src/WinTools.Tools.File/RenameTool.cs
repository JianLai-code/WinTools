using System.Drawing;
using System.Windows.Forms;
using WinTools.Core;
using WinTools.Core.Ui;

namespace WinTools.Tools.Files;

/// <summary>
/// 批量重命名工具：全工具箱唯一的写操作工具。
/// 打开 → 选文件/设规则 → 预览 → 二次确认后执行 → 反馈 + 撤销。
/// 界面按阶段四/原型重做，安全逻辑（防覆盖、二次确认、撤销栈）保持不变。
/// </summary>
[Tool(Order = 50)]
public sealed class RenameTool : ITool
{
    public string Id => "batch-rename";
    public string Name => "批量重命名";
    public string Category => "文件工具";
    public Icon? Icon => null;

    public Control CreateUI()
    {
        // 标签/控件在单元格内垂直居中：只锚左（或左右拉宽），默认的 Top 锚定会让标签贴着行顶，
        // 和旁边输入框里的文字错开半行；左右都锚则控件随卡片宽度伸缩，不再写死宽度。
        static Label Field(string text)
        {
            var l = UiTheme.FieldLabel(text);
            l.Margin = Padding.Empty;
            l.Anchor = AnchorStyles.Left;
            return l;
        }
        static void Cell(Control c, bool stretch)
        {
            c.Margin = Padding.Empty;
            c.Anchor = stretch ? AnchorStyles.Left | AnchorStyles.Right : AnchorStyles.Left;
        }

        var root = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Bg };
        var header = new ToolHeader("批量重命名", "按规则批量改名。这是危险操作，必须先预览、再确认，且绝不静默覆盖原文件。");

        var sourceFiles = new List<string>();
        var lastRecords = new List<RenameRecord>();

        // ---- 警告条（原型 .note）----
        // 原生 FixedSingle 是直角硬框，和同页的圆角卡片、圆角输入框摆在一起就是那颗"过锐的拐角"，改自绘柔边。
        var note = new Panel
        {
            Dock = DockStyle.Top,
            Height = UiTheme.Sx(42),
            BackColor = UiTheme.WarnBg,
            Padding = UiTheme.Pad(14, 0, 14, 0),
            Margin = UiTheme.Pad(0, 0, 0, 10)
        };
        note.Resize += (_, _) => UiTheme.Round(note, UiTheme.RadiusLarge);
        note.Paint += (_, e) => UiTheme.DrawRoundedBorder(e.Graphics,
            new Rectangle(0, 0, Math.Max(1, note.Width - 1), Math.Max(1, note.Height - 1)),
            UiTheme.RadiusLarge, UiTheme.Blend(UiTheme.Warn, UiTheme.WarnBg, 0.42f));
        var noteLabel = new Label
        {
            Text = "⚠  不可逆操作：执行前请核对预览，系统会自动避免文件名冲突，绝不覆盖已有文件。",
            Dock = DockStyle.Fill,
            Font = UiTheme.Small,
            ForeColor = UiTheme.Warn,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = Padding.Empty
        };
        note.Controls.Add(noteLabel);

        // ---- 规则区 ----
        var templateBox = UiTheme.Input("", 200);
        templateBox.Text = "{name}";
        var startNum = new FluentNumeric { Minimum = 0, Maximum = 999999, Value = 1, Width = UiTheme.Sx(80) };
        var findBox = UiTheme.Input("查找文字", 110);
        var replaceBox = UiTheme.Input("替换为", 110);

        // 固定高度 Dock=Top：此前 Dock=Fill 会把富余高度全部堆进网格底部，
        // 第三行与"占位符"提示之间的空档随卡片高度变化，行距忽大忽小。
        var grid = new TableLayoutPanel { Dock = DockStyle.Top, Height = UiTheme.Sx(132), ColumnCount = 2, RowCount = 3, Padding = Padding.Empty };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UiTheme.Sx(84)));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        // 行高 44：单行输入件 34 + 上下各 5，标签才能和框内文字同一条中线
        for (var r = 0; r < 3; r++) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, UiTheme.Sx(44)));

        Cell(templateBox, true);
        Cell(startNum, false);
        Cell(findBox, false);
        Cell(replaceBox, false);
        grid.Controls.Add(Field("命名模板"), 0, 0);
        grid.Controls.Add(templateBox, 1, 0);
        grid.Controls.Add(Field("起始序号"), 0, 1);
        grid.Controls.Add(startNum, 1, 1);
        grid.Controls.Add(Field("查找 / 替换"), 0, 2);
        // 查找/替换一排：整条随单元格宽度伸缩并垂直居中，行内三件同高
        var findRow = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false, BackColor = Color.Transparent
        };
        Cell(findRow, true);
        var arrow = new Label
        {
            Text = "→", AutoSize = false, Width = UiTheme.Sx(24),
            TextAlign = ContentAlignment.MiddleCenter, Font = UiTheme.Body, ForeColor = UiTheme.Muted
        };
        // 单行框的实际高度由字体决定，箭头跟它取齐才和两个框共用一条中线
        arrow.Height = Math.Max(findBox.Height, replaceBox.Height);
        findRow.Controls.Add(findBox);
        findRow.Controls.Add(arrow);
        findRow.Controls.Add(replaceBox);
        int findRowH = Math.Max(findBox.Height, Math.Max(arrow.Height, replaceBox.Height));
        // 行内按实测高度居中 + 统一 8 设计像素间隙，默认的 3px 边距不随 DPI 放大
        foreach (Control c in findRow.Controls)
            c.Margin = new Padding(0, Math.Max(0, (findRowH - c.Height) / 2), UiTheme.Sx(8), 0);
        grid.Controls.Add(findRow, 1, 2);

        var hint = UiTheme.HintLabel("占位符：{name} 原名、{n} 序号；扩展名自动保留。例：照片{n} + 起始 1 → 照片1.jpg、照片2.jpg");
        hint.Dock = DockStyle.Top;
        hint.Margin = Padding.Empty;
        // 提示文字与上方网格、下方按钮行的间距统一为 10（网格行内间隙 44-34=10，按钮条上边距 10）
        hint.Padding = UiTheme.Pad(0, 10, 0, 0);

        var btnSelect = UiTheme.Button("选择文件");
        var btnPreview = UiTheme.Button("刷新预览", UiTheme.ButtonKind.Secondary);
        var btnExecute = UiTheme.Button("执行重命名", UiTheme.ButtonKind.Danger);
        var btnUndo = UiTheme.Button("撤销上一步", UiTheme.ButtonKind.Secondary);
        btnUndo.Enabled = false;
        var buttonBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = UiTheme.Sx(42), WrapContents = false,
            Margin = UiTheme.Pad(0, 10, 0, 0)
        };
        buttonBar.Controls.Add(btnSelect);
        buttonBar.Controls.Add(btnPreview);
        buttonBar.Controls.Add(btnExecute);
        buttonBar.Controls.Add(btnUndo);
        // 间隙与上下留白按设计像素缩放：默认的 3px 边距在 200% 下不放大，按钮会挤成一坨并贴住条顶
        buttonBar.Padding = UiTheme.Pad(0, (42 - 32) / 2, 0, 0);
        foreach (Control c in buttonBar.Controls) c.Margin = UiTheme.Pad(0, 0, 8, 0);

        // 高度实算：表头 40 + 描边 2 + 内容上下内边距 28 + 网格 132 + 提示（10+约20）+ 按钮条（10+42）≈ 284。
        var ruleCard = new CardPanel("命名规则") { Dock = DockStyle.Top, Height = UiTheme.Sx(284) };
        // 后加的先贴边：grid 最后加 → 贴最顶；hint 次之；buttonBar 最先入栈贴底。
        // 视觉顺序 网格 → 提示 → 按钮，富余空间留在按钮条下方而不是堆进网格。
        ruleCard.Content.Controls.Add(buttonBar);
        ruleCard.Content.Controls.Add(hint);
        ruleCard.Content.Controls.Add(grid);

        // ---- 预览表 ----
        var preview = UiTheme.List();
        preview.Dock = DockStyle.Fill;
        preview.Columns.Add("原名", UiTheme.Sx(230));
        preview.Columns.Add("新名", UiTheme.Sx(230));
        preview.Columns.Add("说明", UiTheme.Sx(230));

        var feedback = new FeedbackBar { Dock = DockStyle.Bottom };
        var previewCard = new CardPanel("改名预览（原名 → 新名）") { Dock = DockStyle.Fill, Margin = new Padding(0) };
        // 表格保留卡片内边距：零边距会让表头分隔线和直角边框顶进卡片的圆角区
        previewCard.Content.Controls.Add(preview);
        previewCard.Content.Controls.Add(feedback);

        void RefreshPreview()
        {
            preview.Items.Clear();
            if (sourceFiles.Count == 0)
            {
                feedback.Show(FeedbackBar.Kind.Info, "请先选择文件。");
                return;
            }
            var rule = new RenameRule
            {
                Template = templateBox.Text,
                StartIndex = (int)startNum.Value,
                FindText = findBox.Text,
                ReplaceText = replaceBox.Text
            };
            var plan = RenamePlanner.BuildPlan(sourceFiles, rule);
            foreach (var item in plan.Items)
            {
                var row = new ListViewItem(item.OldName);
                row.SubItems.Add(item.IsChanged ? item.NewName : "（不变）");
                row.SubItems.Add(string.IsNullOrEmpty(item.Note) ? (item.IsChanged ? "准备就绪" : "无变化") : item.Note);
                if (!item.IsChanged) row.ForeColor = UiTheme.Muted;
                preview.Items.Add(row);
            }
            var unchanged = plan.Items.Count - plan.ChangedCount;
            feedback.Show(FeedbackBar.Kind.Info,
                $"共 {plan.Items.Count} 个文件：将改名 {plan.ChangedCount} 个" +
                (unchanged > 0 ? $"，{unchanged} 个无变化" : "") + "。请逐行核对「新名」，确认后再执行。");
        }

        // M21：规则输入改为防抖预览——原实现逐字符同步枚举目录（大目录/网络盘会卡住 UI）。
        // 手动刷新与选文件仍立即执行，只有连续键入才合并为一次延迟刷新。
        // Timer 必须随 root 一起释放，不能用 using（CreateUI 返回即释放会导致 Tick 抛异常）。
        var debounce = new System.Windows.Forms.Timer { Interval = 250 };
        debounce.Tick += (_, _) => { debounce.Stop(); RefreshPreview(); };
        root.Disposed += (_, _) => debounce.Dispose();
        void SchedulePreview() { debounce.Stop(); debounce.Start(); }

        btnSelect.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog { Multiselect = true, Title = "选择要重命名的文件（可多选）", Filter = "所有文件|*.*" };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            sourceFiles.Clear();
            sourceFiles.AddRange(dlg.FileNames);
            lastRecords.Clear();
            btnUndo.Enabled = false;
            RefreshPreview();
        };
        btnPreview.Click += (_, _) => { debounce.Stop(); RefreshPreview(); };
        templateBox.TextChanged += (_, _) => SchedulePreview();
        startNum.ValueChanged += (_, _) => SchedulePreview();
        findBox.TextChanged += (_, _) => SchedulePreview();
        replaceBox.TextChanged += (_, _) => SchedulePreview();

        btnExecute.Click += (_, _) =>
        {
            if (sourceFiles.Count == 0)
            {
                feedback.Show(FeedbackBar.Kind.Error, "错误：尚未选择文件，无法执行。");
                return;
            }
            var rule = new RenameRule
            {
                Template = templateBox.Text, StartIndex = (int)startNum.Value,
                FindText = findBox.Text, ReplaceText = replaceBox.Text
            };
            var plan = RenamePlanner.BuildPlan(sourceFiles, rule);
            if (plan.ChangedCount == 0)
            {
                feedback.Show(FeedbackBar.Kind.Error, "错误：没有需要改名的文件（新名与原名相同）。");
                return;
            }
            var sample = plan.Items.Where(i => i.IsChanged).Take(3).Select(i => $"{i.OldName} → {i.NewName}");
            var previewText = string.Join("\n", sample);
            if (plan.ChangedCount > 3) previewText += $"\n…… 共 {plan.ChangedCount} 个文件";
            var confirm = MessageBox.Show(
                $"即将重命名 {plan.ChangedCount} 个文件：\n\n{previewText}\n\n" +
                "此操作会修改磁盘上的文件名，但可通过「撤销上一步」恢复。\n确定执行吗？",
                "确认批量重命名", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.OK)
            {
                feedback.Show(FeedbackBar.Kind.Info, "已取消，未修改任何文件。");
                return;
            }

            var cursor = root.Cursor;
            root.Cursor = Cursors.WaitCursor;
            var result = RenameExecutor.Execute(plan);
            root.Cursor = cursor;

            lastRecords.Clear();
            lastRecords.AddRange(result.Records);
            btnUndo.Enabled = result.Records.Count > 0;

            if (result.Errors.Count > 0)
                feedback.Show(FeedbackBar.Kind.Warn,
                    $"完成：成功 {result.SucceededCount} 个，跳过 {result.Errors.Count} 个。\n" + string.Join("\n", result.Errors.Take(5)));
            else
                feedback.Show(FeedbackBar.Kind.Success,
                    $"完成：成功重命名 {result.SucceededCount} 个文件。可点击「撤销上一步」恢复。");

            sourceFiles.Clear();
            preview.Items.Clear();
        };

        btnUndo.Click += (_, _) =>
        {
            if (lastRecords.Count == 0)
            {
                feedback.Show(FeedbackBar.Kind.Error, "错误：没有可撤销的操作。");
                return;
            }
            var confirm = MessageBox.Show(
                $"将把这 {lastRecords.Count} 个文件恢复为改名前的名称，确定吗？",
                "确认撤销", MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.OK) return;

            var errors = RenameExecutor.Undo(lastRecords);
            if (errors.Count > 0)
            {
                // M12：部分失败时不能无条件清空撤销栈——否则未恢复的文件永久失去重试机会。
                // 保留"新名仍存在、原名未恢复"的记录，供用户再次点击撤销重试。
                var pending = lastRecords
                    .Where(r => File.Exists(r.NewPath) && !File.Exists(r.OldPath))
                    .ToList();
                lastRecords.Clear();
                lastRecords.AddRange(pending);
                btnUndo.Enabled = lastRecords.Count > 0;
                feedback.Show(FeedbackBar.Kind.Warn,
                    $"撤销部分完成，{errors.Count} 个失败：\n" + string.Join("\n", errors.Take(5)) +
                    (lastRecords.Count > 0 ? $"\n\n仍有 {lastRecords.Count} 个未恢复，可再次点击「撤销上一步」重试。" : ""));
            }
            else
            {
                feedback.Show(FeedbackBar.Kind.Success, $"已撤销 {lastRecords.Count} 个文件，恢复为原名。");
                lastRecords.Clear();
                btnUndo.Enabled = false;
            }
        };

        // Dock.Top 后加的排上面：Fill 先加，再规则卡、警告条、头部
        root.Controls.Add(previewCard);
        root.Controls.Add(UiTheme.CardGap());
        root.Controls.Add(ruleCard);
        root.Controls.Add(UiTheme.CardGap());
        root.Controls.Add(note);
        root.Controls.Add(header);
        return root;
    }
}
