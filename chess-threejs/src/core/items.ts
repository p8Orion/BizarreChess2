import { CROSSBOW_SHOT } from "./pieces";
import { ItemState, MovementPattern, UnitState } from "./types";

let nextItemId = 0;

export function createForceFieldGenerator(nodeId: number): ItemState {
  return {
    id: `item_${nextItemId++}`,
    kind: "ForceFieldGenerator",
    displayName: "Force Field Generator",
    description: "Grants a protective forcefield that blocks one capture. Drops on death.",
    nodeId,
    dropOnDeath: true,
    color: "#33ccff",
    shape: "sphere",
  };
}

export function createBomb(nodeId: number): ItemState {
  return {
    id: `item_${nextItemId++}`,
    kind: "Bomb",
    displayName: "Bomb",
    description: "Ignite, break, or die to explode this tile and the orthogonal neighbors, and smash mountains there.",
    nodeId,
    dropOnDeath: true,
    color: "#1a1a1a",
    shape: "sphere",
    model: "items/powder_barrel",
  };
}

export function catalogStartingItem(definitionId: string): { displayName: string } | null {
  if (definitionId === "Bomber") return { displayName: "Bomb" };
  return null;
}

export function startingHeldItem(definitionId: string): ItemState | null {
  if (definitionId === "Bomber") return createBomb(-1);
  return null;
}

export function createPowderBarrel(nodeId: number): ItemState {
  return {
    id: `item_${nextItemId++}`,
    kind: "PowderBarrel",
    displayName: "Powder Barrel",
    description: "Ignite, break, or die to explode this tile and the orthogonal neighbors, and smash mountains there.",
    nodeId,
    dropOnDeath: true,
    color: "#4a2f1c",
    shape: "cylinder",
    model: "items/powder_barrel",
  };
}

export function createTransmuteScroll(nodeId: number): ItemState {
  return {
    id: `item_${nextItemId++}`,
    kind: "TransmuteScroll",
    displayName: "Transmutation Scroll",
    description: "Once per match: turn an empty square this piece can attack into stone. The scroll stays with the piece.",
    nodeId,
    dropOnDeath: true,
    color: "#6a6a6a",
    shape: "capsule",
    model: "items/pergamino",
  };
}

export function createEscapeScroll(nodeId: number): ItemState {
  return {
    id: `item_${nextItemId++}`,
    kind: "EscapeScroll",
    displayName: "Escape Scroll",
    description: "Once per match: return this piece to the square where it started the match. The scroll stays with the piece.",
    nodeId,
    dropOnDeath: true,
    color: "#d4b87a",
    shape: "capsule",
    model: "items/pergamino",
  };
}

export function createCrossbow(nodeId: number): ItemState {
  return {
    id: `item_${nextItemId++}`,
    kind: "Crossbow",
    displayName: "Crossbow",
    description: "Grants a diagonal ranged shot up to 3 squares. Drops on death.",
    nodeId,
    dropOnDeath: true,
    color: "#c4783a",
    shape: "cube",
    model: "items/crossbow",
  };
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
  const makers = [createCrossbow, createForceFieldGenerator, createPowderBarrel, createEscapeScroll, createTransmuteScroll];
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
  const makers = [createCrossbow, createForceFieldGenerator, createPowderBarrel, createEscapeScroll, createTransmuteScroll];
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
  if (item.kind === "PowderBarrel" || item.kind === "Bomb") {
    unit.actions ??= [];
    if (!unit.actions.some((a) => a.id === "PowderBarrel")) {
      unit.actions = [...unit.actions, { id: "PowderBarrel", label: item.kind === "Bomb" ? "Ignite bomb" : "Ignite powder" }];
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
}

export interface ItemBreakContext {
  item: ItemState;
  nodeId: number;
  explode: (nodeId: number) => void;
}

export type ItemOnBreak = (ctx: ItemBreakContext) => void;

const ON_BREAK: Record<string, ItemOnBreak> = {
  PowderBarrel: ({ nodeId, explode }) => explode(nodeId),
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
  if (item.kind === "PowderBarrel" || item.kind === "Bomb") {
    unit.actions = (unit.actions ?? []).filter((a) => a.id !== "PowderBarrel");
  }
  if (item.kind === "EscapeScroll") {
    unit.actions = (unit.actions ?? []).filter((a) => a.id !== "EscapeScroll");
  }
  if (item.kind === "TransmuteScroll") {
    unit.actions = (unit.actions ?? []).filter((a) => a.id !== "TransmuteScroll");
  }
}

export function cloneItem(item: ItemState): ItemState {
  return { ...item };
}

const ITEM_MODELS: Record<string, string> = {
  Crossbow: "items/crossbow",
  PowderBarrel: "items/powder_barrel",
  Bomb: "items/powder_barrel",
  EscapeScroll: "items/pergamino",
  TransmuteScroll: "items/pergamino",
};

export function rehydrateItem(item: Omit<ItemState, "id" | "nodeId"> & { id?: string; nodeId?: number }): ItemState {
  return {
    id: item.id ?? `item_${nextItemId++}`,
    kind: item.kind,
    displayName: item.displayName,
    description: item.description,
    nodeId: item.nodeId ?? -1,
    dropOnDeath: item.dropOnDeath,
    color: item.color,
    shape: item.shape,
    model: item.model ?? ITEM_MODELS[item.kind],
  };
}

function grantedCrossbowShot(): MovementPattern {
  return { ...CROSSBOW_SHOT, grantedBy: "Crossbow" };
}
