# Windows 版开发、测试与发布

## 图标

- 源文件：`branding/source/icon.svg`
- 生成：`cd branding && npm install && npm run generate`
- Windows 使用：`windows/HarnessPortable.Windows/Assets/HarnessPortable.ico`

## 技术栈

- .NET 10（WPF）
- WebView2（系统浏览器内核）
- SSH.NET（SSH 本地端口转发；公钥优先，密码可选，认证失败不重试）
- Velopack（安装包与自动更新）
- Windows DPAPI（密码加密）
- JSON 文件配置（`%APPDATA%\HarnessPortable\`）
- 支持**同时连接多个 SSH 隧道**，每个隧道独立重连/停止
- 单一工作区窗口：隧道与直连聚合为**标签页**，支持拖拽标签到窗口边缘进行**左右/上下分屏**
- 右键标签可**复制标签**：同一隧道/直连可开多个独立 WebView 标签（如网页内切换不同对话）
- 右键标签可**重命名**，自定义 label 随布局预设保存
- 右键标签可**四向拆分**（复制当前标签到左/右/上/下新 pane），递归组合出三列/2×2/不规则布局
- 右键标签可**切换为其他隧道/直连**，在原布局位置替换当前标签
- **布局预设**：保存当前分屏结构、每个标签对应的隧道/直连，以及拖动后的**窗口比例**，顶部下拉一键切换
- **F11 全屏**：按显示器物理边界覆盖任务栏（含 DPI 换算），Esc 退出
- 关闭主窗口默认**直接退出**；管理页“设置”里可改为“最小化到托盘”
- 启动时默认**自动恢复上次布局**（有预设时直接进入工作区，不显示主界面）
- 关闭标签不停止隧道；停止隧道才关闭对应标签
- DeepSeek Harness 风格 UI：黑底主按钮、白底描边按钮、描边 pill 标签
- 支持**直连多开**：每次点击直连项都会新开一个标签

## 目录

```text
windows/
├─ HarnessPortable.Windows/        # WPF 应用
├─ HarnessPortable.Windows.Tests/  # xUnit 单元测试
└─ HarnessPortable.Windows.slnx
```

## 构建

```powershell
dotnet build windows/HarnessPortable.Windows.slnx -c Release
```

## 测试

```powershell
dotnet test windows/HarnessPortable.Windows.slnx -c Release
```

## 运行（开发）

```powershell
dotnet run --project windows/HarnessPortable.Windows/HarnessPortable.Windows.csproj
```

或直接运行：

```text
windows/HarnessPortable.Windows/bin/Debug/net10.0-windows/HarnessPortable.exe
```

## 版本与更新日志

仓库根目录：

- `VERSION` —— 当前版本（如 `2.5.0`），Windows 程序集版本与 Android `versionName` 都读它
- `CHANGELOG.md` —— 按 `## x.y.z` 分段；打包时把当前版本那一段写入 Velopack 包

## 发布（Velopack，推荐分发）

不再使用单文件 exe。`vpk pack` 把 self-contained 目录打成安装包和更新源：

```text
HarnessPortable-win-Setup.exe      # 给用户的安装程序
HarnessPortable-win-Portable.zip   # 便携包
HarnessPortable-<ver>-full.nupkg   # 更新包
releases.win.json                  # 更新索引
```

安装后程序在 `%LocalAppData%\HarnessPortable\current\`，设置仍在 `%APPDATA%\HarnessPortable\`。

```powershell
# 需要已安装：dotnet tool install -g vpk --version 1.2.0
pwsh -File scripts/release-windows.ps1
```

脚本会：

1. `dotnet publish`（self-contained，非单文件）
2. `vpk pack`（带当前版本的更新日志，并安装 WebView2 引导）
3. 把更新源同步到 `\\server-home\public\Software\HarnessPortable-Releases`
4. 若有 `GITHUB_TOKEN` / `GH_TOKEN` / git credential，则发布 GitHub Release `v<version>`

跳过某一步：

```powershell
pwsh -File scripts/release-windows.ps1 -SkipGitHub
pwsh -File scripts/release-windows.ps1 -SkipShare
```

应用设置里可改更新服务器（UNC、http 目录或 GitHub 仓库 URL），并查看更新日志。

开发时直接 `dotnet run` 不会走更新（Velopack 未安装）。

## 配置文件

- `%APPDATA%\HarnessPortable\profiles.json` —— 隧道与直连列表
- `%APPDATA%\HarnessPortable\secrets.json` —— DPAPI 加密的 SSH 密码与可选 Web 认证输入
- `%APPDATA%\HarnessPortable\known_hosts.json` —— TOFU 主机密钥
- `%APPDATA%\HarnessPortable\layouts.json` —— 布局预设
- `%APPDATA%\HarnessPortable\settings.json` —— 应用设置（关闭行为、更新服务器等）
- `%APPDATA%\HarnessPortable\WebView2\` —— 浏览器用户数据

> 所有配置都写系统用户目录（`%APPDATA%`），不会在 exe 旁边生成任何文件。

字段约定见 `spec/config-schema.md`。
