import type { PlayerStyle } from "../core/colors";
import type { DraftPickModeId, DraftUniq } from "../core/draft";
import type { ArmyFormat, MatchMode } from "../core/format";
import type { ArmyKind } from "../core/pieces";
import type { BoardKind, ItemState } from "../core/types";

export const USER_STORE_KEY = "bizarre-chess.user.v1";
export const USER_STORE_VERSION = 4 as const;

export interface UserSettings {
  autoPickupItems: boolean;
  matchMode: MatchMode;
  armyFormat: ArmyFormat;
  board: BoardKind;
  draftBanCount: number;
  draftPickMode: DraftPickModeId;
  draftUniqueness: DraftUniq;
  itemAmount: number;
  obstacleAmount: number;
  symmetricObstacles: boolean;
}

export const DEFAULT_SETTINGS: UserSettings = {
  autoPickupItems: false,
  matchMode: "normal",
  armyFormat: "mini",
  board: "lane11",
  draftBanCount: 1,
  draftPickMode: "pieces-first",
  draftUniqueness: "free",
  itemAmount: 5,
  obstacleAmount: 5,
  symmetricObstacles: true,
};

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
  settings: UserSettings;
}
