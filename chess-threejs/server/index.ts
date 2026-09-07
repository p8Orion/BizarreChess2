import { randomBytes } from "node:crypto";
import { createServer } from "node:http";
import { WebSocketServer, WebSocket } from "ws";
import { formatClock, normalizeTimeControlId, remainingNow } from "../src/core/clock";
import { actionNotice, Game } from "../src/core/gameState";
import { Draft, normalizeDraftConfig } from "../src/core/draft";
import { defaultBoardForFormat, parseArmyFormat, type ArmyFormat } from "../src/core/format";
import { boardSupportsFormat } from "../src/core/board";
import { normalizeDecorAmounts } from "../src/core/boardDecor";
import type { BoardKind, PublicState } from "../src/core/types";
import { GameEndReason, GamePhase } from "../src/core/types";
import { DEFAULT_STYLE_P1, DEFAULT_STYLE_P2, normalizeStyle, type PlayerStyle } from "../src/core/colors";
import type { ArmySpec } from "../src/core/gameState";
import type { ClientMessage, MatchSummary, PublicDraft, PublicLobby, ServerMessage, StateMessage } from "../src/net/protocol";
import { MatchStore, type MatchRecord, type MatchSetup, type MatchStatus, type SeatLoadout } from "./db";

const PORT = 8787;
const CODE_CHARS = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

interface Room {
  code: string;
  game: Game | null;
  draft: Draft | null;
  host: WebSocket | null;
  guest: WebSocket | null;
  hostToken: string;
  guestToken: string | null;
  board: BoardKind;
  colors?: [PlayerStyle, PlayerStyle];
  loadouts: [SeatLoadout, SeatLoadout];
  autoPickupItems: boolean;
  itemAmount: number;
  obstacleAmount: number;
  symmetricObstacles: boolean;
  seed: number;
  setup: MatchSetup;
  rematchReady: [boolean, boolean];
}

const rooms = new Map<string, Room>();
const sockets = new Map<WebSocket, { room: Room; playerId: number }>();
let store: MatchStore;

function send(ws: WebSocket | null, message: ServerMessage): void {
  if (!ws || ws.readyState !== WebSocket.OPEN) return;
  ws.send(JSON.stringify(message));
}

function makeCode(): string {
  for (let attempt = 0; attempt < 40; attempt++) {
    let code = "";
    for (let i = 0; i < 6; i++) code += CODE_CHARS[Math.floor(Math.random() * CODE_CHARS.length)];
    if (!rooms.has(code) && !store.hasCode(code)) return code;
  }
  return `R${Date.now().toString(36).slice(-5).toUpperCase()}`;
}

function makeToken(): string {
  return randomBytes(18).toString("base64url");
}

function makeSeed(): number {
  return (Math.random() * 0x7fffffff) | 0;
}

function now(): number {
  return Date.now();
}

function resolveFormat(raw: string | undefined): ArmyFormat {
  return parseArmyFormat(raw);
}

function resolveBoard(board: BoardKind | undefined, format: ArmyFormat): BoardKind {
  if (board && boardSupportsFormat(board, format)) return board;
  return defaultBoardForFormat(format);
}

function defaultLoadouts(): [SeatLoadout, SeatLoadout] {
  return [
    { style: { ...DEFAULT_STYLE_P1 }, ready: false },
    { style: { ...DEFAULT_STYLE_P2 }, ready: false },
  ];
}

function looksLikeRoster(raw: unknown): raw is ArmySpec {
  if (!raw || typeof raw !== "object") return false;
  const o = raw as ArmySpec;
  return Array.isArray(o.slots) && typeof o.fillEmptyFront === "boolean";
}

function rosterKey(spec?: ArmySpec): string {
  if (!spec || !Array.isArray(spec.slots)) return "";
  const slots = spec.slots
    .map((slot) => `${slot.row}:${slot.x}:${slot.piece}:${slot.pieceId ?? ""}:${slot.heldItem?.kind ?? ""}`)
    .sort();
  return `${spec.fillEmptyFront ? 1 : 0}|${slots.join(",")}`;
}

function normalizeLoadout(raw: SeatLoadout | undefined, fallback: SeatLoadout): SeatLoadout {
  if (!raw || typeof raw !== "object") return { ...fallback, style: { ...fallback.style } };
  return {
    style: normalizeStyle(raw.style, fallback.style),
    roster: looksLikeRoster(raw.roster) ? raw.roster : fallback.roster,
    armyLabel: typeof raw.armyLabel === "string" ? raw.armyLabel.slice(0, 48) : fallback.armyLabel,
    ready: raw.ready === true,
  };
}

function loadoutsFromSetup(setup: MatchSetup): [SeatLoadout, SeatLoadout] {
  const defaults = defaultLoadouts();
  if (setup.loadouts) {
    return [normalizeLoadout(setup.loadouts[0], defaults[0]), normalizeLoadout(setup.loadouts[1], defaults[1])];
  }
  if (setup.colors) {
    defaults[0].style = normalizeStyle(setup.colors[0], defaults[0].style);
    defaults[1].style = normalizeStyle(setup.colors[1], defaults[1].style);
  }
  if (setup.hostRoster) defaults[0].roster = setup.hostRoster;
  if (setup.guestRoster) defaults[1].roster = setup.guestRoster;
  return defaults;
}

function syncSetupLoadouts(room: Room): void {
  room.colors = [room.loadouts[0].style, room.loadouts[1].style];
  room.setup = {
    ...room.setup,
    colors: room.colors,
    loadouts: room.loadouts,
    hostRoster: room.loadouts[0].roster,
    guestRoster: room.loadouts[1].roster,
  };
}

function publicLobby(room: Room): PublicLobby {
  return {
    format: parseArmyFormat(room.setup.format),
    matchMode: room.setup.matchMode === "draft" ? "draft" : "normal",
    seats: [
      {
        connected: !!room.host,
        ready: room.loadouts[0].ready,
        armyLabel: room.loadouts[0].armyLabel,
        style: room.loadouts[0].style,
        hasRoster: !!room.loadouts[0].roster,
      },
      {
        connected: !!room.guest,
        ready: room.loadouts[1].ready,
        armyLabel: room.loadouts[1].armyLabel,
        style: room.loadouts[1].style,
        hasRoster: !!room.loadouts[1].roster,
      },
    ],
  };
}

function matchStatus(room: Room): MatchStatus {
  if (room.game?.phase === GamePhase.Ended) return "ended";
  if (room.draft) return room.draft.phase === "waiting" ? "waiting" : "draft";
  if (room.game) return room.guestToken ? "playing" : "waiting";
  return "waiting";
}

function toRecord(room: Room): MatchRecord {
  const ended = room.game?.phase === GamePhase.Ended;
  return {
    code: room.code,
    createdAt: now(),
    updatedAt: now(),
    status: matchStatus(room),
    hostToken: room.hostToken,
    guestToken: room.guestToken,
    seed: room.seed,
    board: room.board,
    setup: room.setup,
    snapshot: room.game ? room.game.toPublic() : null,
    draft: room.draft ? room.draft.toPublic() : null,
    winnerId: ended ? room.game?.winnerId ?? null : null,
    endReason: ended ? room.game?.endReason ?? null : null,
  };
}

function persist(room: Room, playerId: number, kind: string, payload: unknown): void {
  const existing = store.get(room.code);
  const record = toRecord(room);
  if (existing) {
    record.createdAt = existing.createdAt;
    store.save(record);
  } else {
    store.create(record);
  }
  store.appendEvent(room.code, playerId, kind, payload);
}

function roomFromRecord(record: MatchRecord): Room {
  return {
    code: record.code,
    game: record.snapshot ? Game.fromPublic(record.snapshot) : null,
    draft: record.draft ? Draft.fromPublic(record.draft) : null,
    host: null,
    guest: null,
    hostToken: record.hostToken,
    guestToken: record.guestToken,
    board: record.board,
    colors: record.setup.colors,
    loadouts: loadoutsFromSetup(record.setup),
    autoPickupItems: record.setup.autoPickupItems,
    itemAmount: record.setup.itemAmount,
    obstacleAmount: record.setup.obstacleAmount,
    symmetricObstacles: record.setup.symmetricObstacles,
    seed: record.seed,
    setup: record.setup,
    rematchReady: [false, false],
  };
}

function loadRoom(code: string): Room | null {
  const live = rooms.get(code);
  if (live) return live;
  const record = store.get(code);
  if (!record) return null;
  const room = roomFromRecord(record);
  rooms.set(code, room);
  settleFlag(room);
  return room;
}

function seatSocket(room: Room, playerId: number): WebSocket | null {
  return playerId === 0 ? room.host : room.guest;
}

function setSeat(room: Room, playerId: number, ws: WebSocket | null): void {
  if (playerId === 0) room.host = ws;
  else room.guest = ws;
}

function attach(room: Room, playerId: number, ws: WebSocket): void {
  const prev = seatSocket(room, playerId);
  if (prev && prev !== ws) {
    send(prev, { type: "error", message: "err.reconnectedOtherTab" });
    sockets.delete(prev);
    if (prev.readyState === WebSocket.OPEN) prev.close();
  }
  const old = sockets.get(ws);
  if (old) {
    if (old.room !== room || old.playerId !== playerId) setSeat(old.room, old.playerId, null);
  }
  setSeat(room, playerId, ws);
  sockets.set(ws, { room, playerId });
  rooms.set(room.code, room);
}

function forgetIfEmpty(room: Room): void {
  if (room.host || room.guest) return;
  rooms.delete(room.code);
}

function pushState(room: Room, extra: Omit<StateMessage, "type" | "state"> = {}): void {
  if (!room.game) return;
  const deliver = (ws: WebSocket | null, playerId: number) => {
    if (!ws) return;
    const lastMove = extra.lastMove ? room.game!.sanitizeMove(extra.lastMove, playerId) : undefined;
    const lastAction = extra.lastAction ? room.game!.sanitizeAction(extra.lastAction, playerId) : undefined;
    send(ws, {
      type: "state",
      state: room.game!.toPublic(playerId),
      notice: extra.notice,
      lastMove,
      lastPickup: extra.lastPickup,
      lastDrop: extra.lastDrop,
      lastAction,
      rematch: room.game!.phase === GamePhase.Ended ? room.rematchReady : undefined,
    });
  };
  deliver(room.host, 0);
  deliver(room.guest, 1);
}

function broadcast(room: Room, notice?: string): void {
  pushState(room, { notice });
}

function broadcastDraft(room: Room): void {
  if (!room.draft) return;
  const message: ServerMessage = { type: "draft-state", draft: room.draft.toPublic() };
  send(room.host, message);
  send(room.guest, message);
}

function broadcastLobby(room: Room): void {
  if (room.game) return;
  const message: ServerMessage = { type: "lobby-state", lobby: publicLobby(room) };
  send(room.host, message);
  send(room.guest, message);
}

function seatPayload(
  room: Room,
  playerId: number
): { state?: PublicState; draft?: PublicDraft; lobby?: PublicLobby; rematch?: [boolean, boolean] } {
  if (room.draft) return { draft: room.draft.toPublic(), lobby: publicLobby(room) };
  if (room.game) {
    const rematch = room.game.phase === GamePhase.Ended ? room.rematchReady : undefined;
    return { state: room.game.toPublic(playerId), rematch };
  }
  return { lobby: publicLobby(room) };
}

function broadcastRematch(room: Room): void {
  const message: ServerMessage = { type: "rematch-state", ready: room.rematchReady };
  send(room.host, message);
  send(room.guest, message);
}

function tokenOf(room: Room, playerId: number): string {
  return playerId === 0 ? room.hostToken : room.guestToken ?? room.hostToken;
}

function startMatchFromDraft(room: Room): void {
  const draft = room.draft;
  if (!draft || draft.phase !== "done") return;
  const colors: [PlayerStyle, PlayerStyle] = [room.loadouts[0].style, room.loadouts[1].style];
  const army0 = draft.toArmy(0);
  const army1 = draft.toArmy(1);
  room.colors = colors;
  room.loadouts[0].roster = army0;
  room.loadouts[1].roster = army1;
  room.loadouts[0].ready = false;
  room.loadouts[1].ready = false;
  room.rematchReady = [false, false];
  room.game = new Game(army0, colors, room.board, army1, {
    autoPickupItems: room.autoPickupItems,
    itemAmount: room.itemAmount,
    obstacleAmount: room.obstacleAmount,
    symmetricObstacles: room.symmetricObstacles,
    seed: room.seed,
    timeControl: room.setup.timeControl,
  });
  room.draft = null;
  room.game.startClock();
  persist(room, 0, "draft-done", { board: room.board });
  pushState(room, { notice: "notice.draftComplete" });
}

function tryStartNormal(room: Room): boolean {
  if (room.draft || room.game) return false;
  if (!room.guestToken) return false;
  const [a, b] = room.loadouts;
  if (!a.ready || !b.ready || !a.roster || !b.roster) return false;
  const colors: [PlayerStyle, PlayerStyle] = [a.style, b.style];
  room.colors = colors;
  a.ready = false;
  b.ready = false;
  room.rematchReady = [false, false];
  room.game = new Game(a.roster, colors, room.board, b.roster, {
    autoPickupItems: room.autoPickupItems,
    itemAmount: room.itemAmount,
    obstacleAmount: room.obstacleAmount,
    symmetricObstacles: room.symmetricObstacles,
    seed: room.seed,
    timeControl: room.setup.timeControl,
  });
  room.game.startClock();
  persist(room, 0, "start", { board: room.board });
  pushState(room, { notice: "notice.matchStarted" });
  return true;
}

function tryStartRematch(room: Room): boolean {
  if (!room.game || room.game.phase !== GamePhase.Ended) return false;
  if (!room.rematchReady[0] || !room.rematchReady[1]) return false;
  const [a, b] = room.loadouts;
  if (!a.roster || !b.roster) return false;
  const colors: [PlayerStyle, PlayerStyle] = [a.style, b.style];
  room.colors = colors;
  room.seed = makeSeed();
  room.setup = { ...room.setup, seed: room.seed, colors, hostRoster: a.roster, guestRoster: b.roster };
  room.rematchReady = [false, false];
  room.game = new Game(a.roster, colors, room.board, b.roster, {
    autoPickupItems: room.autoPickupItems,
    itemAmount: room.itemAmount,
    obstacleAmount: room.obstacleAmount,
    symmetricObstacles: room.symmetricObstacles,
    seed: room.seed,
    timeControl: room.setup.timeControl,
  });
  room.game.startClock();
  persist(room, 0, "rematch", { board: room.board, seed: room.seed });
  pushState(room, { notice: "notice.rematch" });
  return true;
}

function settleFlag(room: Room, playerId = 0): boolean {
  if (!room.game?.applyFlag()) return false;
  persist(room, playerId, "timeout", {});
  broadcast(room, "end.Timeout");
  return true;
}

function summarize(record: MatchRecord, playerId: number, room?: Room): MatchSummary {
  const game = room?.game ?? (record.snapshot ? Game.fromPublic(record.snapshot) : null);
  if (game) game.applyFlag();
  const status = room ? matchStatus(room) : record.status;
  const clock = game?.clock ?? record.snapshot?.clock ?? null;
  const currentPlayerId = game?.currentPlayerId ?? record.snapshot?.currentPlayerId;
  const now = Date.now();
  return {
    code: record.code,
    playerId,
    status,
    currentPlayerId,
    myTurn: status === "playing" && currentPlayerId === playerId,
    timeControl: clock?.control.id ?? record.setup.timeControl,
    clockLabel: clock && currentPlayerId != null ? formatClock(remainingNow(clock, currentPlayerId, now)) : undefined,
    updatedAt: room ? now : record.updatedAt,
    endReason: game?.endReason ?? record.endReason ?? undefined,
  };
}

function leave(ws: WebSocket): void {
  const seat = sockets.get(ws);
  if (!seat) return;
  sockets.delete(ws);
  const { room, playerId } = seat;
  if (seatSocket(room, playerId) === ws) setSeat(room, playerId, null);
  const other = playerId === 0 ? room.guest : room.host;
  send(other, { type: "opponent-disconnected" });
  if (room.game?.phase === GamePhase.Ended) {
    room.rematchReady[playerId] = false;
    broadcastRematch(room);
  } else if (!room.game) broadcastLobby(room);
  forgetIfEmpty(room);
}

function ended(room: Room): boolean {
  return room.game?.phase === GamePhase.Ended || store.get(room.code)?.status === "ended";
}

const httpServer = createServer((_req, res) => {
  res.writeHead(200, { "Content-Type": "text/plain" });
  res.end("Bizarre Chess WebSocket server");
});

const wss = new WebSocketServer({ server: httpServer, path: "/ws" });

wss.on("connection", (ws) => {
  ws.on("message", (raw) => {
    let msg: ClientMessage;
    try {
      msg = JSON.parse(String(raw)) as ClientMessage;
    } catch {
      send(ws, { type: "error", message: "err.invalidMessage" });
      return;
    }

    if (msg.type === "list") {
      const games: MatchSummary[] = [];
      for (const seat of msg.seats) {
        const code = seat.code.trim().toUpperCase();
        const record = store.get(code);
        if (!record) continue;
        const playerId = seat.token === record.hostToken ? 0 : seat.token === record.guestToken ? 1 : -1;
        if (playerId < 0) continue;
        const room = rooms.get(code);
        if (room) settleFlag(room, playerId);
        else if (record.snapshot) {
          const peek = Game.fromPublic(record.snapshot);
          if (peek.applyFlag()) {
            record.snapshot = peek.toPublic();
            record.status = "ended";
            record.winnerId = peek.winnerId;
            record.endReason = peek.endReason;
            store.save(record);
          }
        }
        games.push(summarize(store.get(code) ?? record, playerId, room));
      }
      send(ws, { type: "games", games });
      return;
    }

    if (msg.type === "host") {
      if (sockets.has(ws)) {
        send(ws, { type: "error", message: "err.alreadyInRoom" });
        return;
      }
      const code = makeCode();
      const scatter = normalizeDecorAmounts(msg);
      const seed = makeSeed();
      const hostToken = makeToken();
      if (msg.matchMode === "draft") {
        const format = resolveFormat(msg.format);
        const config = normalizeDraftConfig(msg.draft, format);
        const draft = new Draft(config, true);
        const loadouts = defaultLoadouts();
        const setup: MatchSetup = {
          seed,
          board: resolveBoard(msg.board, format),
          colors: [loadouts[0].style, loadouts[1].style],
          autoPickupItems: msg.autoPickupItems !== false,
          itemAmount: scatter.itemAmount,
          obstacleAmount: scatter.obstacleAmount,
          symmetricObstacles: scatter.symmetricObstacles,
          matchMode: "draft",
          format,
          draft: config,
          loadouts,
          timeControl: normalizeTimeControlId(msg.timeControl),
        };
        const room: Room = {
          code,
          game: null,
          draft,
          host: ws,
          guest: null,
          hostToken,
          guestToken: null,
          board: setup.board,
          colors: setup.colors,
          loadouts,
          autoPickupItems: setup.autoPickupItems,
          itemAmount: scatter.itemAmount,
          obstacleAmount: scatter.obstacleAmount,
          symmetricObstacles: scatter.symmetricObstacles,
          seed,
          setup,
          rematchReady: [false, false],
        };
        rooms.set(code, room);
        sockets.set(ws, { room, playerId: 0 });
        persist(room, 0, "host", { matchMode: "draft", board: room.board, seed });
        send(ws, { type: "hosted", code, playerId: 0, token: hostToken, draft: draft.toPublic(), lobby: publicLobby(room) });
        return;
      }
      const format = resolveFormat(msg.format);
      const timeControl = normalizeTimeControlId(msg.timeControl);
      const loadouts = defaultLoadouts();
      const setup: MatchSetup = {
        seed,
        board: resolveBoard(msg.board, format),
        colors: [loadouts[0].style, loadouts[1].style],
        autoPickupItems: msg.autoPickupItems !== false,
        itemAmount: scatter.itemAmount,
        obstacleAmount: scatter.obstacleAmount,
        symmetricObstacles: scatter.symmetricObstacles,
        matchMode: "normal",
        format,
        loadouts,
        timeControl,
      };
      const room: Room = {
        code,
        game: null,
        draft: null,
        host: ws,
        guest: null,
        hostToken,
        guestToken: null,
        board: setup.board,
        colors: setup.colors,
        loadouts,
        autoPickupItems: setup.autoPickupItems,
        itemAmount: scatter.itemAmount,
        obstacleAmount: scatter.obstacleAmount,
        symmetricObstacles: scatter.symmetricObstacles,
        seed,
        setup,
        rematchReady: [false, false],
      };
      rooms.set(code, room);
      sockets.set(ws, { room, playerId: 0 });
      persist(room, 0, "host", { matchMode: "normal", board: room.board, seed });
      send(ws, { type: "hosted", code, playerId: 0, token: hostToken, lobby: publicLobby(room) });
      return;
    }

    if (msg.type === "resume") {
      const code = msg.code.trim().toUpperCase();
      const room = loadRoom(code);
      if (!room) {
        send(ws, { type: "error", message: "err.roomNotFound" });
        return;
      }
      const playerId = msg.token === room.hostToken ? 0 : msg.token === room.guestToken ? 1 : -1;
      if (playerId < 0) {
        send(ws, { type: "error", message: "err.notASeat" });
        return;
      }
      attach(room, playerId, ws);
      settleFlag(room, playerId);
      const payload = seatPayload(room, playerId);
      send(ws, { type: "resumed", code: room.code, playerId, token: tokenOf(room, playerId), ...payload });
      const other = playerId === 0 ? room.guest : room.host;
      if (room.game) send(other, { type: "state", state: room.game.toPublic(1 - playerId), notice: "notice.opponentReconnected" });
      else {
        if (room.draft) send(other, { type: "draft-state", draft: room.draft.toPublic() });
        send(other, { type: "lobby-state", lobby: publicLobby(room) });
      }
      return;
    }

    if (msg.type === "join") {
      const code = msg.code.trim().toUpperCase();
      const room = loadRoom(code);
      if (!room) {
        send(ws, { type: "error", message: "err.roomNotFound" });
        return;
      }
      if (room.guestToken) {
        send(ws, { type: "error", message: "err.seatTaken" });
        return;
      }
      if (ended(room)) {
        send(ws, { type: "error", message: "err.matchEnded" });
        return;
      }
      if (room.guest) {
        send(ws, { type: "error", message: "err.roomFull" });
        return;
      }
      room.guestToken = makeToken();
      attach(room, 1, ws);
      if (room.draft) {
        const started = room.draft.beginFromWaiting();
        if (!started.ok) {
          send(ws, { type: "error", message: started.error ?? "err.draftStarted" });
          return;
        }
        persist(room, 1, "join", { draft: true });
        const draft: PublicDraft = room.draft.toPublic();
        send(ws, { type: "joined", code: room.code, playerId: 1, token: room.guestToken, draft, lobby: publicLobby(room) });
        send(room.host, { type: "draft-state", draft });
        send(room.host, { type: "lobby-state", lobby: publicLobby(room) });
        return;
      }
      if (room.game) {
        room.game.startClock();
        persist(room, 1, "join", { started: true });
        send(ws, { type: "joined", code: room.code, playerId: 1, token: room.guestToken, state: room.game.toPublic(1) });
        send(room.host, { type: "state", state: room.game.toPublic(0), notice: "notice.opponentJoined" });
        return;
      }
      persist(room, 1, "join", { lobby: true });
      send(ws, { type: "joined", code: room.code, playerId: 1, token: room.guestToken, lobby: publicLobby(room) });
      send(room.host, { type: "lobby-state", lobby: publicLobby(room) });
      return;
    }

    const seat = sockets.get(ws);
    if (!seat) {
      send(ws, { type: "error", message: "err.joinFirst" });
      return;
    }

    if (msg.type === "draft-action") {
      const draft = seat.room.draft;
      if (!draft) {
        send(ws, { type: "error", message: "err.noDraft" });
        return;
      }
      const result = msg.action === "ban" ? draft.tryBan(seat.playerId, msg.piece) : draft.tryPick(seat.playerId, msg.piece);
      if (!result.ok) {
        send(ws, { type: "error", message: result.error ?? "err.invalidDraft" });
        return;
      }
      persist(seat.room, seat.playerId, "draft-action", { action: msg.action, piece: msg.piece });
      if (draft.phase === "done") {
        startMatchFromDraft(seat.room);
        return;
      }
      broadcastDraft(seat.room);
      return;
    }

    if (msg.type === "seat-setup") {
      const room = seat.room;
      if (room.game) {
        send(ws, { type: "error", message: "err.matchStarted" });
        return;
      }
      const loadout = room.loadouts[seat.playerId];
      loadout.style = normalizeStyle(msg.style, loadout.style);
      if (!room.draft && looksLikeRoster(msg.roster)) {
        const changed = rosterKey(loadout.roster) !== rosterKey(msg.roster);
        loadout.roster = msg.roster;
        loadout.armyLabel = typeof msg.armyLabel === "string" ? msg.armyLabel.slice(0, 48) : loadout.armyLabel;
        if (changed) loadout.ready = false;
      }
      if (!room.draft && typeof msg.ready === "boolean") {
        if (msg.ready && !loadout.roster) {
          send(ws, { type: "error", message: "err.chooseArmyFirst" });
          return;
        }
        loadout.ready = msg.ready;
      }
      syncSetupLoadouts(room);
      persist(room, seat.playerId, "seat-setup", {
        style: loadout.style,
        armyLabel: loadout.armyLabel ?? null,
        ready: loadout.ready,
      });
      if (tryStartNormal(room)) return;
      broadcastLobby(room);
      return;
    }

    if (msg.type === "rematch") {
      const room = seat.room;
      if (room.game?.phase !== GamePhase.Ended) {
        send(ws, { type: "error", message: "err.matchNotStarted" });
        return;
      }
      room.rematchReady[seat.playerId] = msg.ready === true;
      if (tryStartRematch(room)) return;
      broadcastRematch(room);
      return;
    }

    if (!seat.room.game) {
      send(ws, { type: "error", message: "err.matchNotStarted" });
      return;
    }

    if (settleFlag(seat.room, seat.playerId)) return;

    if (msg.type === "move") {
      const result = seat.room.game.tryMove(msg.unitId, msg.targetNode, seat.playerId);
      if (!result.success) {
        send(ws, { type: "error", message: result.error ?? "err.invalidMove" });
        return;
      }
      persist(seat.room, seat.playerId, "move", { unitId: msg.unitId, targetNode: msg.targetNode });
      const notice = result.captureBlocked
        ? "notice.forcefieldBlocked"
        : result.gameEnded
          ? `end.${result.endReason}`
          : undefined;
      pushState(seat.room, { notice, lastMove: result });
      return;
    }

    if (msg.type === "pickup") {
      const result = seat.room.game.tryPickup(msg.unitId, seat.playerId);
      if (!result.success) {
        send(ws, { type: "error", message: result.error ?? "err.cannotPickup" });
        return;
      }
      persist(seat.room, seat.playerId, "pickup", { unitId: msg.unitId });
      pushState(seat.room, { notice: "notice.pickedUp", lastPickup: result });
      return;
    }

    if (msg.type === "drop") {
      const result = seat.room.game.tryDrop(msg.unitId, seat.playerId);
      if (!result.success) {
        send(ws, { type: "error", message: result.error ?? "err.cannotDrop" });
        return;
      }
      persist(seat.room, seat.playerId, "drop", { unitId: msg.unitId });
      pushState(seat.room, { notice: "notice.dropped", lastDrop: result });
      return;
    }

    if (msg.type === "action") {
      const result = seat.room.game.tryAction(msg.unitId, msg.actionId, seat.playerId, msg.targetNode);
      if (!result.success) {
        send(ws, { type: "error", message: result.error ?? "err.cannotAction" });
        return;
      }
      persist(seat.room, seat.playerId, "action", { unitId: msg.unitId, actionId: msg.actionId, targetNode: msg.targetNode });
      const notice = actionNotice(result);
      pushState(seat.room, { notice, lastAction: result });
      return;
    }

    if (msg.type === "resign") {
      seat.room.game.resign(seat.playerId);
      persist(seat.room, seat.playerId, "resign", { reason: GameEndReason.Resignation });
      broadcast(seat.room, "notice.resignation");
    }
  });

  ws.on("close", () => leave(ws));
});

const boot = await MatchStore.open();
store = boot;
httpServer.listen(PORT, "127.0.0.1", () => {
  console.log(`Bizarre Chess WS listening on ws://127.0.0.1:${PORT}/ws`);
  console.log(`Match DB ${store.filePath}`);
});

setInterval(() => {
  for (const room of rooms.values()) settleFlag(room);
}, 1000);
