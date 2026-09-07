import type { PlayerStyle } from "../core/colors";
import type { ArmyKind } from "../core/pieces";
import type { ItemState } from "../core/types";

export const USER_STORE_KEY = "bizarre-chess.user.v1";
export const USER_STORE_VERSION = 3 as const;

export interface PersistedItem {
  kind: string;
  displayName: string;
  description: string;
  dropOnDeath: boolean;
  color: string;
  shape: ItemState["shape"];
  model?: string;
}

export interface PersistedPiece {
  id: string;
  definitionId: string;
  item?: PersistedItem;
}

export interface PersistedSlot {
  x: number;
  row: "back" | "front";
  definitionId: string;
  pieceId?: string;
}

export interface PersistedArmy {
  id: string;
  name: string;
  basedOn: ArmyKind;
  isDefault: boolean;
  fillEmptyFront: boolean;
  slots: PersistedSlot[];
  fights: number;
  wins: number;
  draws: number;
}

export interface PersistedUser {
  version: typeof USER_STORE_VERSION;
  style: PlayerStyle;
  guestStyle: PlayerStyle;
  armies: PersistedArmy[];
  pieces: PersistedPiece[];
  activeArmyId: string;
  guestArmyId: string;
}
