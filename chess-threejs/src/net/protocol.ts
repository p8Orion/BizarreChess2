import type { PlayerStyle } from "../core/colors";
import type { ArmyKind } from "../core/pieces";
import type { ActionExecution, ArmySpec, MoveExecution, PickupExecution } from "../core/gameState";
import type { BoardKind, PublicState } from "../core/types";

export type { ArmyKind, BoardKind };

export interface HostMessage {
  type: "host";
  army?: ArmyKind;
  roster?: ArmySpec;
  opponentRoster?: ArmySpec;
  board?: BoardKind;
  colors?: [PlayerStyle, PlayerStyle];
}

export interface JoinMessage {
  type: "join";
  code: string;
  roster?: ArmySpec;
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

export interface ActionMessage {
  type: "action";
  unitId: number;
  actionId: string;
  targetNode?: number;
}

export interface ResignMessage {
  type: "resign";
}

export type ClientMessage = HostMessage | JoinMessage | MoveMessage | PickupMessage | ActionMessage | ResignMessage;

export interface HostedMessage {
  type: "hosted";
  code: string;
  playerId: number;
  state: PublicState;
}

export interface JoinedMessage {
  type: "joined";
  code: string;
  playerId: number;
  state: PublicState;
}

export interface StateMessage {
  type: "state";
  state: PublicState;
  notice?: string;
  lastMove?: MoveExecution;
  lastPickup?: PickupExecution;
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
  | StateMessage
  | ErrorMessage
  | OpponentLeftMessage;
