using System.Reflection;
using System.Runtime.Loader;
using WinTools.Core;
using WinTools.Services.Logging;

namespace WinTools.Shell;

/// <summary>
/// 工具发现器：扫描程序目录下所有 WinTools.Tools.*.dll，
/// 找出带 [Tool] 特性且实现 ITool 的类型并实例化。
/// 这是"新增工具不改宿主"的关键——Shell 运行时才知道有哪些工具。
///
/// 采用"读入内存 + 独立 AssemblyLoadContext"方式加载，不锁定 DLL 文件，
/// 因此工具 DLL 可以被删除/替换（便于反证验证与后续热更新）。
/// </summary>
public static class ToolDiscovery
{
    public static IReadOnlyList<ITool> DiscoverFromDirectory(string appDirectory, TextLogger? logger = null)
    {
        var found = new List<(int Order, ITool Tool)>();
        const string pattern = "WinTools.Tools.*.dll";

        string[] dlls;
        try
        {
            dlls = Directory.GetFiles(appDirectory, pattern);
        }
        catch (Exception ex)
        {
            logger?.Error($"扫描工具目录失败: {ex.Message}");
            return Array.Empty<ITool>();
        }

        // 受信任目录的绝对路径：只加载规范化后仍位于应用目录内的 DLL，挡住 ..\ 路径逃逸。
        var trustedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(appDirectory));

        // 可选白名单：仅当应用目录下存在该清单时启用，清单内容是允许加载的 DLL 文件名（每行一个）。
        // 文件不存在时行为与修复前完全一致——因此不影响现有工具与现有部署。
        var allowListPath = Path.Combine(appDirectory, "tools.allowlist.txt");
        HashSet<string>? allowList = null;
        if (File.Exists(allowListPath))
        {
            allowList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in File.ReadAllLines(allowListPath))
            {
                var name = line.Trim();
                if (name.Length > 0 && !name.StartsWith('#')) allowList.Add(name);
            }
        }

        foreach (var dll in dlls)
        {
            // 跳过测试程序集（命名约定：工具以 WinTools.Tools. 开头，但 *.Tests 不是工具）。
            var fileName = Path.GetFileName(dll);
            if (fileName.EndsWith(".Tests.dll", StringComparison.OrdinalIgnoreCase)) continue;

            // 白名单存在时，未在清单内的 DLL 一律不加载。
            if (allowList is not null && !allowList.Contains(fileName))
            {
                logger?.Warn($"工具不在白名单内，已拒绝加载: {fileName}");
                continue;
            }

            // 目录边界校验：规范化完整路径必须位于受信任应用目录之内。
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(dll);
            }
            catch (Exception ex)
            {
                logger?.Warn($"无法解析工具路径，已拒绝加载: {fileName} - {ex.Message}");
                continue;
            }
            if (!fullPath.StartsWith(trustedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                logger?.Warn($"工具路径越出应用目录，已拒绝加载: {fileName}");
                continue;
            }

            Assembly asm;
            try
            {
                // 先把 DLL 读进内存，再用独立上下文加载——不锁定源文件。
                var image = File.ReadAllBytes(fullPath);
                using var stream = new MemoryStream(image);
                var context = new AssemblyLoadContext($"ToolLoad-{Path.GetFileName(dll)}", isCollectible: false);

                // 关键：工具依赖的共享程序集（如 WinTools.Core）必须从默认上下文复用，
                // 否则独立上下文会加载出"另一份"Core，导致 ITool 类型不兼容、工具被跳过。
                context.Resolving += (_, name) => AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == name.Name);

                asm = context.LoadFromStream(stream);
            }
            catch (Exception ex)
            {
                // 坏 DLL 不应导致整个程序崩溃，记录后跳过。
                logger?.Warn($"加载工具程序集失败，已跳过: {Path.GetFileName(dll)} - {ex.Message}");
                continue;
            }

            Type[] types;
            try
            {
                types = asm.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                // 部分类型依赖缺失时，取能加载的那些，而不是整体放弃。
                types = ex.Types.Where(t => t is not null).Cast<Type>().ToArray();
            }
            catch (Exception ex)
            {
                logger?.Warn($"读取工具类型失败，已跳过: {Path.GetFileName(dll)} - {ex.Message}");
                continue;
            }

            foreach (var type in types)
            {
                if (type.IsAbstract || !typeof(ITool).IsAssignableFrom(type)) continue;
                if (type.GetCustomAttribute<ToolAttribute>() is not { } attr) continue;

                try
                {
                    var tool = (ITool)Activator.CreateInstance(type)!;
                    found.Add((attr.Order, tool));
                }
                catch (Exception ex)
                {
                    logger?.Warn($"实例化工具失败，已跳过: {type.FullName} - {ex.Message}");
                }
            }
        }

        return found.OrderBy(x => x.Order)
                    .ThenBy(x => x.Tool.Name)
                    .Select(x => x.Tool)
                    .ToList();
    }
}
