import * as THREE from "three";
import { hexShade } from "../core/hex";
import type { BoardLayout } from "../core/types";

const LIGHT = new THREE.Color(0xf2d9b3);
const MID = new THREE.Color(0xb07840);
const DARK = new THREE.Color(0x2a1810);
const HEX = [new THREE.Color(0xf0d6b0), new THREE.Color(0xb07840), new THREE.Color(0x5c3a22)];
const SQUARE = [LIGHT, MID, DARK];

const WORLD_TILES = 64;
const TEX_SIZE = 2048;
const FRAME = 0.22;
const FADE_START = 1.1;
const FADE_END = 13;

export interface CheckerGround {
  map: THREE.CanvasTexture;
  mid: THREE.Color;
  cx: number;
  cz: number;
  size: number;
}

function mod(n: number, m: number): number {
  return ((n % m) + m) % m;
}

function isOdd(n: number): boolean {
  return mod(n, 2) === 1;
}

function boardZRange(height: number, layout: BoardLayout): { z0: number; z1: number } {
  if (layout === "hex-offset") return { z0: -0.5, z1: height - 0.5 };
  return { z0: 0, z1: height };
}

function shadeOf(x: number, y: number, layout: BoardLayout): number {
  if (layout === "hex-offset") return hexShade(x, y);
  return isOdd(x + y) ? 0 : 1;
}

function palette(layout: BoardLayout): THREE.Color[] {
  return layout === "hex-offset" ? HEX : SQUARE;
}

function averageColors(colors: THREE.Color[]): THREE.Color {
  const mid = new THREE.Color(0, 0, 0);
  for (const c of colors) mid.add(c);
  return mid.multiplyScalar(1 / colors.length);
}

function midColor(layout: BoardLayout): THREE.Color {
  const colors = palette(layout);
  if (layout === "hex-offset") return averageColors(colors);
  return colors[0].clone().lerp(colors[1], 0.5);
}

/** Floor sits behind the board: same hue, just enough to read as a quieter echo. */
function mute(color: THREE.Color, toward: THREE.Color): THREE.Color {
  const c = color.clone().lerp(toward, 0.62);
  const hsl = { h: 0, s: 0, l: 0 };
  c.getHSL(hsl);
  return c.setHSL(hsl.h, hsl.s * 0.36, hsl.l * 0.4 + 0.05);
}

function backdropColors(layout: BoardLayout): { shades: THREE.Color[]; mid: THREE.Color } {
  const src = palette(layout);
  const rawMid = midColor(layout);
  const used = layout === "hex-offset" ? src : src.slice(0, 2);
  const shades = src.map((c) => mute(c, rawMid));
  return { shades, mid: mute(averageColors(used), rawMid) };
}

function distToRect(x: number, z: number, x0: number, z0: number, x1: number, z1: number): number {
  const dx = x < x0 ? x0 - x : x > x1 ? x - x1 : 0;
  const dz = z < z0 ? z0 - z : z > z1 ? z - z1 : 0;
  return Math.hypot(dx, dz);
}

function smoothstep(edge0: number, edge1: number, x: number): number {
  const t = Math.min(1, Math.max(0, (x - edge0) / (edge1 - edge0)));
  return t * t * (3 - 2 * t);
}

function woodSource(map: THREE.Texture | null): CanvasImageSource | null {
  const img = map?.image as CanvasImageSource | undefined;
  if (!img) return null;
  if (img instanceof HTMLImageElement || img instanceof HTMLCanvasElement || img instanceof ImageBitmap) return img;
  return null;
}

function worldToPx(world: number, origin: number, size: number): number {
  return ((world - origin) / size) * TEX_SIZE;
}

export function bakeCheckerGround(opts: {
  width: number;
  height: number;
  layout: BoardLayout;
  wood?: THREE.Texture | null;
  cutBoard?: boolean;
}): CheckerGround {
  const { width, height, layout } = opts;
  const { shades: colors, mid } = backdropColors(layout);
  const { z0, z1 } = boardZRange(height, layout);
  const cx = width / 2;
  const cz = (z0 + z1) / 2;
  const size = WORLD_TILES;
  const minX = cx - size / 2;
  const minZ = cz - size / 2;
  const hex = layout === "hex-offset";
  const cut = opts.cutBoard !== false;
  const holePad = FRAME - 0.05;

  const canvas = document.createElement("canvas");
  canvas.width = TEX_SIZE;
  canvas.height = TEX_SIZE;
  const ctx = canvas.getContext("2d", { willReadFrequently: true })!;
  ctx.fillStyle = `#${mid.getHexString()}`;
  ctx.fillRect(0, 0, TEX_SIZE, TEX_SIZE);

  const x0 = Math.floor(minX) - 1;
  const x1 = Math.ceil(minX + size) + 1;
  const y0 = Math.floor(minZ) - 1;
  const y1 = Math.ceil(minZ + size) + 1;
  for (let x = x0; x < x1; x++) {
    for (let y = y0; y < y1; y++) {
      const tz = hex && isOdd(x) ? y - 0.5 : y;
      const sx = worldToPx(x, minX, size);
      const sy = worldToPx(tz, minZ, size);
      const sw = worldToPx(x + 1, minX, size) - sx;
      const sh = worldToPx(tz + 1, minZ, size) - sy;
      const shade = shadeOf(x, y, layout);
      ctx.fillStyle = `#${(colors[shade] ?? colors[0]).getHexString()}`;
      ctx.fillRect(sx, sy, sw + 0.6, sh + 0.6);
    }
  }

  const wood = woodSource(opts.wood ?? null);
  if (wood) {
    ctx.save();
    ctx.globalAlpha = 0.32;
    ctx.globalCompositeOperation = "multiply";
    const tilePx = TEX_SIZE / WORLD_TILES;
    for (let y = 0; y < TEX_SIZE; y += tilePx) {
      for (let x = 0; x < TEX_SIZE; x += tilePx) {
        ctx.drawImage(wood, x, y, tilePx, tilePx);
      }
    }
    ctx.restore();
  }

  const pixels = ctx.getImageData(0, 0, TEX_SIZE, TEX_SIZE);
  const data = pixels.data;
  const mr = Math.round(mid.r * 255);
  const mg = Math.round(mid.g * 255);
  const mb = Math.round(mid.b * 255);
  const cutX0 = -holePad;
  const cutZ0 = z0 - holePad;
  const cutX1 = width + holePad;
  const cutZ1 = z1 + holePad;

  for (let py = 0; py < TEX_SIZE; py++) {
    const wz = minZ + ((py + 0.5) / TEX_SIZE) * size;
    for (let px = 0; px < TEX_SIZE; px++) {
      const i = (py * TEX_SIZE + px) * 4;
      const wx = minX + ((px + 0.5) / TEX_SIZE) * size;
      if (cut && wx >= cutX0 && wx <= cutX1 && wz >= cutZ0 && wz <= cutZ1) {
        data[i + 3] = 0;
        continue;
      }
      const dist = distToRect(wx, wz, 0, z0, width, z1);
      const t = smoothstep(FADE_START, FADE_END, dist);
      if (t <= 0) continue;
      data[i] = Math.round(data[i] + (mr - data[i]) * t);
      data[i + 1] = Math.round(data[i + 1] + (mg - data[i + 1]) * t);
      data[i + 2] = Math.round(data[i + 2] + (mb - data[i + 2]) * t);
    }
  }
  ctx.putImageData(pixels, 0, 0);

  const map = new THREE.CanvasTexture(canvas);
  map.colorSpace = THREE.SRGBColorSpace;
  map.flipY = false;
  map.wrapS = map.wrapT = THREE.ClampToEdgeWrapping;
  map.magFilter = THREE.LinearFilter;
  map.minFilter = THREE.LinearMipmapLinearFilter;
  map.generateMipmaps = true;
  map.needsUpdate = true;

  return { map, mid, cx, cz, size };
}

export function createGroundGeometry(size: number): THREE.PlaneGeometry {
  const geo = new THREE.PlaneGeometry(size, size);
  geo.rotateX(-Math.PI / 2);
  const pos = geo.attributes.position;
  const uv = geo.attributes.uv;
  for (let i = 0; i < pos.count; i++) {
    uv.setXY(i, (pos.getX(i) + size / 2) / size, (pos.getZ(i) + size / 2) / size);
  }
  uv.needsUpdate = true;
  return geo;
}

export function fogRange(width: number, height: number): { near: number; far: number } {
  const span = Math.max(width, height);
  return { near: 13 + span * 0.7, far: 22 + span * 1.25 };
}

const FOG_HUES = [0.07, 0.12, 0.18, 0.55, 0.62, 0.72, 0.92];

export function subtleFogTint(_base: THREE.Color, rand: () => number = Math.random): THREE.Color {
  const h = FOG_HUES[Math.floor(rand() * FOG_HUES.length)] + (rand() - 0.5) * 0.035;
  const s = 0.12 + rand() * 0.1;
  const l = 0.36 + rand() * 0.1;
  return new THREE.Color().setHSL((h + 1) % 1, s, l);
}

function mulberry32(seed: number): () => number {
  let a = seed >>> 0;
  return () => {
    a += 0x6d2b79f5;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

export function fogSeedFromNodes(width: number, height: number, types: string[]): number {
  let h = 2166136261 ^ (width * 374761393 + height);
  for (let i = 0; i < types.length; i++) {
    h = Math.imul(h ^ (i + 1 + types[i].charCodeAt(0)), 16777619);
  }
  return h >>> 0;
}

export function fogRandFromSeed(seed: number): () => number {
  return mulberry32(seed);
}
