import * as THREE from "three";
import type { PatternId } from "../core/colors";

const SIZE = 256;
const TILES = 3;

function canvas(): CanvasRenderingContext2D {
  const el = document.createElement("canvas");
  el.width = SIZE;
  el.height = SIZE;
  const ctx = el.getContext("2d")!;
  ctx.clearRect(0, 0, SIZE, SIZE);
  return ctx;
}

function fillField(ctx: CanvasRenderingContext2D, field: string): void {
  ctx.fillStyle = field;
  ctx.fillRect(0, 0, SIZE, SIZE);
}

function motifAt(ctx: CanvasRenderingContext2D, color: string, draw: () => void): void {
  ctx.save();
  ctx.translate(SIZE / 2, SIZE / 2);
  ctx.scale(SIZE * 0.34, SIZE * 0.34);
  ctx.fillStyle = color;
  ctx.strokeStyle = color;
  draw();
  ctx.restore();
}

function fleur(ctx: CanvasRenderingContext2D): void {
  const petal = (angle: number, sx: number, sy: number) => {
    ctx.save();
    ctx.rotate(angle);
    ctx.scale(sx, sy);
    ctx.beginPath();
    ctx.moveTo(0, 0.15);
    ctx.bezierCurveTo(0.38, -0.15, 0.42, -0.72, 0, -1);
    ctx.bezierCurveTo(-0.42, -0.72, -0.38, -0.15, 0, 0.15);
    ctx.fill();
    ctx.restore();
  };
  petal(0, 1, 1);
  petal(-0.85, 0.78, 0.82);
  petal(0.85, 0.78, 0.82);
  ctx.fillRect(-0.38, -0.02, 0.76, 0.12);
  ctx.beginPath();
  ctx.moveTo(0, 0.08);
  ctx.lineTo(0.16, 0.85);
  ctx.lineTo(0, 0.62);
  ctx.lineTo(-0.16, 0.85);
  ctx.closePath();
  ctx.fill();
}

function heart(ctx: CanvasRenderingContext2D): void {
  ctx.beginPath();
  ctx.moveTo(0, 0.85);
  ctx.bezierCurveTo(0.95, 0.15, 0.7, -0.75, 0, -0.28);
  ctx.bezierCurveTo(-0.7, -0.75, -0.95, 0.15, 0, 0.85);
  ctx.fill();
}

function dot(ctx: CanvasRenderingContext2D): void {
  ctx.beginPath();
  ctx.arc(0, 0, 0.42, 0, Math.PI * 2);
  ctx.fill();
}

function check(ctx: CanvasRenderingContext2D, color: string): void {
  ctx.fillStyle = color;
  ctx.fillRect(0, 0, SIZE / 2, SIZE / 2);
  ctx.fillRect(SIZE / 2, SIZE / 2, SIZE / 2, SIZE / 2);
}

function diamond(ctx: CanvasRenderingContext2D): void {
  ctx.beginPath();
  ctx.moveTo(0, -0.85);
  ctx.lineTo(0.7, 0);
  ctx.lineTo(0, 0.85);
  ctx.lineTo(-0.7, 0);
  ctx.closePath();
  ctx.fill();
}

function cross(ctx: CanvasRenderingContext2D): void {
  ctx.fillRect(-0.22, -0.85, 0.44, 1.7);
  ctx.fillRect(-0.85, -0.22, 1.7, 0.44);
}

export function createPatternMap(
  pattern: PatternId,
  field: string,
  motif: string,
  tiles = TILES
): THREE.CanvasTexture {
  const ctx = canvas();
  fillField(ctx, field);
  if (pattern === "check") check(ctx, motif);
  else {
    motifAt(ctx, motif, () => {
      if (pattern === "fleur") fleur(ctx);
      else if (pattern === "heart") heart(ctx);
      else if (pattern === "diamond") diamond(ctx);
      else if (pattern === "cross") cross(ctx);
      else dot(ctx);
    });
  }
  const tex = new THREE.CanvasTexture(ctx.canvas);
  tex.colorSpace = THREE.SRGBColorSpace;
  tex.wrapS = tex.wrapT = THREE.RepeatWrapping;
  tex.repeat.set(tiles, tiles);
  tex.anisotropy = 4;
  tex.needsUpdate = true;
  return tex;
}
