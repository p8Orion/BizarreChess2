import { AXIS_A, AXIS_B, AXIS_C, EdgeDef, EdgeType, NodeType } from "./types";

/** 1-based even files (b, d, f, h) are the offset columns. */
export function isHexOffsetColumn(x: number): boolean {
  return x % 2 === 1;
}

export function hexCellExists(x: number, y: number, width: number, height: number): boolean {
  if (x < 0 || y < 0 || x >= width || y >= height) return false;
  return isHexOffsetColumn(x) || y < height - 1;
}

/** Logical row → world Z of the tile's min corner. Offset columns sit 50% back. */
export function hexWorldZ(x: number, y: number): number {
  return isHexOffsetColumn(x) ? y - 0.5 : y;
}

export function toCube(x: number, y: number): { q: number; r: number; s: number } {
  const q = x;
  const r = y - (x + (x & 1)) / 2;
  return { q, r, s: -q - r };
}

export function fromCube(q: number, r: number): { x: number; y: number } {
  return { x: q, y: r + (q + (q & 1)) / 2 };
}

/** Proper 3-coloring of the hex graph: adjacent tiles never share a shade. */
export function hexShade(x: number, y: number): number {
  const { q, r } = toCube(x, y);
  return (((q - r) % 3) + 3) % 3;
}

/** Same-color vertex steps — bishop lines on a hex. */
export const HEX_DIAG_DIRS = [
  { q: 2, r: -1 },
  { q: -2, r: 1 },
  { q: -1, r: 2 },
  { q: 1, r: -2 },
  { q: -1, r: -1 },
  { q: 1, r: 1 },
] as const;

export function hexSpawnZones(width: number, height: number): { back: number[]; front: number[] }[] {
  const p1Back: number[] = [];
  const p1Front: number[] = [];
  const p2Back: number[] = [];
  const p2Front: number[] = [];
  for (let x = 0; x < width; x++) {
    const far = isHexOffsetColumn(x) ? height - 1 : height - 2;
    p1Back.push(x);
    p1Front.push(width + x);
    p2Front.push((far - 1) * width + x);
    p2Back.push(far * width + x);
  }
  return [
    { back: p1Back, front: p1Front },
    { back: p2Back, front: p2Front },
  ];
}

/** 6-neighbor hex edges on the brick layout. Three axes, no diagonals. */
export function hexEdges(width: number, height: number, exists: (id: number) => boolean): EdgeDef[] {
  const edges: EdgeDef[] = [];
  const add = (x0: number, y0: number, x1: number, y1: number, axis: string) => {
    if (!hexCellExists(x0, y0, width, height) || !hexCellExists(x1, y1, width, height)) return;
    const from = y0 * width + x0;
    const to = y1 * width + x1;
    if (!exists(from) || !exists(to)) return;
    edges.push({ from, to, bidirectional: true, type: EdgeType.Normal, axis });
  };

  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      if (!hexCellExists(x, y, width, height)) continue;
      add(x, y, x, y + 1, AXIS_A);
      if (!isHexOffsetColumn(x)) {
        add(x, y, x + 1, y + 1, AXIS_B);
        add(x, y, x - 1, y, AXIS_B);
        add(x, y, x + 1, y, AXIS_C);
        add(x, y, x - 1, y + 1, AXIS_C);
      }
    }
  }
  return edges;
}

export function isStructuralGap(type: NodeType, x: number, y: number, width: number, height: number, layout: string): boolean {
  return layout === "hex-offset" && type === NodeType.Destroyed && !hexCellExists(x, y, width, height);
}
