import { DEFAULT_STYLE_P1, DEFAULT_STYLE_P2, normalizeStyle, type PlayerStyle } from "../core/colors";
import { rehydrateItem } from "../core/items";
import { ARMIES, armyByKind, catalogPieces, type ArmyKind } from "../core/pieces";
import type { ItemState, UnitState } from "../core/types";
import type { SpawnSlot } from "../core/gameState";
import {
  USER_STORE_KEY,
  USER_STORE_VERSION,
  type PersistedArmy,
  type PersistedItem,
  type PersistedPiece,
  type PersistedSlot,
  type PersistedUser,
} from "./types";

function newId(prefix: string): string {
  const rand = globalThis.crypto?.randomUUID?.() ?? `${Date.now().toString(36)}${Math.random().toString(36).slice(2, 8)}`;
  return `${prefix}_${rand}`;
}

export function catalogDefaultId(kind: ArmyKind): string {
  return `default:${armyByKind(kind).id}`;
}

function catalogSlots(kind: ArmyKind): PersistedSlot[] {
  return armyByKind(kind).slots.map((slot) => ({
    x: slot.x,
    row: slot.row,
    definitionId: slot.piece,
  }));
}

function catalogDefaultArmy(kind: ArmyKind, stats?: Pick<PersistedArmy, "fights" | "wins" | "draws">): PersistedArmy {
  const def = armyByKind(kind);
  return {
    id: catalogDefaultId(def.id),
    name: def.label,
    basedOn: def.id,
    isDefault: true,
    fillEmptyFront: def.fillEmptyFront,
    slots: catalogSlots(def.id),
    fights: stats?.fights ?? 0,
    wins: stats?.wins ?? 0,
    draws: stats?.draws ?? 0,
  };
}

function copyArmyFromTemplate(kind: ArmyKind, name?: string): PersistedArmy {
  const def = armyByKind(kind);
  return {
    id: newId("army"),
    name: name ?? def.label,
    basedOn: def.id,
    isDefault: false,
    fillEmptyFront: def.fillEmptyFront,
    slots: catalogSlots(def.id),
    fights: 0,
    wins: 0,
    draws: 0,
  };
}

function defaultArmyId(armies: PersistedArmy[]): string {
  return armies.find((army) => army.id === catalogDefaultId("mini-bizarre") || army.basedOn === "mini-bizarre")?.id ?? armies[0]?.id ?? "";
}

function slotKey(slot: { x: number; row: string; definitionId?: string; piece?: string }): string {
  return `${slot.row}:${slot.x}:${slot.definitionId ?? slot.piece}`;
}

function matchesCatalog(army: PersistedArmy): boolean {
  const def = armyByKind(army.basedOn);
  if (army.slots.some((slot) => slot.pieceId)) return false;
  if (army.slots.length !== def.slots.length) return false;
  const have = new Set(army.slots.map(slotKey));
  return def.slots.every((slot) => have.has(slotKey(slot)));
}

const STALE_DEFAULT_NAMES = new Set(["Mini Bizarre — Cannon Crossbow King Camel + 4 pawns", "Mini Bizarre"]);

function looksLikeCatalogDefault(army: PersistedArmy): boolean {
  if (army.isDefault || army.id.startsWith("default:")) return true;
  const def = armyByKind(army.basedOn);
  const stockName = army.name === def.label || STALE_DEFAULT_NAMES.has(army.name);
  return stockName && matchesCatalog(army);
}

function ensureCatalogDefaults(armies: PersistedArmy[]): { armies: PersistedArmy[]; remap: Map<string, string> } {
  const remap = new Map<string, string>();
  const stats = new Map<ArmyKind, { fights: number; wins: number; draws: number }>();
  const customs: PersistedArmy[] = [];
  for (const army of armies) {
    if (looksLikeCatalogDefault(army)) {
      const kind = armyByKind(army.basedOn).id;
      const prev = stats.get(kind) ?? { fights: 0, wins: 0, draws: 0 };
      stats.set(kind, {
        fights: prev.fights + army.fights,
        wins: prev.wins + army.wins,
        draws: prev.draws + army.draws,
      });
      remap.set(army.id, catalogDefaultId(kind));
      continue;
    }
    customs.push({ ...army, isDefault: false });
  }
  const defaults = ARMIES.map((def) => catalogDefaultArmy(def.id, stats.get(def.id)));
  return { armies: [...defaults, ...customs], remap };
}

function seedUser(): PersistedUser {
  const armies = ARMIES.map((def) => catalogDefaultArmy(def.id));
  const activeArmyId = defaultArmyId(armies);
  return {
    version: USER_STORE_VERSION,
    style: { ...DEFAULT_STYLE_P1 },
    guestStyle: { ...DEFAULT_STYLE_P2 },
    armies,
    pieces: [],
    activeArmyId,
    guestArmyId: activeArmyId,
  };
}

function normalizeUser(raw: unknown): PersistedUser {
  const fallback = seedUser();
  if (!raw || typeof raw !== "object") return fallback;
  const o = raw as Partial<PersistedUser>;
  const rawVersion = typeof o.version === "number" ? o.version : 0;
  const rawArmies = Array.isArray(o.armies) && o.armies.length > 0 ? o.armies.map(normalizeArmy).map(migrateMiniBizarre) : fallback.armies;
  const { armies, remap } = ensureCatalogDefaults(rawArmies);
  const pieces = Array.isArray(o.pieces) ? o.pieces.filter(isPiece) : [];
  const preferred = rawVersion < 2 ? defaultArmyId(armies) : "";
  const requested = typeof o.activeArmyId === "string" ? remap.get(o.activeArmyId) ?? o.activeArmyId : "";
  const activeArmyId = armies.some((a) => a.id === requested) && rawVersion >= 2 ? requested : preferred || defaultArmyId(armies);
  const guestRequested = typeof o.guestArmyId === "string" ? remap.get(o.guestArmyId) ?? o.guestArmyId : "";
  const guestArmyId = armies.some((a) => a.id === guestRequested && a.isDefault) ? guestRequested : defaultArmyId(armies);
  return {
    version: USER_STORE_VERSION,
    style: normalizeStyle(o.style, DEFAULT_STYLE_P1),
    guestStyle: normalizeStyle(o.guestStyle, DEFAULT_STYLE_P2),
    armies,
    pieces,
    activeArmyId,
    guestArmyId,
  };
}

function migrateMiniBizarre(army: PersistedArmy): PersistedArmy {
  if (army.basedOn !== "mini-bizarre") return army;
  const customized = army.slots.some((slot) => slot.pieceId);
  if (customized) {
    const slots = army.slots.map((slot) => {
      if (slot.row === "back" && slot.definitionId === "Rook") return { ...slot, definitionId: "Camel" };
      if (slot.row === "front" && slot.x === 2 && slot.definitionId === "Pawn" && !slot.pieceId) {
        return { ...slot, definitionId: "Bomber" };
      }
      return slot;
    });
    return { ...army, slots };
  }
  const fresh = copyArmyFromTemplate("mini-bizarre");
  const staleName = army.name === "Mini Bizarre — Cannon Crossbow King Camel + 4 pawns" || army.name === "Mini Bizarre";
  return {
    ...army,
    name: staleName ? fresh.name : army.name,
    fillEmptyFront: fresh.fillEmptyFront,
    slots: fresh.slots,
  };
}

function normalizeArmy(raw: PersistedArmy): PersistedArmy {
  const basedOn = armyByKind(raw.basedOn).id;
  return {
    id: typeof raw.id === "string" && raw.id ? raw.id : newId("army"),
    name: typeof raw.name === "string" && raw.name.trim() ? raw.name.trim() : armyByKind(basedOn).label,
    basedOn,
    isDefault: raw.isDefault === true || (typeof raw.id === "string" && raw.id.startsWith("default:")),
    fillEmptyFront: raw.fillEmptyFront !== false,
    slots: Array.isArray(raw.slots)
      ? raw.slots
          .filter((s) => s && typeof s.definitionId === "string" && (s.row === "back" || s.row === "front"))
          .map((s) => ({
            x: Number(s.x) || 0,
            row: s.row,
            definitionId: s.definitionId,
            pieceId: typeof s.pieceId === "string" ? s.pieceId : undefined,
          }))
      : copyArmyFromTemplate(basedOn).slots,
    fights: Math.max(0, Math.floor(Number(raw.fights) || 0)),
    wins: Math.max(0, Math.floor(Number(raw.wins) || 0)),
    draws: Math.max(0, Math.floor(Number(raw.draws) || 0)),
  };
}

function isPiece(raw: PersistedPiece): raw is PersistedPiece {
  return !!raw && typeof raw.id === "string" && typeof raw.definitionId === "string";
}

export function loadUser(): PersistedUser {
  try {
    const text = localStorage.getItem(USER_STORE_KEY);
    if (!text) return saveUser(seedUser());
    return saveUser(normalizeUser(JSON.parse(text)));
  } catch {
    return seedUser();
  }
}

export function saveUser(user: PersistedUser): PersistedUser {
  const next = normalizeUser(user);
  try {
    localStorage.setItem(USER_STORE_KEY, JSON.stringify(next));
  } catch {
    /* quota / private mode */
  }
  return next;
}

export function updateUser(patch: (user: PersistedUser) => void): PersistedUser {
  const user = loadUser();
  patch(user);
  return saveUser(user);
}

export function activeArmy(user: PersistedUser): PersistedArmy {
  return user.armies.find((a) => a.id === user.activeArmyId) ?? user.armies[0];
}

function toPersistedItem(item: ItemState): PersistedItem {
  return {
    kind: item.kind,
    displayName: item.displayName,
    description: item.description,
    dropOnDeath: item.dropOnDeath,
    color: item.color,
    shape: item.shape,
    model: item.model,
  };
}

export function resolveArmy(user: PersistedUser, armyId = user.activeArmyId): { slots: SpawnSlot[]; fillEmptyFront: boolean } {
  const army = user.armies.find((a) => a.id === armyId) ?? activeArmy(user);
  return {
    fillEmptyFront: army.fillEmptyFront,
    slots: army.slots.map((slot) => {
      const piece = slot.pieceId ? user.pieces.find((p) => p.id === slot.pieceId) : undefined;
      return {
        piece: slot.definitionId,
        x: slot.x,
        row: slot.row,
        pieceId: slot.pieceId,
        heldItem: piece?.item ? rehydrateItem(piece.item) : null,
      };
    }),
  };
}

export function setActiveArmy(armyId: string): PersistedUser {
  return updateUser((user) => {
    if (user.armies.some((a) => a.id === armyId)) user.activeArmyId = armyId;
  });
}

export function setGuestArmy(armyId: string): PersistedUser {
  return updateUser((user) => {
    if (user.armies.some((a) => a.id === armyId && a.isDefault)) user.guestArmyId = armyId;
  });
}

export function setUserStyles(style: PlayerStyle, guestStyle: PlayerStyle): PersistedUser {
  return updateUser((user) => {
    user.style = style;
    user.guestStyle = guestStyle;
  });
}

export function duplicateArmy(armyId: string): PersistedUser {
  return updateUser((user) => {
    const src = user.armies.find((a) => a.id === armyId);
    if (!src) return;
    const copy: PersistedArmy = {
      ...structuredClone(src),
      id: newId("army"),
      name: `${src.name} (copy)`,
      isDefault: false,
      slots: src.slots.map((slot) => ({ ...slot, pieceId: undefined })),
      fights: 0,
      wins: 0,
      draws: 0,
    };
    user.armies.push(copy);
    user.activeArmyId = copy.id;
  });
}

export function syncArmyFromUnits(user: PersistedUser, armyId: string, units: UnitState[], ownerId: number): PersistedUser {
  const army = user.armies.find((a) => a.id === armyId);
  if (!army || army.isDefault) return user;
  const mine = units.filter((u) => u.ownerId === ownerId && u.rosterRow != null && u.rosterX != null);
  for (const unit of mine) {
    const slot = findSlot(army.slots, unit);
    if (!slot) continue;
    if (unit.heldItem) {
      if (!slot.pieceId) {
        const piece: PersistedPiece = { id: newId("piece"), definitionId: unit.definitionId };
        slot.pieceId = piece.id;
        user.pieces.push(piece);
      }
      const piece = user.pieces.find((p) => p.id === slot.pieceId);
      if (piece) {
        piece.definitionId = unit.definitionId;
        piece.item = toPersistedItem(unit.heldItem);
      }
      continue;
    }
    if (!slot.pieceId) continue;
    const piece = user.pieces.find((p) => p.id === slot.pieceId);
    if (piece) delete piece.item;
  }
  return saveUser(user);
}

function findSlot(slots: PersistedSlot[], unit: UnitState): PersistedSlot | undefined {
  if (unit.pieceId) {
    const byId = slots.find((s) => s.pieceId === unit.pieceId);
    if (byId) return byId;
  }
  return slots.find(
    (s) => s.row === unit.rosterRow && s.x === unit.rosterX && s.definitionId === unit.definitionId
  );
}

export function armyOptions(user: PersistedUser, onlyDefaults = false): { id: string; label: string; isDefault: boolean }[] {
  return user.armies
    .filter((army) => !onlyDefaults || army.isDefault)
    .map((army) => ({
      id: army.id,
      isDefault: army.isDefault,
      label: army.fights > 0 ? `${army.name} · ${army.fights} game${army.fights === 1 ? "" : "s"}` : army.name,
    }));
}

export function armyStats(army: PersistedArmy): { games: number; wins: number; draws: number; losses: number } {
  const games = army.fights;
  const wins = army.wins;
  const draws = army.draws;
  return { games, wins, draws, losses: Math.max(0, games - wins - draws) };
}

export function armyRecord(army: PersistedArmy): string {
  if (army.isDefault && army.fights <= 0) return "Default · anyone can pick this";
  if (army.fights <= 0) return "No games yet — editable";
  const { games, wins, draws, losses } = armyStats(army);
  const bits = [`${games} game${games === 1 ? "" : "s"}`, `${wins} win${wins === 1 ? "" : "s"}`];
  if (draws) bits.push(`${draws} draw${draws === 1 ? "" : "s"}`);
  if (losses > 0) bits.push(`${losses} loss${losses === 1 ? "" : "es"}`);
  return bits.join(" · ");
}

export function canEditArmy(army: PersistedArmy): boolean {
  return !army.isDefault && army.fights <= 0;
}

export function canDeleteArmy(army: PersistedArmy, armies: PersistedArmy[]): boolean {
  return !army.isDefault && armies.some((other) => other.id !== army.id);
}

export function renameArmy(armyId: string, name: string): PersistedUser {
  const trimmed = name.trim();
  return updateUser((user) => {
    const army = user.armies.find((a) => a.id === armyId);
    if (!army || !canEditArmy(army) || !trimmed) return;
    army.name = trimmed;
  });
}

export function setArmySlot(
  armyId: string,
  row: "back" | "front",
  x: number,
  definitionId: string | null
): { user: PersistedUser; error?: "locked" | "no-king" | "unknown-piece" } {
  let error: "locked" | "no-king" | "unknown-piece" | undefined;
  if (definitionId && !catalogPieces().some((p) => p.id === definitionId)) {
    return { user: loadUser(), error: "unknown-piece" };
  }
  const user = updateUser((next) => {
    const army = next.armies.find((a) => a.id === armyId);
    if (!army || !canEditArmy(army)) {
      error = "locked";
      return;
    }
    const slots = army.slots.filter((s) => !(s.row === row && s.x === x));
    if (definitionId) {
      const prev = army.slots.find((s) => s.row === row && s.x === x);
      slots.push({
        x,
        row,
        definitionId,
        pieceId: prev?.definitionId === definitionId ? prev.pieceId : undefined,
      });
    }
    if (!slots.some((s) => s.definitionId === "King")) {
      error = "no-king";
      return;
    }
    army.slots = slots.sort((a, b) => (a.row === b.row ? a.x - b.x : a.row === "back" ? -1 : 1));
  });
  return { user, error };
}

function pruneOrphanPieces(user: PersistedUser): void {
  const keep = new Set(user.armies.flatMap((army) => army.slots.map((slot) => slot.pieceId).filter((id): id is string => !!id)));
  user.pieces = user.pieces.filter((piece) => keep.has(piece.id));
}

export function deleteArmy(armyId: string): { user: PersistedUser; error?: "last-army" | "default" } {
  const current = loadUser();
  const target = current.armies.find((army) => army.id === armyId);
  if (target?.isDefault) return { user: current, error: "default" };
  if (!canDeleteArmy(target ?? current.armies[0], current.armies)) return { user: current, error: "last-army" };
  const user = updateUser((next) => {
    const army = next.armies.find((item) => item.id === armyId);
    if (!army || army.isDefault) return;
    const remaining = next.armies.filter((item) => item.id !== armyId);
    if (remaining.length === next.armies.length) return;
    next.armies = remaining;
    if (!remaining.some((item) => item.id === next.activeArmyId)) {
      next.activeArmyId = defaultArmyId(remaining);
    }
    pruneOrphanPieces(next);
  });
  return { user };
}

export function recordArmyFight(armyId: string, result: "win" | "loss" | "draw"): PersistedUser {
  return updateUser((user) => {
    const army = user.armies.find((a) => a.id === armyId);
    if (!army) return;
    army.fights += 1;
    if (result === "win") army.wins += 1;
    if (result === "draw") army.draws += 1;
  });
}
