import { DEFAULT_STYLE_P1, DEFAULT_STYLE_P2, normalizeStyle, type PlayerStyle } from "../core/colors";
import { BOARD_OPTIONS, boardSupportsFormat } from "../core/board";
import { clampDecorAmount } from "../core/boardDecor";
import { DRAFT_PICK_MODES, DRAFT_UNIQ_OPTIONS, clampBanCount, type DraftPickModeId, type DraftUniq } from "../core/draft";
import { FORMAT_SIZE, defaultBoardForFormat, formatOfArmyKind, parseArmyFormat, pieceFitsRow, type ArmyFormat, type MatchMode } from "../core/format";
import { rehydrateItem } from "../core/items";
import { ARMIES, armyByKind, catalogPieces, type ArmyKind } from "../core/pieces";
import type { ItemState, UnitState } from "../core/types";
import type { SpawnSlot } from "../core/gameState";
import { armyLabel, detectLocale, isLocale, t } from "../i18n";
import {
  DEFAULT_SETTINGS,
  USER_STORE_KEY,
  USER_STORE_VERSION,
  type PersistedArmy,
  type PersistedItem,
  type PersistedPiece,
  type PersistedSlot,
  type PersistedUser,
  type UserSettings,
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
    name: name ?? armyLabel(def.id),
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

const STALE_DEFAULT_NAMES = new Set([
  "Mini Bizarre — Cannon Crossbow King Camel + 4 pawns",
  "Mini Bizarre — Cannon Crossbow King Camel + Lancer Defender Bomber",
  "Mini Bizarre — Cannon Crossbow King Saltamontes + Lancer Defender Bomber",
  "Mini Bizarre",
]);

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
    settings: { ...DEFAULT_SETTINGS, locale: detectLocale() },
  };
}

function normalizeUser(raw: unknown): PersistedUser {
  const fallback = seedUser();
  if (!raw || typeof raw !== "object") return fallback;
  const o = raw as Partial<PersistedUser>;
  const rawVersion = typeof o.version === "number" ? o.version : 0;
  const rawArmies = Array.isArray(o.armies) && o.armies.length > 0 ? o.armies.map(normalizeArmy) : fallback.armies;
  const migrated = rawVersion < 5 ? rawArmies.map(migrateMiniBizarre) : rawArmies;
  const { armies, remap } = ensureCatalogDefaults(migrated);
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
    settings: normalizeSettings(o.settings, rawVersion),
  };
}

function readSettings(raw: unknown): UserSettings {
  const o = raw && typeof raw === "object" ? (raw as Partial<UserSettings>) : {};
  const matchMode: MatchMode = o.matchMode === "draft" ? "draft" : "normal";
  const armyFormat: ArmyFormat = parseArmyFormat(o.armyFormat);
  const boardId = typeof o.board === "string" ? o.board : "";
  const board = BOARD_OPTIONS.some((option) => option.id === boardId) && boardSupportsFormat(boardId as UserSettings["board"], armyFormat)
    ? (boardId as UserSettings["board"])
    : defaultBoardForFormat(armyFormat);
  const pickMode: DraftPickModeId = DRAFT_PICK_MODES.some((mode) => mode.id === o.draftPickMode)
    ? (o.draftPickMode as DraftPickModeId)
    : "pieces-first";
  const uniqueness: DraftUniq = DRAFT_UNIQ_OPTIONS.some((option) => option.id === o.draftUniqueness)
    ? (o.draftUniqueness as DraftUniq)
    : "free";
  return {
    locale: isLocale(o.locale) ? o.locale : detectLocale(),
    autoPickupItems: o.autoPickupItems !== false,
    matchMode,
    armyFormat,
    board,
    draftBanCount: clampBanCount(o.draftBanCount ?? 1),
    draftPickMode: pickMode,
    draftUniqueness: uniqueness,
    itemAmount: clampDecorAmount(o.itemAmount ?? DEFAULT_SETTINGS.itemAmount),
    obstacleAmount: clampDecorAmount(o.obstacleAmount ?? DEFAULT_SETTINGS.obstacleAmount),
    symmetricObstacles: o.symmetricObstacles !== false,
    timeControl: o.timeControl === "24h" ? "24h" : "5+3",
  };
}

function normalizeSettings(raw: unknown, storeVersion: number = USER_STORE_VERSION): UserSettings {
  const settings = readSettings(raw);
  if (storeVersion < 7) settings.autoPickupItems = true;
  if (storeVersion >= 4) return settings;
  return {
    ...settings,
    itemAmount: DEFAULT_SETTINGS.itemAmount,
    obstacleAmount: DEFAULT_SETTINGS.obstacleAmount,
  };
}

function isStaleMiniBizarreLayout(army: PersistedArmy): boolean {
  if (STALE_DEFAULT_NAMES.has(army.name)) return true;
  const have = new Set(army.slots.map(slotKey));
  const stale = [
    ["Cannon", "Crossbowman", "King", "Camel"],
    ["Cannon", "Crossbowman", "King", "Rook"],
  ];
  const fronts = [
    ["Pawn", "Pawn", "Pawn", "Pawn"],
    ["Lancer", "Defender", "Bomber", "Pawn"],
  ];
  for (const back of stale) {
    for (const front of fronts) {
      const want = new Set([
        ...back.map((piece, x) => `back:${x}:${piece}`),
        ...front.map((piece, x) => `front:${x}:${piece}`),
      ]);
      if (want.size === have.size && [...want].every((key) => have.has(key))) return true;
    }
  }
  return false;
}

function migrateMiniBizarre(army: PersistedArmy): PersistedArmy {
  if (army.basedOn !== "mini-bizarre") return army;
  if (army.slots.some((slot) => slot.pieceId)) {
    if (!isStaleMiniBizarreLayout(army)) return army;
    const slots = army.slots.map((slot) => {
      if (slot.row === "back" && slot.definitionId === "Rook") return { ...slot, definitionId: "Camel" };
      if (slot.row === "front" && slot.x === 2 && slot.definitionId === "Pawn" && !slot.pieceId) {
        return { ...slot, definitionId: "Bomber" };
      }
      return slot;
    });
    return { ...army, slots };
  }
  if (!army.isDefault && !matchesCatalog(army) && !isStaleMiniBizarreLayout(army)) return army;
  const fresh = copyArmyFromTemplate("mini-bizarre");
  const staleName = STALE_DEFAULT_NAMES.has(army.name);
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

export function setUserSettings(patch: Partial<UserSettings>): PersistedUser {
  return updateUser((user) => {
    user.settings = normalizeSettings({ ...user.settings, ...patch });
  });
}

export function duplicateArmy(armyId: string): PersistedUser {
  return updateUser((user) => {
    const src = user.armies.find((a) => a.id === armyId);
    if (!src) return;
    const copy: PersistedArmy = {
      ...structuredClone(src),
      id: newId("army"),
      name: t("army.copyName", { name: src.isDefault ? armyLabel(src.basedOn) : src.name }),
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
  const mine = units.filter(
    (u) => u.ownerId === ownerId && !u.convertedThisMatch && u.rosterRow != null && u.rosterX != null
  );
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

export function armyOptions(
  user: PersistedUser,
  onlyDefaults = false,
  format?: ArmyFormat
): { id: string; label: string; isDefault: boolean }[] {
  return user.armies
    .filter((army) => !onlyDefaults || army.isDefault)
    .filter((army) => !format || formatOfArmyKind(army.basedOn) === format)
    .map((army) => ({
      id: army.id,
      isDefault: army.isDefault,
      label: displayArmyName(army),
    }));
}

export function armyStats(army: PersistedArmy): { games: number; wins: number; draws: number; losses: number } {
  const games = army.fights;
  const wins = army.wins;
  const draws = army.draws;
  return { games, wins, draws, losses: Math.max(0, games - wins - draws) };
}

function displayArmyName(army: PersistedArmy): string {
  const name = army.isDefault ? armyLabel(army.basedOn) : army.name;
  return army.fights > 0 ? t("army.withGames", { name, n: army.fights }) : name;
}

export function armyRecord(army: PersistedArmy): string {
  if (army.isDefault && army.fights <= 0) return t("army.recordDefault");
  if (army.fights <= 0) return t("army.recordFresh");
  const { games, wins, draws, losses } = armyStats(army);
  return t("army.recordLine", {
    games: games === 1 ? t("army.games1") : t("army.gamesN", { n: games }),
    wins: wins === 1 ? t("army.wins1") : t("army.winsN", { n: wins }),
    draws: draws === 0 ? "" : draws === 1 ? t("army.draws1") : t("army.drawsN", { n: draws }),
    losses: losses === 0 ? "" : losses === 1 ? t("army.losses1") : t("army.lossesN", { n: losses }),
  });
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
): { user: PersistedUser; error?: "locked" | "no-king" | "unknown-piece" | "wrong-row" } {
  let error: "locked" | "no-king" | "unknown-piece" | "wrong-row" | undefined;
  if (definitionId && !catalogPieces().some((p) => p.id === definitionId)) {
    return { user: loadUser(), error: "unknown-piece" };
  }
  const user = updateUser((next) => {
    const army = next.armies.find((a) => a.id === armyId);
    if (!army || !canEditArmy(army)) {
      error = "locked";
      return;
    }
    const width = FORMAT_SIZE[formatOfArmyKind(army.basedOn)][row];
    if (x < 0 || x >= width) {
      error = "wrong-row";
      return;
    }
    if (definitionId && !pieceFitsRow(definitionId, row)) {
      error = "wrong-row";
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
