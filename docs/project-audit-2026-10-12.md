# Harness Portable 项目检查与修复清单

检查日期为 2026 年 10 月 12 日，代码基线为 `95bdd77`。本轮整理出 17 项修复任务。拖拽提示存在可确定的越界和命中区域错误，此外还发现浏览器提示层遮挡、释放保护失效、配置和凭据处理等问题。

P1 表示应优先处理的功能阻塞、数据丢失或凭据风险；P2 表示有明确触发条件的功能错误。下表中的“诊断复现”指执行原函数或测量原 XAML 控件，“源码确认”指调用路径和框架规则明确，但尚未做对应平台的完整交互验收。

## 2.6.27 修复和验收状态

Windows 和 Android 的相关代码已修复。Windows 新增 22 项回归用例，共 162 项测试全部通过；共享认证脚本 16 项通过；Android Debug、Release、lint 和 APK 签名验证通过。隔离副本验证了真实标签标题更新、原 XAML 按钮命中和生产定位函数：原先越界 126 DIP 的卡片现位于 654 至 892，越界为零。

无 Release 签名文件的干净 Android 副本可以完成 Debug 构建，Release 会明确拒绝缺失签名的构建。构建过程中另修复了 Android 7 调用高版本前台服务 API 的兼容问题。

macOS 已通过 GitHub 托管 Mac 构建机的 32 项测试，完成 Apple Silicon / Intel 通用 Release 构建和 DMG 打包，包的 SHA256 与构建机输出一致。[构建记录](https://github.com/RailgunHamster/HarnessPortable/actions/runs/38248543089)对应发布源码标签 `v2.6.27`。

按用户最后指定的范围，本次只发布 Windows 和 Android，macOS DMG 保留在本地产物目录，未更新 macOS 发布附件和更新源。

[2.6.27 已正式发布](https://github.com/RailgunHamster/HarnessPortable/releases/tag/v2.6.27)，公开最新版本接口已确认非草稿、非预发布。Windows 安装版、便携版、完整更新包、差分更新包及 Android APK 均与本地产物 SHA256 一致；局域网更新源已同步到 2.6.27，Android versionCode 为 20。macOS 更新源仍保留 2.6.16。

原生截图接口仍返回显示器捕获错误，因此完整鼠标拖拽、不同显示器及 DPI 的像素验收仍待实机确认。以下问题详情和图片记录的是修复前基线。

## 检查范围和验证结果

| 检查 | 结果 |
| --- | --- |
| Windows Release 构建及单元测试 | 140 个测试全部通过，构建无警告和错误 |
| 共享 systemd 认证脚本 | 16 个测试全部通过 |
| 图标源文件一致性 | 3 个 SVG 全部通过 |
| Windows 直接及传递 NuGet 依赖 | 配置的软件源未报告已知漏洞 |
| WPF 控件尺寸及函数诊断 | 复现提示越界、命中错误、标题不同步、配置覆盖、信任记录降级和令牌跨主机嫁接 |
| Windows 原生截图及真实鼠标拖拽 | 捕获接口两次返回 `IGraphicsCaptureItemInterop.CreateForMonitor` 错误；启动接口另有 `GetCursorPos` 拒绝访问。未获得可用截图或完整拖拽录像 |
| Android 构建及 lint | 两次在 Gradle 启动阶段被本机 Java 的 `Unable to establish loopback connection` 阻断，尚未进入项目编译 |
| macOS | 源码检查；当前 Windows 环境未运行 Xcode 构建或实机测试 |

初次检查生成了下列报告和诊断材料，随后按用户授权修复并打包 2.6.27。临时诊断使用独立数据目录、本机测试地址和人工构造的令牌，没有使用真实 SSH 密码或认证令牌。

## 拖拽提示显示不全的定位

原 XAML 将分屏选择卡固定为 **238 × 174 DIP**。定位算法要求卡片至少位于目标窗格左上角加 8，但没有处理窗格比卡片小的情况，也没有把卡片限制在整个覆盖窗口的可见范围内。

以宽 900 的窗口、最右侧范围为 780 至 900 的窗格为例，原公式得到卡片左边界 788、右边界 1026，因此有 **126 DIP 超出窗口右边界**。这能够确定造成右侧提示和按钮显示不全；底部很矮的窗格也有相同的纵向问题。它是一个可确定的原因，反馈若发生在宽窗格或不同 DPI 场景，还需要继续检查真实窗口坐标。

![依据源码尺寸绘制的拖拽提示越界示意图](D:/git/harness-portable/docs/audit/drag-clipping-2026-10-12.png)

上图依据控件尺寸和定位公式绘制，**不是实时窗口截图**。相关修复任务为 R09 和 R10。

## 按优先级排序的修复列表

| 编号 | 优先级 | 平台 | 问题 | 依据 |
| --- | --- | --- | --- | --- |
| R01 | P1 | Windows | 认证框、连接状态提示和浮动菜单会被原生浏览器层遮挡 | 源码及官方框架规则 |
| R02 | P1 | Windows | 浏览器释放超时后调用 Abort 无法解除已经执行的 UI 阻塞 | 诊断复现 |
| R03 | P1 | Windows | 导航日志直接记录带认证令牌的完整 URL | 源码确认 |
| R04 | P1 | 三端 | 主机密钥信任存储出错时降级为首次信任；Android 异常直接放行 | Windows 诊断复现，其他端源码确认 |
| R05 | P1 | Windows 和 macOS | 本机认证令牌能够被嫁接到其他主机的直连 URL | Windows 函数复现，macOS 源码确认 |
| R06 | P1 | Windows | 配置局部损坏导致整份配置被视为空，后续保存覆盖有效数据 | 诊断复现 |
| R07 | P1 | 三端 | 直连 URL 将域名改为 IP，破坏 HTTPS 主机身份及 HTTP 虚拟主机路由 | 源码确认 |
| R08 | P1 | macOS | 下载回调异步交接临时文件，文件可能在移动之前被删除 | 源码及官方 API 规则 |
| R09 | P2 | Windows | 小窗格拖拽选择卡超出窗口边界，提示显示不全 | 原 XAML 尺寸及公式复现 |
| R10 | P2 | Windows | 拖拽按钮的可见边界与放置区域判断不一致 | 原 XAML 及原判断函数复现 |
| R11 | P2 | Windows | 直连标签重命名和预设标签名不更新；切换后的标签可能保留旧序号 | 诊断复现 |
| R12 | P2 | Windows | 保存新布局后没有更新启动恢复的布局名称 | 源码确认 |
| R13 | P2 | Windows | 无效 URL 和不支持的协议可以进入直连列表 | 诊断复现 |
| R14 | P2 | macOS | SSH 进程存活 0.8 秒就被视为已连接，未等待认证及转发就绪 | 源码确认 |
| R15 | P2 | macOS | 首次连接未成功的失败仍会自动进入重试 | 源码确认 |
| R16 | P2 | macOS | 本地端口被占用时没有按契约尝试后续 9 个端口 | 源码确认 |
| R17 | P2 | Android | Debug 构建也依赖没有提交到仓库的 Release 签名配置 | 源码确认；本机编译受环境限制 |

## 每项修复的触发条件和验收标准

### R01 浏览器遮挡认证和状态提示

定位：[SessionView.xaml](D:/git/harness-portable/windows/HarnessPortable.Windows/Controls/SessionView.xaml:7)、[ShowAuthOverlay](D:/git/harness-portable/windows/HarnessPortable.Windows/Controls/SessionView.xaml.cs:713)。

`StatusOverlay`、`AuthOverlay` 和浮动操作菜单与 `WebView2` 放在同一个 Grid 中。浏览器初始化后使用独立 HWND，普通 WPF 元素即使排在其后也无法覆盖它。因此在网页要求重新认证或隧道断开时，提示层和输入框会被浏览器遮住。这个限制由 [Microsoft 的 WPF WebView2 说明](https://learn.microsoft.com/en-us/microsoft-edge/webview2/platforms/wpf)明确说明。

修复：将认证输入移到有 Owner 的独立对话框，或采用能够覆盖浏览器的窗口；状态提示需要隐藏浏览器或安排在不重叠的区域。若迁移到 `WebView2CompositionControl`，需同步调整依赖 HWND 的焦点代码，并验证输入法和分屏行为。

验收：网页已加载后触发认证，提示完整可见且可输入；隧道断开时重连和关闭按钮可见且可点击；浮动菜单在网页上方正常显示。

### R02 浏览器释放超时保护无效

定位：[ScheduleWebViewDisposal](D:/git/harness-portable/windows/HarnessPortable.Windows/Controls/SessionView.xaml.cs:216)。

释放工作仍在主 UI Dispatcher 上执行。五秒后的后台任务调用 `operation.Abort()`，但已经开始执行的 Dispatcher 操作不能被它中断。诊断中操作状态为 `Executing` 时，Abort 返回 `false`，UI 线程仍等待操作结束。这与 [Microsoft 的 Abort API 说明](https://learn.microsoft.com/en-us/dotnet/api/system.windows.threading.dispatcheroperation.abort?view=windowsdesktop-10.0)一致。

修复：移除“Abort 能释放被阻塞 UI 线程”的假设，设计浏览器进程恢复或独立 STA/进程承载的释放机制。不能直接把要求 UI 线程的 `WebView.Dispose()` 改为 `Task.Run`。恢复共享浏览器进程时应考虑其他标签的状态。

验收：模拟释放操作长期不返回，主窗口仍可响应并执行明确的恢复流程。此次复现了保护机制失效，真实 WebView2 永久阻塞仍需专门验收。

### R03 导航日志泄漏认证令牌

定位：[NavigationStarting 订阅](D:/git/harness-portable/windows/HarnessPortable.Windows/Controls/SessionView.xaml.cs:460)、[LogTag](D:/git/harness-portable/windows/HarnessPortable.Windows/Controls/SessionView.xaml.cs:356)。

虽然 `Navigate` 使用了 `MaskUrl`，`NavigationStarting` 又把 `e.Uri` 原样写进 `flicker.log`。打开 `/?token=...` 的认证 URL 时，令牌仍会落盘。直连 URL 本身带查询参数时，`LogTag` 也会重复泄漏它。

修复：所有导航日志和标签日志统一脱敏 URL，覆盖查询、用户信息和可能承载凭据的 fragment；日志保留诊断所需的主机和路径即可。

验收：用人工令牌完成导航和跳转，搜索当前日志及轮转日志，均找不到令牌原文。

### R04 主机密钥存储出错后失去原信任约束

定位：[Windows LoadFile](D:/git/harness-portable/windows/HarnessPortable.Windows/Services/KnownHostsStore.cs:88)、[macOS load](D:/git/harness-portable/macos/HarnessPortable/Services/KnownHostsStore.swift:52)、[Android check](D:/git/harness-portable/app/src/main/java/com/harness/portable/SshTunnelService.kt:713)。

Windows 和 macOS 在解析已有信任文件失败时返回空信任表。Windows 诊断先记录旧密钥，损坏 JSON，再提交不同密钥，结果为 `TrustedNew`。Android 在解析或读取发生异常时直接返回 `OK`；它还按主机和密钥类型存储，服务器切换密钥算法时可以被视为新的记录。

修复：区分首次运行时文件不存在与已有文件损坏，存储损坏时拒绝自动扩大信任并提供恢复路径。Android 异常应拒绝连接，算法变化的处理应与跨平台契约一致。

验收：信任文件损坏、类型错误、持久化失败或服务器更换密钥类型时，不会静默接受新的服务器身份。

### R05 本机令牌嫁接缺少目标身份检查

定位：[Windows GraftToken](D:/git/harness-portable/windows/HarnessPortable.Windows/Services/LocalNssmAuth.cs:163)、[macOS authTarget](D:/git/harness-portable/macos/HarnessPortable/Services/LocalDshAuth.swift:18)。

Windows 只检查端口一致，不检查目标是否为本机服务。诊断将本机 3080 端口的人工令牌嫁接到 `http://unrelated.invalid:3080/`，函数仍返回带令牌的目标 URL。macOS 连端口一致性检查也没有。这会在为其他服务器误启用“本机自动认证”时，把本机令牌发给错误的主机。

修复：验证目标确实属于本机服务，包含解析后的主机地址和端口；其他主机保留原 URL 并使用认证兜底。若需要跨地址访问同一服务，应要求明确的服务身份映射。

验收：其他主机即使端口相同也得不到本机令牌；本机回环地址、有效本机名称和明确映射的地址正常工作。

### R06 配置解析失败后保存覆盖有效数据

定位：[ProfileStore.Load](D:/git/harness-portable/windows/HarnessPortable.Windows/Services/ProfileStore.cs:33)、[SaveDirects](D:/git/harness-portable/windows/HarnessPortable.Windows/Services/ProfileStore.cs:96)。

例如 JSON 中已有有效隧道而 `directs` 为 `null`，过滤列表时发生异常，Load 返回全新的空配置。随后添加直连调用 SaveDirects，会把原有有效隧道也覆盖掉。诊断中已有的一个隧道在读取及保存后都变为零。

修复：对字段分别校验和恢复，保留仍然有效的部分；区分读取失败与真实空配置；无法安全读取时先保留原文件和备份，并禁止无提示覆盖。其他配置 Store 的空值回退也应按同一原则检查。

验收：`directs: null`、损坏列表和截断 JSON 不会在下一次保存时悄悄清空有效数据。

### R07 域名替换为 IP 破坏网页主机身份

定位：[Windows ResolveUrlAsync](D:/git/harness-portable/windows/HarnessPortable.Windows/Services/HostResolver.cs:148)、[Android resolveUrl](D:/git/harness-portable/app/src/main/java/com/harness/portable/HostResolver.kt:154)、[macOS resolveURL](D:/git/harness-portable/macos/HarnessPortable/Services/HostResolver.swift:61)。

三端在直连时把域名 URL 改成解析后的 IP URL。这不只是更改连接地址，还改变了浏览器使用的网页主机身份。证书只覆盖域名的 HTTPS 站点会出现身份不匹配；依赖 Host 路由的 HTTP 站点也可能返回错误页面。完整浏览器握手尚未在本机验收，URL 重写路径已由源码确认。

修复：保留用户输入的 URL 主机身份，优先使用浏览器正常解析；需要处理特定内网名称时，在解析或连接层解决，或提供明确的 IP 访问模式。

验收：仅有域名证书的 HTTPS 服务，以及同一 IP 上多个虚拟主机，都打开正确站点；局域网名称访问场景也正常。

### R08 macOS 下载文件在回调后可能已不存在

定位：[DownloadProgressBridge 下载完成回调](D:/git/harness-portable/macos/HarnessPortable/Services/UpdateService.swift:270)。

回调用 `Task { @MainActor ... }` 异步调用 `bridgeDidFinish`，随后立即返回。下载临时文件的生命周期只保证到回调结束，等主线程稍后校验或移动时文件可能已经被删除。[Apple 的下载回调说明](https://developer.apple.com/documentation/foundation/urlsessiondownloaddelegate/urlsession%28_%3Adownloadtask%3Adidfinishdownloadingto%3A%29?changes=l_6)要求在回调返回前打开或移动该文件。

修复：在回调内先同步移动到应用拥有的暂存路径，再把稳定路径交给后续校验及主线程状态更新；同时校验下载响应状态。

验收：主线程繁忙或人为延迟时，下载完成后仍可正确校验、保存和打开 DMG。

### R09 拖拽选择卡在小窗格越界

定位：[固定卡片尺寸](D:/git/harness-portable/windows/HarnessPortable.Windows/MainWindow.xaml:271)、[cardLeft 和 cardTop](D:/git/harness-portable/windows/HarnessPortable.Windows/MainWindow.xaml.cs:472)。

修复：对小窗格采用紧凑布局或图标模式，让整个选择卡保持可见，并同时约束覆盖窗口边界。若把卡片放到目标窗格之外，需保持当前目标窗格稳定，避免指针移到卡片时被重新识别成相邻窗格。

验收：在最右、最下和极窄窗格中五个目标都可见、可选择，提示文字完整；100%、125%、150% 和 200% DPI，以及最大化和全屏均通过真实拖拽验证。

### R10 拖拽可见按钮和放置区域不一致

定位：[DetermineDropZone](D:/git/harness-portable/windows/HarnessPortable.Windows/MainWindow.xaml.cs:520)。

可见按钮位于带 10 DIP Padding 和 1 DIP 边框的内部 Grid；判断函数却把整个外框等分成三行三列。原 XAML 测量及原函数诊断中，指针位于“向上”按钮内部、距其下边界 1 DIP，函数返回 `Center`，会变成合并标签。

修复：按实际按钮或内部 Grid 的布局边界判定放置区域，避免复制一份与可见控件不同的几何模型。

验收：每个按钮的中心、四边及圆角附近都执行其显示的操作；改变 Padding、尺寸和 DPI 后保持一致。

### R11 标签标题不同步

定位：[CreateDirectDoc](D:/git/harness-portable/windows/HarnessPortable.Windows/MainWindow.xaml.cs:990)、[ReplaceDocumentContent](D:/git/harness-portable/windows/HarnessPortable.Windows/MainWindow.xaml.cs:1398)。

创建直连标签时没有订阅 SessionView.TitleChanged，因此修改 CustomLabel 后实际标签标题仍是原主机名，恢复预设中的标签名也同样不生效。诊断直接执行 SetCustomLabel，视图标题已改变而 LayoutDocument.Title 未变。内容切换后的订阅另有问题：闭包保留旧 `suffix`，即使重命名清空了字典中的序号，标题仍可能带 `(2)`。

修复：统一直连、隧道和内容切换的标题订阅，更新时读取当前后缀；恢复及复制自定义标签名走相同路径。

验收：直连标签重命名立即可见；保存后重启恢复名称；切换来源再重命名不会保留旧序号。

### R12 保存布局没有更新启动恢复目标

定位：[SaveLayout_Click](D:/git/harness-portable/windows/HarnessPortable.Windows/MainWindow.xaml.cs:1441)、[RefreshPresetBox](D:/git/harness-portable/windows/HarnessPortable.Windows/MainWindow.xaml.cs:1493)。

保存后只刷新并选中下拉框，刷新时又抑制了 SelectionChanged，因此没有执行更新 LastLayoutName 的路径。第一次保存布局后，启动可能仍为空；已有旧布局时，重启仍恢复旧的预设。

修复：保存成功后明确记录当前布局名称；删除当前恢复目标时也清理或更新该名称。

验收：新建并保存布局后直接退出，重启恢复新布局；删除该布局后不再引用失效的名称。

### R13 直连地址缺少有效性校验

定位：[NormalizeUrl](D:/git/harness-portable/windows/HarnessPortable.Windows/Services/ProfileStore.cs:114)。

函数只拼接或保留字符串，诊断输入 `http://`、`https://a b`、`javascript://alert(1)` 和 `file:///C:/test.txt` 都会被接受；裸 IPv6 `::1` 又被拼成无效的 `http://::1`。添加地址时无法及时指出问题，之后会产生无法使用的标签。

修复：用 URI 解析验证允许的协议、主机和端口；正确处理 IPv6 方括号，添加或保存前给出具体错误。

验收：无效地址不进入列表；IPv4、域名、带路径的 HTTP/HTTPS 和有效 IPv6 地址能正确保存及打开。

### R14 macOS 过早显示已连接

定位：[SSHProcessTunnel.launch](D:/git/harness-portable/macos/HarnessPortable/Services/SSHProcessTunnel.swift:401)、[Connected 状态发布](D:/git/harness-portable/macos/HarnessPortable/Services/SSHProcessTunnel.swift:268)。

SSH 进程启动 0.8 秒后仍存活，就返回并发布 Connected；进程存活并不能证明认证完成或本地监听已建立。在认证较慢、尤其手动网页认证模式下，界面可能先显示已连接并打开尚不可用的本地端口。

修复：等待明确的 SSH 就绪或本地转发可用信号，在超时前保持 Connecting。

验收：服务端认证延迟数秒时不会提前发布 Connected；认证失败时没有短暂的成功状态。

### R15 macOS 首次失败仍自动重试

定位：[SSHProcessTunnel.run 的 catch](D:/git/harness-portable/macos/HarnessPortable/Services/SSHProcessTunnel.swift:296)。

普通失败直接进入 Retrying，没有区分“首次从未成功”与“成功后断线”。例如首次启动时本地端口占用或 keyscan 后连接断开，会持续重试，与配置契约及 Windows/Android 的规则不一致。

修复：记录确实建立过会话的状态；首次未成功的失败终止，只有已建立会话后的网络断线进入退避重试。

验收：首次失败进入 Failed 并停止后台重试；成功连接后断线按 3、6、12 至 30 秒退避。

### R16 macOS 本地端口占用时缺少回退

定位：[唯一一次 launch 调用](D:/git/harness-portable/macos/HarnessPortable/Services/SSHProcessTunnel.swift:157)、[跨平台端口约定](D:/git/harness-portable/spec/config-schema.md:53)。

macOS 只尝试 profile.localPort，未按契约尝试 localPort 至 localPort+9。默认端口被其他隧道或程序占用时，Windows/Android 能寻找后续端口，macOS 会失败。

修复：区分端口绑定失败与其他 SSH 失败，只在绑定失败时递增候选端口；连接状态和网页 URL 使用最终绑定端口。

验收：首个端口被占用时成功使用下一可用端口；65535 边界不越界；认证失败不会因此额外重复认证。

### R17 Android Debug 构建依赖 Release 签名文件

定位：[signingConfigs](D:/git/harness-portable/app/build.gradle.kts:27)。

虽然读取 keystore.properties 时检查了文件是否存在，Release signingConfig 在所有 Gradle 配置阶段都会执行，并把可能不存在的 storeFile 等值强制转换成 String。新克隆仓库缺少这些未提交的文件时，Debug 构建也会在配置阶段失败。本轮本机 Gradle 在更早的 Java 环境问题处失败，因此此项为源码确认。

修复：仅在 Release 签名信息完整时创建并绑定该配置；Debug 独立构建；请求 Release 时给出清晰的缺失项错误。

验收：干净克隆、只有 JDK 和 Android SDK 时 assembleDebug 可成功；Release 缺少签名材料时明确失败且不回退到错误签名。

## 仍需实机验证的场景

这些是后续验收项，不计入已确认的 17 项问题：

- 普通大窗格拖拽提示不完整：最大化、全屏、跨显示器和不同 DPI 下确认覆盖窗口边界及坐标。
- 拖拽中按 Esc、Alt Tab、松开鼠标或丢失鼠标捕获：修复已补充状态清理，仍需完整鼠标交互确认。
- 真实 WebView2 网页上的认证、断连状态、浮动菜单及中文输入法。
- 真实 SSH 服务的首次失败、睡眠恢复、端口占用、多隧道并发和主机密钥变化。
- macOS 下载完成时主线程繁忙的情况。Android 无 Release 签名文件的干净环境 Debug 构建已通过。

## 诊断材料和执行顺序

诊断输出位于 [probe-results.jsonl](D:/git/harness-portable/temp/project-audit/evidence/probe-results.jsonl)，诊断程序位于 [Program.cs](D:/git/harness-portable/temp/project-audit/probe/Program.cs)。拖拽定位使用原 XAML 的实际尺寸和原放置区域判断函数；卡片定位公式按源码计算。标题诊断引用隔离源码副本，副本仅调整数据目录并关闭启动时的外部更新检查。

建议先处理 R03 至 R07 的凭据、信任和数据问题，再处理 R01、R02 和 R08 的浏览器及更新阻塞；拖拽专项修复同时覆盖 R09、R10 和真实 DPI 验收。之后完成标签和布局恢复及其他平台的 P2 项。每组修复都应增加针对触发条件的验证，当前通过的单元测试没有覆盖这些交互和异常场景。
