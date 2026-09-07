import type { PublicClock } from "./clock";
import type { PlayerStyle } from "./colors";

export enum NodeType {
  Normal = "Normal",
  /** In the graph; cannot land. Blocks leapers and ranged. */
  Impassable = "Impassable",
  Destroyed = "Destroyed",
  /** In the graph; cannot land. Leapers and ranged fly over. */
  Abyss = "Abyss",
}

export enum MovementType {
  Orthogonal = "Orthogonal",
  Diagonal = "Diagonal",
  Leaper = "Leaper",
  Adjacent = "Adjacent",
  Forward = "Forward",
  Backward = "Backward",
  Sideways = "Sideways",
  DiagonalCapture = "DiagonalCapture",
  DiagonalLeaper = "DiagonalLeaper",
}

export enum GamePhase {
  Setup = "Setup",
  Playing = "Playing",
  Ended = "Ended",
}

export enum GameEndReason {
  None = "None",
  Checkmate = "Checkmate",
  KingCaptured = "KingCaptured",
  Stalemate = "Stalemate",
  Resignation = "Resignation",
  Timeout = "Timeout",
}

export type Vec2 = { x: number; y: number };

export interface MovementPattern {
  type: MovementType;
  maxDistance: number;
  minDistance: number;
  canJump: boolean;
  captureOnly: boolean;
  moveOnly: boolean;
  firstMoveOnly: boolean;
  rangedCapture: boolean;
  leapX: number;
  leapY: number;
  /** Edge axes this pattern may use. Empty = infer from movement type. */
  axes: string[];
  /**
   * If > 0, this is a hurdle hop: must jump a piece and land this many squares beyond it.
   * The hurdle is never captured; only the landing square is a target.
   */
  hopBeyond: number;
  /**
   * If > 0, landing on a piece pushes it this many squares in the same direction.
   * -1 or 99 = as far as it can go. 0 = not a pusher.
   */
  pushDistance: number;
  /** If true, adjacent enemies are converted (owner flip) instead of captured. The converter stays put. */
  converts: boolean;
  /** Land on an occupied square: capture an enemy principal, share with a friendly principal. */
  canEnterOccupied: boolean;
  /** Land on an occupied square without capturing (friend or foe). */
  canShare: boolean;
  /**
   * Sliding rays continue through occupied squares (friend or foe).
   * Pits and mountains still stop the ray. Distinct from canJump: leapers fly over abyss.
   */
  passesUnits: boolean;
  /** If set, this pattern was granted by an item and can be removed on drop. */
  grantedBy?: string;
}

export function pattern(partial: Partial<MovementPattern> & { type: MovementType }): MovementPattern {
  return {
    maxDistance: -1,
    minDistance: 1,
    canJump: false,
    captureOnly: false,
    moveOnly: false,
    firstMoveOnly: false,
    rangedCapture: false,
    leapX: 0,
    leapY: 0,
    axes: [],
    hopBeyond: 0,
    pushDistance: 0,
    converts: false,
    canEnterOccupied: false,
    canShare: false,
    passesUnits: false,
    ...partial,
  };
}

export function leaper(leapX: number, leapY: number): MovementPattern {
  return pattern({ type: MovementType.Leaper, leapX, leapY, canJump: true });
}

export interface SkillState {
  id: string;
  isActive: boolean;
}

export interface UnitAction {
  id: string;
  label: string;
  /** Does not mark the piece as having acted and does not end the turn. */
  free?: boolean;
  /** Turns remaining. 0 = ready. -1 = locked until a reset trigger. Omitted = no cooldown. */
  cooldown?: number;
  /** Value applied when a reset trigger starts this cooldown counting. */
  maxCooldown?: number;
  /** After a successful use, set this action's cooldown (-1 = lock). */
  cooldownAfterUse?: number;
  /** After a successful use, set another action's cooldown. */
  resetActionId?: string;
  resetActionTo?: number | "max";
}

export interface UnitDefinition {
  id: string;
  displayName: string;
  isKing: boolean;
  canPromote: boolean;
  model: string;
  patterns: MovementPattern[];
  skills: SkillState[];
  actions: UnitAction[];
  /** Spawns and stays in shadow space (invisible to the opponent). */
  inShadow?: boolean;
}

export interface ItemState {
  id: string;
  kind: string;
  displayName: string;
  description: string;
  nodeId: number;
  dropOnDeath: boolean;
  color: string;
  shape: "sphere" | "cube" | "capsule" | "cylinder";
  model?: string;
  /** Power rank of this item kind (placement, loot, …). Not a variant of the same item. */
  tier: number;
  /** Remaining activations this match. Omitted = unlimited. */
  uses?: number;
  /** Activations at the start of a match. Omitted = unlimited. */
  maxUses?: number;
}

export interface UnitState {
  unitId: number;
  definitionId: string;
  ownerId: number;
  currentNodeId: number;
  patterns: MovementPattern[];
  skills: SkillState[];
  actions: UnitAction[];
  hasMovedThisTurn: boolean;
  hasEverMoved: boolean;
  isAlive: boolean;
  heldItem: ItemState | null;
  /** Board tile where this unit was placed at the start of the match. */
  homeNodeId?: number;
  /** Roster identity. Missing = default catalog piece for this match only. */
  pieceId?: string;
  rosterRow?: "back" | "front";
  rosterX?: number;
  /** Wololo: belongs to a new owner this match only. Not written back to a roster. */
  convertedThisMatch?: boolean;
  /** Secondary occupant of the tile. Does not block. Point targets ignore it; area effects do not. */
  inShadow?: boolean;
}

export interface NodeDef {
  id: number;
  x: number;
  y: number;
  type: NodeType;
  isLight: boolean;
  /** 0 | 1 | 2 on hex boards (3-color tessellation). Square boards use 0/1. */
  shade: number;
}

export interface NodeState {
  id: number;
  currentType: NodeType;
  isActive: boolean;
}

export enum EdgeType {
  Normal = "Normal",
  OneWay = "OneWay",
  Blocked = "Blocked",
  Hazardous = "Hazardous",
}

export interface EdgeDef {
  from: number;
  to: number;
  bidirectional: boolean;
  type: EdgeType;
  /** Geometric family: "h" | "v" | "d1" | "d2" on square boards; hex can use "a" | "b" | "c". */
  axis: string;
}

export const AXIS_H = "h";
export const AXIS_V = "v";
export const AXIS_D1 = "d1";
export const AXIS_D2 = "d2";
export const AXIS_A = "a";
export const AXIS_B = "b";
export const AXIS_C = "c";
export const ORTHO_AXES = [AXIS_H, AXIS_V];
export const DIAG_AXES = [AXIS_D1, AXIS_D2];
export const HEX_AXES = [AXIS_A, AXIS_B, AXIS_C];

export type BoardLayout = "square" | "hex-offset";
export type BoardKind =
  | "bizarre"
  | "classic"
  | "grand"
  | "capablanca"
  | "holes"
  | "hexa"
  | "lane"
  | "lane10"
  | "lane10-terrain"
  | "lane12-terrain"
  | "lane11";

export function isPassable(node: NodeState): boolean {
  return (
    node.isActive &&
    node.currentType !== NodeType.Impassable &&
    node.currentType !== NodeType.Destroyed &&
    node.currentType !== NodeType.Abyss
  );
}

export function isImpassable(node: NodeState): boolean {
  return node.currentType === NodeType.Impassable;
}

/** Walls block leapers and ranged flight. Abyss stays in the graph and does not. */
export function blocksFlight(node: NodeState): boolean {
  return node.currentType === NodeType.Impassable;
}

/** Algebraic square, 1-based rank: "a6", "c5". */
export type SquareName = string;

export type DecorChance = number | "always" | "itemAmount" | "obstacleAmount";

export interface ItemGroupSpec {
  /** One side of a 180° pair is enough; the mirror is added automatically. */
  tiles: SquareName[];
  chance: DecorChance;
}

export interface ObstacleSpec {
  /** Inclusive 1-based ranks. */
  ranks: [number, number];
  pitChance: DecorChance;
  mountainChance: DecorChance;
  /** Skip the two spawn rows at each end. Default true. */
  keepSpawnClear?: boolean;
}

/** Per-map item and obstacle recipe. */
export interface BoardDecorSpec {
  items?: ItemGroupSpec[];
  obstacles?: ObstacleSpec;
}

export interface BoardDefinition {
  id: string;
  displayName: string;
  width: number;
  height: number;
  layout: BoardLayout;
  nodes: NodeDef[];
  edges: EdgeDef[];
  spawn: { back: number[]; front: number[] }[];
  /** If set, board items spawn on these tiles instead of the usual center homes. */
  itemSpawnTiles?: number[];
  /** Optional item/obstacle recipe. Applied at match start with the lobby amounts. */
  decor?: BoardDecorSpec;
}

export interface MoveTargets {
  moveOnly: number[];
  captureOnly: number[];
  both: number[];
  rangedCapture: number[];
  push: number[];
  convert: number[];
}

export function emptyTargets(): MoveTargets {
  return { moveOnly: [], captureOnly: [], both: [], rangedCapture: [], push: [], convert: [] };
}

export function allTargets(t: MoveTargets): number[] {
  return [...t.moveOnly, ...t.captureOnly, ...t.both, ...t.rangedCapture, ...t.push, ...t.convert];
}

export interface PublicState {
  phase: GamePhase;
  turnNumber: number;
  currentPlayerId: number;
  winnerId: number | null;
  endReason: GameEndReason;
  boardId: string;
  width: number;
  height: number;
  nodes: NodeState[];
  edges: EdgeDef[];
  lights: boolean[];
  shades: number[];
  layout: BoardLayout;
  units: UnitState[];
  items: ItemState[];
  playerColors: [PlayerStyle, PlayerStyle];
  autoPickupItems?: boolean;
  clock?: PublicClock | null;
}
