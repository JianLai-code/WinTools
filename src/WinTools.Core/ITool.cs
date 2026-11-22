using System.Drawing;
using System.Windows.Forms;

namespace WinTools.Core;

/// <summary>
/// 所有工具必须实现的统一接口。
/// 宿主（Shell）只依赖此接口，不依赖任何具体工具类——这是"新增工具不改宿主"的核心。
/// </summary>
public interface ITool
{
    /// <summary>唯一标识，用于设置存储与导航定位。建议用小写英文+连字符，如 "json-formatter"。</summary>
    string Id { get; }

    /// <summary>显示名称，会出现在左侧导航中。</summary>
    string Name { get; }

    /// <summary>分类，用于左侧导航分组，如 "文本工具" / "文件工具" / "系统工具"。</summary>
    string Category { get; }

    /// <summary>图标，可为 null（宿主会用名称首字代替）。</summary>
    Icon? Icon { get; }

    /// <summary>创建工具主界面。宿主在用户点击导航项时调用一次。</summary>
    Control CreateUI();
}
