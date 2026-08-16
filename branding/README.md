# Harness Portable 品牌图标

DeepSeek 风格的极简双色图标：深蓝色底 `#4D6BFE` + 白色 H/隧道符号。
两条竖条代表本地与远程节点，中间横管是 SSH 隧道，中心钥匙孔代表凭据与
主机密钥保护。

## 源文件

```text
branding/source/
├─ icon.svg             # 圆角方形完整图标（Windows / macOS / Android legacy）
├─ icon-round.svg       # 圆形版本（Android round legacy）
└─ icon-foreground.svg  # 透明前景（Android adaptive icon foreground）
```

## 重新生成

```bash
cd branding
npm install
npm run generate
```

生成结果：

- Android：
  - `app/src/main/res/mipmap-*/ic_launcher.png`
  - `app/src/main/res/mipmap-*/ic_launcher_round.png`
  - `app/src/main/res/mipmap-*/ic_launcher_foreground.png`
  - 自适应背景色：`app/src/main/res/drawable/ic_launcher_background.xml`
- Windows：
  - `windows/HarnessPortable.Windows/Assets/HarnessPortable.ico`
  - `branding/generated/HarnessPortable.ico`
- macOS：
  - `branding/generated/HarnessPortable.icns`（Mac 工程直接引用，或交给
    `iconutil` / Xcode Assets）
- 母版 PNG：
  - `branding/generated/harness-portable-1024.png`
  - `branding/generated/harness-portable-round-1024.png`
  - `branding/generated/harness-portable-foreground-1024.png`

## 设计参数

- 画布：1024×1024
- 背景：DeepSeek 蓝 `#4D6BFE`
- 前景：纯白符号，只有两种颜色，无渐变/描边
- 自适应图标安全区：主体元素集中在画布中央约 52% 内，适配各平台圆形/异形遮罩
