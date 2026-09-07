import { CROSSBOW_SHOT } from "./pieces";
import { ItemState, MovementPattern, UnitState } from "./types";

let nextItemId = 0;

type ItemShape = ItemState["shape"];

type ItemSpec = {
  displayName: string;
  description: string;
  dropOnDeath: boolean;
  color: string;
  shape: ItemShape;
  model?: string;
  /** Power rank of this kind. Higher = stronger; used later for map placement. */
  tier: number;
  /** Activations per match. Omit = unlimited (passives, or consume-on-break). */
  maxUses?: number;
};

/** One entry per item kind — not per power variant. */
export const ITEMS = {
  Crossbow: {
    displayName: "Crossbow",
    description: "Grants a diagonal ranged shot up to 3 squares. Drops on death.",
    dropOnDeath: true,
    color: "#c4783a",
    shape: "cube",
    model: "items/crossbow",
    tier: 1,
  },
  Bomb: {
    displayName: "Bomb",
    description: "Ignite, break, or die to explode this tile and the orthogonal neighbors, and smash mountains there.",
    dropOnDeath: true,
    color: "#4a2f1c",
    shape: "cylinder",
    model: "items/powder_barrel",
    tier: 1,
  },
  ForceFieldGenerator: {
    displayName: "Force Field Generator",
    description: "Grants a protective forcefield that blocks one capture. Drops on death.",
    dropOnDeath: true,
    color: "#33ccff",
    shape: "sphere",
    tier: 2,
  },
  EscapeScroll: {
    displayName: "Escape Scroll",
    description: "1 use: return this piece to the square where it started the match. The scroll stays with the piece.",
    dropOnDeath: true,
    color: "#d4b87a",
    shape: "capsule",
    model: "items/pergamino",
    tier: 2,
    maxUses: 1,
  },
  TransmuteScroll: {
    displayName: "Transmutation Scroll",
    description: "2 uses: turn an empty square this piece can attack into stone. The scroll stays with the piece.",
    dropOnDeath: true,
    color: "#6a6a6a",
    shape: "capsule",
    model: "items/pergamino",
    tier: 2,
    maxUses: 2,
  },
  SwapCharm: {
    displayName: "Swap Charm",
    description: "1 use, free: swap places with an adjacent piece (friend or foe). Does not spend this turn.",
    dropOnDeath: true,
    color: "#c45ec8",
    shape: "sphere",
    tier: 2,
    maxUses: 1,
  },
} satisfies Record<string, ItemSpec>;

export type ItemKind = keyof typeof ITEMS;

export function itemTier(kind: string): number {
  return ITEMS[kind as ItemKind]?.tier ?? 1;
}

export function itemMaxUses(kind: string): number | undefined {
  return ITEMS[kind as ItemKind]?.maxUses;
}

export function itemIsSpent(item: ItemState): boolean {
  return item.maxUses != null && (item.uses ?? 0) <= 0;
}

export function itemUsesLabel(item: ItemState): string | null {
  if (item.maxUses == null) return null;
  return `${item.uses ?? 0}/${item.maxUses}`;
}

/** Spend one activation. Unlimited items always succeed. */
export function spendItemUse(item: ItemState): boolean {
  if (item.maxUses == null) return true;
  if ((item.uses ?? 0) <= 0) return false;
  item.uses = (item.uses ?? 0) - 1;
  return true;
}

export function refillItemUses(item: ItemState): void {
  const max = item.maxUses ?? itemMaxUses(item.kind);
  if (max == null) {
    delete item.uses;
    delete item.maxUses;
    return;
  }
  item.maxUses = max;
  item.uses = max;
}

function spawnItem(kind: ItemKind, nodeId: number): ItemState {
  const spec = ITEMS[kind];
  const maxUses = spec.maxUses;
  return {
    id: `item_${nextItemId++}`,
    kind,
    displayName: spec.displayName,
    description: spec.description,
    nodeId,
    dropOnDeath: spec.dropOnDeath,
    color: spec.color,
    shape: spec.shape,
    model: spec.model,
    tier: spec.tier,
    ...(maxUses != null ? { maxUses, uses: maxUses } : {}),
  };
}

export function createForceFieldGenerator(nodeId: number): ItemState {
  return spawnItem("ForceFieldGenerator", nodeId);
}

export function createBomb(nodeId: number): ItemState {
  return spawnItem("Bomb", nodeId);
}

export function catalogStartingItem(definitionId: string): { displayName: string } | null {
  if (definitionId === "Bomber") return { displayName: "Bomb" };
  if (definitionId === "Crossbowman") return { displayName: "Crossbow" };
  return null;
}

export function startingHeldItem(definitionId: string): ItemState | null {
  if (definitionId === "Bomber") return createBomb(-1);
  if (definitionId === "Crossbowman") return createCrossbow(-1);
  return null;
}

export function createTransmuteScroll(nodeId: number): ItemState {
  return spawnItem("TransmuteScroll", nodeId);
}

export function createEscapeScroll(nodeId: number): ItemState {
  return spawnItem("EscapeScroll", nodeId);
}

export function createSwapCharm(nodeId: number): ItemState {
  return spawnItem("SwapCharm", nodeId);
}

export function createCrossbow(nodeId: number): ItemState {
  return spawnItem("Crossbow", nodeId);
}

export function pickupActionLabel(item: ItemState): string {
  if (item.kind === "ForceFieldGenerator") return "Pick up Force Field";
  return `Pick up ${item.displayName}`;
}

/** Unity d4/e5 on 8×8: (width/2-1, height/2-1) and its mirror. */
export function itemHomeTiles(width = 8, height = 8): number[] {
  const x = Math.floor(width / 2) - 1;
  const y = Math.floor(height / 2) - 1;
  const a = y * width + x;
  const b = (height - 1 - y) * width + (width - 1 - x);
  const c = y * width + Math.min(width - 1, x + 1);
  const d = (height - 1 - y) * width + x;
  const e = y * width + Math.max(0, x - 1);
  return [...new Set([a, b, c, d, e])];
}

function shuffleInPlace<T>(list: T[]): T[] {
  for (let i = list.length - 1; i > 0; i--) {
    const j = Math.floor(Math.random() * (i + 1));
    [list[i], list[j]] = [list[j], list[i]];
  }
  return list;
}

export function defaultBoardItems(
  passable?: (nodeId: number) => boolean,
  width = 8,
  height = 8,
  preferred?: number[]
): ItemState[] {
  const makers = [
    createCrossbow,
    createForceFieldGenerator,
    createBomb,
    createEscapeScroll,
    createTransmuteScroll,
    createSwapCharm,
  ];
  const useWings = !!preferred?.length;
  const pool = useWings ? shuffleInPlace([...preferred!]) : itemHomeTiles(width, height);
  const chosen: number[] = [];
  for (const id of pool) {
    if (chosen.length >= makers.length) break;
    if (!passable || passable(id)) chosen.push(id);
  }
  if (!useWings && chosen.length < makers.length && passable) {
    const midStart = 2 * width;
    const midEnd = (height - 2) * width;
    for (let id = midStart; id < midEnd && chosen.length < makers.length; id++) {
      if (passable(id) && !chosen.includes(id)) chosen.push(id);
    }
  }
  const tiles = chosen.length > 0 ? chosen : pool.slice(0, makers.length);
  const assign = useWings ? shuffleInPlace([...makers]) : makers;
  return tiles.map((id, i) => assign[i % assign.length](id));
}

/** Place one shuffled catalog item on each tile (used by board decor recipes). */
export function placeBoardItems(tiles: number[], passable?: (nodeId: number) => boolean): ItemState[] {
  const makers = [
    createCrossbow,
    createForceFieldGenerator,
    createBomb,
    createEscapeScroll,
    createTransmuteScroll,
    createSwapCharm,
  ];
  const spots = tiles.filter((id) => !passable || passable(id));
  if (!spots.length) return [];
  const assign = shuffleInPlace([...makers]);
  return spots.map((id, i) => assign[i % assign.length](id));
}

export function applyItemPick(unit: UnitState, item: ItemState): void {
  if (item.kind === "ForceFieldGenerator") {
    if (!unit.skills.some((s) => s.id === "Forcefield")) {
      unit.skills = [...unit.skills, { id: "Forcefield", isActive: true }];
    }
    return;
  }
  if (item.kind === "Crossbow") {
    if (!unit.patterns.some((p) => p.grantedBy === "Crossbow")) {
      unit.patterns = [...unit.patterns, grantedCrossbowShot()];
    }
    return;
  }
  if (item.kind === "Bomb") {
    unit.actions ??= [];
    if (!unit.actions.some((a) => a.id === "Bomb")) {
      unit.actions = [...unit.actions, { id: "Bomb", label: "Ignite bomb" }];
    }
  }
  if (item.kind === "EscapeScroll") {
    unit.actions ??= [];
    if (!unit.actions.some((a) => a.id === "EscapeScroll")) {
      unit.actions = [...unit.actions, { id: "EscapeScroll", label: "Escape home" }];
    }
  }
  if (item.kind === "TransmuteScroll") {
    unit.actions ??= [];
    if (!unit.actions.some((a) => a.id === "TransmuteScroll")) {
      unit.actions = [...unit.actions, { id: "TransmuteScroll", label: "Turn to stone" }];
    }
  }
  if (item.kind === "SwapCharm") {
    unit.actions ??= [];
    if (!unit.actions.some((a) => a.id === "SwapCharm")) {
      unit.actions = [...unit.actions, { id: "SwapCharm", label: "Swap", free: true }];
    }
  }
}

export interface ItemBreakContext {
  item: ItemState;
  nodeId: number;
  explode: (nodeId: number) => void;
}

export type ItemOnBreak = (ctx: ItemBreakContext) => void;

const ON_BREAK: Record<string, ItemOnBreak> = {
  Bomb: ({ nodeId, explode }) => explode(nodeId),
};

export function registerItemOnBreak(kind: string, handler: ItemOnBreak): void {
  ON_BREAK[kind] = handler;
}

export function hasOnBreak(kind: string): boolean {
  return Object.hasOwn(ON_BREAK, kind);
}

/** Called when an item is destroyed, not when it is dropped. */
export function applyItemBreak(item: ItemState, ctx: Omit<ItemBreakContext, "item">): void {
  ON_BREAK[item.kind]?.({ ...ctx, item });
}

export function applyItemDrop(unit: UnitState, item: ItemState): void {
  if (item.kind === "ForceFieldGenerator") {
    unit.skills = unit.skills.filter((s) => s.id !== "Forcefield");
    return;
  }
  if (item.kind === "Crossbow") {
    unit.patterns = unit.patterns.filter((p) => p.grantedBy !== "Crossbow");
    return;
  }
  if (item.kind === "Bomb") {
    unit.actions = (unit.actions ?? []).filter((a) => a.id !== "Bomb");
  }
  if (item.kind === "EscapeScroll") {
    unit.actions = (unit.actions ?? []).filter((a) => a.id !== "EscapeScroll");
  }
  if (item.kind === "TransmuteScroll") {
    unit.actions = (unit.actions ?? []).filter((a) => a.id !== "TransmuteScroll");
  }
  if (item.kind === "SwapCharm") {
    unit.actions = (unit.actions ?? []).filter((a) => a.id !== "SwapCharm");
  }
}

export function cloneItem(item: ItemState): ItemState {
  return { ...item };
}

export function rehydrateItem(item: Omit<ItemState, "id" | "nodeId" | "tier"> & { id?: string; nodeId?: number; tier?: number }): ItemState {
  const kind = item.kind === "PowderBarrel" ? "Bomb" : item.kind;
  const spec = ITEMS[kind as ItemKind];
  const maxUses = spec?.maxUses ?? item.maxUses;
  return {
    id: item.id ?? `item_${nextItemId++}`,
    kind,
    displayName: spec?.displayName || item.displayName || kind,
    description: spec?.description || item.description || "",
    nodeId: item.nodeId ?? -1,
    dropOnDeath: item.dropOnDeath,
    color: item.color || spec?.color || "#c9a15b",
    shape: item.shape ?? spec?.shape ?? "cube",
    model: item.model ?? spec?.model,
    tier: spec?.tier ?? item.tier ?? 1,
    ...(maxUses != null ? { maxUses, uses: item.uses ?? maxUses } : {}),
  };
}

function grantedCrossbowShot(): MovementPattern {
  return { ...CROSSBOW_SHOT, grantedBy: "Crossbow" };
}
