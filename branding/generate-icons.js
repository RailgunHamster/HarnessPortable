const fs = require("fs");
const path = require("path");
const sharp = require("sharp");
const png2icons = require("png2icons");
const pngToIco = require("png-to-ico").default;
const G = require("./geometry");

const root = path.resolve(__dirname, "..");
const sourceDir = path.join(__dirname, "source");
const generatedDir = path.join(__dirname, "generated");
const androidResDir = path.join(root, "app", "src", "main", "res");
const windowsAssetsDir = path.join(root, "windows", "HarnessPortable.Windows", "Assets");
const macosResourcesDir = path.join(root, "macos", "HarnessPortable", "Resources");

/**
 * 源文件必须与 geometry.js 一致，否则渲染出来的产物和「唯一真源」脱节。
 * 改了造型就跑 `npm run write-sources` 再生成。
 */
function assertSourcesInSync() {
  const expected = {
    "icon.svg": G.squareSvg(),
    "icon-round.svg": G.roundSvg(),
    "icon-foreground.svg": G.foregroundSvg(),
  };
  const drift = Object.entries(expected)
    .filter(([name, content]) => fs.readFileSync(path.join(sourceDir, name), "utf8") !== content)
    .map(([name]) => name);
  if (drift.length > 0) {
    throw new Error(
      `source/*.svg 与 geometry.js 不一致: ${drift.join(", ")}\n` +
      `先运行: npm run write-sources`
    );
  }
  console.log(`geometry: ${drift.length === 0 ? "sources in sync" : "DRIFT"}`);
}

/** 平台约束自检：圆形与安全圈不能被外接角切到 */
function assertContainment() {
  const checks = [
    ["round", G.circumradius(G.SCALE.round), G.CONTAINER_ROUND.radius],
    ["foreground", G.circumradius(G.SCALE.foreground), G.SAFE_CIRCLE_RADIUS],
  ];
  for (const [name, actual, limit] of checks) {
    if (actual > limit + 1e-6) {
      throw new Error(`${name}: 外接半径 ${actual.toFixed(1)} 超出容器 ${limit.toFixed(1)}`);
    }
    console.log(`containment ${name}: ${actual.toFixed(1)} <= ${limit.toFixed(1)} ok`);
  }
}

const readSvg = (name) => fs.readFileSync(path.join(sourceDir, name));

async function renderPng(svgName, size) {
  const svg = readSvg(svgName);
  return sharp(svg, { density: 72 })
    .resize(size, size, { kernel: sharp.kernel.lanczos3 })
    .png()
    .toBuffer();
}

function write(baseDir, relative, buffer) {
  const file = path.join(baseDir, relative);
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, buffer);
  return file;
}

async function main() {
  fs.mkdirSync(generatedDir, { recursive: true });
  assertSourcesInSync();
  assertContainment();

  // ---------------------------------------------------------------------
  // 1. Master PNGs
  // ---------------------------------------------------------------------
  const masterSquare = await renderPng("icon.svg", 1024);
  const masterRound = await renderPng("icon-round.svg", 1024);
  const masterForeground = await renderPng("icon-foreground.svg", 1024);

  write(generatedDir, "harness-portable-1024.png", masterSquare);
  write(generatedDir, "harness-portable-round-1024.png", masterRound);
  write(generatedDir, "harness-portable-foreground-1024.png", masterForeground);

  // ---------------------------------------------------------------------
  // 2. Android legacy launcher icons
  // ---------------------------------------------------------------------
  const androidDensities = [
    { name: "mdpi", legacy: 48, foreground: 108 },
    { name: "hdpi", legacy: 72, foreground: 162 },
    { name: "xhdpi", legacy: 96, foreground: 216 },
    { name: "xxhdpi", legacy: 144, foreground: 324 },
    { name: "xxxhdpi", legacy: 192, foreground: 432 },
  ];

  for (const density of androidDensities) {
    const dir = path.join(androidResDir, `mipmap-${density.name}`);
    write(dir, "ic_launcher.png", await renderPng("icon.svg", density.legacy));
    write(dir, "ic_launcher_round.png", await renderPng("icon-round.svg", density.legacy));
    write(
      dir,
      "ic_launcher_foreground.png",
      await renderPng("icon-foreground.svg", density.foreground)
    );
    console.log(`android ${density.name}: legacy ${density.legacy}px, foreground ${density.foreground}px`);
  }

  // ---------------------------------------------------------------------
  // 3. Windows .ico and a PNG asset for WPF window/title-bar icons
  // ---------------------------------------------------------------------
  const icoSizes = [16, 24, 32, 48, 64, 128, 256];
  const icoFiles = [];
  for (const size of icoSizes) {
    const file = path.join(generatedDir, `ico-${size}.png`);
    fs.writeFileSync(file, await renderPng("icon.svg", size));
    icoFiles.push(file);
  }

  const ico = await pngToIco(icoFiles);
  write(windowsAssetsDir, "HarnessPortable.ico", ico);
  write(generatedDir, "HarnessPortable.ico", ico);

  for (const file of icoFiles) {
    fs.rmSync(file, { force: true });
  }

  const windowPng = await renderPng("icon.svg", 256);
  write(windowsAssetsDir, "HarnessPortable.png", windowPng);
  write(generatedDir, "HarnessPortable.png", windowPng);
  console.log("windows: HarnessPortable.ico + HarnessPortable.png");

  // ---------------------------------------------------------------------
  // 4. macOS .icns + AppIcon.appiconset
  //
  // macOS 工程既引用 .icns，也引用 Assets.xcassets 里的 appiconset；
  // 两处都由这里生成，避免手工维护的副本漏更（原来 .appiconset 就是旧的）。
  // 注意：macOS 图标自带圆角遮罩，这里用的是带白底的圆角方形母版，
  // 系统再套一层圆角即可，不会出现双层圆角。
  // ---------------------------------------------------------------------
  const icns = png2icons.createICNS(masterSquare, png2icons.BICUBIC2, 0);
  if (!icns) {
    throw new Error("png2icons failed to create ICNS");
  }
  write(generatedDir, "HarnessPortable.icns", icns);
  write(macosResourcesDir, "HarnessPortable.icns", icns);

  const macAppIconDir = path.join(macosResourcesDir, "Assets.xcassets", "AppIcon.appiconset");
  const macAppIconFiles = [
    ["icon_16x16.png", 16],
    ["icon_16x16@2x.png", 32],
    ["icon_32x32.png", 32],
    ["icon_32x32@2x.png", 64],
    ["icon_128x128.png", 128],
    ["icon_128x128@2x.png", 256],
    ["icon_256x256.png", 256],
    ["icon_256x256@2x.png", 512],
    ["icon_512x512.png", 512],
    ["icon_512x512@2x.png", 1024],
  ];
  for (const [name, size] of macAppIconFiles) {
    write(macAppIconDir, name, await renderPng("icon.svg", size));
  }
  console.log(`macos: HarnessPortable.icns + AppIcon.appiconset (${macAppIconFiles.length} png)`);

  console.log("done.");
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
