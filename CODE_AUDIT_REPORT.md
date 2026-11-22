# WinTools 代码审计报告

- **审计日期**：2026-11-20
- **审计范围**：`src/` 全部 44 个源码文件（约 6900 行），覆盖 Core / Shell / Services / Tools.File / Tools.Text / Tools.System / Tools.Hello 七个项目，以及全部 `.csproj`、`WinTools.sln`、`Directory.Build.props`、`app.manifest`
- **审计方式**：六个模块并行静态审阅 + 对关键发现（入口异常处理、日志、密码算法、netstat、重命名避让）逐行源码复核
- **审计维度**：安全 / 并发重入 / 资源泄漏 / 错误处理 / 输入验证与边界 / 逻辑与状态 / 代码质量与配置

---

## 一、总体结论

**总体风险评级：中（Medium）。**

项目整体代码质量较高：零第三方依赖、统一 `net10.0-windows` + `Nullable`/`ImplicitUsings`、无远程可利用漏洞、无 SQL/命令注入面、无明确的数据丢失路径。核心安全机制基本到位（密码用 `RandomNumberGenerator` 无模偏差、重命名防覆盖二次校验、哈希流式读取 + `ArrayPool`、JSON 深度受默认 64 限制、剪贴板占用已兜底）。

发现的问题集中在四类：① 插件信任模型与全局异常兜底缺失（影响面最大）；② 多个"失败被当作成功/空闲"的误导性结果；③ 主题切换在自绘控件间的一致性；④ GDI 资源释放依赖 GC。没有"严重（崩溃/数据丢失/远程利用）"级问题。

**问题统计**：高危 2 · 中危 26 · 低危 35（含若干"当前无调用方，暂不可触发"项，已单独标注）。

---

## 二、优先修复建议（Top 8）

| 序 | 问题 | 级别 | 位置 |
|---|---|---|---|
| 1 | 插件 DLL 无签名/白名单校验即执行 | 高 | `ToolDiscovery.cs:44-53,85` |
| 2 | 未注册全局异常处理，未捕获异常静默杀进程 | 高 | `Program.cs:21-28` |
| 3 | `TextLogger` 构造未捕获目录创建异常 → 启动即崩溃 | 中 | `TextLogger.cs:17` + `MainForm.cs:27` |
| 4 | 独立工具窗口 `CreateUI()` 无异常兜底 | 中 | `MainForm.cs:1181,1195` |
| 5 | netstat 失败被误判为"端口空闲" + stderr 未读死锁 | 中 | `PortQuery.cs:74,82-90` |
| 6 | 密码补齐"按类别均匀抽样"致字符分布偏差 | 中 | `PasswordGenerator.cs:45-49` |
| 7 | 超大输入无长度上限、UI 线程同步处理 → OOM/卡死 | 中 | `Base64Codec.cs:39-44`、`JsonFormatter.cs:33-34` |
| 8 | 大小写改名/交换命名被错误加 `(1)` 后缀 | 中 | `RenamePlanner.cs:43,55,86,93` |

---

## 三、高危（High）

### H1. 插件 DLL 无校验加载，等同"目录写权限即代码执行"
- **位置**：`ToolDiscovery.cs:44-53,85`
- **触发**：程序目录被放入（或替换为）名为 `WinTools.Tools.*.dll` 的程序集并实现 `ITool` + `[Tool]`。便携版/解压到用户可写目录时条件成立。
- **后果**：`LoadFromStream` + `Activator.CreateInstance` 在启动时执行构造函数；随后 `CreateUI()`/`Name`/`Id` 等被直接调用。无沙箱、无签名或哈希白名单 → 以当前用户权限任意代码执行。另 `Resolving` 用简单名 `FirstOrDefault` 匹配，可能加载到同名错误版本。
- **建议**：对插件做强名称签名或哈希白名单校验；插件目录固定到受信任位置并校验 ACL。说明书明确"勿将便携版置于可写共享目录"。

### H2. 未注册任何全局异常处理
- **位置**：`Program.cs:21-28`（`Application.Run` 前无任何处理器）；全项目检索无 `ThreadException`/`UnhandledException`/`UnobservedTaskException`。
- **触发**：任一 UI 事件或后台线程抛出未捕获异常（本报告中多处问题可触发，如 H1 之外的构造崩溃、`CreateUI` 崩溃、`ThemeChanged` 订阅者异常）。
- **后果**：进程直接终止，无日志、无提示、无转储，与"日志用于排障"的设计目标冲突，并放大所有其他崩溃类问题。
- **建议**：`Application.Run` 前注册 `Application.ThreadException`、`AppDomain.CurrentDomain.UnhandledException`（可选 `TaskScheduler.UnobservedTaskException`），记录日志并友好提示。

---

## 四、中危（Medium）

### 安全 / 崩溃
- **M1 日志注入（CWE-117）** — `TextLogger.cs:29`；`message` 含 `\n` 即伪造整行，而 `ToolDiscovery.cs:30,58,74,90`、`MainForm.cs:1149` 均把 `ex.Message` 直接拼入。建议写入前 `Replace("\r"," ").Replace("\n"," ")` 并限长。
- **M2 `TextLogger` 构造可致启动崩溃** — `TextLogger.cs:17` 的 `Directory.CreateDirectory` 未捕获；作为 `MainForm` 字段初始化（`MainForm.cs:27`），`%AppData%` 不可用时构造抛出 → 启动即崩（叠加 H2）。建议构造内兜底为禁用日志。
- **M3 独立工具窗口 `CreateUI()` 无兜底** — `MainForm.cs:1181,1195`；对比 `OpenTool` 有 `try-catch`（`1081-1152`）。工具 `CreateUI()` 抛异常或切主题重建抛异常时，经 `ThemeChanged` 分发冒泡 → 崩溃。建议补 `try-catch` 并记录；由 H2 兜底。
- **M4 `ThemeChanged` 广播无异常隔离** — `UiTheme.cs:62,68`；多播委托在首个异常处中断，后续订阅者收不到更新，异常冒泡。建议遍历 `GetInvocationList()` 逐个 `try-catch`。
- **M5 `ThemeChanged` 跨线程 UI 访问** — `UiTheme.cs:64-69` 及各订阅者（`FluentFields.cs:73,201,337,506`、`FluentButton.cs:83`）；`SetTheme` 无线程亲和性约束，回调直接写控件属性且无 `InvokeRequired` 判断。建议封送 UI 线程或强制/断言仅 UI 线程调用。
- **M6 超大输入 OOM / UI 冻结** — `Base64Codec.cs:39-44`（解码峰值内存约输入 3 倍）、`JsonFormatter.cs:33-34`，UI 入口同步执行（`Base64Tool.cs:91-106` 等）；`OutOfMemoryException` 在 catch 之外 → 进程崩溃。建议设 `MaxInputLength`（1–5 MB）超限返回 `Fail`，或流式 + 后台处理。
- **M7 netstat 失败误判"端口空闲"** — `PortQuery.cs:82-90`；退出码未检查，`ParseListeners("")` 返回空 → `Free(port)`，用户得到错误的"安全"结论。建议检查 `ExitCode`，非 0 转 `Error`，纳入 stderr。
- **M8 stderr 重定向未读取，潜在死锁** — `PortQuery.cs:74,79-90`；stderr 写满管道（约 4 KB）后 netstat 阻塞，查询挂起且诊断信息丢失。建议排空 stderr 或关闭重定向。
- **M9 多实例日志写竞争** — `TextLogger.cs:10,30-33`；`Gate` 仅进程内有效，两实例用 `FileShare.Read` 追加时后者抛 `IOException` 被空 catch 吞 → 日志静默丢失。建议命名 `Mutex` 或 `FileShare.ReadWrite` + 重试。
- **M10 密码字符分布偏差（非模偏差）** — `PasswordGenerator.cs:45-49`；补齐阶段先按 `1/类别数` 选类再类内取字符，单字符概率与类大小成反比（默认配置下数字概率约为大写的 3 倍），实际熵低于 `EstimateEntropyBits` 估算。建议改为从合并字符集按字符均匀抽取，保留"每类至少一个"。

### 逻辑 / 功能正确性
- **M11 大小写改名 / 交换命名被错误避让** — `RenamePlanner.cs:43,55,86,93`；判"变化"用 `Ordinal`、占用集合用 `OrdinalIgnoreCase`，`photo.jpg→Photo.jpg` 变成 `Photo(1).jpg`，`A↔B` 交换变成 `A→B(1)`。建议判变化改 `OrdinalIgnoreCase`，避让时将本批次将被移走的源名剔除。
- **M12 撤销部分失败后清空撤销栈** — `RenameTool.cs:266-272`；部分文件恢复失败仍 `lastRecords.Clear()` 且禁用按钮，失去重试机会。建议仅 `errors.Count==0` 时清空，并列出未恢复项。
- **M13 未校验 Windows 保留设备名/尾随空格句点** — `RenamePlanner.cs:84,109-121`；`CON.txt`/`PRN`/`AUX`/`COM1` 等被生成，`File.Move` 抛异常被吞成"跳过"，用户不知根因。建议在 `Sanitize` 拦截并说明。
- **M14 DRY 预览与实际执行分离** — `RenameTool.cs:172,215` + `RenameExecutor.cs:50-59`；预览后目标被外部占用时执行只"跳过"不重新避让，结果与确认的预览不符。建议执行期对冲突项重新 `Disambiguate` 或执行前重算预览。
- **M15 设置页残留搜索态，切主题跳回搜索页** — `MainForm.cs:1213-1263` 未清理 `_keyword`/`_search`，而 `OnThemeChanged:1387` 见 `_keyword>0` 即 `Search()`；从搜索页进设置再切主题会跳回搜索结果。建议 `ShowSettings` 重置搜索态（配合抑制标志防重入）。
- **M16 `ToolHeader` 注入图标后不重排不重绘** — `ToolHeader.cs:50-61,66,70`；`IconName`/`Accent` setter 无副作用，标题仍从 `left=0` 起笔与图标块重叠，图标不立即出现。建议 setter 调 `LayoutChildren(); Invalidate();`。
- **M17 `FluentScrollPanel` 对动态嵌套子控件滚轮失效** — `FluentScrollPanel.cs:48,109-114`；`HookWheel` 只挂接加入瞬间的后代，之后往 Panel 内加的孙级控件不冒泡滚轮 → 页面不滚动。建议监听后代 `ControlAdded` 递归挂接（并在移除时退订）。
- **M18 多处主题切换配色残留** — `CardPanel.cs:20,32,54,74`、`Chip.cs:14,22,23`、`ToolHeader.cs:28,34,42`、`ToolCard.cs:55,66,69,121`、`FluentScrollPanel.cs:44-45,52`；控件构造时快照 `UiTheme.X` 且未订阅/不完整响应 `ThemeChanged`，切主题后与已刷新的按钮/输入框色板不一致。建议订阅主题广播重设颜色并重绘，`ToolCard` 重算 `_accent`。
- **M19 GDI `Region` 替换未释放旧对象（5 处）** — `FluentFields.cs:112,249,549`、`UiTheme.cs:427`、`MainForm.cs:165`；每次 `Resize` 累积一个 `Region` 句柄，配额紧张时触发 GDI 异常。建议 `old?.Dispose()`。
- **M20 大文件哈希无法取消** — `HashTool.cs:136-191,161` + `FileHasher.cs:26`；`ComputeAsync` 暴露 `cancellationToken` 但始终传 `default`，计算期间所有按钮禁用且无取消入口。建议加 `CancellationTokenSource` 与取消按钮。
- **M21 输入即时预览同步枚举目录阻塞 UI** — `RenameTool.cs:157-201` + `RenamePlanner.cs:145-146`；4 个事件每次触发都同步 `EnumerateFileSystemEntries().ToList()`，大目录/网络盘逐字符卡顿。建议防抖 200–300 ms 或缓存目录快照（异步化需 token 防旧结果覆盖）。

---

## 五、低危（Low）

> 说明：标「※不可触发」项经全项目检索确认 `JsonSettingsService` 当前无任何调用方，属潜在缺陷，接入后应升级。

**安全**
- `L01` netstat.exe 用相对文件名，PATH/当前目录劫持（`PortQuery.cs:71`）— 用 `Environment.SystemDirectory` 绝对路径
- `L02` 系统信息含计算机名并复制到全局剪贴板（`SystemInfoCollector.cs:26`）— 非必要可不收集
- `L03` 设置文件名未校验致路径遍历（`JsonSettingsService.cs:14,19`）※不可触发 — `Path.GetFileName` + 白名单
- `L04` Base64 非 UTF-8 静默替换 U+FFFD，往返失真（`Base64Codec.cs:23,44`）— 改用 `throwOnInvalidBytes` UTF8 编码

**可靠性 / 错误处理**
- `L05` `async void` 事件处理器（`HashTool.cs:210,218`；`PortQueryTool.cs:138-144`）— 包成返回 `Task` 的方法并兜底
- `L06` netstat 超时 `Kill()` 后未 `WaitForExit()`（`PortQuery.cs:83-89`）— 补等待
- `L07` `WaitForExit(1000)` 返回值忽略（`PortQuery.cs:89`）— 超时则 Kill+Wait
- `L08` 空 catch 完全静默（`TextLogger.cs:35-38`；`JsonSettingsService.cs:31-34,46-49`）— 至少输出到调试器
- `L09` 损坏设置文件未隔离（`JsonSettingsService.cs:31-34`）※不可触发 — 改名为 `.corrupt.json` 备份
- `L10` 等长内容改写检测失效（`FileHasher.cs:72-75`）— 复核 `LastWriteTimeUtc`/`Length` 或改注释
- `L11` 哈希外层 catch-all 致未完成行停留"计算中"（`HashTool.cs:181-184`）— 单文件粒度 try/catch
- `L12` `WaitCursor` 无 `try/finally`（`RenameTool.cs:234-237`）— 包 finally 恢复
- `L13` 异常信息丢失类型/路径上下文（`RenameExecutor.cs:64-67,112-115`）— 含 `ex.GetType().Name` + 路径
- `L14` 确认框弹出期间其他控件仍可操作（`RenameTool.cs:215,224-232`）— 弹框期间禁用规则区
- `L15` 日志无大小上限/轮转、消息无长度限制（`TextLogger.cs:28-32`）— 阈值滚动 + 截断

**逻辑 / 状态**
- `L16` 搜索 `TextChanged` 与导航刷新递归，重复构建整页（`MainForm.cs:307-312,846,958`）— 抑制标志
- `L17` 收藏/最近页取消收藏后列表不刷新（`MainForm.cs:1025-1034`）— 当前为收藏页时刷新
- `L18` 同一端口多监听地址只保留最后 PID（`PortQuery.cs:132`）— 改 `HashSet<int>` 返回全部
- `L19` 密码熵估算短密码边界高估（`PasswordGenerator.cs:65-80`）— 修正"每类必含"约束后的组合数
- `L20` `Generate` 隐含依赖 `MinLength≥类别数`（`PasswordGenerator.cs:34,39-49`）— 加校验 `length < sets.Count`
- `L21` 越界长度静默钳制（`PasswordGenerator.cs:34`）— 越界返回 `Fail`
- `L22` `ITool`/`ToolMeta` 公共方法未校验 null/重复引用（`ToolMeta.cs:58-66`、`IconFactory.cs:506,512`）— 入口判空/去重
- `L23` `FluentNumeric` 极小高度产生负矩形（`FluentFields.cs:543-544,586`）— `Math.Max(0,…)` 夹住
- `L24` `FeedbackBar` 高度缓存不随字体变化失效（`FeedbackBar.cs:22-23,54-80`）— DPI 变化时失效缓存
- `L25` 设置写入非原子（`JsonSettingsService.cs:44`）※不可触发 — 临时文件 + `Move(overwrite)`
- `L26` 设置 Save 失败无反馈 / 默认值回退语义不完整（`JsonSettingsService.cs:29,46-49`）※不可触发
- `L27` `StartIndex + i` 整数溢出（`RenamePlanner.cs:59`）— `checked` 或 `long`
- `L28` 符号链接/junction 未显式识别（`RenameExecutor.cs:50-61`）— 按需求拒绝或告警

**样式 / 交互**
- `L29` `FluentButton` 焦点来源静态共享，实例间串味（`FluentButton.cs:17,95,104,110`）— 改实例字段
- `L30` 部分可绘属性 setter 不触发重绘（`Chip.cs:36`；`FluentButton.cs:71-81`）— setter 加 `Invalidate()`
- `L31` 主题设置未持久化（`UiTheme.cs:58` + 设置服务未接入）— 接入持久化
- `L32` 搜索无防抖、`_search` 无 `MaxLength`（`MainForm.cs:298-312`）— 加 150–250 ms 防抖 + 上限

**资源 / 性能**
- `L33` ThemeChanged 静态事件强引用，控件移除未 Dispose 则不回收、`Dispose(false)` 不退订（多控件）— 弱事件或确保移除即 Dispose
- `L34` 字体缓存与重建不释放旧 Font（`UiTheme.cs:249-282`）、`HashTool.cs:47` glyphFont 未释放 — 释放旧 Font
- `L35` 进度 Report 风暴（`FileHasher.cs:58,68`）、OnPaint 重复分配绘制对象（`IconFactory.cs`、`UiTheme.cs:435-456`）、搜索/切换导航重复全量重建（`MainForm.cs:542-550`）— 节流/缓存/按变化重绘

**配置 / 部署**
- `L36` 发布依赖预编译 bin + 宽泛通配（`WinTools.Shell.csproj:37-44`）— 发布前清理构建，收窄 Include
- `L37` `app.manifest` 版本 `0.1.0.0` 与程序集 `0.1.2.0` 不一致 — 统一版本号

**代码质量（未单列级）**
- `L38` 页面构建样板重复（`ShowList`/`Search`/`ShowSettings` 的 `Reflow+Resize+SwapContent` 几乎相同）、`AlignBar`/`Copy` 在三处文本工具重复 — 抽取公共辅助
- `L39` 魔法数字散落（品牌宽 210、navFoot 高 116、徽章上限 99、Toast 时长 3400/3600、超时 5s/1000ms 等）— 统一为常量/主题令牌
- `L40` `IconFactory` 大量未具名比例系数（0.42/0.58/0.12）— 命名常量

---

## 六、正面确认（无问题，避免误报）

- **无命令/参数注入**：端口查询 `Arguments` 硬编码为 `-ano -p tcp`，端口仅本地 `TryParsePort` 后用于查字典。
- **无 modulo bias**：密码全部用 `RandomNumberGenerator.GetInt32(n)`（内部拒绝采样）。
- **无栈溢出**：`JsonDocument.Parse` 默认 `MaxDepth=64`，超深嵌套在解析期抛 `JsonException` 被捕获。
- **无路径逃逸/覆盖**：重命名写操作收敛到 `RenameExecutor`，`File.Move` 不用 overwrite 且执行前二次校验；哈希用 `await using FileStream` + `using IncrementalHash` + `ArrayPool`，资源释放完备。
- **无第三方依赖漏洞面**：7 个 csproj 均无 `PackageReference`；`TargetFramework`/`Nullable`/`ImplicitUsings` 配置一致；`app.manifest` 为 `asInvoker`（不默认提权）。
- **并发控制基本到位**：端口查询有 `busy` 标志 + `Enabled=false` + `finally` 复位；哈希/端口的 `await` 经 `WindowsFormsSynchronizationContext` 回 UI 线程；`SwapContent`/`OnFormClosed` 有成对的 Dispose/退订。

---

*本报告为静态审计结论，未修改任何源码；如需，可按"优先修复建议"逐项落地并回归验证。*
