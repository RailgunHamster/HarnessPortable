/**
 * 由 geometry.js 生成三个 SVG 源文件。
 *
 *   npm run write-sources    写入 branding/source/*.svg
 *   npm run check            只校验源文件是否与 geometry.js 一致（CI / 手工检查用）
 *
 * 图标几何只有 geometry.js 一处真源；需要改造型时改那里再跑一次。
 */
const fs = require("fs");
const path = require("path");
const G = require("./geometry");

const sourceDir = path.join(__dirname, "source");
const checkOnly = process.argv.includes("--check");

const files = [
  ["icon.svg", G.squareSvg()],
  ["icon-round.svg", G.roundSvg()],
  ["icon-foreground.svg", G.foregroundSvg()],
];

let drift = 0;
for (const [name, content] of files) {
  const file = path.join(sourceDir, name);
  const existing = fs.existsSync(file) ? fs.readFileSync(file, "utf8") : null;
  const same = existing === content;

  if (checkOnly) {
    console.log(`${same ? "  ok  " : "DRIFT "} ${name}`);
    if (!same) drift++;
    continue;
  }
  fs.writeFileSync(file, content);
  console.log(`${existing === null ? "create" : same ? "  ok  " : "update"} ${name}`);
}

if (checkOnly && drift > 0) {
  console.error(`\n${drift} 个源文件与 geometry.js 不一致，请运行: npm run write-sources`);
  process.exit(1);
}

if (!checkOnly) {
  console.log("\n几何摘要（画布 1024）");
  for (const label of ["square", "round", "foreground"]) {
    const k = G.SCALE[label];
    const g = G.mark(k);
    console.log(
      `  ${label.padEnd(11)} k=${k.toFixed(3)}  外框=${g.frame.w.toFixed(0)}x${g.frame.h.toFixed(0)}` +
      `  线宽=${g.stroke.toFixed(1)}  外接半径=${G.circumradius(k).toFixed(1)}`
    );
  }
  console.log(`\n  约束: 圆形容器 r=${G.CONTAINER_ROUND.radius}  Android 安全圈 r=${G.SAFE_CIRCLE_RADIUS.toFixed(1)}`);
  const rOK = G.circumradius(G.SCALE.round) <= G.CONTAINER_ROUND.radius;
  const fOK = G.circumradius(G.SCALE.foreground) <= G.SAFE_CIRCLE_RADIUS + 1e-9;
  console.log(`  round       ${rOK ? "PASS" : "FAIL"} 圆内切`);
  console.log(`  foreground  ${fOK ? "PASS" : "FAIL"} 安全圈内切`);

  // 居中自检：任何 k 下外接矩形都应关于画布中心对称
  for (const label of ["square", "round", "foreground"]) {
    const k = G.SCALE[label];
    const b = G.markBox();
    const left = G.CENTER + (b.x0 - G.CENTER) * k;
    const right = G.CENTER + (b.x1 - G.CENTER) * k;
    const top = G.CENTER + (b.y0 - G.CENTER) * k;
    const bottom = G.CENTER + (b.y1 - G.CENTER) * k;
    const dx = Math.abs(left - (G.CANVAS - right));
    const dy = Math.abs(top - (G.CANVAS - bottom));
    console.log(`  ${label.padEnd(11)} 居中偏差 ${dx.toFixed(2)} / ${dy.toFixed(2)}  ${dx < 0.6 && dy < 0.6 ? "PASS" : "FAIL"}`);
  }
}
