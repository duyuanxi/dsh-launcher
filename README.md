# DeepSeek Harness 启动器（DSH Launcher）

Windows 原生图形界面程序（C# / WPF，.NET 5，零第三方运行时依赖），把「启动 DeepSeek Harness」变成一键操作，并提供开机自启、守护、托盘、日志、备份、更新等完整生命周期管理。

## 下载

最新发布（预构建 zip）见 [Releases](https://github.com/duyuanxi/dsh-launcher/releases)：

- `DshLauncher-vX.Y.Z-win-x64.zip`：解压后把内容放到 `%LOCALAPPDATA%\DSHLauncher\`，双击 `DshLauncher.exe` 即可（需已装 .NET 5 运行时）。
- `DshLauncher-vX.Y.Z-win-x64-selfcontained.zip`：自包含单文件版，免装 .NET。

前置依赖：Windows 10/11、Node.js ≥ 22.15（会话识别用到内置 zstd）、全局安装 `@deepseek-ai/dsh`（`npm install -g @deepseek-ai/dsh`）。

## 功能

- **一键启动**：双击桌面快捷方式（`--start`）即拉起 `dsh web` 并打开浏览器。
- **启动 / 停止 / 重启**：完整控制 harness 进程树。
- **运行状态与健康检查**：轮询监听端口，实时显示「已停止 / 启动中 / 运行中 / 异常」。
- **开机自启**：写入 `HKCU\...\Run`（按用户登录自启，无需管理员），支持静默启动与延迟 N 秒。
- **守护模式**：harness 崩溃后指数退避自动重启（3s → 60s 封顶）。
- **最小化到系统托盘**：关窗/最小化退到托盘，托盘菜单可快速控制。
- **端口 / 主机可配置**：图形界面改监听端口与绑定地址（`0.0.0.0` 因 dsh 安全限制被禁止）。
- **日志查看**：实时 tail dsh 输出，日志落在 `%LOCALAPPDATA%\DSHLauncher\logs\dsh.log`。
- **自动更新 dsh**：检查版本 + 一键 `npm install -g @deepseek-ai/dsh@latest`。
- **插件管理**：列出已加载插件（标注「内置/插件/残留/未加载」+ 版本）、安装、卸载、重装依赖、扫描并清理卸载残留。
- **一键自动修复**：一键执行「检查 dsh 安装 → 清理卸载残留 → 重装依赖 → 修复 koffi 原生模块」，每步结果与输出明细呈现，完成后托盘通知。
- **一键备份 / 恢复**：把 `%USERPROFILE%\.dsh` 打包为 zip（排除 node_modules/cache），可从备份恢复（带 zip-slip 防护）。
- **快捷打开最近会话 / 工作区**：解析 `.dsh\sessions` 下的会话日志（含 zstd 压缩格式），显示「时间 + 会话标题 + 工作区」。
- **桌面通知**：托盘气泡提示启动/崩溃/备份等事件。
- **单实例锁**：重复启动只聚焦已有实例。

## 构建

前置：.NET 5 SDK（含 WPF/WindowsDesktop 工作负载）。

```powershell
dotnet build DshLauncher.sln -c Release
dotnet test  DshLauncher.sln -c Release      # 41 个单元测试
dotnet publish src\DshLauncher\DshLauncher.csproj -c Release -o publish
```

## 部署

```powershell
# 1. 固定安装 dsh 到全局 npm（摆脱 npx 缓存，开机自启必需）
npm install -g @deepseek-ai/dsh

# 2. 部署到稳定目录
Copy-Item -Recurse -Force publish\* "$env:LOCALAPPDATA\DSHLauncher\"

# 3. 启动（双击 DshLauncher.exe 或桌面快捷方式）
& "$env:LOCALAPPDATA\DSHLauncher\DshLauncher.exe" --start
```

命令行参数：

- （无参数）：打开图形界面。
- `--start`：打开界面并立即启动 harness（桌面快捷方式用此参数）。
- `--autostart`：静默启动（配合「开机静默启动」，隐藏到托盘并后台拉起 harness）。

## 目录结构

```
src/DshLauncher/              # WPF 应用
  App.xaml(.cs)               # 单实例、--start/--autostart 参数、事件唤醒
  MainWindow.xaml(.cs)        # 主界面 + 托盘
  Services/
    AppSettings.cs            # 配置模型
    SettingsStore.cs          # config.json 读写
    AppPaths.cs               # 稳定路径
    DshResolver.cs            # 定位全局 node/dsh
    DshCommandBuilder.cs      # 构造 dsh argv / URL
    DshProcessManager.cs      # 派生/监控/终止 dsh 子进程
    DshController.cs          # 状态机 + 健康轮询 + 守护
    HealthChecker.cs          # 端口探测 / URL 解析
    AutostartManager.cs       # HKCU Run 键
    BackupManager.cs          # .dsh 打包/恢复
    Updater.cs / SemVer.cs    # 版本读取/比较
    SessionScanner.cs         # 会话文件枚举（jsonl / jsonl.zstd）
    SessionTitleReader.cs     # 调 node 辅助脚本读取会话标题
    Notifier.cs               # 托盘气泡
    HarnessDetector.cs        # 端口占用者识别（是否运行中的 harness）
    PluginManager.cs          # 插件清单读取/分类/清理残留
    RepairRunner.cs           # 一键修复序列（检查安装/清残留/重装依赖/修 koffi）
  session-reader.mjs          # 内置 node 脚本：zstd 逐帧解压 + 会话标题提取
src/DshLauncher.Tests/        # xUnit 单元测试
```

## 说明

- 配置文件：`%LOCALAPPDATA%\DSHLauncher\config.json`。
- 数据 / 日志 / 备份目录：`%LOCALAPPDATA%\DSHLauncher\`（logs / backups）。
- 程序以「生命周期所有者」身份管理 harness：托盘「退出」会同时停止 harness。
- 通知暂用托盘气泡（零依赖），可后续升级为原生 Windows Toast。
