using System.Windows.Forms;

namespace WinTools.Core;

/// <summary>
/// 可选扩展：需要在宿主启动后立即开始工作的工具（全局热键、文件监听、后台提醒等）。
///
/// 宿主发现工具后，对实现了本接口的实例调用一次 <see cref="Start"/>；退出时调用 <see cref="Stop"/>。
/// 这是"新增工具不改宿主"的延续——宿主只认接口，不认具体工具；以后新增后台类工具同样零改宿主。
/// 未实现本接口的工具行为完全不变。
/// </summary>
public interface IBackgroundTool
{
    void Start(BackgroundToolContext context);

    void Stop();
}

/// <summary>后台工具拿到的宿主上下文。</summary>
public sealed class BackgroundToolContext
{
    /// <summary>宿主主窗体。热键触发时可用它判断是否需要临时隐藏，避免把宿主自己截进去。</summary>
    public Form? MainWindow { get; init; }

    /// <summary>写入宿主日志（%AppData%\WinTools\logs）。热键注册失败这类不影响主流程的问题走这里。</summary>
    public Action<string> Log { get; init; } = _ => { };
}
