namespace WinTools.Core;

/// <summary>
/// 可选扩展：工具愿意把某些能力暴露给外部入口（托盘菜单、命令行、快捷键转发）时实现本接口。
/// 宿主只按"命令 id"调用，不认识具体工具，也不依赖工具内部的 UI——新增可被触发的工具仍然零改宿主。
/// </summary>
public interface IToolCommand
{
    /// <summary>
    /// 执行命令。返回 true 表示已识别并处理；返回 false 表示不认识该命令，调用方自行兜底。
    /// 实现方必须自行兜住异常——这是跨程序集调用边界。
    /// </summary>
    bool TryInvoke(string command);
}
