import * as THREE from "three";
import { hexWorldZ } from "../core/hex";
import { BoardLayout } from "../core/types";

export interface WorldRect {
  x0: number;
  z0: number;
  x1: number;
  z1: number;
}

interface Pt {
  x: number;
  z: number;
}

const STEP = 0.5;

export function tileWorldRect(x: number, y: number, width: number, layout: BoardLayout): WorldRect {
  const x0 = width - 1 - x;
  const z0 = layout === "hex-offset" ? hexWorldZ(x, y) : y;
  return { x0, z0, x1: x0 + 1, z1: z0 + 1 };
}

export function makeRectCutTest(rects: WorldRect[], pad: number): (x: number, z: number) => boolean {
  if (!rects.length) return () => false;
  let minX = Infinity;
  let minZ = Infinity;
  let maxX = -Infinity;
  let maxZ = -Infinity;
  for (const r of rects) {
    minX = Math.min(minX, r.x0 - pad);
    minZ = Math.min(minZ, r.z0 - pad);
    maxX = Math.max(maxX, r.x1 + pad);
    maxZ = Math.max(maxZ, r.z1 + pad);
  }
  const step = 0.05;
  const w = Math.ceil((maxX - minX) / step) + 1;
  const h = Math.ceil((maxZ - minZ) / step) + 1;
  const mask = new Uint8Array(w * h);
  for (const r of rects) {
    const x0 = Math.max(0, Math.floor((r.x0 - pad - minX) / step));
    const x1 = Math.min(w, Math.ceil((r.x1 + pad - minX) / step));
    const z0 = Math.max(0, Math.floor((r.z0 - pad - minZ) / step));
    const z1 = Math.min(h, Math.ceil((r.z1 + pad - minZ) / step));
    for (let z = z0; z < z1; z++) {
      const row = z * w;
      for (let x = x0; x < x1; x++) mask[row + x] = 1;
    }
  }
  return (x, z) => {
    if (x < minX || z < minZ || x > maxX || z > maxZ) return false;
    const ix = Math.min(w - 1, Math.max(0, Math.floor((x - minX) / step)));
    const iz = Math.min(h - 1, Math.max(0, Math.floor((z - minZ) / step)));
    return mask[iz * w + ix] === 1;
  };
}

export function createBoardFrameGeometry(
  rects: WorldRect[],
  frameT: number,
  frameH: number
): THREE.BufferGeometry | null {
  const loops = loopsFromRects(rects);
  if (!loops.length) return null;
  const { outers, holes } = classifyLoops(loops);
  const shapes: THREE.Shape[] = [];
  for (const outer of outers) {
    const expanded = offsetLoop(outer, frameT);
    const nested = holes.filter((hole) => containsLoop(outer, hole));
    shapes.push(ringShape(expanded, outer));
    for (const hole of nested) {
      const region = ensureCcw(hole);
      const inset = offsetLoop(region, -frameT);
      if (Math.abs(signedArea(inset)) < 1e-4) continue;
      shapes.push(ringShape(region, inset));
    }
  }
  if (!shapes.length) return null;
  const geo = new THREE.ExtrudeGeometry(shapes, { depth: frameH, bevelEnabled: false, curveSegments: 1 });
  geo.rotateX(-Math.PI / 2);
  geo.computeVertexNormals();
  return geo;
}

function loopsFromRects(rects: WorldRect[]): Pt[][] {
  const occ = new Set<string>();
  for (const r of rects) {
    for (let x = r.x0; x < r.x1 - 1e-9; x += STEP) {
      for (let z = r.z0; z < r.z1 - 1e-9; z += STEP) {
        occ.add(cellKey(snap(x), snap(z)));
      }
    }
  }
  const edges = new Map<string, { x0: number; z0: number; x1: number; z1: number }>();
  const addEdge = (x0: number, z0: number, x1: number, z1: number) => {
    const rev = edgeKey(x1, z1, x0, z0);
    if (edges.has(rev)) {
      edges.delete(rev);
      return;
    }
    edges.set(edgeKey(x0, z0, x1, z1), { x0, z0, x1, z1 });
  };
  for (const key of occ) {
    const { x, z } = parseCell(key);
    addEdge(x, z, x + STEP, z);
    addEdge(x + STEP, z, x + STEP, z + STEP);
    addEdge(x + STEP, z + STEP, x, z + STEP);
    addEdge(x, z + STEP, x, z);
  }
  const byStart = new Map<string, { x0: number; z0: number; x1: number; z1: number }[]>();
  for (const edge of edges.values()) {
    const k = cellKey(edge.x0, edge.z0);
    const list = byStart.get(k);
    if (list) list.push(edge);
    else byStart.set(k, [edge]);
  }
  const used = new Set<string>();
  const loops: Pt[][] = [];
  for (const edge of edges.values()) {
    const id = edgeKey(edge.x0, edge.z0, edge.x1, edge.z1);
    if (used.has(id)) continue;
    const loop: Pt[] = [{ x: edge.x0, z: edge.z0 }];
    let cur = edge;
    let closed = false;
    for (let guard = 0; guard < edges.size + 2; guard++) {
      used.add(edgeKey(cur.x0, cur.z0, cur.x1, cur.z1));
      loop.push({ x: cur.x1, z: cur.z1 });
      if (cur.x1 === edge.x0 && cur.z1 === edge.z0) {
        closed = true;
        break;
      }
      const next = pickNext(byStart.get(cellKey(cur.x1, cur.z1)) ?? [], cur, used);
      if (!next) break;
      cur = next;
    }
    if (closed && loop.length > 3) {
      loop.pop();
      loops.push(simplify(loop));
    }
  }
  return loops;
}

function pickNext(
  candidates: { x0: number; z0: number; x1: number; z1: number }[],
  incoming: { x0: number; z0: number; x1: number; z1: number },
  used: Set<string>
): { x0: number; z0: number; x1: number; z1: number } | null {
  const ix = incoming.x1 - incoming.x0;
  const iz = incoming.z1 - incoming.z0;
  let best: { x0: number; z0: number; x1: number; z1: number } | null = null;
  let bestRank = 99;
  for (const edge of candidates) {
    const id = edgeKey(edge.x0, edge.z0, edge.x1, edge.z1);
    if (used.has(id)) continue;
    const ox = edge.x1 - edge.x0;
    const oz = edge.z1 - edge.z0;
    const cross = ix * oz - iz * ox;
    const dot = ix * ox + iz * oz;
    const rank = cross > 1e-9 ? 0 : Math.abs(cross) <= 1e-9 && dot > 0 ? 1 : cross < -1e-9 ? 2 : 3;
    if (rank < bestRank) {
      bestRank = rank;
      best = edge;
    }
  }
  return best;
}

function classifyLoops(loops: Pt[][]): { outers: Pt[][]; holes: Pt[][] } {
  const outers: Pt[][] = [];
  const holes: Pt[][] = [];
  for (const loop of loops) {
    if (signedArea(loop) > 0) outers.push(loop);
    else holes.push(loop);
  }
  return { outers, holes };
}

function ringShape(outer: Pt[], hole: Pt[]): THREE.Shape {
  const shape = new THREE.Shape();
  tracePath(shape, ensureCw(outer));
  const holePath = new THREE.Path();
  tracePath(holePath, ensureCcw(hole));
  shape.holes.push(holePath);
  return shape;
}

function tracePath(path: THREE.Path, pts: Pt[]): void {
  path.moveTo(pts[0].x, -pts[0].z);
  for (let i = 1; i < pts.length; i++) path.lineTo(pts[i].x, -pts[i].z);
  path.closePath();
}

function offsetLoop(pts: Pt[], dist: number): Pt[] {
  const n = pts.length;
  const out: Pt[] = [];
  for (let i = 0; i < n; i++) {
    const a = pts[(i - 1 + n) % n];
    const b = pts[i];
    const c = pts[(i + 1) % n];
    const d1x = b.x - a.x;
    const d1z = b.z - a.z;
    const d2x = c.x - b.x;
    const d2z = c.z - b.z;
    const len1 = Math.hypot(d1x, d1z) || 1;
    const len2 = Math.hypot(d2x, d2z) || 1;
    const n1x = (d1z / len1) * dist;
    const n1z = (-d1x / len1) * dist;
    const n2x = (d2z / len2) * dist;
    const n2z = (-d2x / len2) * dist;
    out.push({ x: b.x + n1x + n2x, z: b.z + n1z + n2z });
  }
  return simplify(out);
}

function simplify(pts: Pt[]): Pt[] {
  if (pts.length < 3) return pts;
  const out: Pt[] = [];
  const n = pts.length;
  for (let i = 0; i < n; i++) {
    const a = pts[(i - 1 + n) % n];
    const b = pts[i];
    const c = pts[(i + 1) % n];
    const cross = (b.x - a.x) * (c.z - b.z) - (b.z - a.z) * (c.x - b.x);
    const dup = Math.hypot(b.x - a.x, b.z - a.z) < 1e-9;
    if (dup || Math.abs(cross) < 1e-9) continue;
    out.push(b);
  }
  return out.length >= 3 ? out : pts;
}

function ensureCcw(pts: Pt[]): Pt[] {
  return signedArea(pts) >= 0 ? pts : reversePts(pts);
}

function ensureCw(pts: Pt[]): Pt[] {
  return signedArea(pts) <= 0 ? pts : reversePts(pts);
}

function reversePts(pts: Pt[]): Pt[] {
  return pts.slice().reverse();
}

function signedArea(pts: Pt[]): number {
  let a = 0;
  for (let i = 0; i < pts.length; i++) {
    const p = pts[i];
    const q = pts[(i + 1) % pts.length];
    a += p.x * q.z - q.x * p.z;
  }
  return a / 2;
}

function containsLoop(outer: Pt[], inner: Pt[]): boolean {
  let cx = 0;
  let cz = 0;
  for (const p of inner) {
    cx += p.x;
    cz += p.z;
  }
  cx /= inner.length;
  cz /= inner.length;
  return pointInPoly(outer, cx, cz);
}

function pointInPoly(pts: Pt[], x: number, z: number): boolean {
  let inside = false;
  for (let i = 0, j = pts.length - 1; i < pts.length; j = i++) {
    const zi = pts[i].z;
    const zj = pts[j].z;
    const xi = pts[i].x;
    const xj = pts[j].x;
    const hit = zi > z !== zj > z && x < ((xj - xi) * (z - zi)) / (zj - zi + 1e-12) + xi;
    if (hit) inside = !inside;
  }
  return inside;
}

function snap(n: number): number {
  return Math.round(n / STEP) * STEP;
}

function cellKey(x: number, z: number): string {
  return `${snap(x)},${snap(z)}`;
}

function edgeKey(x0: number, z0: number, x1: number, z1: number): string {
  return `${snap(x0)},${snap(z0)},${snap(x1)},${snap(z1)}`;
}

function parseCell(key: string): Pt {
  const [x, z] = key.split(",").map(Number);
  return { x, z };
}
