# Harness Portable 品牌图标

## 设计

**深蓝底 + 白色「终端窗口 + 命令提示符」**。

- 底：DeepSeek 蓝 `#4D6BFE`，圆角方形 / 圆形，Android 自适应前景用透明底
- 图形：纯白，无渐变、无描边、无阴影
  - 圆角方框 = 打开的那个控制台窗口（也正是内嵌浏览器的那个窗口）
  - `>` 与 `_` = 命令行提示符，也就是敲进去的 `ssh -L`

一个符号同时说清两件事：**这是 SSH 命令行工具**，而且**它在窗口里跑**。

### 为什么是这个符号

前面试过三版，都因为「不像这个软件」被否掉，记录在这里免得再走回头路：

| 方案 | 问题 |
|---|---|
| 蓝底 + 白色 H + 钥匙孔 | 蓝色当底、白色当图形，颜色关系反了；缩到 48px 以下横杠被中心圆盘吃掉，只剩两根白条，像暂停键 ⏸；钥匙孔在 32px 只剩约 3px，读不出来 |
| 白底 + 蓝色 H（字母图标） | 字母没有含义，视觉上就是个普通大写 H，跟任何叫 H 的东西都能撞 |
| 深蓝底 + 钥匙孔 + 箭头 | 大尺寸下钥匙孔的剪影会被看成**人形**（头 + 肩），语义又和系统钥匙串图标撞车 |

最后选「终端窗口」是因为：缩到 16px 仍然能认出 `>_`，语义直接对应
`ssh -N -L ...` 和内置 WebView，而且它不是通用符号（不是箭头那种
“前进/分享”，任何 App 都能用）。

## 源文件

```text
branding/
├─ geometry.js          # 几何唯一真源（改造型只改这里）
├─ write-sources.js     # geometry.js -> source/*.svg
├─ generate-icons.js    # source/*.svg -> 各平台产物（渲染前校验一致性）
└─ source/
   ├─ icon.svg             # 蓝底圆角方 + 白符号（Windows / macOS / favicon / Android legacy）
   ├─ icon-round.svg       # 蓝圆 + 白符号（Android round legacy）
   └─ icon-foreground.svg  # 透明底 + 蓝符号（Android 自适应前景）
```

## 重新生成

```powershell
cd branding
npm install
npm run write-sources   # 只有改了几何才需要
npm run generate
npm run check           # 校验 source/*.svg 是否与 geometry.js 一致
```

`generate` 渲染前会做两件事，不一致就直接报错退出：

1. `source/*.svg` 必须与 `geometry.js` 生成的内容逐字节一致；
2. 圆形底与 Android 安全圈必须完整容纳图形外接圆。

生成结果：

| 目标 | 产物 |
|---|---|
| Android | `app/src/main/res/mipmap-*/ic_launcher.png`、`ic_launcher_round.png`、`ic_launcher_foreground.png` |
| Android 自适应底色 | `app/src/main/res/drawable/ic_launcher_background.xml`（`#4D6BFE`，这个颜色就在这个文件里） |
| Windows | `windows/HarnessPortable.Windows/Assets/HarnessPortable.ico`（16/24/32/48/64/128/256）、`HarnessPortable.png`（256，窗口标题栏用） |
| macOS | `macos/HarnessPortable/Resources/HarnessPortable.icns`、`Resources/Assets.xcassets/AppIcon.appiconset/*.png`（10 个尺寸） |
| 母版 PNG | `branding/generated/harness-portable-1024.png`、`-round-1024.png`、`-foreground-1024.png` |

## 设计参数

画布 `1024×1024`。基准几何（`k = 1`）：外框 `720×624`、圆角 `132`、
线宽 `80`、提示符折角 `>` 起止 `(338,390)→(462,512)→(338,634)`、下划线 `200×86`。

每种底各自定标，保证图形在每个形状里占的视觉比例一致：

| 底 | k | 外框 | 线宽 | 外接半径 | 约束 |
|---|---|---|---|---|---|
| 圆角方形 | 1.000 | 720×624 | 80.0 | 532.8 | 直接使用基准几何 |
| 圆形 | 0.904 | 651×564 | 72.3 | 481.6 | ≤ 容器半径 484 |
| 自适应前景 | 0.584 | 421×365 | 46.7 | 311.3 | ≤ 安全圈 312.9 |

实测左右留白：圆角方形 112px，圆形 101px。

### 三个坑（改代码时注意）

1. **缩放必须以画布中心为原点**。`geometry.js` 里用 `sc(v, k) = 512 + (v-512)*k`。
   如果图省事写成 `v * k`，那是按画布左上角缩放：`k < 1` 时整个图形会往左上角跑，
   外接半径反而**变大**，`fitK()` 会被带偏到几乎 0，图形缩成一小点。
   `write-sources.js` 会打印居中偏差自检，正常应当是 `0.00 / 0.00`。
2. **自适应前景必须按安全圈内切**。Android 自适应图标的 108dp 画布上，
   只有 66dp 圆是「任何厂商遮罩下都必然可见」的，半径约 `512 × 66/108 = 312.9`。
   外接角必须落在这个圆内，否则圆形遮罩会切掉方框的四角。
   代价是自适应版里的符号比 legacy 版小（0.584 vs 1.000），这是约束的必然结果。
3. **自适应前景的颜色必须和底色相反**。Android 把 adaptive 的 `background`
   和 `foreground` 直接叠在一起：底色是 DeepSeek 蓝（见
   `app/src/main/res/drawable/ic_launcher_background.xml`），所以前景**必须白色**。
   前景若也做成蓝色，合成结果就是一整块纯蓝、mark 完全不可见。

### 校验自适应图标（不要只看 legacy）

`--foreground` 是**透明底**的，单独看它「有蓝色 content」会觉得正常，
但那是两套渲染路径：

| 路径 | 期望 |
|---|---|
| legacy `ic_launcher.png` | 蓝底 + 白 mark |
| adaptive `ic_launcher.xml` 合成后 | 蓝底 + 白 mark（与 legacy 观感一致） |

改完图标务必合成验证一次，别只看单张：

```powershell
# 把 xxxhdpi 前景叠到蓝底上，再看白/蓝像素占比
node -e "..."   # 或参考 git 历史里 temp/verify-apk-icon.js 的做法
```

判据：前景里**白 > 5% 且蓝 < 1%**。前景蓝色占比高 = 和底色撞色，图标会是纯色块。

`.ico` 的帧是 BMP/DIB 编码（不是 PNG），这是 `png-to-ico` 的正常输出。
