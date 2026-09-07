import { hexCellExists, hexEdges, hexShade, hexSpawnZones, hexWorldZ } from "./hex";
import { LANE11_DECOR } from "./boardDecor";
import { itemHomeTiles } from "./items";
import type { ArmyFormat } from "./format";
import {
  AXIS_D1,
  AXIS_D2,
  AXIS_H,
  AXIS_V,
  BoardDefinition,
  BoardKind,
  BoardLayout,
  EdgeDef,
  EdgeType,
  HEX_AXES,
  NodeDef,
  NodeState,
  NodeType,
  ORTHO_AXES,
  isPassable,
} from "./types";

export class Board {
  readonly def: BoardDefinition;
  nodes: NodeState[];
  private adjacency = new Map<number, number[]>();
  private links = new Map<number, { to: number; axis: string }[]>();

  constructor(def: BoardDefinition, nodes?: NodeState[]) {
    this.def = def;
    this.nodes = nodes ?? def.nodes.map((n) => ({ id: n.id, currentType: n.type, isActive: n.type !== NodeType.Destroyed }));
    this.rebuildAdjacency();
  }

  nodeId(x: number, y: number): number {
    return y * this.def.width + x;
  }

  coords(nodeId: number): { x: number; y: number } {
    return { x: nodeId % this.def.width, y: Math.floor(nodeId / this.def.width) };
  }

  getNode(nodeId: number): NodeState | undefined {
    return this.nodes[nodeId];
  }

  hasNode(nodeId: number): boolean {
    const node = this.nodes[nodeId];
    return !!node && node.isActive;
  }

  passable(nodeId: number): boolean {
    const node = this.nodes[nodeId];
    if (!node) return false;
    return isPassable(node);
  }

  changeType(nodeId: number, type: NodeType): boolean {
    const node = this.nodes[nodeId];
    if (!node) return false;
    node.currentType = type;
    node.isActive = type !== NodeType.Destroyed;
    this.rebuildAdjacency();
    return true;
  }

  nodeDef(nodeId: number): NodeDef {
    return this.def.nodes[nodeId];
  }

  neighbors(nodeId: number): number[] {
    return this.adjacency.get(nodeId) ?? [];
  }

  neighborsOnAxis(nodeId: number, axis: string): number[] {
    return (this.links.get(nodeId) ?? []).filter((link) => link.axis === axis).map((link) => link.to);
  }

  axisBetween(from: number, to: number): string | null {
    return (this.links.get(from) ?? []).find((link) => link.to === to)?.axis ?? null;
  }

  areConnected(a: number, b: number): boolean {
    return this.neighbors(a).includes(b);
  }

  get layout(): BoardLayout {
    return this.def.layout ?? "square";
  }

  get isHex(): boolean {
    return this.layout === "hex-offset";
  }

  get orthoAxes(): string[] {
    return this.isHex ? HEX_AXES : ORTHO_AXES;
  }

  /** World-space center of a tile (x, z), squares stay 1×1. */
  worldCenter(nodeId: number): { x: number; z: number } {
    const { x, y } = this.coords(nodeId);
    const z = this.isHex ? hexWorldZ(x, y) : y;
    return { x: x + 0.5, z: z + 0.5 };
  }

  idAt(x: number, y: number): number | undefined {
    if (this.isHex && !hexCellExists(x, y, this.def.width, this.def.height)) return undefined;
    if (x < 0 || y < 0 || x >= this.def.width || y >= this.def.height) return undefined;
    const id = y * this.def.width + x;
    const node = this.nodes[id];
    if (!node || node.currentType === NodeType.Destroyed) return undefined;
    return id;
  }

  private rebuildAdjacency(): void {
    this.adjacency.clear();
    this.links.clear();
    // Abyss and walls stay in the graph. Only missing squares (Destroyed) are omitted.
    for (const node of this.nodes) {
      if (node.currentType === NodeType.Destroyed) continue;
      this.adjacency.set(node.id, []);
      this.links.set(node.id, []);
    }
    for (const edge of this.def.edges) {
      if (edge.type === EdgeType.Blocked) continue;
      const axis = edge.axis || inferAxis(this.def, edge.from, edge.to);
      const add = (from: number, to: number) => {
        const list = this.adjacency.get(from);
        const linkList = this.links.get(from);
        if (list && !list.includes(to)) list.push(to);
        if (linkList && !linkList.some((link) => link.to === to)) linkList.push({ to, axis });
      };
      add(edge.from, edge.to);
      if (edge.bidirectional && edge.type !== EdgeType.OneWay) add(edge.to, edge.from);
    }
  }
}

function spawnZones(
  width: number,
  height: number,
  nodes?: { type?: NodeType; currentType?: NodeType }[]
): BoardDefinition["spawn"] {
  const keep = (id: number) => {
    if (!nodes) return true;
    const node = nodes[id];
    if (!node) return false;
    const type = node.currentType ?? node.type;
    return type !== NodeType.Destroyed;
  };
  const p1Back: number[] = [];
  const p1Front: number[] = [];
  const p2Back: number[] = [];
  const p2Front: number[] = [];
  for (let x = 0; x < width; x++) {
    const back1 = x;
    const front1 = width + x;
    const front2 = (height - 2) * width + x;
    const back2 = (height - 1) * width + x;
    if (keep(back1)) p1Back.push(back1);
    if (keep(front1)) p1Front.push(front1);
    if (keep(front2)) p2Front.push(front2);
    if (keep(back2)) p2Back.push(back2);
  }
  return [
    { back: p1Back, front: p1Front },
    { back: p2Back, front: p2Front },
  ];
}

function gridNodes(width: number, height: number, typeAt?: (id: number, x: number, y: number) => NodeType): NodeDef[] {
  const nodes: NodeDef[] = [];
  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      const id = y * width + x;
      nodes.push({
        id,
        x,
        y,
        type: typeAt?.(id, x, y) ?? NodeType.Normal,
        isLight: (x + y) % 2 === 1,
        shade: (x + y) % 2 === 1 ? 0 : 1,
      });
    }
  }
  return nodes;
}

function inferAxis(def: { nodes: NodeDef[]; width: number }, from: number, to: number): string {
  const a = def.nodes[from];
  const b = def.nodes[to];
  if (!a || !b) return AXIS_H;
  const dx = b.x - a.x;
  const dy = b.y - a.y;
  if (dy === 0 && dx !== 0) return AXIS_H;
  if (dx === 0 && dy !== 0) return AXIS_V;
  if (dx * dy > 0) return AXIS_D1;
  return AXIS_D2;
}

/** 8-way edges through every square that exists — including abyss and walls. */
export function eightWayEdges(width: number, height: number, exists: (id: number) => boolean): EdgeDef[] {
  const edges: EdgeDef[] = [];
  const add = (from: number, to: number, axis: string) => {
    if (!exists(from) || !exists(to)) return;
    edges.push({ from, to, bidirectional: true, type: EdgeType.Normal, axis });
  };
  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      const id = y * width + x;
      if (x < width - 1) add(id, id + 1, AXIS_H);
      if (y < height - 1) add(id, id + width, AXIS_V);
      if (x < width - 1 && y < height - 1) add(id, id + width + 1, AXIS_D1);
      if (x > 0 && y < height - 1) add(id, id + width - 1, AXIS_D2);
    }
  }
  return edges;
}

function finishBoard(
  id: string,
  displayName: string,
  width: number,
  height: number,
  nodes: NodeDef[]
): BoardDefinition {
  // Holes (Destroyed) are missing squares. Abyss and walls stay connected.
  const exists = (nodeId: number) => {
    const node = nodes[nodeId];
    return !!node && node.type !== NodeType.Destroyed;
  };
  return {
    id,
    displayName,
    width,
    height,
    layout: "square",
    nodes,
    edges: eightWayEdges(width, height, exists),
    spawn: spawnZones(width, height, nodes),
  };
}

const HEX_W = 8;
const HEX_H = 9;

export function createHexBoard(): BoardDefinition {
  const width = HEX_W;
  const height = HEX_H;
  const nodes: NodeDef[] = [];
  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      const id = y * width + x;
      const exists = hexCellExists(x, y, width, height);
      const shade = exists ? hexShade(x, y) : 0;
      nodes.push({
        id,
        x,
        y,
        type: exists ? NodeType.Normal : NodeType.Destroyed,
        isLight: shade !== 2,
        shade,
      });
    }
  }
  const exists = (nodeId: number) => {
    const node = nodes[nodeId];
    return !!node && node.type !== NodeType.Destroyed;
  };
  return {
    id: "hexa",
    displayName: "Hexa — brick hex",
    width,
    height,
    layout: "hex-offset",
    nodes,
    edges: hexEdges(width, height, exists),
    spawn: hexSpawnZones(width, height),
  };
}

export function createRectangularBoard(
  width: number,
  height: number,
  id = `board_${width}x${height}`,
  displayName = `${width}×${height} Board`
): BoardDefinition {
  return finishBoard(id, displayName, width, height, gridNodes(width, height));
}

export function createClassicBoard(width = 8, height = 8): BoardDefinition {
  return createRectangularBoard(width, height, "classic_8x8", "Classic Chess Board");
}

export function createGrandBoard(): BoardDefinition {
  return createRectangularBoard(10, 10, "grand_10x10", "Grand Chess Board");
}

export function createCapablancaBoard(): BoardDefinition {
  return createRectangularBoard(10, 8, "capablanca_10x8", "Capablanca Chess Board");
}

/** Narrow corridor for mini armies (4 pieces + 4 pawns, empty flanks). */
export function createLaneBoard(width = 6, height = 12): BoardDefinition {
  return createRectangularBoard(width, height, `lane_${width}x${height}`, `Lane — ${width}×${height}`);
}

const LANE11_WIDTH = 10;
const LANE11_HEIGHT = 11;
const LANE11_CORRIDOR = 6;

/** 6-wide ends; 5 central rows open to 8; the 6th row opens to 10. */
function lane11RowWidth(y: number): number {
  if (y === 5) return 10;
  if (y >= 3 && y <= 7) return 8;
  return LANE11_CORRIDOR;
}

function lane11Col0(y: number): number {
  return Math.floor((LANE11_WIDTH - lane11RowWidth(y)) / 2);
}

/** Lane 6×11 with a mid-board bulge. Item/obstacle recipe is `LANE11_DECOR`. */
export function createLane11Board(): BoardDefinition {
  const width = LANE11_WIDTH;
  const height = LANE11_HEIGHT;
  const nodes = gridNodes(width, height, (_id, x, y) => {
    const x0 = lane11Col0(y);
    return x >= x0 && x < x0 + lane11RowWidth(y) ? NodeType.Normal : NodeType.Destroyed;
  });
  const board = finishBoard("lane_10x11_bulge", "Lane — 6×11 wide center", width, height, nodes);
  board.decor = LANE11_DECOR;
  return board;
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

function shuffle<T>(list: T[], rand: () => number): void {
  for (let i = list.length - 1; i > 0; i--) {
    const j = Math.floor(rand() * (i + 1));
    [list[i], list[j]] = [list[j], list[i]];
  }
}

/** Unity default: 5% abyss + 5% walls, mirrored, never on spawn rows. */
export function createBoardWithTerrain(
  width = 8,
  height = 8,
  abyssPercent = 5,
  impassablePercent = 5,
  seed = (Math.random() * 0x7fffffff) | 0,
  id = "abyss_8x8",
  displayName = "8×8 Board (5% Abyss, 5% Mountains)",
  pairCounts?: { abyss: number; walls: number }
): BoardDefinition {
  const rand = mulberry32(seed);
  const centerY = Math.floor(height / 2);
  const reserved = new Set(itemHomeTiles(width, height));
  const half: { x: number; y: number }[] = [];
  for (let y = 2; y < centerY; y++) {
    for (let x = 0; x < width; x++) {
      const nodeId = y * width + x;
      const mirror = (height - 1 - y) * width + (width - 1 - x);
      if (reserved.has(nodeId) || reserved.has(mirror)) continue;
      half.push({ x, y });
    }
  }
  const eligible = half.length * 2;
  const abyssNeed = pairCounts?.abyss ?? Math.floor(Math.round((eligible * abyssPercent) / 100) / 2);
  const wallNeed = pairCounts?.walls ?? Math.floor(Math.round((eligible * impassablePercent) / 100) / 2);
  shuffle(half, rand);

  const abyss = new Set<number>();
  const walls = new Set<number>();
  let i = 0;
  const addPair = (set: Set<number>, count: number) => {
    for (let n = 0; n < count && i < half.length; n++, i++) {
      const pos = half[i];
      set.add(pos.y * width + pos.x);
      set.add((height - 1 - pos.y) * width + (width - 1 - pos.x));
    }
  };
  addPair(abyss, abyssNeed);
  addPair(walls, wallNeed);

  const nodes = gridNodes(width, height, (id) => {
    if (abyss.has(id)) return NodeType.Abyss;
    if (walls.has(id)) return NodeType.Impassable;
    return NodeType.Normal;
  });
  return finishBoard(id, displayName, width, height, nodes);
}

/** Same pit/mountain count as the 6×10 lane (2 mirrored pairs of each). */
const LANE_TERRAIN_PAIRS = { abyss: 2, walls: 2 };

/** Narrow lane with mirrored random pits and mountains, spawn rows kept clear. */
export function createLaneTerrainBoard(width = 6, height = 10, seed?: number): BoardDefinition {
  return createBoardWithTerrain(
    width,
    height,
    12,
    12,
    seed ?? ((Math.random() * 0x7fffffff) | 0),
    `lane_${width}x${height}_terrain`,
    `Lane — ${width}×${height} pits & mountains`,
    LANE_TERRAIN_PAIRS
  );
}

/** Symmetric 8×8 holes away from spawn and item homes — Unity CreateBoardWithHoles. */
export const DEFAULT_HOLES: { x: number; y: number }[] = [
  { x: 2, y: 2 },
  { x: 5, y: 2 },
  { x: 2, y: 5 },
  { x: 5, y: 5 },
];

export function createBoardWithHoles(
  width = 8,
  height = 8,
  holes: { x: number; y: number }[] = DEFAULT_HOLES
): BoardDefinition {
  const holeSet = new Set(holes.map((h) => h.y * width + h.x));
  const nodes = gridNodes(width, height, (id) => (holeSet.has(id) ? NodeType.Destroyed : NodeType.Normal));
  return finishBoard("holes_8x8", "8×8 Board with Holes", width, height, nodes);
}

export const BOARD_OPTIONS: { id: BoardKind; label: string; formats: ArmyFormat[] }[] = [
  { id: "lane11", label: "Lane — 6×11 wide center", formats: ["midi", "mini"] },
  { id: "lane12-terrain", label: "Lane — 6×12 pits & mountains", formats: ["midi", "mini"] },
  { id: "lane10-terrain", label: "Lane — 6×10 pits & mountains", formats: ["midi", "mini"] },
  { id: "bizarre", label: "Bizarre — 8×8 pits & walls", formats: ["normal", "midi", "mini"] },
  { id: "classic", label: "Classic — 8×8 open", formats: ["normal", "midi", "mini"] },
  { id: "lane10", label: "Lane — 6×10 mini", formats: ["midi", "mini"] },
  { id: "lane", label: "Lane — 6×12 mini", formats: ["midi", "mini"] },
  { id: "hexa", label: "Hexa — brick hex (3 colors)", formats: ["normal", "midi", "mini"] },
  { id: "grand", label: "Grand — 10×10", formats: ["normal"] },
  { id: "capablanca", label: "Capablanca — 10×8", formats: ["normal"] },
  { id: "holes", label: "Holes — 8×8 irregular", formats: ["normal", "midi", "mini"] },
];

export function boardsForFormat(format: ArmyFormat): typeof BOARD_OPTIONS {
  return BOARD_OPTIONS.filter((option) => option.formats.includes(format));
}

export function boardSupportsFormat(board: BoardKind, format: ArmyFormat): boolean {
  return BOARD_OPTIONS.some((option) => option.id === board && option.formats.includes(format));
}

export function createBoardByKind(kind: BoardKind = "lane11", seed?: number): BoardDefinition {
  switch (kind) {
    case "classic":
      return createClassicBoard();
    case "grand":
      return createGrandBoard();
    case "capablanca":
      return createCapablancaBoard();
    case "holes":
      return createBoardWithHoles();
    case "hexa":
      return createHexBoard();
    case "lane10":
      return createLaneBoard(6, 10);
    case "lane10-terrain":
      return createLaneTerrainBoard(6, 10, seed);
    case "lane12-terrain":
      return createLaneTerrainBoard(6, 12, seed);
    case "lane":
      return createLaneBoard(6, 12);
    case "bizarre":
      return createBoardWithTerrain(8, 8, 5, 5, seed);
    case "lane11":
    default:
      return createLane11Board();
  }
}

export function definitionFromPublic(state: {
  boardId: string;
  width: number;
  height: number;
  nodes: NodeState[];
  edges: EdgeDef[];
  lights: boolean[];
  shades?: number[];
  layout?: BoardLayout;
}): BoardDefinition {
  const layout = state.layout ?? (state.boardId === "hexa" ? "hex-offset" : "square");
  return {
    id: state.boardId,
    displayName: state.boardId,
    width: state.width,
    height: state.height,
    layout,
    nodes: state.nodes.map((n, i) => {
      const x = i % state.width;
      const y = Math.floor(i / state.width);
      const shade =
        state.shades?.[i] ??
        (layout === "hex-offset" && hexCellExists(x, y, state.width, state.height) ? hexShade(x, y) : state.lights[i] ? 0 : 1);
      return {
        id: n.id,
        x,
        y,
        type: n.currentType,
        isLight: state.lights[i],
        shade,
      };
    }),
    edges: state.edges,
    spawn:
      layout === "hex-offset"
        ? hexSpawnZones(state.width, state.height)
        : spawnZones(state.width, state.height, state.nodes),
  };
}
