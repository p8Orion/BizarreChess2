import type { BoardDecorSpec, BoardDefinition, DecorChance, NodeDef, SquareName } from "./types";
import { NodeType } from "./types";

/** 0–40 lobby sliders: extra item-pair chance and obstacle density. */
export interface DecorAmounts {
  itemAmount: number;
  obstacleAmount: number;
  /** When true, obstacles are placed in 180° pairs (each pair costs 2 of the budget). */
  symmetricObstacles: boolean;
}

export const MAX_DECOR_AMOUNT = 40;
export const DEFAULT_ITEM_AMOUNT = 5;
export const DEFAULT_OBSTACLE_AMOUNT = 5;
export const DEFAULT_SYMMETRIC_OBSTACLES = true;

export function clampPercent(n: number): number {
  if (!Number.isFinite(n)) return 0;
  return Math.max(0, Math.min(100, Math.round(n)));
}

export function clampDecorAmount(n: number): number {
  if (!Number.isFinite(n)) return 0;
  return Math.max(0, Math.min(MAX_DECOR_AMOUNT, Math.round(n)));
}

export function normalizeDecorAmounts(raw?: Partial<DecorAmounts>): DecorAmounts {
  return {
    itemAmount: clampDecorAmount(raw?.itemAmount ?? DEFAULT_ITEM_AMOUNT),
    obstacleAmount: clampDecorAmount(raw?.obstacleAmount ?? DEFAULT_OBSTACLE_AMOUNT),
    symmetricObstacles: raw?.symmetricObstacles !== false,
  };
}

export const LANE11_DECOR: BoardDecorSpec = {
  items: [
    { tiles: ["a6"], chance: "always" },
    // 180° of c5 is h7 on this 10-file board (not g7).
    { tiles: ["c5"], chance: "itemAmount" },
    { tiles: ["c7"], chance: "itemAmount" },
  ],
  obstacles: {
    ranks: [3, 9],
    pitChance: "obstacleAmount",
    mountainChance: "obstacleAmount",
  },
};

export function parseSquare(name: SquareName): { x: number; y: number } | null {
  const match = name.trim().toLowerCase().match(/^([a-z])(\d+)$/);
  if (!match) return null;
  const x = match[1].charCodeAt(0) - 97;
  const y = Number(match[2]) - 1;
  if (x < 0 || y < 0) return null;
  return { x, y };
}

export function mirrorOf(x: number, y: number, width: number, height: number): { x: number; y: number } {
  return { x: width - 1 - x, y: height - 1 - y };
}

function nodeId(x: number, y: number, width: number): number {
  return y * width + x;
}

function existsPlayable(node: NodeDef | undefined): boolean {
  return !!node && node.type !== NodeType.Destroyed;
}

function resolveChance(chance: DecorChance, amounts: DecorAmounts): number {
  if (chance === "always") return 100;
  if (chance === "itemAmount") return amounts.itemAmount;
  if (chance === "obstacleAmount") return amounts.obstacleAmount;
  return clampPercent(chance);
}

function expandGroup(tiles: SquareName[], width: number, height: number): number[] {
  const ids = new Set<number>();
  for (const name of tiles) {
    const pos = parseSquare(name);
    if (!pos || pos.x >= width || pos.y >= height) continue;
    ids.add(nodeId(pos.x, pos.y, width));
    const mirror = mirrorOf(pos.x, pos.y, width, height);
    ids.add(nodeId(mirror.x, mirror.y, width));
  }
  return [...ids];
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

function shuffle<T>(list: T[], rand: () => number): T[] {
  for (let i = list.length - 1; i > 0; i--) {
    const j = Math.floor(rand() * (i + 1));
    [list[i], list[j]] = [list[j], list[i]];
  }
  return list;
}

function uniquePairs(ids: number[], width: number, height: number): number[][] {
  const eligible = new Set(ids);
  const seen = new Set<number>();
  const pairs: number[][] = [];
  for (const id of ids) {
    if (seen.has(id)) continue;
    const x = id % width;
    const y = Math.floor(id / width);
    const other = nodeId(width - 1 - x, height - 1 - y, width);
    if (!eligible.has(other)) {
      seen.add(id);
      continue;
    }
    seen.add(id);
    seen.add(other);
    pairs.push(id === other ? [id] : [id, other]);
  }
  return pairs;
}

/**
 * Rolls item groups and paints pits/mountains onto a board definition.
 * Obstacle % is a single budget of playable squares. Symmetric mode spends 2 per 180° pair.
 * Boards without `decor` are returned unchanged (`itemTiles` undefined → legacy spawn).
 */
export function applyBoardDecor(
  def: BoardDefinition,
  amounts: DecorAmounts,
  seed?: number
): { def: BoardDefinition; itemTiles?: number[] } {
  const spec = def.decor;
  if (!spec) return { def };

  const rand = mulberry32(seed ?? ((Math.random() * 0x7fffffff) | 0));
  const next: BoardDefinition = {
    ...def,
    nodes: def.nodes.map((node) => ({ ...node })),
  };
  const { width, height, nodes } = next;
  const reserved = new Set<number>();
  let itemTiles: number[] | undefined;

  if (spec.items) {
    itemTiles = [];
    for (const group of spec.items) {
      if (rand() * 100 >= resolveChance(group.chance, amounts)) continue;
      for (const id of expandGroup(group.tiles, width, height)) {
        if (!existsPlayable(nodes[id]) || itemTiles.includes(id)) continue;
        itemTiles.push(id);
        reserved.add(id);
      }
    }
  }

  const obstacles = spec.obstacles;
  if (obstacles) {
    const [rank0, rank1] = obstacles.ranks;
    const y0 = Math.min(rank0, rank1) - 1;
    const y1 = Math.max(rank0, rank1) - 1;
    const keepSpawn = obstacles.keepSpawnClear !== false;
    const eligible: number[] = [];
    for (const node of nodes) {
      if (!existsPlayable(node)) continue;
      if (reserved.has(node.id)) continue;
      if (node.y < y0 || node.y > y1) continue;
      if (keepSpawn && (node.y < 2 || node.y >= height - 2)) continue;
      eligible.push(node.id);
    }
    const groups = amounts.symmetricObstacles
      ? shuffle(uniquePairs(eligible, width, height), rand)
      : shuffle(eligible, rand).map((id) => [id]);
    const playable = nodes.filter(existsPlayable).length;
    let remaining = Math.round((playable * amounts.obstacleAmount) / 100);
    for (const group of groups) {
      if (remaining <= 0) break;
      if (group.length > remaining) continue;
      const type = rand() < 0.5 ? NodeType.Abyss : NodeType.Impassable;
      for (const id of group) nodes[id].type = type;
      remaining -= group.length;
    }
  }

  return { def: next, itemTiles };
}
