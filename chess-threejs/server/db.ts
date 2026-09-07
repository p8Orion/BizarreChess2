import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import initSqlJs, { type Database, type SqlValue } from "sql.js";
import type { TimeControlId } from "../src/core/clock";
import type { DraftConfig, PublicDraft } from "../src/core/draft";
import type { ArmyFormat, MatchMode } from "../src/core/format";
import type { ArmySpec } from "../src/core/gameState";
import type { PlayerStyle } from "../src/core/colors";
import type { BoardKind, PublicState } from "../src/core/types";

export type MatchStatus = "waiting" | "draft" | "playing" | "ended";

export interface SeatLoadout {
  style: PlayerStyle;
  roster?: ArmySpec;
  armyLabel?: string;
  ready: boolean;
}

export interface MatchSetup {
  seed: number;
  board: BoardKind;
  colors?: [PlayerStyle, PlayerStyle];
  autoPickupItems: boolean;
  itemAmount: number;
  obstacleAmount: number;
  symmetricObstacles: boolean;
  matchMode: MatchMode;
  format?: ArmyFormat;
  hostRoster?: ArmySpec;
  guestRoster?: ArmySpec;
  loadouts?: [SeatLoadout, SeatLoadout];
  draft?: DraftConfig;
  timeControl?: TimeControlId;
}

export interface MatchRecord {
  code: string;
  createdAt: number;
  updatedAt: number;
  status: MatchStatus;
  hostToken: string;
  guestToken: string | null;
  seed: number;
  board: BoardKind;
  setup: MatchSetup;
  snapshot: PublicState | null;
  draft: PublicDraft | null;
  winnerId: number | null;
  endReason: string | null;
}

export interface MatchEvent {
  seq: number;
  at: number;
  playerId: number;
  kind: string;
  payload: unknown;
}

const SCHEMA = `
CREATE TABLE IF NOT EXISTS matches (
  code TEXT PRIMARY KEY,
  created_at INTEGER NOT NULL,
  updated_at INTEGER NOT NULL,
  status TEXT NOT NULL,
  host_token TEXT NOT NULL,
  guest_token TEXT,
  seed INTEGER NOT NULL,
  board TEXT NOT NULL,
  setup_json TEXT NOT NULL,
  snapshot_json TEXT,
  draft_json TEXT,
  winner_id INTEGER,
  end_reason TEXT
);
CREATE TABLE IF NOT EXISTS events (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  code TEXT NOT NULL,
  seq INTEGER NOT NULL,
  at INTEGER NOT NULL,
  player_id INTEGER NOT NULL,
  kind TEXT NOT NULL,
  payload_json TEXT NOT NULL,
  UNIQUE(code, seq)
);
`;

function defaultDbPath(): string {
  return join(dirname(fileURLToPath(import.meta.url)), "..", "data", "matches.sqlite");
}

function wasmDir(): string {
  return join(dirname(fileURLToPath(import.meta.url)), "..", "node_modules", "sql.js", "dist");
}

function asString(value: SqlValue): string {
  return String(value ?? "");
}

function asNumber(value: SqlValue): number {
  return typeof value === "number" ? value : Number(value ?? 0);
}

function parseJson<T>(raw: SqlValue, fallback: T): T {
  if (raw == null || raw === "") return fallback;
  try {
    return JSON.parse(asString(raw)) as T;
  } catch {
    return fallback;
  }
}

export class MatchStore {
  readonly filePath: string;
  private constructor(
    private readonly db: Database,
    filePath: string
  ) {
    this.filePath = filePath;
  }

  static async open(filePath = defaultDbPath()): Promise<MatchStore> {
    mkdirSync(dirname(filePath), { recursive: true });
    const SQL = await initSqlJs({ locateFile: (file) => join(wasmDir(), file) });
    let db: Database;
    try {
      db = new SQL.Database(readFileSync(filePath));
    } catch {
      db = new SQL.Database();
    }
    const store = new MatchStore(db, filePath);
    store.db.run(SCHEMA);
    store.flush();
    return store;
  }

  hasCode(code: string): boolean {
    const stmt = this.db.prepare("SELECT 1 FROM matches WHERE code = ?");
    stmt.bind([code]);
    const found = stmt.step();
    stmt.free();
    return found;
  }

  get(code: string): MatchRecord | null {
    const stmt = this.db.prepare("SELECT * FROM matches WHERE code = ?");
    stmt.bind([code]);
    if (!stmt.step()) {
      stmt.free();
      return null;
    }
    const row = stmt.getAsObject();
    stmt.free();
    return {
      code: asString(row.code),
      createdAt: asNumber(row.created_at),
      updatedAt: asNumber(row.updated_at),
      status: asString(row.status) as MatchStatus,
      hostToken: asString(row.host_token),
      guestToken: row.guest_token == null || row.guest_token === "" ? null : asString(row.guest_token),
      seed: asNumber(row.seed),
      board: asString(row.board) as BoardKind,
      setup: parseJson<MatchSetup>(row.setup_json, {
        seed: asNumber(row.seed),
        board: asString(row.board) as BoardKind,
        autoPickupItems: true,
        itemAmount: 5,
        obstacleAmount: 5,
        symmetricObstacles: true,
        matchMode: "normal",
      }),
      snapshot: parseJson<PublicState | null>(row.snapshot_json, null),
      draft: parseJson<PublicDraft | null>(row.draft_json, null),
      winnerId: row.winner_id == null ? null : asNumber(row.winner_id),
      endReason: row.end_reason == null ? null : asString(row.end_reason),
    };
  }

  create(record: MatchRecord): void {
    this.db.run(
      `INSERT INTO matches (
        code, created_at, updated_at, status, host_token, guest_token, seed, board,
        setup_json, snapshot_json, draft_json, winner_id, end_reason
      ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
      [
        record.code,
        record.createdAt,
        record.updatedAt,
        record.status,
        record.hostToken,
        record.guestToken,
        record.seed,
        record.board,
        JSON.stringify(record.setup),
        record.snapshot ? JSON.stringify(record.snapshot) : null,
        record.draft ? JSON.stringify(record.draft) : null,
        record.winnerId,
        record.endReason,
      ]
    );
    this.flush();
  }

  save(record: MatchRecord): void {
    record.updatedAt = Date.now();
    this.db.run(
      `UPDATE matches SET
        updated_at = ?, status = ?, host_token = ?, guest_token = ?, seed = ?, board = ?,
        setup_json = ?, snapshot_json = ?, draft_json = ?, winner_id = ?, end_reason = ?
      WHERE code = ?`,
      [
        record.updatedAt,
        record.status,
        record.hostToken,
        record.guestToken,
        record.seed,
        record.board,
        JSON.stringify(record.setup),
        record.snapshot ? JSON.stringify(record.snapshot) : null,
        record.draft ? JSON.stringify(record.draft) : null,
        record.winnerId,
        record.endReason,
        record.code,
      ]
    );
    this.flush();
  }

  appendEvent(code: string, playerId: number, kind: string, payload: unknown): void {
    const stmt = this.db.prepare("SELECT COALESCE(MAX(seq), 0) AS seq FROM events WHERE code = ?");
    stmt.bind([code]);
    const seq = stmt.step() ? asNumber(stmt.getAsObject().seq) + 1 : 1;
    stmt.free();
    this.db.run(
      "INSERT INTO events (code, seq, at, player_id, kind, payload_json) VALUES (?, ?, ?, ?, ?, ?)",
      [code, seq, Date.now(), playerId, kind, JSON.stringify(payload ?? null)]
    );
    this.flush();
  }

  events(code: string): MatchEvent[] {
    const stmt = this.db.prepare(
      "SELECT seq, at, player_id, kind, payload_json FROM events WHERE code = ? ORDER BY seq"
    );
    stmt.bind([code]);
    const out: MatchEvent[] = [];
    while (stmt.step()) {
      const row = stmt.getAsObject();
      out.push({
        seq: asNumber(row.seq),
        at: asNumber(row.at),
        playerId: asNumber(row.player_id),
        kind: asString(row.kind),
        payload: parseJson(row.payload_json, null),
      });
    }
    stmt.free();
    return out;
  }

  private flush(): void {
    writeFileSync(this.filePath, Buffer.from(this.db.export()));
  }
}
