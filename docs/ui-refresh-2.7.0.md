# 2.7.0 原生界面更新

Windows、Android 和 macOS 的界面统一采用蓝色强调色、分层背景、圆角卡片与系统字体，并跟随系统深浅色。管理页拆分为隧道、直连和设置，连接操作、状态、空列表及错误提示具有清晰的层级。网页继续使用各平台原生浏览器容器。

Windows 的工作区、标签、拖拽提示、认证弹层、编辑器及菜单共用主题，标题栏也跟随系统外观。窄分屏中的认证与状态提示可以滚动，操作按钮可以换行。Android 为连接行提供更多操作菜单，保留独立分区的滚动位置，限制大屏列表宽度，输入区域避让键盘，更新按钮可以换行。macOS 保留原生分段选择器和菜单，统一工具栏、标签、卡片、状态及空列表。

## 性能检查

没有增加界面依赖、模糊、持续动画或逐帧定时器。WPF 使用冻结画刷，拖拽复用主题画刷；相同外观的重复系统通知不会重建主题。管理页在关闭后解除事件订阅。Android 缓存两套配色，继续使用带稳定键的 LazyColumn；macOS 管理页使用 LazyVStack。浏览器实例与隧道服务的生命周期保持原有方式。

WPF 使用相同的三条隧道和两条直连样例，在 780 / 940 DIP 宽度之间交替执行 60 次布局，舍弃前 10 次：修复前页面布局中位数约 10.5 ms，新页面约 2.7 ms；每次布局的线程分配约 500 KB / 114 KB，视觉树元素为 268 / 137。主要原因是未选中的设置及直连页面不再同时参与当前页面布局。数据来自离屏 XAML 布局样本，不能据此推断网页帧率、整机性能或多显示器拖拽表现。

正常文字与主按钮在核心背景上的对比度经过计算检查。浅色次要文字进一步加深，避免小号文字过淡。

## 验证和预览

- Windows：162 项测试通过；验证原 XAML 的拖拽按钮边界；检查管理页三个分区、空状态及深浅色。预览采用 WPF 视觉树导出的 XPS 栅格化，包含真实控件布局，不是桌面截屏，不包含原生网页像素。
- Android：Debug 构建和 lint 通过；在隔离预览包中检查 320 DIP、小屏 1.3 倍字体、长名称、地址换行和更多操作菜单。预览包使用不同 applicationId，没有覆盖模拟器中的正式应用。
- macOS：33 项测试通过，包含 NSHostingView 的深浅色控件渲染检查；Apple Silicon / Intel 通用构建通过。[验证记录](https://github.com/RailgunHamster/HarnessPortable/actions/runs/38253849591)。

Windows 预览：

![Windows 浅色连接中心](D:/git/harness-portable/artifacts/ui-preview/windows/after-light-0.png)

![Windows 深色设置](D:/git/harness-portable/artifacts/ui-preview/windows/after-dark-2.png)

macOS 预览：

![macOS 深色连接中心](D:/git/harness-portable/artifacts/ui-preview/macos/macos-dark.png)

Android 预览：

![Android 连接中心](D:/git/harness-portable/artifacts/ui-preview/android/portrait-connections.png)

## 发布范围

沿用用户指定的范围，本次发布 Windows 与 Android，macOS 界面源码和通用构建已完成，DMG 保留为本地构建产物。

仍需实机检查不同显示器及 DPI 下的完整鼠标拖拽、真实网页与输入法，以及长时间运行的资源使用情况。
