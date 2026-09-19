# Changelog

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
