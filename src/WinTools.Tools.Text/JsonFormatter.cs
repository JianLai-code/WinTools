using System.Text.Encodings.Web;
using System.Text.Json;

namespace WinTools.Tools.Text;

/// <summary>
/// JSON 格式化/校验的纯逻辑类，不依赖任何 UI，便于单元测试。
/// 对应验收用例 T1-AC1 ~ AC4（见阶段二文档）。
/// </summary>
public static class JsonFormatter
{
    // 输入上限：防止 UI 线程同步解析超大 JSON 导致卡死/OOM。
    public const int MaxInputLength = 5 * 1024 * 1024;

    // 关键坑位：System.Text.Json 序列化默认把中文转义成 \u4E2D\u6587，
    // 必须用 UnsafeRelaxedJsonEscaping 才会原样保留中文（T1-AC4 中文不乱码）。
    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>格式化 JSON。成功返回缩进后的文本，失败返回结果对象携带错误原因与行号。</summary>
    public static JsonFormatResult Format(string? input)
    {
        // T1-AC3：空输入给友好提示，不抛异常。
        if (string.IsNullOrWhiteSpace(input))
        {
            return JsonFormatResult.Fail("请输入内容");
        }
        if (input.Length > MaxInputLength)
        {
            return JsonFormatResult.Fail($"内容过长（最多 {MaxInputLength / 1024 / 1024} MB）");
        }

        try
        {
            // 第一步：解析校验。JsonDocument.Parse 遇到语法错误会抛 JsonException，
            // 其中带 LineNumber（0 起）和 Message，可定位错误行。
            using var document = JsonDocument.Parse(input);
            var output = JsonSerializer.Serialize(document.RootElement, SerializeOptions);
            return JsonFormatResult.Ok(output);
        }
        catch (JsonException ex)
        {
            // T1-AC2：语法错误指出错误行号与原因，不输出半成品结果。
            var line = ex.LineNumber.HasValue ? (int)ex.LineNumber.Value + 1 : (int?)null;
            return JsonFormatResult.Fail(ex.Message, line);
        }
    }
}
