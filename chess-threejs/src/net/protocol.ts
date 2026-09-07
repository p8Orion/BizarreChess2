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
}

export interface JoinMessage {
  type: "join";
  code: string;
  roster?: ArmySpec;
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

export type ClientMessage =
  | HostMessage
  | JoinMessage
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
  state?: PublicState;
  draft?: PublicDraft;
}

export interface JoinedMessage {
  type: "joined";
  code: string;
  playerId: number;
  state?: PublicState;
  draft?: PublicDraft;
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

export type ServerMessage =
  | HostedMessage
  | JoinedMessage
  | DraftStateMessage
  | StateMessage
  | ErrorMessage
  | OpponentLeftMessage;
