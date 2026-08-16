const fs = require("fs");
const path = require("path");
const sharp = require("sharp");
const png2icons = require("png2icons");
const pngToIco = require("png-to-ico").default;

const root = path.resolve(__dirname, "..");
const sourceDir = path.join(__dirname, "source");
const generatedDir = path.join(__dirname, "generated");
const androidResDir = path.join(root, "app", "src", "main", "res");
const windowsAssetsDir = path.join(root, "windows", "HarnessPortable.Windows", "Assets");

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
  // 4. macOS .icns
  // ---------------------------------------------------------------------
  const icns = png2icons.createICNS(masterSquare, png2icons.BICUBIC2, 0);
  if (!icns) {
    throw new Error("png2icons failed to create ICNS");
  }
  write(generatedDir, "HarnessPortable.icns", icns);
  console.log("macos: HarnessPortable.icns");

  console.log("done.");
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
