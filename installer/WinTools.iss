; WinTools 轻量级工具箱 — Inno Setup 安装脚本
; 目标：双击 → 下一步 → 完成，普通权限安装，含中文界面与卸载项
; 配套 Inno Setup 7.1.x（64 位）；脚本语法兼容 6.x
; 版本与 src/WinTools.Shell/WinTools.Shell.csproj 的 <Version> 保持一致

#define MyAppName "WinTools 轻量级工具箱"
#define MyAppVersion "0.8.1"
#define MyAppPublisher "WinTools"
#define MyAppExeName "WinTools.Shell.exe"
; 发布目录可覆盖：宿主正在运行时 publish 下的 DLL 会被锁定，
; 可用 ISCC /dPublishDir=..\publish-xxxx 从另一份发布目录打包。
#ifndef PublishDir
#define PublishDir "..\publish"
#endif

[Setup]
; AppId 一旦发布切勿修改：改了会被当成另一个软件，无法覆盖升级
AppId={{8F3C2A1E-5D7B-4A6F-9C31-2E8B7A4D1F60}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\WinTools
DefaultGroupName={#MyAppName}
OutputDir=..\installer-output
OutputBaseFilename=WinTools-{#MyAppVersion}-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; 最小权限：安装到用户可写目录，全程无 UAC 弹窗（阶段五最小权限原则）
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
VersionInfoVersion={#MyAppVersion}
VersionInfoProductName={#MyAppName}
DisableProgramGroupPage=yes

[Languages]
; 简体中文安装界面：语言包随脚本放在本目录（不再依赖 Inno 安装位置，换机可编译）
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："

[Files]
; 方案 A（PublishSingleFile=false）：publish 目录里是 exe + 所有运行时 DLL + 工具 DLL。
; 用一条通配递归包含全部文件与语言子目录（cs/de/es/zh-Hans 等），排除调试符号减小体积。
; 工具 DLL（WinTools.Tools.*.dll）也在其中，运行时由 ToolDiscovery 反射发现。
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{group}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "立即运行 WinTools"; Flags: nowait postinstall skipifsilent

; ── 编译前检查 ────────────────────────────────────────────────
; 1) 先执行发布：见 installer/发布说明.md 或 docs/阶段八 4.1 节，
;    产物输出到仓库根 publish\ 目录（本脚本按方案 A，PublishSingleFile=false）。
; 2) 确认 publish\ 下含 WinTools.Shell.exe、WinTools.Tools.*.dll 及大量 *.dll。
; 3) 若报缺 ChineseSimplified.isl：从 Inno Setup 官网下载「Additional Language files」
;    中的 ChineseSimplified.isl，放到 Inno Setup 安装目录 Languages\ 下重新编译。
; 4) 编译：ISCC.exe installer\WinTools.iss，产物在 installer-output\。
; ─────────────────────────────────────────────────────────────
