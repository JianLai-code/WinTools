# WinTools 审计问题修复实施报告

- **实施日期**：2026-11-20
- **对应审计**：`CODE_AUDIT_REPORT.md`
- **原则**：按危险程度从高到低逐项修复；每项只做必要改动；保持原有功能与既有验收不变。
- **验证结果**：全量编译 **0 警告 / 0 错误**；测试 **92 项全部通过**（原 81 项 + 新增 11 项回归测试）。

---

## 一、已修复清单（按危险等级排序）

### 高危（2/2 已修复）

| 编号 | 问题 | 修复方式 | 影响范围 |
|---|---|---|---|
| H1 | 插件 DLL 无校验加载，任意代码执行 | 加载前规范化路径并限定在应用目录内（挡 `..\` 逃逸）；新增可选 `tools.allowlist.txt` 白名单，**文件不存在时行为与修复前完全一致**；`Resolving` 同名匹配改为精确程序集名 | 工具发现机制，正常工具加载不受影响 |
| H2 | 无全局异常处理，异常静默终止进程 | 注册 `Application.ThreadException`、`AppDomain.UnhandledException`、`TaskScheduler.UnobservedTaskException` 三处兜底，统一写日志 | 全进程；不改变正常流程 |

### 中危（20/21 已修复，1 项按安全红线保留）

| 编号 | 问题 | 修复方式 |
|---|---|---|
| M1 | 日志注入 CWE-117 | 写入前净化 `\r`/`\n`，单条截断 4000 字符 |
| M2 | `TextLogger` 构造失败致启动崩溃 | 构造内 `try-catch`，失败降级为禁用日志 |
| M3 | 独立窗口 `CreateUI()` 无兜底 | 创建与主题重建两处加 `try-catch`，异常记录日志并提示，不冒泡 |
| M4 | `ThemeChanged` 广播无异常隔离 | 逐个订阅者 `try-catch`，一个失败不影响其余 |
| M5 | 主题广播跨线程 UI 风险 | 检测到非 UI 线程时 `BeginInvoke` 封送 |
| M6 | 超大输入 OOM/卡死 | Base64 上限 2 MB、JSON 上限 5 MB，超限返回友好失败 |
| M7 | netstat 失败误判"端口空闲" | 检查 `ExitCode`，非 0 抛错转 `Error` 状态，纳入 stderr 详情 |
| M8 | stderr 未读致死锁 | `BeginErrorReadLine` 排空并收集诊断信息 |
| M9 | 多实例日志写竞争 | 命名 `Mutex` 跨进程同步 + `FileShare.ReadWrite` |
| M10 | 密码字符分布偏差 | 补齐阶段从合并字符集均匀抽取（保留"每类至少一个"） |
| M11 | 大小写改名/交换命名误加 `(1)` | 重构为两阶段避让：先把"将发生变化的源文件名"从占用集合移除，再统一避让 |
| M12 | 撤销部分失败清空栈 | 仅全部成功才清空；部分失败保留未恢复项供重试 |
| M13 | 保留设备名写入失败 | `IsUsableBaseName` 拦截 `CON/PRN/COM1…` 与尾随空格/句点，回退原名并说明 |
| M14 | DRY 预览与执行一致性 | **保留不改**：执行期防覆盖二次校验是安全红线，已有 `Execute_TargetExists_NeverOverwrites` 测试锁定；自动重新避让会破坏"绝不覆盖"保证。现有 UI 已逐条反馈跳过项 |
| M15 | 设置页残留搜索态致切主题跳页 | 新增 `ClearSearch()`，进设置页清理搜索态 |
| M16 | `ToolHeader` 注入图标后不重排/重绘 | `IconName`/`Accent` setter 改为触发 `LayoutChildren()`+`Invalidate()` |
| M17 | 滚动面板动态嵌套子控件滚轮失效 | `HookWheel` 递归监听任意层级 `ControlAdded`，`HashSet` 防重复挂接 |
| M18 | 多处主题切换配色残留 | `ToolHeader`/`CardPanel`/`Chip`/`ToolCard`/`FluentScrollPanel` 订阅主题广播同步各自颜色（保留调用方自定义色） |
| M19 | GDI `Region` 未释放（5 处） | 新增 `UiTheme.SetRegion()` 统一释放旧对象后替换 |
| M20 | 大文件哈希无法取消 | 新增"取消"按钮 + `CancellationTokenSource`，token 传入 `ComputeAsync`，取消时清理半成品行 |
| M21 | 重命名输入逐字符阻塞 UI | 规则输入改 250 ms 防抖预览；Timer 随控件生命周期释放 |

### 低危（已修复 10 项，其余经评估保留）

| 编号 | 问题 | 处理 |
|---|---|---|
| L01 | netstat 相对路径劫持 | ✅ 改 `Environment.SystemDirectory` 绝对路径 |
| L03 | 设置文件名路径遍历 | ✅ `Path.GetFileName` 取 basename |
| L04 | Base64 非 UTF-8 静默替换 | ✅ 严格 UTF-8 编码，非法序列返回失败 |
| L05 | `async void` 异常失控 | ✅ HashTool 两处加兜底；PortQueryTool 内部已有完整 try/catch + H2 全局兜底，保留 |
| L06/L07 | netstat 超时/退出处理 | ✅ `Kill` 后 `WaitForExit`，检查 `WaitForExit` 返回值 |
| L08 | 空 catch 完全静默 | ✅ 日志失败保留 `Debug.WriteLine` 诊断线索 |
| L09 | 损坏设置文件未隔离 | ✅ 解析失败改名为 `.corrupt` 备份 |
| L13 | 异常信息缺类型/路径 | ✅ 错误串含 `ex.GetType().Name` 与完整路径 |
| L15 | 日志无轮转/无上限 | ✅ 5 MB 轮转，保留 3 份历史 |
| L16 | 搜索重入重复建页 | ✅ 抑制标志 + `ClearSearch()` |
| L19 | 熵估算高估 | ✅ 按"每类必含"约束修正 |
| L20 | 长度<类别数脆弱 | ✅ 加 `length = max(length, sets.Count)` 防御 |
| L23 | `FluentNumeric` 负高度矩形 | ✅ `Math.Max(0,…)` 夹住 + OnPaint 保护 |
| L25 | 设置非原子写入 | ✅ 临时文件 + `File.Move(overwrite)` |
| L26 | 设置 Save 失败无反馈 | ✅ 返回 `bool` |
| L27 | 序号整数溢出 | ✅ `checked` + 溢出夹到 `int.MaxValue` |
| L30 | 可绘属性 setter 不重绘 | ✅ `FluentButton`/`Chip` 相关 setter 加 `Invalidate()` |
| L34 | 字体 GDI 句柄累积 | ✅ DPI 变化时释放旧字体与缓存 |
| L37 | manifest 版本不一致 | ✅ 统一为 `0.1.2.0` |

### 经评估保留（不改，附理由）

| 编号 | 问题 | 保留理由 |
|---|---|---|
| L02 | 系统信息含计算机名 | 系统信息工具的设计功能，删除即破坏原有功能 |
| L14 | 确认框期间其他控件可操作 | 计划已在弹框前生成，不影响本次执行结果；改模态确认成本高于收益 |
| L21 | 越界长度静默钳制 | `Generate_LengthOutOfRange_ClampsToBounds` 测试明确锁定该行为，改 Fail 会破坏验收 |
| L22 | 公共方法 null 校验 | 全部调用点均已判空/来自受控 UI，无实际触发路径 |
| L24 | FeedbackBar 高度缓存 | 仅 DPI 运行时变化触发；本项目 DPI 只在启动设一次，不可触发 |
| L28 | 符号链接未识别 | 防覆盖二次校验已挡住对链接目标的覆盖；改拒绝策略会改变工具用途 |
| L29 | `FluentButton` 焦点静态字段 | `static _keyboardInput` 是有意的"全局最近输入模式"追踪：Tab 导航时 B 需读到 A 的按键状态才能画焦点环；改实例字段会引入键盘焦点环 bug |
| L31 | 主题未持久化 | 属功能增强，超出"仅做必要改动"范围 |
| L32 | 搜索无防抖 | 搜索页重建开销可控，且防抖会改变"逐字即时搜索"的既有交互 |
| L33 | 静态事件强引用 | 现有控件均在 `Dispose(false)` 前退订，`SwapContent`/`OnFormClosed` 路径完整 |
| L35 | OnPaint 对象分配/进度风暴 | 对象均 `using` 释放，不构成泄漏；节流会改变进度反馈粒度 |
| L36 | 发布依赖 bin 通配 | 现用精确路径 + 排除 Tests，是插件发现的必要机制 |
| L38–L40 | 重复代码/魔法数字/未具名系数 | 纯代码风格重构，改动面大且与"仅做必要改动"冲突 |

---

## 二、修改文件汇总（22 个文件）

**Shell（4）**：`Program.cs`、`ToolDiscovery.cs`、`MainForm.cs`、`app.manifest`
**Services（2）**：`TextLogger.cs`、`JsonSettingsService.cs`
**Core UI（8）**：`UiTheme.cs`、`ToolHeader.cs`、`CardPanel.cs`、`Chip.cs`、`ToolCard.cs`、`FluentButton.cs`、`FluentFields.cs`、`FluentScrollPanel.cs`
**Tools.File（4）**：`RenamePlanner.cs`、`RenameExecutor.cs`、`RenameTool.cs`、`HashTool.cs`
**Tools.Text（3）**：`Base64Codec.cs`、`JsonFormatter.cs`、`PasswordGenerator.cs`
**Tools.System（1）**：`PortQuery.cs`
**Tests（新增 1）**：`AuditRegressionTests.cs`（11 项回归测试）
**文档（新增 2）**：`CODE_AUDIT_REPORT.md`、`AUDIT_FIX_REPORT.md`

---

## 三、建议的验证方式

### 1. 自动化（已执行，可重复）
```bash
cd D:/Desktop/WinTools
dotnet build WinTools.sln -c Debug -v minimal   # 期望：0 警告 / 0 错误
dotnet test  WinTools.sln -c Debug --no-build   # 期望：92 项全部通过
```

### 2. 针对性手工验证（建议按序）
1. **H2/H1**：启动程序无异常；在应用目录放一个伪造的 `WinTools.Tools.*.dll`，确认被拒绝并写日志；创建 `tools.allowlist.txt` 只列白名单 DLL，确认未列出的不加载。
2. **M1/M2/M9**：制造一个含换行的异常消息，检查日志不会伪造新行；临时把 `%AppData%\WinTools\logs` 设为只读文件，确认程序仍能启动；开两个实例同时操作，确认日志不丢。
3. **M6**：在 Base64/JSON 工具粘贴超大文本，确认提示"内容过长"而非卡死。
4. **M7/M8**：用防火墙或策略让 netstat 失败，确认显示"查询失败"而非"端口空闲"。
5. **M10**：连续生成 100 次密码，统计各字符出现频率应大致均匀。
6. **M11–M13**：批量重命名中测试：`photo.jpg→Photo.jpg`（不应出现 `(1)`）、`1.txt→2.txt/2.txt→3.txt` 链式、用 `CON` 作模板（应回退原名）；执行一次再撤销，制造一个文件被占用导致部分失败，确认可再次撤销重试。
7. **M15/L16**：搜索关键词后进设置页，切主题，确认停留在设置页且搜索框已清空。
8. **M17/M18**：在动态填充的面板上滚轮应滚动页面；切换明暗主题，确认卡片/标题/徽章/滚动区配色一致无残留。
9. **M19**：反复缩放主窗口，观察 GDI 对象计数（任务管理器"句柄数"）不应持续增长。
10. **M20**：对大文件开始哈希后点"取消"，确认停止、无"计算中…"残行、可再次选择文件。
11. **L04**：解码一段非 UTF-8 字节的 Base64，应提示失败而非乱码。

### 3. 回归重点
既有 81 项测试是"功能不变"的基线，本次 92 项全绿说明核心验收（F1 哈希、F2 重命名、T1 JSON、T2 Base64、T4 密码、S2 端口、程序集发现）均未被破坏。
