/**
 * Harness Portable — 图标几何定义（唯一真源）
 *
 * 设计定稿：
 *   深蓝底 + 白色「终端窗口 + 命令提示符」。
 *
 *   造型：圆角方框（窗口 / 内嵌浏览器）+ `>` 与 `_`（命令行提示符）。
 *   方框 = 打开的那个控制台窗口；提示符 = 敲进去的 ssh -L 命令。
 *   一个符号同时说清「SSH 命令行工具」和「窗口里跑」两件事。
 *
 *   不用首字母 H：字母没有含义，缩到 16px 只剩一个 H 形色块。
 *   不用钥匙孔：它的剪影在大尺寸下会被看成「人形」，语义还和系统钥匙串图标撞车。
 *   不用纯箭头：那是通用的“前进/分享”，任何 App 都能用，没有身份。
 *
 * 所有 SVG 源文件由本文件生成，generate-icons.js 渲染前会用 assertSourcesInSync()
 * 校验源文件中的几何是否与这里一致，避免模板与产物脱节。
 */

const CANVAS = 1024;
const CENTER = CANVAS / 2;

/** 品牌主色 */
const BLUE = "#4D6BFE";
const WHITE = "#FFFFFF";

/** 圆角方形底：Windows / macOS / favicon / Android legacy */
const CONTAINER_SQUARE = { x: 28, y: 28, side: 968, radius: 230 };
/** 圆形底：Android round legacy */
const CONTAINER_ROUND = { kind: "circle", cx: CENTER, cy: CENTER, radius: 484 };
/** Android 自适应图标安全圈：108dp 画布中「必然可见」的 66dp 圆 */
const SAFE_CIRCLE_RADIUS = CENTER * (66 / 108);

/** 内切类容器再收 0.5%，吸收取整误差，保证严格落在圆内 */
const FIT_SAFETY = 0.995;

/**
 * 基准几何（k = 1，画布 1024）——「终端窗口 + 提示符」。
 * frame*   : 窗口外框矩形与其圆角
 * stroke   : 外框线条粗细
 * glyph*   : 提示符（`>` 与 `_`）的位置与尺寸
 */
const BASE = {
  frame: { x: 152, y: 200, w: 720, h: 624, r: 132 },
  stroke: 80,
  chevron: { x0: 338, y0: 390, x1: 462, y1: 512, y2: 634, sw: 86 },
  cursor: { x: 528, y: 576, w: 200, h: 86 },
};

const round1 = (v) => Math.round(v * 10) / 10;
/**
 * 以画布中心为原点缩放一个坐标。
 * 注意：不能直接写 `x * k` —— 那样是以画布左上角 (0,0) 为原点缩放，
 * k < 1 时整个图形会往左上角跑、外接半径反而变大。
 */
const sc = (v, k) => CENTER + (v - CENTER) * k;

/** k = 1 时整个图形的外接矩形（用于按容器内切） */
function markBox() {
  const s = BASE.stroke / 2 + BASE.chevron.sw / 2;
  const x0 = BASE.frame.x - BASE.stroke / 2;
  const y0 = BASE.frame.y - BASE.stroke / 2;
  const x1 = BASE.frame.x + BASE.frame.w + BASE.stroke / 2;
  const y1 = BASE.frame.y + BASE.frame.h + BASE.stroke / 2;
  return { x0, y0, x1, y1, w: x1 - x0, h: y1 - y0 };
}
const BOX = markBox();

/** 缩放 k 之后的全部几何（以画布中心为原点，含四舍五入） */
function mark(k) {
  const b = BASE;
  return {
    k,
    frame: {
      x: round1(sc(b.frame.x, k)), y: round1(sc(b.frame.y, k)),
      w: round1(b.frame.w * k), h: round1(b.frame.h * k), r: round1(b.frame.r * k),
    },
    stroke: round1(b.stroke * k),
    chevron: {
      x0: round1(sc(b.chevron.x0, k)), y0: round1(sc(b.chevron.y0, k)),
      x1: round1(sc(b.chevron.x1, k)), y1: round1(sc(b.chevron.y1, k)), y2: round1(sc(b.chevron.y2, k)),
      sw: round1(b.chevron.sw * k),
    },
    cursor: {
      x: round1(sc(b.cursor.x, k)), y: round1(sc(b.cursor.y, k)),
      w: round1(b.cursor.w * k), h: round1(b.cursor.h * k),
    },
  };
}

/** 从中心到外接矩形最远外角的距离 —— 校验圆形遮罩是否切到图形 */
function circumradius(k) {
  let max = 0;
  for (const [x, y] of [[BOX.x0, BOX.y0], [BOX.x1, BOX.y0], [BOX.x0, BOX.y1], [BOX.x1, BOX.y1]]) {
    max = Math.max(max, Math.hypot(sc(x, k) - CENTER, sc(y, k) - CENTER));
  }
  return max;
}

/** 求最大缩放系数，使图形外接圆落在半径 r 之内（外接半径对 k 线性） */
function fitK(radius) {
  return (radius * FIT_SAFETY) / circumradius(1);
}

/**
 * 三种容器各自定标：
 *   square     : 直接使用基准几何（k = 1）
 *   round      : 内切于白圆，四角不被圆边切到
 *   foreground : 内切于 Android 安全圈，任何厂商遮罩下都完整
 */
const SCALE = {
  square: 1,
  round: fitK(CONTAINER_ROUND.radius),
  foreground: fitK(SAFE_CIRCLE_RADIUS),
};

const r = (v) => round1(v).toString();

/** 图形本体（fill 由调用方决定） */
function markSvg(k, indent) {
  const g = mark(k);
  const f = g.frame;
  const ch = g.chevron;
  const cu = g.cursor;
  // 用「外框矩形 + 内框矩形」的 evenodd 路径画出真正的方框（比 stroke 更可控）
  const half = g.stroke / 2;
  const outer = { x: f.x - half, y: f.y - half, w: f.w + g.stroke, h: f.h + g.stroke, r: f.r + half };
  const rect = (q) =>
    `M${r(q.x)} ${r(q.y + q.r)}a${r(q.r)} ${r(q.r)} 0 0 1 ${r(q.r)} ${r(-q.r)}` +
    `h${r(q.w - 2 * q.r)}a${r(q.r)} ${r(q.r)} 0 0 1 ${r(q.r)} ${r(q.r)}` +
    `v${r(q.h - 2 * q.r)}a${r(q.r)} ${r(q.r)} 0 0 1 ${r(-q.r)} ${r(q.r)}` +
    `h${r(-(q.w - 2 * q.r))}a${r(q.r)} ${r(q.r)} 0 0 1 ${r(-q.r)} ${r(-q.r)}Z`;
  const pad = { x: outer.x + g.stroke, y: outer.y + g.stroke, w: outer.w - 2 * g.stroke, h: outer.h - 2 * g.stroke, r: Math.max(4, outer.r - g.stroke) };
  const i = indent;
  return [
    `${i}<path fill-rule="evenodd" d="${rect(outer)} ${rect(pad)}"/>`,
    `${i}<path fill="none" stroke-width="${r(ch.sw)}" stroke-linecap="round" stroke-linejoin="round"`,
    `${i}      d="M${r(ch.x0)} ${r(ch.y0)} L${r(ch.x1)} ${r(ch.y1)} L${r(ch.x0)} ${r(ch.y2)}"/>`,
    `${i}<rect x="${r(cu.x)}" y="${r(cu.y)}" width="${r(cu.w)}" height="${r(cu.h)}" rx="${r(cu.h / 2)}"/>`,
  ].join("\n");
}

/** icon.svg —— 蓝底圆角方 + 白色窗口符号 */
function squareSvg() {
  const c = CONTAINER_SQUARE;
  return `<svg width="1024" height="1024" viewBox="0 0 1024 1024" xmlns="http://www.w3.org/2000/svg">
  <rect x="${c.x}" y="${c.y}" width="${c.side}" height="${c.side}" rx="${c.radius}" fill="${BLUE}"/>

  <g fill="${WHITE}" stroke="${WHITE}">
${markSvg(SCALE.square, "    ")}
  </g>
</svg>
`;
}

/** icon-round.svg —— 蓝圆 + 白色窗口符号 */
function roundSvg() {
  const c = CONTAINER_ROUND;
  return `<svg width="1024" height="1024" viewBox="0 0 1024 1024" xmlns="http://www.w3.org/2000/svg">
  <circle cx="${c.cx}" cy="${c.cy}" r="${c.radius}" fill="${BLUE}"/>

  <g fill="${WHITE}" stroke="${WHITE}">
${markSvg(SCALE.round, "    ")}
  </g>
</svg>
`;
}

/** icon-foreground.svg —— 透明底 + 蓝色窗口符号（Android 自适应前景） */
function foregroundSvg() {
  return `<svg width="1024" height="1024" viewBox="0 0 1024 1024" xmlns="http://www.w3.org/2000/svg">
  <g fill="${BLUE}" stroke="${BLUE}">
${markSvg(SCALE.foreground, "    ")}
  </g>
</svg>
`;
}

module.exports = {
  CANVAS, CENTER, BLUE, WHITE, BASE, SCALE, BOX,
  CONTAINER_SQUARE, CONTAINER_ROUND, SAFE_CIRCLE_RADIUS,
  mark, markBox, circumradius, fitK,
  squareSvg, roundSvg, foregroundSvg,
};
