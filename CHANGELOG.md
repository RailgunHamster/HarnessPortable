# Changelog

## 2.7.4

- Windows / macOS：连接库支持拖动排序，全部视图中拖入其他分组会移动分组，常用视图只调整顺序；顺序重启后保留，最近视图仍按打开时间排列。空白处右键可添加 SSH 隧道或直连网页，与“＋”入口一致。
- Windows / macOS：修复 2.7.1 自动写入关闭重连后，升级仍然无法自动连接的问题。旧配置恢复为默认自动连接；从本版起单独记录用户在设置中的明确选择，主动关闭后仍保持暂停。
- Windows：恢复同一服务器的多个标签时立即标记连接中，避免并发重复启动；停止通知保留连接 ID，过期通知不再覆盖当前状态。尚未启动的隧道显示“尚未连接”，不再误报“隧道已停止”。
- Android：沿用现有界面与行为，版本同步为 2.7.4，versionCode 25。正式发布 Windows 和 Android；macOS 同步源码。

## 2.7.3

- 三端：在“设置 → 更新与关于”增加独立的“更新使用代理”开关与 HTTP 代理地址，默认关闭，默认地址 `localhost:7890`。开关与地址需要明确保存，支持撤销；升级后保持关闭。
- 更新代理覆盖更新检查、安装包下载和 HTTP(S) 跳转，只作用于更新请求。关闭时直接连接，开启后代理不可用会报告失败，不自动绕过代理；局域网文件目录仍直接读取。
- Android：提示 `localhost` 指手机本身，使用电脑的代理需要填写电脑局域网 IP；修复自定义 HTTP(S) 更新源的地址验证。versionCode 24。
- 正式发布 Windows 和 Android；macOS 同步源码。

## 2.7.2

- Windows / macOS：重新整理左侧连接库，改为“全部／常用／最近”三个视图，分组只在全部视图中展示。每个连接在当前视图只出现一次，去掉多层缩进和逐条按钮框，使用紧凑图标、名称、状态和右侧菜单；新增当前连接高亮，搜索、状态刷新保留折叠状态。
- Windows / macOS：修复 2.7.1 启动恢复默认仅恢复标签、不重连 SSH 的行为回退。旧配置缺少新选项时恢复自动连接已有凭据的隧道；用户明确保存关闭自动连接的偏好继续保留。
- Android：沿用已确认的界面和行为，版本号同步为 2.7.2，versionCode 23。

## 2.7.1

- 三端原生界面采用统一的蓝色强调色、分层背景、圆角卡片和更清晰的文字层级，支持系统、浅色和深色外观。
- Windows / macOS：连接入口改为可折叠侧栏，包含搜索、常用、最近和分组。连接菜单统一提供编辑、收藏、分组、断开和删除；重复打开同一连接聚焦已有标签，复制标签仍可独立多开。
- 桌面工作区自动保存上次标签、分屏和当前选中位置，与手动命名的预设分别存储。启动恢复默认保持 SSH 暂停，可在设置中开启自动重连。停止 SSH 保留标签，删除配置才关闭对应标签。
- 设置成为独立窗口，分为通用、工作区、数据与安全、更新与关于。偏好即时保存；更新源使用草稿、保存和撤销，检查更新只读取已保存的源。新安装默认 GitHub，升级保留已有局域网源。
- Windows：工作区标签、拖拽提示、认证与连接提示、编辑器及菜单统一主题；系统标题栏跟随深浅色。连接操作更醒目，空列表提供明确的添加入口。
- Android：SSH 与直连统一为连接列表，独立设置页，单窗口、单服务，无标签导航。返回列表或进入设置保留网页实例与当前连接；切换其他服务先确认，确认或主动断开才释放旧网页。默认保留浏览器导航栏，也可开启服务全屏菜单。长名称省略、地址换行、按钮可换行，输入区域避让键盘，列表保留滚动位置。versionCode 更新为 22。
- macOS：更新连接中心、工作区标签、工具栏、状态标记、输入框及空状态，保留原生分段选择器、菜单和网页容器。
- 性能：复用冻结的 WPF 画刷，连接侧栏使用虚拟化列表；工作区按结构变化延迟保存，仅写入有变化的快照。暂停的 SSH 标签延迟创建浏览器。Android 缓存配色、使用懒加载列表，并保持最多一个服务网页实例；未引入模糊、持续动画或新增界面依赖。
- 正式发布 Windows 和 Android。macOS 源码、37 项测试和通用构建已验证，本次不发布 DMG。

## 2.7.0

- 原生界面样式及 UX 预览候选，未公开分发；完整 UX 更新合并至 2.7.1。

## 2.6.27

- Windows：修复窄窗格和底部窗格拖拽时分屏选择卡被截断，卡片完整保持在窗口内；方向判断使用实际按钮边界，修复“向上”按钮边缘被当作合并标签；切出窗口或丢失鼠标捕获时清理拖拽状态。
- Windows：认证和隧道断连提示显示时隐藏原生浏览器，浮动操作使用独立弹层，避免被网页遮挡；弹层随窗口移动、标签切换和最小化更新。
- Windows：浏览器释放超时改为恢复本应用的浏览器进程，移除无法中断执行的 Dispatcher Abort；受到影响的标签可点击“恢复网页”。
- Windows：修复直连标签重命名、布局恢复标签名和切换后的旧序号；保存新布局后同步记录启动恢复目标，删除预设时清理失效引用。
- Windows：配置读取失败不再回退为空并覆盖原文件，保存前保留 .bak；空列表字段保留其他有效数据；直连地址校验 HTTP/HTTPS、主机和端口，支持 IPv6。
- 安全：导航日志统一移除令牌、用户信息及 fragment；主机密钥存储损坏或无法保存时拒绝连接；本机自动认证只向本机且端口匹配的服务发送令牌，Android 主机密钥算法变化也不再自动扩大信任。
- Windows / Android / macOS：直连保留原始域名，避免替换为 IP 后破坏 HTTPS 证书、虚拟主机及 cookie 身份。仅靠 NetBIOS 广播可解析的 Android 直连名称应改用 DNS 名、IP 或 SSH 隧道。
- Android：Debug 构建无需 Release 签名文件；Release 缺少签名时明确报错；修复 Android 7 前台服务启动兼容性，versionCode 更新为 20。Windows 打包脚本修复 JDK 临时套接字路径导致的构建失败。
- macOS 源码：下载文件在回调返回前保存至稳定暂存路径并校验 HTTP 状态，支持已挂载共享上的 DMG；SSH 等待认证及转发就绪，首次失败停止，仅连通后断线退避重试，本地端口占用尝试后续 9 个端口。

## 2.6.26

- Windows：修复分屏后**在左半边窗格拖动时投放 UI 只显示一半**：覆盖窗口此前按 `Window.Left/Top/Width/Height` 对齐——窗口**最大化**时这些属性存的是还原位置（且不含边框），覆盖层因此与实际客户区错位、投放 UI 被裁掉一半。现在改用客户区屏幕物理坐标经 DPI 换算定位，最大化/多 DPI 显示器下都精确贴合。

## 2.6.25

- Windows：修复 2.6.24 拖动仍崩溃的新栈：`CaptureMouse()` 会**同步**再派发一次 mouse-move（`MouseDevice.Synchronize` 重入），那一刻覆盖窗口还没 `Show()`、Canvas 未连接 PresentationSource，`PointFromScreen` 抛 "此 Visual 未连接到 PresentationSource"。
  - `BeginTabDrag` 现在**先 `Show()` 覆盖窗口再 `CaptureMouse()`**；
  - 坐标换算（`PointToOverlay` 与窗格矩形映射）都加了无源回退：覆盖窗口尚未显示时直接用主窗口坐标（覆盖层与主窗口逐像素对齐，两者等价）。

## 2.6.24

- Windows：修复 2.6.23 引入的**拖动标签即崩溃**：覆盖层 Canvas 原来还在主窗口 Grid 的 `Children` 里，而 WPF 从不自动把子元素移出面板，透明子窗口 `Show()` 时直接抛 "指定的子级已连接到另一个父 Visual"（crash.log 12:35）。现在构造覆盖窗口前先显式从 Grid 摘除 Canvas，并补了一个复现该崩溃形态的 STA 冒烟测试（detach → reparent → Show 全程无异常）。
- Windows：修复更新源**切换下拉后地址框不跟着换**：`SelectionChanged` 在切换之后才读地址框，把离开的槽位地址写进了新选中的槽位，看起来像切换完全没生效。现在用 `RemovedItems` 把地址框文本归档到**正在离开**的槽位，再显示新槽位的地址；补了回归单测（双向切换、自定义地址各保留）。

## 2.6.23

- Windows：修复**拖拽标签分屏时看不到任何投放 UI**（只剩顶部一行提示文字）。
  - 根因是 airspace：拖拽覆盖层（目标窗格高亮 + 方向选择卡片）原来画在主窗口里的一个 WPF Canvas 上，而窗格内容是 WebView2（`HwndHost`）——Win32 子窗口永远绘制在重叠的 WPF 内容**之上**，覆盖层恰好在网页区域上方时全部被浏览器盖住，只有位于工具栏区域的提示文字可见。
  - 覆盖层移进一个**独立的透明子窗口**（无边框、`WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`）：layered 窗口画在 HwndHost 之上，鼠标消息完全穿透、绝不抢焦点，拖拽期间实时贴合主窗口位置/尺寸。这与 AvalonDock 自家 OverlayWindow 用的是同一手法。

## 2.6.22

- Windows：**根除直连「NSSM 自动」在普通（非管理员）会话下的 exit=1**。2.6.21 的日志锁定了真凶：`nssm.exe : LsaOpenPolicy(): 拒绝访问`——`nssm get` 内部要开 LSA 策略，交互启动的应用拿到的是 UAC 过滤令牌，这一步被拒；而 SSH 会话拿到的是完整管理员令牌，所以同一脚本手工/远程跑都好、应用里跑就死（exit=1 的 terminating error 还会吞掉后续所有逻辑）。
  - 发现脚本不再调用 `nssm.exe`：它读的 Application / AppParameters / AppStdout 本来就在 `HKLM\SYSTEM\CurrentControlSet\Services\<服务>\Parameters` 注册表键里，**普通用户可读**，无 LSA、无权限问题。本机直连与隧道（经 SSH）两种模式共用此脚本，一并受益。
  - 同时给 curl 探测临时放宽 `$ErrorActionPreference`：`Stop` 下任何一条重定向的原生 stderr 都会终止脚本，2.6.21 里 nssm 的诊断信息就是这样把脚本杀掉的。

## 2.6.21

- Windows：排查并加固直连「NSSM 自动」在本机的取 token 失败（railgunhamster 实测 `local nssm fetch exit=1`——同一脚本在同一用户下手工/SSH 执行都成功，只有应用拉起的子进程失败）：
  - 调用 `powershell` 改用**绝对路径** `%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe`：应用常由 Velopack 更新器重启，其 PATH 可能与交互 Shell 不同，解析到错误的 powershell 只会表现为无头绪的 exit=1。
  - 发现脚本开头补一道 `PSModulePath` 自愈：为空或不含 `WindowsPowerShell` 时（更新器重启的进程常见）重建标准模块路径，避免 `Get-CimInstance` 模块自动加载失败直接终止脚本。隧道（经 SSH）模式共用此脚本，同样受益。
  - 失败日志带上子进程 **stderr 摘要**（≤300 字符）：以后 exit=1 这类终止错误能一眼看到是哪条命令抛的，而不是只有一个退出码。

## 2.6.20

- Windows：修复直连行「认证模式」按钮**点了没反应**的假象。
  - 根因：`DirectListItem` 触发了 `PropertyChanged` 事件却**没有声明 `INotifyPropertyChanged` 接口**，WPF 绑定引擎只认接口、不订阅裸事件——点击后设置其实已经保存，但按钮文字永远停在「无」。看起来像没生效，多点几下反而把模式又切回了关，最后 `settings.json` 里什么也没留下（railgunhamster 上的实测：点完仍是 `DirectAuthModes: {}`）。
  - 现在类正确实现接口，点击后按钮立即在「无」/「NSSM 自动」之间翻转，所见即所存；补了回归单测锁住接口声明。

## 2.6.19

- Windows：更新源从单地址框升级为**「家庭目录 + GitHub」两个槽位加一个下拉选择**（对齐 macOS 2.6.14、spec/config-schema.md 的双槽位约定）。
  - 管理页「自动更新」区块顶部是下拉选择（`家庭目录` / `GitHub`），下面一个地址框显示**当前选中槽位**的地址，改哪个就存哪个——两个地址都保留，切换不丢，不用再「背」一个 GitHub 地址：GitHub 槽位默认就是本仓库地址，选上即可直接用。
  - 旧单字段 `UpdateServerUrl` 首次升级自动折进对应槽位（GitHub 地址进 GitHub 槽、UNC/http 进家庭槽），选择也随之跟过去，用户原来填的地址不丢；该字段此后只作镜像，供旧版本回读。
  - 新字段：`UpdateHomeUrl` / `UpdateGitHubUrl` / `UpdateSourceSelected`（`home` / `github`），默认选 `home`。

## 2.6.18

- Windows：在管理窗口点「连接」（隧道或直连）后**自动把主窗口弹到前台**。
  - 此前标签在主窗口打开，但管理窗口仍挡在前面（或主窗口在托盘/最小化中），容易意识不到已经打开了。
  - 现在隧道连接成功、标签真正创建时唤起主窗口：隐藏（托盘）则显示、最小化则还原，再激活置顶——与托盘图标「重新打开窗口」同一逻辑；连接失败不弹，留在管理窗口看状态。

## 2.6.17

- Windows：**直连地址支持「NSSM 自动」认证**（对齐 macOS 2.6.16 的直连本机自动）。
  - 管理页直连列表每行新增模式按钮，点击在 `无`（默认，行为不变）/ `NSSM 自动` 之间切换；模式按 URL 存于 `settings.json` 的 `DirectAuthModes`（不改动 `profiles.json` 的跨平台 `directs` 契约），删除直连项时一并清掉。
  - 选 `NSSM 自动` 适用于 dsh web 就跑在**这台 Windows** 且由 NSSM 托管的直连地址（localhost）：打开标签时在本机执行与隧道 NSSM 模式相同的发现脚本——定位 NSSM 托管的 dsh 服务、读 stdout 日志取最新 token URL、curl 验证返回 303；端口与直连地址一致才把 token query 嫁接到该地址（host/path/其它参数原样保留）。
  - 日志缺失、token 失效或端口不一致时地址原样打开，仍走页面 401 手动粘贴框兜底；隧道经 SSH 的 NSSM 模式不受影响。
- Windows / macOS / Android：新增「systemd 自动（Linux 服务器）」Web 登录方式。
  支持用户服务、系统服务、自定义服务名及日志路径；自动读取本次启动的 journal
  或指定的 stdout 文件，验证端口、HTTP 303 和服务启动标识，失败沿用手动认证回退。

## 2.6.16

- macOS：**直连地址支持选认证模式**（此前只有隧道能选）。
  - 管理页直连列表每行新增模式菜单：`无`（默认，行为不变）/ `本机自动`——读取**本机** `/tmp/dsh-web.stdout.log`（Launch Agent `com.deepseek.dsh-web` 自启动的 dsh web），把最新 token 拼到该直连地址后打开；适合直连运行在这台 Mac 上的 dsh。
  - 模式按 URL 存于 macOS 设置 `directAuthModes`（不改动 `profiles.json` 的跨平台 `directs` 字符串数组契约）；日志缺失或 token 不可用时地址原样打开，仍可走 401 手动粘贴框兜底。
  - 隧道的 NSSM / Launch Agent（经 SSH）模式不适用于直连（没有 SSH 一跳），故直连只提供上述两档。

## 2.6.15

- macOS：新增第三种 Web 登录方式 **Launch Agent 自动**（与 NSSM 自动、手动输入并列），用于服务器是 Mac、`dsh web` 由 `com.deepseek.dsh-web` Launch Agent 自启动的场景。
  - 连接后经 SSH 读取服务器上的 `/tmp/dsh-web.stdout.log`，取最新的 `dsh web: http…token…` URL，并在服务器本地用 curl 验证返回 303（令牌仍有效）才采用；失败自动回退到保存的手动认证输入，再退到 401 粘贴框——回退链与 NSSM 模式一致。
  - `authMode` 新增值 `launchd`；旧配置里的未知值统一回落 `nssm`。
- spec：`tunnels[].authMode` 增补 `launchd`（macOS 端）。

## 2.6.14

- macOS：管理界面（隧道/直连维护、应用设置、检查更新）从工作区标签改成**独立窗口**，对齐 Windows 2.6.8。
  - 入口有两个：主窗口工具栏「管理 / 设置」，以及菜单栏（托盘）「管理 / 设置」；主窗口关到菜单栏时也照样能进。
  - 工作区只放会话标签；旧布局预设里残留的 `management` 标签在恢复时被忽略（不报错、不再重建），只有管理标签的旧预设恢复为空面板（新标签照常落入，面板有占位提示）。
  - 管理窗口里的「连接 / 打开标签」通过通知转发到工作区窗口（需要时自动唤起主窗口）；SSH 密码提示在管理窗口内就地完成。
- macOS：应用内自动更新，对齐 Windows 2.6.0–2.6.10 的能力（Velopack 不跨平台，改为轻量自建管道）。
  - 更新源是「家庭目录 + GitHub」两个槽位加一个选择，两个地址都保留，切换不丢；旧字段 `updateServerUrl` 首次升级折进对应槽位。家庭目录默认 `smb://server-home/public/Software/HarnessPortable-Releases`，运行时映射到 `/Volumes/public[…]` 挂载点（未挂载时给出「在访达连接共享」的明确提示）；GitHub 槽位默认 `releases/latest/download`。
  - feed 为 `macos.json`（`version` / `dmg` / `sha256` / `notes`，与 android.json 同族），由 `scripts/release-macos.ps1` 生成并发布到共享与 GitHub Release。
  - 管理页「自动更新」区块：检查更新、更新日志阅读、**4px 下载进度条** + 「正在下载更新… 42%」状态行（对齐 2.6.10）；下载到「下载」文件夹并做 SHA256 校验，之后打开 dmg 由用户拖入「应用程序」（macOS 不允许运行中的应用自替换）。
  - 菜单栏新增「检测并更新」和「当前版本」行（对齐 2.6.8 托盘行为）。
- macOS 设置新增字段：`updateHomeUrl` / `updateGithubUrl` / `updateSourceSelected`（语义对齐 `spec/config-schema.md` 的双槽位约定）。

## 2.6.13

- macOS：补齐 2.6.11 / 2.6.12 的布局预设**网页状态**记录与恢复，行为对齐 Windows 版。
  - **保存布局**时从每个标签的页面地址里只挑三个参数（`dsh_session` / `dsh_sidebar` / `dsh_rightbar`）写进预设标签的 `web` 段；绝不保存原始 URL（dsh 启动 URL 带进程 token）。需要 Mac 端也装上配套插件 [`dsh-view-state`](https://github.com/RailgunHamster/dsh-view-state)。
  - **恢复布局**时把这三个参数并回导航目标，页面当前显示的状态优先于预设记录；启动令牌 URL 302 丢参后，页面加载完成 1.5 秒仍未见参数则用记录状态对干净根路径再导航一次（认证交接期间不打扰，一次性触发）。
  - 恢复/切换布局时清理旧标签的 WKWebView 会话，不再残留；预设 `web` 段同时兼容 camelCase（macOS）与 PascalCase（Windows 预设文件）。
  - macOS 版本号从 `1.0` 对齐到 `2.6.13`；新增 dmg 分发包构建（`scripts/release-macos.ps1`，远程 Mac 上 xcodegen + xcodebuild + ad-hoc 签名 + hdiutil）。

## 2.6.12

- Windows：修"恢复布局时网页状态到不了页面"。dsh 的启动令牌 URL 会 **302 到干净根路径，而这个重定向会丢掉 query**——2.6.11 把预设里的 `dsh_*` 参数并到令牌 URL 上时，它们根本到不了页面（插件只在启动时读一次参数），于是恢复只剩插件自己的 localStorage 兜底（同 origin 同端口才行）。
  - 现在页面加载完成后先给它 1.5 秒回写自己的状态；若那三个参数始终没出现，就用同一份记录状态**再导航一次**到该标签的干净根路径。一次性触发，认证交接期间不打扰（由其完成事件重入），不会循环。
  - 另外：配套插件同步升到 [`dsh-view-state` 0.2.0](https://github.com/RailgunHamster/dsh-view-state)——0.1.0 在真浏览器里其实是坏的（只探一次 layout store 就放弃；并且把"读不到"当成"没有"，会**删掉** wrapper 传进来的参数）。三台机器（`claw` / `railgunhamster` / `winserver`）已更新到 0.2.0；客户端模块按请求读盘，**不需要重启 `dsh web`**。

## 2.6.11

- Windows：布局预设开始记录**每个标签的网页状态**——你在哪个会话、侧边栏开着还是收着、面板多宽。前提是装上配套的 dsh 插件 [`dsh-view-state`](https://github.com/RailgunHamster/dsh-view-state)：dsh 前端没有路由，这些状态原本只活在页面内存里，所以以前"保存布局/重开"永远回到一个空会话。
  - **保存布局**时从每个标签的页面地址里**只挑三个参数**（`dsh_session` / `dsh_sidebar` / `dsh_rightbar`）写进预设的 `Web` 段；**绝不保存原始 URL** —— dsh 的启动 URL 带进程 token，而预设是磁盘上的明文文件。
  - **恢复布局**时把这三个参数并回导航目标，其它参数（尤其 `token`）与路径原样不动，所以与 `dsh-session-link` 的 `/s/<会话ID>` 深链并存不冲突。
  - 页面**当前**显示的状态优先于预设里记的：切换了会话之后重新加载/重连，不会把你踢回预设里的旧会话。
  - 没装插件（或页面里没有这些参数）时行为与 2.6.10 完全一致；旧预设照常加载。
  - 插件装法：`dsh plugin --profile web add github:RailgunHamster/dsh-view-state`，然后重启 `dsh web`。部署与能力边界见 `\\server-home\public\Documents\dsh-personal\docs\dsh-view-state.md`。
  - 已知边界：侧边栏"收起后原来拖的宽度"会被 dsh 自身丢弃（重新展开用契约默认 280）；右侧面板"当前是否显示"不可还原（属 `ui-sidebar-right` 每会话 store，外部驱动会与它抢），能还原的是它的宽度偏好。

## 2.6.10

- Windows：更新下载现在看得见进度。
  - 管理窗口「自动更新」区块新增一条 4px 进度条（轨道 `layer-3`、进度用强调色，随主题走），按 Velopack 回报的百分比实时推进；状态行仍保留「正在下载更新… 42%」的文字。
  - 托盘：右键菜单一点就关了，菜单文字承载不了进度，所以改走两条**常驻通道**——图标 tooltip 实时显示「Harness Portable · 正在下载更新… 42%」，并每隔 5% 或 4 秒（先到者）用气泡刷新一次百分比。
  - 主窗口可见时，任务栏按钮同步显示进度（下载阶段为确定值，检查阶段为不确定态）。
- Android 无需改动：本来就有下载进度条（`ApkUpdate.download(onProgress)` → `LinearProgressIndicator`）。macOS 目前没有应用内更新，也就没有可显示的进度。

## 2.6.9

- 三端界面统一改用 DSH 自己的设计 token 重做，配色**跟随系统深浅色**。
  - token 不是自创的：直接取自 DSH 前端的设计系统（`dsh-client-ui-theme`）——底色与分层（浅色 `#FFFFFF`/`#F5F6F7`/`#EBEEF2`，深色 `#151517`/`#232324`/`#2C2C2E`/`#353638`）、三档描边（`#0000000A`↔`#FFFFFF0F` 等）、三级文本色、品牌主按钮（浅色黑底白字／深色白底深字）、强调色 `#4176E6`↔`#5686FE`、状态色（绿 `#22C55E`／红 `#EC1313`↔`#F25A5A`／琥珀 `#F7AD31`）、圆角（卡片 12／控件 8）、技术值（host、端口、URL、版本）一律等宽字体；用 1px 描边取代阴影。
  - Windows：管理窗口整体重做（新增 `Themes/DshTokens.Light.xaml`、`Themes/DshTokens.Dark.xaml`、`Themes/DshManagement.xaml`、`Services/AppTheme.cs`）。读 Windows「应用模式」并监听 `UserPreferenceChanged`，切换系统深浅色时整本 token 字典热替换，管理窗口立即换肤、无需重启；`flicker.log` 的 `[theme]` 行会记下当前生效的是哪套。主窗口与其它弹窗保持原有浅色样式。
  - macOS：新增 `Theme/DshTheme.swift`（同一套 token，按 `colorScheme` 解析），管理界面、会话页（含认证/错误/状态浮层）、工作区标签与状态栏全部改用 token。
  - Android：新增 `Theme.kt`（同一套 token 生成 Material3 `light/darkColorScheme`，不再用 `dynamic*ColorScheme` 动态取色），管理/设置页、连接中与认证界面、网页浮层统一到 token。
  - Android 仍然只有应用内的设置页，没有单独的“管理窗口”。
- 管理页细节：直连输入框补上占位提示；更新日志改用次要色（原先纯白太抢眼）；隧道状态由文本里的“●”字符改为彩色圆点 + 文字，圆点随状态走绿/蓝/琥珀/红。

## 2.6.8

- Windows：管理界面（隧道/直连维护、应用设置、检查更新）从工作区标签改成**独立窗口**，不再可能被工作区的问题连累到打不开。
  - 入口有两个：主窗口工具栏「管理 / 设置」，以及托盘右键「管理 / 设置」。后者不需要主窗口可见，主窗口关到托盘、最小化、甚至渲染异常时都照样能进。
  - 工作区现在只放会话标签；旧布局预设里残留的 `Kind = "management"` 标签在恢复时被忽略（不报错，也不再重建该标签），只有管理标签的旧预设会得到一个空面板，新开的标签照常落入。
  - 关闭管理窗口只是关闭窗口：面板对象复用，下次打开保持原状态；应用退出仍以主窗口为准（`ShutdownMode = OnMainWindowClose`）。
  - 窗口图标在代码里带兜底地设置：图标资源解析失败只是少了图标，绝不会变成"窗口打不开"的理由。
- Windows：托盘右键新增「检测并更新」。
  - 不经过管理页、也不要求主窗口打开：点击后直接检查更新，有新版就下载安装并自动重启，全过程用托盘气泡提示；菜单里同时常驻一行「当前版本 X（安装版/便携版/开发构建）」，旁边那条在发现有新版时变成「安装 X 并重启」。
  - 对 `dotnet publish` 这类非 Velopack 构建，它会用 2.6.6 起的行为回报「最新版本 X（当前…不能自更新：请用 Setup 安装包或便携 zip）」。

## 2.6.7

- Windows：修复 2.6.5 引入的回归——点「管理」标签会被立刻切回网页标签，导致管理界面根本打不开。
  - 原因：2.6.5 为了让「点进网页时该文档仍被标记为激活」，把 `SessionView` 的 `WebView.GotFocus` 接到了「把这个文档设为激活」上。可是 WebView2 是 `HwndHost`：标签切换时它会被卸载并重新挂载，这个过程同样会触发 `GotFocus`，于是应用又把刚刚被切走的那个文档设回 `IsActive`/`IsSelected`，把用户点的那一下顶掉。
  - 现在这条接线整个去掉：切换标签时 AvalonDock 的 `LayoutDocumentPaneControl.OnSelectionChanged` 本来就会把选中的文档置为激活，不需要额外补偿。2.6.5 的闪烁修复（面板内容不再包 `LayoutDocumentControl`，焦点钩子无法再把网页里的焦点事件写成"激活"）保持不变。

## 2.6.6

- Windows：把「检查更新」从黑盒变成有据可查，并让非 Velopack 构建也能得到有用的回答。
  - 每次检查更新都会写进 `flicker.log`：源地址、Velopack 的判定（`installed` / `portable` / `current` / `appId`），以及结果 `available X` / `no update` / `FAILED <异常类型>: <消息>`。以后再遇到「某个源检查更新没用」，点一次按钮就能定性：是源读不到、不是 Velopack 部署、还是已经最新。
  - 以前只要当前进程不是 Velopack 部署（`dotnet publish` 输出、拷贝出来的 exe、或直接跑便携 zip 里 `current\` 的那个 exe），按钮只会回一句「无法在线更新」。现在它会**直接读 feed**，回答「最新版本 2.6.5（当前 …，开发构建 不能自更新：请用 Setup 安装包或便携 zip）」，并展示该版本的更新日志。
  - 实测确认：安装版与便携版（解压后运行根目录 `Harness Portable.exe`）都能从 `\\server-home\public\Software\HarnessPortable-Releases` 读到 feed 并识别新版本。

## 2.6.5

- Windows / Android：修复「失败后疯狂重连，最后被服务器挡住」。
  - 45 秒看门狗以前把「正在退避等待」（RETRYING）也算成卡死，重启 worker 时又把退避重置回 3 秒。由于 3+6+12+24 恰好等于 45 秒，指数退避**永远到不了 30 秒上限**，连接失败后大约每 10 秒就敲一次服务器，足以触发 fail2ban / sshd `MaxStartups` / `pam_faillock`。现在看门狗只把「仍停在 CONNECTING 且超过自身超时」当作卡死，退避保存在引擎/服务上，重启不再清零。
  - 按 `spec/config-schema.md` 的既有约定，重连循环只覆盖「曾经连接成功之后的网络中断」：从未连通的失败（端口写错、主机不可达、已被服务端封锁）直接进入失败态并停止，不再自动重试，只有手动「重连」才会再试。此前 Windows / Android 都在**首次失败**就进入重连循环，这正是账号/IP 被锁的直接原因。
- Windows：修复「一打开、自动恢复布局预设就一直疯狂闪烁 / 抢焦点」。
  - 根因：AvalonDock 5 的 `FocusElementManager` 会挂一个系统级 Win32 焦点钩子，只要焦点事件落进某个 `HwndHost`，它就把该 `HwndHost` **在视觉树上的 `LayoutDocumentControl` 祖先**所对应的文档置为 `IsActive`；而 `IsActive` 会连带写 `IsSelected` 和 `Root.ActiveContent`（也就是本应用订阅的 `ActiveContentChanged`）。WebView2 正是 `HwndHost`，且原来就包在 `LayoutDocumentControl` 里——所以网页里每发生一次焦点变化（页面加载、点输入框、IME 激活），AvalonDock 都会"激活"该标签，面板随之重新挂载内容，WebView2 的子窗口被销毁重建，接着又产生新的焦点事件。而应用每收到一次 `ActiveContentChanged` 还会主动认领一次焦点（原来一次激活排两次 `Focus()`），把这个环继续喂下去。启动恢复预设时"程序化激活 + 窗口激活 + 页面抢焦点"撞在一起，正是环闭合的时刻。
  - 修法：面板内容不再用 `LayoutDocumentControl` 包裹，直接把文档的 `Content` 交给 `ContentPresenter`，焦点钩子再也无法把焦点事件关联到文档上；文档激活改由应用在用户真正点进页面时完成（`SessionView.WebViewFocused` → `MainWindow.ActivateSessionDoc`），保证 `Layout.ActiveContent`（决定新标签开在哪个面板）仍然跟着用户走。另外一次激活爆发最多认领一次焦点（Input + Background 两趟合并），隐藏或未加载的标签页不再被认领，并新增 `session Loaded/Unloaded handle=… chain=…` 诊断，用于确认 WebView2 是否在被反复重建。

## 2.6.4

- Windows：修复运行中窗口「卡死、只能用任务管理器关」的三个成因。
  - 诊断日志 `flicker.log` 不再无上限增长。旧实现每次写入后都要把整个文件读回内存再重写一遍，文件一旦超出预算就静默失败，实测长到 **146 MB**，而这场「读整个文件 + 整份重写」每 200 ms 还会重试一次——慢盘或被杀毒扫描时足以让窗口看起来像死了。现在改为轮转（`flicker.log` + `flicker.log.1`，各 2 MB），遗留的超大文件会在第一次写入时流式裁掉、只保留最新的一段。
  - 同一批里连续重复的焦点事件折叠成一条并标注次数，焦点风暴不再写进几十万行。
  - 拖动标签到另一个面板时，AvalonDock 的 `Children` 修改会（见 `crash.log`）抛「无法显式修改 Panel 的 Children 集合」。该操作现在延后到输入事件之外执行，并保证任何一步失败后标签仍留在有效面板里，不再把布局半改坏。
  - 关闭标签时，若 WebView2 浏览器进程已经卡住，`Dispose` 会一直阻塞 UI 线程。现在它最多占用 5 秒，超时后自动放行，窗口不会再因此彻底失去响应。

## 2.6.3

- Android：桌面应用名从 `Harness Portable` 缩短为 `Harness` —— 原来 13 个字符在图标标签下会被截断。

## 2.6.2

- Android / Windows：更新源改成「家庭目录 + GitHub」两个槽位加一个选择，两个地址都保留，切换不丢。Android 默认走 GitHub（手机读不了 UNC 共享），Windows 默认走家庭目录（PC 能直接读）。升级时原来填过的地址会自动落到对应槽位。
- Android：修复每次打开 APK 都显示「隧道未连接」——进程被杀后会自动重连上次隧道，不再只恢复一个空的网页层。
- Android：SSH 后台保活改为在隧道存活期间持有 WakeLock / Wi‑Fi Lock，并使用 `specialUse` 前台服务；切到后台后不再只有 10 分钟宽限。
- Android：首次连接时提示忽略电池优化，设置页也可手动「允许后台运行」。

## 2.6.0

- Android 支持应用内更新：设置页底部「检查更新 / 立即安装」。默认检查 GitHub Release 的 `android.json`，安装包同时放在局域网 `HarnessPortable-Releases`。
- Windows 管理页右上角增加「检查更新」按钮。

## 2.5.2

- Android 内置浏览器支持选择本地文件作为附件（网页 `<input type="file">` / dsh 附件按钮）。

## 2.5.1

- 便携 zip 与 Setup 安装包一样支持自动更新（解压后运行根目录的 `Harness Portable.exe`）。无法更新的只有开发运行和旧版单文件 exe。

## 2.5.0

- Windows 使用 Velopack 自动更新。设置里可查看并修改更新服务器、阅读更新日志、检查并安装新版本。默认更新源为局域网 `\\server-home\public\Software\HarnessPortable-Releases`，也可改为 GitHub 仓库地址。
- 支持 SSH 私钥登录（可不填密码）；从 `~/.ssh/config` 读取 IdentityFile。密码错误只尝试一次，不再反复重连以免锁死账号。
