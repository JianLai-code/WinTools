using System.Collections.Generic;
using System.Linq;

namespace WinTools.Core.Ui;

/// <summary>
/// 展示层元数据映射：把工具的展示信息（矢量图标名 / 一句话描述 / 快捷键 / 原型六分类归属）
/// 与业务契约解耦。ITool 只负责 Id/Name/Category/CreateUI，测试断言不受影响；
/// 宿主首页卡片从这里取数据。未登记的工具自动用兜底值，仍可被发现、可点开。
///
/// Icon 是 IconFactory 注册的图标名，由 GDI+ 矢量绘制，不依赖图标字体。
/// 每个工具占用一个独立色相（UiTheme.Accent 索引），避免同屏出现多个同色块。
/// </summary>
public static class ToolMeta
{
    public sealed record Meta(
        string Icon,         // IconFactory 图标名
        string Description,  // 卡片两行描述
        string Shortcut,     // 快捷键徽章，如 Ctrl+1；空表示不显示
        string Group,        // 六分类之一
        int AccentIndex,     // 专属色相索引
        bool IsCommon);      // 是否进"常用工具"区

    private static readonly Dictionary<string, Meta> Map = new()
    {
        ["json-formatter"]     = new("json", "美化 / 压缩 / 校验 JSON，定位语法错误", "Ctrl+1", "文本编码", 2, true),
        ["base64-codec"]       = new("base64", "文本 / Base64 双向编解码，中文不乱码", "Ctrl+2", "文本编码", 6, true),
        ["password-generator"] = new("key", "密码学安全随机密码，强度实时评估", "Ctrl+3", "文本编码", 5, true),
        ["hash-checker"]       = new("fingerprint", "一次算出 MD5 / SHA1 / SHA256，拖放即用", "Ctrl+4", "文件处理", 1, true),
        ["batch-rename"]       = new("tag", "按序号 / 查找替换批量改名，可预览可撤销", "Ctrl+5", "文件处理", 3, true),
        ["system-info"]        = new("chip", "CPU / 内存 / 磁盘 / 系统版本总览", "", "系统维护", 0, false),
        ["port-query"]         = new("port", "查询端口占用与进程 PID，快速定位", "Ctrl+6", "网络工具", 4, true),
        ["network-info"]       = new("network-info", "查看本机 IP / 网关 / DNS / MAC，逐网卡展示", "Ctrl+7", "网络工具", 1, false),
        ["environment-variables"] = new("environment", "查看当前进程环境变量，支持筛选与复制", "Ctrl+0", "系统维护", 3, false),
        ["url-codec"]          = new("url", "URL 参数编解码，空格 %20，+ 按空格处理", "Ctrl+8", "文本编码", 7, true),
        ["timestamp-converter"] = new("clock", "Unix 时间戳 ↔ 日期，秒 / 毫秒自动识别", "Ctrl+9", "开发辅助", 2, false),
        ["qr-code-generator"]  = new("qrcode", "文本 / 链接生成二维码，可保存 PNG", "", "媒体办公", 5, true),
        ["text-diff"]          = new("diff", "逐行对比两段文本，高亮新增与删除", "", "开发辅助", 6, false),
        ["file-encoding-converter"] = new("encoding", "UTF-8 / GBK / GB18030 互转，另存不覆盖", "", "文件处理", 4, false),
        ["image-format-converter"] = new("image-format", "PNG / JPG / BMP / GIF / TIFF 互转，另存不覆盖", "", "媒体办公", 5, false),
        ["image-compress"]     = new("image-compress", "批量压缩图片为 JPG，可调质量，变大保留原图", "", "媒体办公", 6, false),
        ["folder-size-analyzer"] = new("folder-size", "按大小排序看哪个子目录最占空间，只读", "", "文件处理", 3, false),
        ["ping-test"]          = new("ping", "测试主机连通性与延迟，丢包率一目了然", "", "网络工具", 4, false),
        ["duplicate-finder"]   = new("duplicate", "按大小与 SHA-256 找重复文件，只报告不删除", "", "文件处理", 1, false),
        ["text-stats"]         = new("stats", "字符 / 行 / 词 / UTF-8 字节即时统计", "", "文本编码", 1, false),
        ["uuid-generator"]     = new("uuid", "随机 UUID v4，三种格式可一次生成多个", "", "开发辅助", 5, false),
        ["hello"]              = new("sparkle", "插件发现机制的示例工具", "", "开发辅助", 7, false)
    };

    /// <summary>左侧导航的六大分类（顺序固定）。</summary>
    public static readonly string[] Groups =
        { "系统维护", "文件处理", "文本编码", "网络工具", "开发辅助", "媒体办公" };

    private static readonly Dictionary<string, (string Icon, int Accent)> GroupMarks = new()
    {
        ["系统维护"] = ("system", 0),
        ["文件处理"] = ("folder", 3),
        ["文本编码"] = ("text", 1),
        ["网络工具"] = ("network", 4),
        ["开发辅助"] = ("code", 2),
        ["媒体办公"] = ("media", 5),
    };

    /// <summary>分类的线性图标名。</summary>
    public static string GroupIcon(string group) =>
        GroupMarks.TryGetValue(group, out var m) ? m.Icon : "apps";

    /// <summary>分类的色相索引。</summary>
    public static int GroupAccent(string group) =>
        GroupMarks.TryGetValue(group, out var m) ? m.Accent : 7;

    public static Meta For(ITool tool) =>
        Map.TryGetValue(tool.Id, out var m)
            ? m
            : new Meta("apps", "本地运行的小工具", "", "开发辅助", 7, false);

    /// <summary>按六分类归档工具；空分类不出现在首页。</summary>
    public static IReadOnlyList<IGrouping<string, ITool>> GroupTools(IEnumerable<ITool> tools)
    {
        var meta = tools.ToDictionary(t => t, For);
        return tools
            .GroupBy(t => meta[t].Group)
            .OrderBy(g => System.Array.IndexOf(Groups, g.Key))
            .ToList();
    }
}
