# Harness Portable 品牌图标

统一风格的“H + 安全隧道”图标：两个竖条代表本地与远程节点，中间横管是 SSH
隧道，中心钥匙孔代表凭据与主机密钥保护。

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
- 背景：深蓝渐变 `#0B1220 → #132A63 → #1D4ED8`
- 前景：白色渐变竖条 + 青色渐变横管 + 中心锁孔
- 自适应图标安全区：主体元素集中在画布中央约 52% 内，适配各平台圆形/异形遮罩
