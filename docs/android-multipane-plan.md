# Android 多标签 / 多分屏方案（待实施）

目标：Android 端也能像桌面端一样同时使用多个隧道/直连，并针对折叠屏和平板提供分屏。

## 推荐交互

| 设备 | 推荐布局 |
|---|---|
| 普通手机 | 单窗口 + 顶部 Tab 切换 |
| 折叠屏展开 | 左右两个 pane，中缝对齐铰链 |
| 平板 | 2×2 四 pane（可切换 1/2/4 布局） |
| 所有设备 | 每个 pane 内可有多 Tab |

## 第一阶段：多隧道核心（必做前提）

当前 Android 是单例 `SshTunnelService` + 单一 `TunnelState`。需要：

1. 抽出与 Windows 版对应的 `TunnelManager`：
   - 每个 profile 一个独立 engine/session；
   - 每个 engine 有自己的 `StateFlow<TunnelInfo>`；
   - 前台服务只负责把进程保活，不再绑定单个隧道。
2. `SecureStore` / `ProfileStore` 保持不变，只增加并发调用安全性。
3. WebView 会话模型：`SessionTab(profileId|directUrl, label, stateFlow)`。

## 第二阶段：手机 Tab

- 用 Compose 的 `PrimaryTabRow` / `ScrollableTabRow` 显示会话标签；
- 每个 Tab 内容是一个 `WebViewScreen`；
- 与桌面端一致的右键等价操作：长按标签弹出“复制 / 重命名 / 切换为 / 关闭”；
- 标签 label 与桌面端共用 `LayoutTabRef.Label` 语义。

## 第三阶段：折叠屏左右分屏

使用 Jetpack WindowManager：

```kotlin
WindowInfoTracker.getOrCreate(context)
    .windowLayoutInfo(activity)
    .collect { layoutInfo ->
        val hinge = layoutInfo.displayFeatures
            .filterIsInstance<FoldingFeature>()
            .firstOrNull()
        // hinge.bounds 决定左右 pane 的宽度，两个 WebView 分别避开铰链
    }
```

- 展开时自动切成左右两栏；
- 合上时回到 Tab 模式；
- 每个栏内部可再放 Tab 组。

## 第四阶段：平板 2×2

用 `WindowSizeClass`：

```kotlin
val widthClass = currentWindowAdaptiveInfo().windowSizeClass.windowWidthSizeClass
// Compact  → 单栏 + Tab
// Medium   → 左右两栏
// Expanded → 2×2 四 pane（横屏时）
```

- 四 pane 是固定网格（不引入 Android 版递归分屏树，复杂度不划算）；
- 每个 pane 顶部一条小 Tab 条，可切换该 pane 显示哪个会话；
- 长按 pane 标题可“切换为/复制/重命名”。

## 与桌面布局预设互通

- 桌面预设 JSON 的结构已经通用：`LayoutNode(Kind=pane|split, Tabs...)`；
- Android 只需实现 `LayoutPresetStore` 的同格式读写（Android 文件路径：
  `filesDir/layouts.json` 或同步到 `%APPDATA%` 对应目录）；
- Android 四 pane 固定网格映射为：
  - 根 `split(horizontal)` → 两个 `split(vertical)` → 每个含两个 pane；
- 手机 Tab 映射为单 pane、多个 Tabs。

## 建议实施顺序

1. Android `TunnelManager` + 多 WebView Tab（手机先受益）；
2. 折叠屏铰链双栏；
3. 平板 Expanded 2×2；
4. 布局预设导入/导出（与 Windows 同格式）。

> 该方案不动现有 Android 的 Compose 风格与悬浮球逻辑，只把“单会话”升级为“多会话容器”。
