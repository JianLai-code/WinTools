using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace WinTools.Core;

/// <summary>
/// 剪贴板写入兜底：剪贴链常被远程桌面/剪贴板管理器/杀软短暂占用，
/// SetText 会抛 ExternalException；事件处理器里不接住就是未处理异常，进程直接退出。
/// </summary>
public static class ClipboardUtil
{
    public static bool TryCopy(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (ExternalException) { return false; }
        catch (ThreadStateException) { return false; }
    }

    /// <summary>
    /// 剪贴板写入图片的同款兜底：远程桌面/剪贴板管理器占用剪贴链时 SetImage 会抛 ExternalException。
    /// 图片对象调用方持有，本方法只写入引用不复制位图。
    /// </summary>
    public static bool TryCopyImage(Image image)
    {
        if (image is null) return false;
        try
        {
            Clipboard.SetImage(image);
            return true;
        }
        catch (ExternalException) { return false; }
        catch (ThreadStateException) { return false; }
    }
}
