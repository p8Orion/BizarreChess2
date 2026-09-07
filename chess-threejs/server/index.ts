import { createServer } from "node:http";
import { WebSocketServer, WebSocket } from "ws";
import { actionNotice, Game } from "../src/core/gameState";
import { Draft, normalizeDraftConfig } from "../src/core/draft";
import { defaultBoardForFormat, type ArmyFormat } from "../src/core/format";
import { boardSupportsFormat } from "../src/core/board";
import { normalizeDecorAmounts } from "../src/core/boardDecor";
import { armyByKind } from "../src/core/pieces";
import type { BoardKind } from "../src/core/types";
import type { PlayerStyle } from "../src/core/colors";
import type { ClientMessage, PublicDraft, ServerMessage } from "../src/net/protocol";

const PORT = 8787;
const CODE_CHARS = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

interface Room {
  code: string;
  game: Game | null;
  draft: Draft | null;
  host: WebSocket;
  guest: WebSocket | null;
  board: BoardKind;
  colors?: [PlayerStyle, PlayerStyle];
  autoPickupItems: boolean;
  itemAmount: number;
  obstacleAmount: number;
  symmetricObstacles: boolean;
}

const rooms = new Map<string, Room>();
const sockets = new Map<WebSocket, { room: Room; playerId: number }>();

function send(ws: WebSocket, message: ServerMessage): void {
  if (ws.readyState === WebSocket.OPEN) ws.send(JSON.stringify(message));
}

function makeCode(): string {
  for (let attempt = 0; attempt < 20; attempt++) {
    let code = "";
    for (let i = 0; i < 6; i++) code += CODE_CHARS[Math.floor(Math.random() * CODE_CHARS.length)];
    if (!rooms.has(code)) return code;
  }
  return `R${Date.now().toString(36).slice(-5).toUpperCase()}`;
}

function broadcast(room: Room, notice?: string): void {
  if (!room.game) return;
  const message: ServerMessage = { type: "state", state: room.game.toPublic(), notice };
  send(room.host, message);
  if (room.guest) send(room.guest, message);
}

function broadcastDraft(room: Room): void {
  if (!room.draft) return;
  const message: ServerMessage = { type: "draft-state", draft: room.draft.toPublic() };
  send(room.host, message);
  if (room.guest) send(room.guest, message);
}

function resolveFormat(raw: string | undefined): ArmyFormat {
  return raw === "normal" ? "normal" : "mini";
}

function resolveBoard(board: BoardKind | undefined, format: ArmyFormat): BoardKind {
  if (board && boardSupportsFormat(board, format)) return board;
  return defaultBoardForFormat(format);
}

function startMatchFromDraft(room: Room): void {
  const draft = room.draft;
  if (!draft || draft.phase !== "done") return;
  room.game = new Game(draft.toArmy(0), room.colors, room.board, draft.toArmy(1), {
    autoPickupItems: room.autoPickupItems,
    itemAmount: room.itemAmount,
    obstacleAmount: room.obstacleAmount,
    symmetricObstacles: room.symmetricObstacles,
  });
  room.draft = null;
  const message: ServerMessage = { type: "state", state: room.game.toPublic(), notice: "Draft complete" };
  send(room.host, message);
  if (room.guest) send(room.guest, message);
}

function leave(ws: WebSocket): void {
  const seat = sockets.get(ws);
  if (!seat) return;
  sockets.delete(ws);
  const { room, playerId } = seat;
  if (playerId === 0) {
    if (room.guest) {
      send(room.guest, { type: "opponent-left" });
      sockets.delete(room.guest);
    }
    rooms.delete(room.code);
    return;
  }
  room.guest = null;
  send(room.host, { type: "opponent-left" });
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
      send(ws, { type: "error", message: "Invalid message" });
      return;
    }

    if (msg.type === "host") {
      if (sockets.has(ws)) {
        send(ws, { type: "error", message: "Already in a room" });
        return;
      }
      const code = makeCode();
      const scatter = normalizeDecorAmounts(msg);
      if (msg.matchMode === "draft") {
        const format = resolveFormat(msg.format);
        const config = normalizeDraftConfig(msg.draft, format);
        const draft = new Draft(config, true);
        const room: Room = {
          code,
          game: null,
          draft,
          host: ws,
          guest: null,
          board: resolveBoard(msg.board, format),
          colors: msg.colors,
          autoPickupItems: msg.autoPickupItems === true,
          itemAmount: scatter.itemAmount,
          obstacleAmount: scatter.obstacleAmount,
          symmetricObstacles: scatter.symmetricObstacles,
        };
        rooms.set(code, room);
        sockets.set(ws, { room, playerId: 0 });
        send(ws, { type: "hosted", code, playerId: 0, draft: draft.toPublic() });
        return;
      }
      const army = msg.roster ?? armyByKind(msg.army);
      const game = new Game(army, msg.colors, msg.board ?? "bizarre", msg.opponentRoster, {
        autoPickupItems: msg.autoPickupItems === true,
        itemAmount: scatter.itemAmount,
        obstacleAmount: scatter.obstacleAmount,
        symmetricObstacles: scatter.symmetricObstacles,
      });
      const room: Room = {
        code,
        game,
        draft: null,
        host: ws,
        guest: null,
        board: msg.board ?? "bizarre",
        colors: msg.colors,
        autoPickupItems: msg.autoPickupItems === true,
        itemAmount: scatter.itemAmount,
        obstacleAmount: scatter.obstacleAmount,
        symmetricObstacles: scatter.symmetricObstacles,
      };
      rooms.set(code, room);
      sockets.set(ws, { room, playerId: 0 });
      send(ws, { type: "hosted", code, playerId: 0, state: game.toPublic() });
      return;
    }

    if (msg.type === "join") {
      const code = msg.code.trim().toUpperCase();
      const room = rooms.get(code);
      if (!room) {
        send(ws, { type: "error", message: "Room not found" });
        return;
      }
      if (room.guest) {
        send(ws, { type: "error", message: "Room is full" });
        return;
      }
      room.guest = ws;
      sockets.set(ws, { room, playerId: 1 });
      if (room.draft) {
        const started = room.draft.beginFromWaiting();
        if (!started.ok) {
          send(ws, { type: "error", message: started.error ?? "Draft already started" });
          return;
        }
        const draft: PublicDraft = room.draft.toPublic();
        send(ws, { type: "joined", code: room.code, playerId: 1, draft });
        send(room.host, { type: "draft-state", draft });
        return;
      }
      if (!room.game) {
        send(ws, { type: "error", message: "Room is not ready" });
        return;
      }
      if (msg.roster) room.game.replaceArmy(1, msg.roster);
      send(ws, { type: "joined", code: room.code, playerId: 1, state: room.game.toPublic() });
      send(room.host, { type: "state", state: room.game.toPublic(), notice: "Opponent joined" });
      return;
    }

    const seat = sockets.get(ws);
    if (!seat) {
      send(ws, { type: "error", message: "Join or host a room first" });
      return;
    }

    if (msg.type === "draft-action") {
      const draft = seat.room.draft;
      if (!draft) {
        send(ws, { type: "error", message: "No draft in this room" });
        return;
      }
      const result = msg.action === "ban" ? draft.tryBan(seat.playerId, msg.piece) : draft.tryPick(seat.playerId, msg.piece);
      if (!result.ok) {
        send(ws, { type: "error", message: result.error ?? "Invalid draft action" });
        return;
      }
      if (draft.phase === "done") {
        startMatchFromDraft(seat.room);
        return;
      }
      broadcastDraft(seat.room);
      return;
    }

    if (!seat.room.game) {
      send(ws, { type: "error", message: "Match has not started" });
      return;
    }

    if (msg.type === "move") {
      const result = seat.room.game.tryMove(msg.unitId, msg.targetNode, seat.playerId);
      if (!result.success) {
        send(ws, { type: "error", message: result.error ?? "Invalid move" });
        return;
      }
      const notice = result.captureBlocked
        ? "Forcefield blocked the capture"
        : result.gameEnded
          ? result.endReason
          : undefined;
      const message: ServerMessage = { type: "state", state: seat.room.game.toPublic(), notice, lastMove: result };
      send(seat.room.host, message);
      if (seat.room.guest) send(seat.room.guest, message);
      return;
    }

    if (msg.type === "pickup") {
      const result = seat.room.game.tryPickup(msg.unitId, seat.playerId);
      if (!result.success) {
        send(ws, { type: "error", message: result.error ?? "Cannot pick up" });
        return;
      }
      const message: ServerMessage = {
        type: "state",
        state: seat.room.game.toPublic(),
        notice: "Picked up Force Field Generator",
        lastPickup: result,
      };
      send(seat.room.host, message);
      if (seat.room.guest) send(seat.room.guest, message);
      return;
    }

    if (msg.type === "drop") {
      const result = seat.room.game.tryDrop(msg.unitId, seat.playerId);
      if (!result.success) {
        send(ws, { type: "error", message: result.error ?? "Cannot drop" });
        return;
      }
      const message: ServerMessage = {
        type: "state",
        state: seat.room.game.toPublic(),
        notice: "Dropped item",
        lastDrop: result,
      };
      send(seat.room.host, message);
      if (seat.room.guest) send(seat.room.guest, message);
      return;
    }

    if (msg.type === "action") {
      const result = seat.room.game.tryAction(msg.unitId, msg.actionId, seat.playerId, msg.targetNode);
      if (!result.success) {
        send(ws, { type: "error", message: result.error ?? "Cannot use action" });
        return;
      }
      const notice = actionNotice(result);
      const message: ServerMessage = { type: "state", state: seat.room.game.toPublic(), notice, lastAction: result };
      send(seat.room.host, message);
      if (seat.room.guest) send(seat.room.guest, message);
      return;
    }

    if (msg.type === "resign") {
      seat.room.game.resign(seat.playerId);
      broadcast(seat.room, "Resignation");
    }
  });

  ws.on("close", () => leave(ws));
});

httpServer.listen(PORT, "127.0.0.1", () => {
  console.log(`Bizarre Chess WS listening on ws://127.0.0.1:${PORT}/ws`);
});
