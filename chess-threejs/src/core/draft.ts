import { FORMAT_SIZE, draftRowNeed, kingFile, isBackPiece, isFrontPiece, type ArmyFormat } from "./format";
import type { ArmySpec } from "./gameState";
import { catalogPieces } from "./pieces";

export type DraftPickModeId = "pieces-first" | "pawns-first";
export type DraftPhase = "waiting" | "ban" | "pick" | "done";
export type DraftPickRow = "back" | "front";

export interface DraftPickMode {
  id: DraftPickModeId;
  label: string;
  hint: string;
  /** Which row to fill first. Later modes can add placement steps without changing this. */
  firstRow: DraftPickRow;
}

export const DRAFT_PICK_MODES: DraftPickMode[] = [
  {
    id: "pieces-first",
    label: "Piezas primero",
    hint: "Fila de atrás, después peones y similares. Se ubican en orden de pick.",
    firstRow: "back",
  },
  {
    id: "pawns-first",
    label: "Peones primero",
    hint: "Fila de adelante, después las piezas. Se ubican en orden de pick.",
    firstRow: "front",
  },
];

export type DraftUniq = "unique" | "unique-per-player" | "free";

export const DRAFT_UNIQ_OPTIONS: { id: DraftUniq; label: string }[] = [
  { id: "unique", label: "Unique" },
  { id: "unique-per-player", label: "Unique per player" },
  { id: "free", label: "No restrictions" },
];

export interface DraftConfig {
  format: ArmyFormat;
  banCount: number;
  pickMode: DraftPickModeId;
  uniqueness: DraftUniq;
}

export interface DraftPlayerPicks {
  back: string[];
  front: string[];
}

export interface PublicDraft {
  config: DraftConfig;
  phase: DraftPhase;
  currentPlayerId: 0 | 1;
  bans: string[];
  picks: [DraftPlayerPicks, DraftPlayerPicks];
  pickRow: DraftPickRow | null;
}

export function clampBanCount(n: number): number {
  if (!Number.isFinite(n)) return 1;
  return Math.max(0, Math.min(4, Math.floor(n)));
}

export function normalizeDraftConfig(raw: Partial<DraftConfig> | undefined, format: ArmyFormat): DraftConfig {
  const pickMode = DRAFT_PICK_MODES.some((mode) => mode.id === raw?.pickMode) ? (raw!.pickMode as DraftPickModeId) : "pieces-first";
  const uniqueness = DRAFT_UNIQ_OPTIONS.some((option) => option.id === raw?.uniqueness)
    ? (raw!.uniqueness as DraftUniq)
    : "free";
  return {
    format,
    banCount: clampBanCount(raw?.banCount ?? 1),
    pickMode,
    uniqueness,
  };
}

export function pickModeDef(id: DraftPickModeId): DraftPickMode {
  return DRAFT_PICK_MODES.find((mode) => mode.id === id) ?? DRAFT_PICK_MODES[0];
}

function pickedIds(picks: DraftPlayerPicks): string[] {
  return [...picks.back, ...picks.front];
}

function countOf(ids: string[], definitionId: string): number {
  return ids.filter((id) => id === definitionId).length;
}

export function backRowWithKing(picks: string[], format: ArmyFormat): (string | null)[] {
  const width = FORMAT_SIZE[format].back;
  const kingX = kingFile(format);
  const row: (string | null)[] = Array.from({ length: width }, () => null);
  row[kingX] = "King";
  let i = 0;
  for (const piece of picks) {
    while (i < width && row[i] != null) i += 1;
    if (i >= width) break;
    row[i] = piece;
    i += 1;
  }
  return row;
}

function emptyPicks(): DraftPlayerPicks {
  return { back: [], front: [] };
}

function rowFilled(picks: DraftPlayerPicks, row: DraftPickRow, format: ArmyFormat): boolean {
  return picks[row].length >= draftRowNeed(format, row);
}

function bothFilled(picks: [DraftPlayerPicks, DraftPlayerPicks], row: DraftPickRow, format: ArmyFormat): boolean {
  return rowFilled(picks[0], row, format) && rowFilled(picks[1], row, format);
}

/** Pick order 1-2-2-1-1-2-2-1… so P1 opens and, when the count is even per pair, also closes. */
function snakePicker(index: number): 0 | 1 {
  const pair = Math.floor(index / 2);
  const odd = index % 2;
  return (pair % 2 === 0 ? odd : 1 - odd) as 0 | 1;
}

export class Draft {
  config: DraftConfig;
  phase: DraftPhase;
  currentPlayerId: 0 | 1;
  bans: string[];
  picks: [DraftPlayerPicks, DraftPlayerPicks];
  pickRow: DraftPickRow | null;

  constructor(config: DraftConfig, waitForOpponent: boolean) {
    this.config = normalizeDraftConfig(config, config.format);
    this.currentPlayerId = 0;
    this.bans = [];
    this.picks = [emptyPicks(), emptyPicks()];
    this.pickRow = null;
    this.phase = waitForOpponent ? "waiting" : this.nextPhaseAfterWaiting();
    if (this.phase === "pick") this.pickRow = pickModeDef(this.config.pickMode).firstRow;
  }

  static fromPublic(state: PublicDraft): Draft {
    const draft = new Draft(state.config, false);
    draft.phase = state.phase;
    draft.currentPlayerId = state.currentPlayerId;
    draft.bans = [...state.bans];
    draft.picks = [
      { back: [...state.picks[0].back], front: [...state.picks[0].front] },
      { back: [...state.picks[1].back], front: [...state.picks[1].front] },
    ];
    draft.pickRow = state.pickRow;
    return draft;
  }

  toPublic(): PublicDraft {
    return {
      config: { ...this.config },
      phase: this.phase,
      currentPlayerId: this.currentPlayerId,
      bans: [...this.bans],
      picks: [
        { back: [...this.picks[0].back], front: [...this.picks[0].front] },
        { back: [...this.picks[1].back], front: [...this.picks[1].front] },
      ],
      pickRow: this.pickRow,
    };
  }

  beginFromWaiting(): { ok: boolean; error?: string } {
    if (this.phase !== "waiting") return { ok: false, error: "Draft already started" };
    this.phase = this.nextPhaseAfterWaiting();
    this.currentPlayerId = 0;
    if (this.phase === "pick") this.pickRow = pickModeDef(this.config.pickMode).firstRow;
    return { ok: true };
  }

  legalBans(): string[] {
    if (this.phase !== "ban") return [];
    return catalogPieces()
      .map((piece) => piece.id)
      .filter((id) => this.canBan(id));
  }

  legalPicks(playerId: number = this.currentPlayerId): string[] {
    if (this.phase !== "pick" || this.pickRow == null) return [];
    return catalogPieces()
      .map((piece) => piece.id)
      .filter((id) => this.canPick(playerId, id));
  }

  tryBan(playerId: number, definitionId: string): { ok: boolean; error?: string } {
    if (this.phase !== "ban") return { ok: false, error: "Not banning now" };
    if (playerId !== this.currentPlayerId) return { ok: false, error: "Not your turn" };
    if (!this.canBan(definitionId)) return { ok: false, error: "Cannot ban that piece" };
    this.bans.push(definitionId);
    this.currentPlayerId = this.currentPlayerId === 0 ? 1 : 0;
    if (this.bans.length >= this.config.banCount * 2) {
      this.phase = "pick";
      this.currentPlayerId = 0;
      this.pickRow = pickModeDef(this.config.pickMode).firstRow;
    }
    return { ok: true };
  }

  tryPick(playerId: number, definitionId: string): { ok: boolean; error?: string } {
    if (this.phase !== "pick" || this.pickRow == null) return { ok: false, error: "Not picking now" };
    if (playerId !== this.currentPlayerId) return { ok: false, error: "Not your turn" };
    if (!this.canPick(playerId, definitionId)) return { ok: false, error: "Cannot pick that piece" };
    const row = this.pickRow;
    this.picks[playerId][row].push(definitionId);
    this.advanceAfterPick();
    return { ok: true };
  }

  toArmy(playerId: 0 | 1): ArmySpec {
    const picks = this.picks[playerId];
    return {
      fillEmptyFront: false,
      slots: [
        ...backRowWithKing(picks.back, this.config.format).flatMap((piece, x) =>
          piece ? [{ piece, x, row: "back" as const }] : []
        ),
        ...picks.front.map((piece, x) => ({ piece, x, row: "front" as const })),
      ],
    };
  }

  private nextPhaseAfterWaiting(): DraftPhase {
    return this.config.banCount > 0 ? "ban" : "pick";
  }

  private canBan(definitionId: string): boolean {
    if (definitionId === "King") return false;
    if (this.bans.includes(definitionId)) return false;
    if (!catalogPieces().some((piece) => piece.id === definitionId)) return false;
    const next = [...this.bans, definitionId];
    const backLeft = catalogPieces().filter((piece) => isBackPiece(piece.id) && piece.id !== "King" && !next.includes(piece.id));
    const frontLeft = catalogPieces().filter((piece) => isFrontPiece(piece.id) && !next.includes(piece.id));
    return backLeft.length > 0 && frontLeft.length > 0;
  }

  private canPick(playerId: number, definitionId: string): boolean {
    if (definitionId === "King") return false;
    if (this.bans.includes(definitionId)) return false;
    if (!catalogPieces().some((piece) => piece.id === definitionId)) return false;
    const row = this.pickRow;
    if (row == null) return false;
    const picks = this.picks[playerId];
    if (rowFilled(picks, row, this.config.format)) return false;
    if (row === "front") {
      if (!isFrontPiece(definitionId)) return false;
    } else if (!isBackPiece(definitionId)) {
      return false;
    }
    const uniq = this.config.uniqueness;
    if (uniq === "unique") {
      return countOf(pickedIds(this.picks[0]), definitionId) + countOf(pickedIds(this.picks[1]), definitionId) === 0;
    }
    if (uniq === "unique-per-player") return countOf(pickedIds(picks), definitionId) === 0;
    return true;
  }

  private advanceAfterPick(): void {
    const format = this.config.format;
    const row = this.pickRow;
    if (row && bothFilled(this.picks, row, format)) {
      const first = pickModeDef(this.config.pickMode).firstRow;
      const second: DraftPickRow = first === "back" ? "front" : "back";
      if (row === first && !bothFilled(this.picks, second, format)) {
        this.pickRow = second;
        this.currentPlayerId = 0;
        return;
      }
      this.phase = "done";
      this.pickRow = null;
      this.currentPlayerId = 0;
      return;
    }
    if (!row) return;
    const made = this.picks[0][row].length + this.picks[1][row].length;
    this.currentPlayerId = snakePicker(made);
  }
}
