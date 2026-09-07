import type { TimeControlId } from "../core/clock";
import type { PlayerStyle } from "../core/colors";
import type { DraftConfig, PublicDraft } from "../core/draft";
import type { ArmyFormat, MatchMode } from "../core/format";
import type { ArmyKind } from "../core/pieces";
import type { ActionExecution, ArmySpec, DropExecution, MoveExecution, PickupExecution } from "../core/gameState";
import type { BoardKind, PublicState } from "../core/types";

export type { ArmyKind, BoardKind, ArmyFormat, MatchMode, DraftConfig, PublicDraft };

export interface HostMessage {
  type: "host";
  army?: ArmyKind;
  roster?: ArmySpec;
  opponentRoster?: ArmySpec;
  board?: BoardKind;
  colors?: [PlayerStyle, PlayerStyle];
  autoPickupItems?: boolean;
  matchMode?: MatchMode;
  format?: ArmyFormat;
  draft?: DraftConfig;
  itemAmount?: number;
  obstacleAmount?: number;
  symmetricObstacles?: boolean;
  timeControl?: TimeControlId;
}

export interface JoinMessage {
  type: "join";
  code: string;
  roster?: ArmySpec;
}

export interface SeatSetupMessage {
  type: "seat-setup";
  style: PlayerStyle;
  roster?: ArmySpec;
  armyLabel?: string;
  ready?: boolean;
}

export interface PublicLobbySeat {
  connected: boolean;
  ready: boolean;
  armyLabel?: string;
  style: PlayerStyle;
  hasRoster: boolean;
}

export interface PublicLobby {
  seats: [PublicLobbySeat, PublicLobbySeat];
  format: ArmyFormat;
  matchMode: MatchMode;
}

export interface ResumeMessage {
  type: "resume";
  code: string;
  token: string;
}

export interface DraftActionMessage {
  type: "draft-action";
  action: "ban" | "pick";
  piece: string;
}

export interface MoveMessage {
  type: "move";
  unitId: number;
  targetNode: number;
}

export interface PickupMessage {
  type: "pickup";
  unitId: number;
}

export interface DropMessage {
  type: "drop";
  unitId: number;
}

export interface ActionMessage {
  type: "action";
  unitId: number;
  actionId: string;
  targetNode?: number;
}

export interface ResignMessage {
  type: "resign";
}

export interface ListMessage {
  type: "list";
  seats: { code: string; token: string }[];
}

export type ClientMessage =
  | HostMessage
  | JoinMessage
  | ResumeMessage
  | ListMessage
  | SeatSetupMessage
  | DraftActionMessage
  | MoveMessage
  | PickupMessage
  | DropMessage
  | ActionMessage
  | ResignMessage;

export interface HostedMessage {
  type: "hosted";
  code: string;
  playerId: number;
  token: string;
  state?: PublicState;
  draft?: PublicDraft;
  lobby?: PublicLobby;
}

export interface JoinedMessage {
  type: "joined";
  code: string;
  playerId: number;
  token: string;
  state?: PublicState;
  draft?: PublicDraft;
  lobby?: PublicLobby;
}

export interface ResumedMessage {
  type: "resumed";
  code: string;
  playerId: number;
  token: string;
  state?: PublicState;
  draft?: PublicDraft;
  lobby?: PublicLobby;
}

export interface LobbyStateMessage {
  type: "lobby-state";
  lobby: PublicLobby;
}

export interface DraftStateMessage {
  type: "draft-state";
  draft: PublicDraft;
}

export interface StateMessage {
  type: "state";
  state: PublicState;
  notice?: string;
  lastMove?: MoveExecution;
  lastPickup?: PickupExecution;
  lastDrop?: DropExecution;
  lastAction?: ActionExecution;
}

export interface ErrorMessage {
  type: "error";
  message: string;
}

export interface OpponentLeftMessage {
  type: "opponent-left";
}

export interface OpponentDisconnectedMessage {
  type: "opponent-disconnected";
}

export interface MatchSummary {
  code: string;
  playerId: number;
  status: "waiting" | "draft" | "playing" | "ended";
  currentPlayerId?: number;
  myTurn: boolean;
  timeControl?: TimeControlId;
  clockLabel?: string;
  updatedAt: number;
  endReason?: string;
}

export interface GamesMessage {
  type: "games";
  games: MatchSummary[];
}

export type ServerMessage =
  | HostedMessage
  | JoinedMessage
  | ResumedMessage
  | DraftStateMessage
  | LobbyStateMessage
  | StateMessage
  | GamesMessage
  | ErrorMessage
  | OpponentLeftMessage
  | OpponentDisconnectedMessage;
