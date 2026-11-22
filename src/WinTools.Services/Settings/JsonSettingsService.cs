using System.Text.Json;

namespace WinTools.Services.Settings;

/// <summary>
/// 轻量 JSON 设置服务：把设置对象存到用户目录，零第三方依赖。
/// 存储位置：%AppData%\WinTools\settings.json（每台机器每个用户独立）。
/// </summary>
public sealed class JsonSettingsService
{
    private static readonly object Gate = new();
    private readonly string _filePath;

    /// <param name="fileName">设置文件名，默认 settings.json。</param>
    public JsonSettingsService(string fileName = "settings.json")
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "WinTools");
        Directory.CreateDirectory(dir);
        // L03：只取文件名部分，挡住 ..\ 逃逸与绝对路径注入。
        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "settings.json";
        _filePath = Path.Combine(dir, safeName);
    }

    /// <summary>读取设置。文件不存在或损坏时返回默认值，绝不抛异常给调用方。</summary>
    public T Load<T>(T defaultValue) where T : new()
    {
        lock (Gate)
        {
            try
            {
                if (!File.Exists(_filePath)) return defaultValue;
                var json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<T>(json) ?? defaultValue;
            }
            catch
            {
                // L09：损坏的设置文件隔离备份，避免每次启动都解析失败、历史设置不可恢复。
                try
                {
                    if (File.Exists(_filePath))
                        File.Move(_filePath, _filePath + ".corrupt", overwrite: true);
                }
                catch { /* 隔离失败也不影响返回默认值 */ }
                return defaultValue;
            }
        }
    }

    /// <summary>
    /// 保存设置。写入失败时返回 false 而不抛异常（设置不是关键数据）。
    /// L25：先写临时文件再原子替换，避免写入中途崩溃留下半截 JSON。
    /// </summary>
    public bool Save<T>(T settings)
    {
        lock (Gate)
        {
            var tmp = _filePath + ".tmp";
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(settings, options);
                File.WriteAllText(tmp, json);
                File.Move(tmp, _filePath, overwrite: true);
                return true;
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* 忽略清理失败 */ }
                return false;
            }
        }
    }

    /// <summary>设置文件的完整路径，便于"关于"页展示或排障。</summary>
    public string FilePath => _filePath;
}
