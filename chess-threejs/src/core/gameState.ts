import { Board, createBoardByKind, definitionFromPublic } from "./board";
import { normalizeStyles, type PlayerStyle } from "./colors";
import { applyItemBreak, applyItemDrop, applyItemPick, cloneItem, defaultBoardItems, hasOnBreak, startingHeldItem } from "./items";
import { ArmyDef, BIZARRE_ARMY, PIECES, Slot } from "./pieces";
import { BoardKind, GameEndReason, GamePhase, ItemState, NodeType, PublicState, UnitState } from "./types";
import { isCheckmate, isStalemate, transmutationTargets, validateMove } from "./validator";

export type SpawnSlot = Slot & { pieceId?: string; heldItem?: ItemState | null };

export interface ArmySpec {
  slots: SpawnSlot[];
  fillEmptyFront: boolean;
}

function toArmySpec(army: Slot[] | ArmyDef | ArmySpec): ArmySpec {
  if (Array.isArray(army)) return { slots: army, fillEmptyFront: true };
  if ("fillEmptyFront" in army && Array.isArray((army as ArmySpec).slots)) {
    const spec = army as ArmySpec;
    return { slots: spec.slots, fillEmptyFront: spec.fillEmptyFront };
  }
  const def = army as ArmyDef;
  return { slots: def.slots, fillEmptyFront: def.fillEmptyFront };
}

export interface MoveExecution {
  success: boolean;
  error?: string;
  unitId: number;
  fromNode: number;
  toNode: number;
  isCapture: boolean;
  capturedUnitId: number | null;
  isRangedCapture: boolean;
  captureBlocked: boolean;
  forcefieldConsumedUnitId: number | null;
  droppedItemId: string | null;
  droppedItemNodeId: number | null;
  explosionOrigins: number[];
  blastNodes: number[];
  killedUnitIds: number[];
  destroyedItemIds: string[];
  gameEnded: boolean;
  winnerId: number | null;
  endReason: GameEndReason;
  turnNumber: number;
  currentPlayerId: number;
}

export interface PickupExecution {
  success: boolean;
  error?: string;
  unitId: number;
  itemId: string;
  nodeId: number;
  turnNumber: number;
  currentPlayerId: number;
}

export interface ActionExecution {
  success: boolean;
  error?: string;
  unitId: number;
  actionId: string;
  originNode: number;
  destNode?: number;
  explosionOrigins: number[];
  blastNodes: number[];
  killedUnitIds: number[];
  destroyedItemIds: string[];
  droppedItems: { itemId: string; nodeId: number }[];
  gameEnded: boolean;
  winnerId: number | null;
  endReason: GameEndReason;
  turnNumber: number;
  currentPlayerId: number;
}

export function explosionNodes(board: Board, origin: number): number[] {
  const next: number[] = [];
  for (const axis of board.orthoAxes) {
    for (const id of board.neighborsOnAxis(origin, axis)) {
      if (!next.includes(id)) next.push(id);
    }
  }
  return [origin, ...next];
}

export function previewNodesForAction(
  actionId: string,
  board: Board,
  originNode: number,
  unit?: UnitState,
  all: UnitState[] = []
): number[] {
  if (actionId === "PowderBarrel") return explosionNodes(board, originNode);
  if (actionId === "EscapeScroll") {
    if (unit?.heldItem?.kind === "EscapeScroll" && unit.heldItem.usedThisMatch) return [];
    if (unit?.homeNodeId != null && unit.homeNodeId !== originNode) return [unit.homeNodeId];
    return [];
  }
  if (actionId === "TransmuteScroll" && unit) {
    if (unit.heldItem?.kind === "TransmuteScroll" && unit.heldItem.usedThisMatch) return [];
    return transmutationTargets(board, unit, all);
  }
  return [];
}

export function actionNotice(action: ActionExecution): string | undefined {
  if (!action.success) return action.error;
  if (action.gameEnded) return action.endReason;
  if (action.actionId === "EscapeScroll") return "Returned to the starting square";
  if (action.actionId === "TransmuteScroll") return "The square turned to stone";
  if (action.actionId === "PowderBarrel") return "Explosion";
  return undefined;
}

interface BlastSession {
  origins: number[];
  queued: number[];
  nodes: Set<number>;
  killedUnitIds: number[];
  destroyedItemIds: string[];
  broken: Set<string>;
}

export class Game {
  board: Board;
  units: UnitState[] = [];
  items: ItemState[] = [];
  playerColors: [PlayerStyle, PlayerStyle] = normalizeStyles();
  phase = GamePhase.Setup;
  turnNumber = 1;
  currentPlayerId = 0;
  winnerId: number | null = null;
  endReason = GameEndReason.None;
  private nextUnitId = 0;

  constructor(
    army: Slot[] | ArmyDef | ArmySpec = BIZARRE_ARMY,
    colors?: unknown,
    boardKind: BoardKind = "lane11",
    opponentArmy?: Slot[] | ArmyDef | ArmySpec
  ) {
    this.board = new Board(createBoardByKind(boardKind));
    this.playerColors = normalizeStyles(colors);
    const spec = toArmySpec(army);
    const opponent = opponentArmy
      ? toArmySpec(opponentArmy)
      : { slots: spec.slots.map(({ piece, x, row }) => ({ piece, x, row })), fillEmptyFront: spec.fillEmptyFront };
    this.placeArmy(spec.slots, 0, spec.fillEmptyFront);
    this.placeArmy(opponent.slots, 1, opponent.fillEmptyFront);
    this.items = defaultBoardItems(
      (id) => this.board.passable(id),
      this.board.def.width,
      this.board.def.height,
      this.board.def.itemSpawnTiles
    );
    this.phase = GamePhase.Playing;
  }

  replaceArmy(playerId: number, army: Slot[] | ArmyDef | ArmySpec): void {
    const spec = toArmySpec(army);
    this.units = this.units.filter((unit) => unit.ownerId !== playerId);
    this.placeArmy(spec.slots, playerId, spec.fillEmptyFront);
  }

  static fromPublic(state: PublicState): Game {
    const game = Object.create(Game.prototype) as Game;
    game.board = new Board(definitionFromPublic(state), state.nodes);
    game.units = structuredClone(state.units);
    for (const unit of game.units) unit.actions ??= [];
    game.items = structuredClone(state.items);
    game.playerColors = normalizeStyles(state.playerColors);
    game.phase = state.phase;
    game.turnNumber = state.turnNumber;
    game.currentPlayerId = state.currentPlayerId;
    game.winnerId = state.winnerId;
    game.endReason = state.endReason;
    game.nextUnitId = 0;
    return game;
  }

  toPublic(): PublicState {
    return {
      phase: this.phase,
      turnNumber: this.turnNumber,
      currentPlayerId: this.currentPlayerId,
      winnerId: this.winnerId,
      endReason: this.endReason,
      boardId: this.board.def.id,
      width: this.board.def.width,
      height: this.board.def.height,
      nodes: this.board.nodes.map((n) => ({ ...n })),
      edges: this.board.def.edges.map((e) => ({ ...e })),
      lights: this.board.def.nodes.map((n) => n.isLight),
      shades: this.board.def.nodes.map((n) => n.shade),
      layout: this.board.layout,
      units: structuredClone(this.units),
      items: structuredClone(this.items),
      playerColors: [{ ...this.playerColors[0] }, { ...this.playerColors[1] }],
    };
  }

  private placeArmy(slots: SpawnSlot[], playerId: number, fillEmptyFront = true): void {
    const zone = this.board.def.spawn[playerId];
    const width = zone.back.length;
    const armyWidth = slots.reduce((max, slot) => Math.max(max, slot.x + 1), 0);
    const offset = Math.max(0, Math.floor((width - armyWidth) / 2));
    const occupied = new Set<number>();
    for (const slot of slots) {
      const def = PIECES[slot.piece];
      if (!def) continue;
      const row = slot.row === "back" ? zone.back : zone.front;
      const index = slot.x + offset;
      const nodeId = row[index];
      if (nodeId === undefined || !this.board.passable(nodeId)) continue;
      occupied.add(nodeId);
      const unit: UnitState = {
        unitId: this.nextUnitId++,
        definitionId: def.id,
        ownerId: playerId,
        currentNodeId: nodeId,
        patterns: structuredClone(def.patterns),
        skills: structuredClone(def.skills),
        actions: structuredClone(def.actions ?? []),
        hasMovedThisTurn: false,
        hasEverMoved: false,
        isAlive: true,
        heldItem: slot.heldItem ? cloneItem(slot.heldItem) : startingHeldItem(def.id),
        homeNodeId: nodeId,
        pieceId: slot.pieceId,
        rosterRow: slot.row,
        rosterX: slot.x,
      };
      if (unit.heldItem) {
        unit.heldItem.usedThisMatch = false;
        applyItemPick(unit, unit.heldItem);
      }
      this.units.push(unit);
    }
    if (!fillEmptyFront) return;
    const front = zone.front;
    for (let i = 0; i < front.length; i++) {
      const nodeId = front[i];
      if (occupied.has(nodeId) || !this.board.passable(nodeId)) continue;
      const pawn = PIECES.Pawn;
      this.units.push({
        unitId: this.nextUnitId++,
        definitionId: pawn.id,
        ownerId: playerId,
        currentNodeId: nodeId,
        patterns: structuredClone(pawn.patterns),
        skills: structuredClone(pawn.skills),
        actions: structuredClone(pawn.actions ?? []),
        hasMovedThisTurn: false,
        hasEverMoved: false,
        isAlive: true,
        heldItem: null,
        homeNodeId: nodeId,
      });
    }
  }

  tryMove(unitId: number, targetNode: number, playerId: number): MoveExecution {
    if (playerId !== this.currentPlayerId) return this.failMove("Not your turn");
    if (this.phase !== GamePhase.Playing) return this.failMove("Game is not playing");
    const unit = this.units.find((u) => u.unitId === unitId);
    if (!unit) return this.failMove("Unit not found");

    const validation = validateMove(this.board, unit, targetNode, this.units, playerId);
    if (!validation.isValid) return this.failMove(validation.error ?? "Invalid move");

    const fromNode = unit.currentNodeId;
    let captureBlocked = false;
    let forcefieldConsumed: number | null = null;
    let droppedItemId: string | null = null;
    let droppedItemNodeId: number | null = null;
    let explosionOrigins: number[] = [];
    let blastNodes: number[] = [];
    let killedUnitIds: number[] = [];
    let destroyedItemIds: string[] = [];

    let pendingShatter: { item: ItemState; nodeId: number } | undefined;

    if (validation.isCapture && validation.capturedUnitId != null) {
      const captured = this.units.find((u) => u.unitId === validation.capturedUnitId);
      const field = captured?.skills.find((s) => s.id === "Forcefield" && s.isActive);
      if (captured && field) {
        field.isActive = false;
        captureBlocked = true;
        forcefieldConsumed = captured.unitId;
        unit.hasMovedThisTurn = true;
      } else if (captured) {
        const loot = this.killUnit(captured);
        if (loot.drop) {
          droppedItemId = loot.drop.itemId;
          droppedItemNodeId = loot.drop.nodeId;
        }
        pendingShatter = loot.shatter;
      }
    }

    if (!captureBlocked) {
      if (!validation.isRangedCapture) {
        unit.currentNodeId = targetNode;
        unit.hasEverMoved = true;
        unit.hasMovedThisTurn = true;
      } else {
        unit.hasMovedThisTurn = true;
      }
    }

    if (pendingShatter) {
      const blast = this.breakItem(pendingShatter.item, pendingShatter.nodeId);
      explosionOrigins = blast.origins;
      blastNodes = blast.nodes;
      killedUnitIds = blast.killedUnitIds;
      destroyedItemIds = blast.destroyedItemIds;
    }

    if (!captureBlocked) this.checkWin();

    const gameEnded = this.endReason !== GameEndReason.None;
    if (!gameEnded) this.endTurn();

    return {
      success: true,
      unitId,
      fromNode,
      toNode: targetNode,
      isCapture: validation.isCapture && !captureBlocked,
      capturedUnitId: captureBlocked ? null : validation.capturedUnitId,
      isRangedCapture: validation.isRangedCapture,
      captureBlocked,
      forcefieldConsumedUnitId: forcefieldConsumed,
      droppedItemId,
      droppedItemNodeId,
      explosionOrigins,
      blastNodes,
      killedUnitIds,
      destroyedItemIds,
      gameEnded,
      winnerId: this.winnerId,
      endReason: this.endReason,
      turnNumber: this.turnNumber,
      currentPlayerId: this.currentPlayerId,
    };
  }

  tryPickup(unitId: number, playerId: number): PickupExecution {
    if (playerId !== this.currentPlayerId) return this.failPickup("Not your turn");
    if (this.phase !== GamePhase.Playing) return this.failPickup("Game is not playing");
    const unit = this.units.find((u) => u.unitId === unitId);
    if (!unit) return this.failPickup("Unit not found");
    if (unit.ownerId !== playerId) return this.failPickup("Not your unit");
    if (unit.hasMovedThisTurn) return this.failPickup("Unit has already acted this turn");
    if (unit.heldItem) return this.failPickup("Unit already holds an item");
    const item = this.items.find((i) => i.nodeId === unit.currentNodeId);
    if (!item) return this.failPickup("No item at this position");

    unit.heldItem = { ...item, nodeId: -1 };
    applyItemPick(unit, item);
    unit.hasMovedThisTurn = true;
    this.items = this.items.filter((i) => i.id !== item.id);
    this.endTurn();

    return {
      success: true,
      unitId,
      itemId: item.id,
      nodeId: unit.currentNodeId,
      turnNumber: this.turnNumber,
      currentPlayerId: this.currentPlayerId,
    };
  }

  tryAction(unitId: number, actionId: string, playerId: number, targetNode?: number): ActionExecution {
    if (playerId !== this.currentPlayerId) return this.failAction("Not your turn");
    if (this.phase !== GamePhase.Playing) return this.failAction("Game is not playing");
    const unit = this.units.find((u) => u.unitId === unitId);
    if (!unit) return this.failAction("Unit not found");
    if (unit.ownerId !== playerId) return this.failAction("Not your unit");
    if (!unit.isAlive) return this.failAction("Unit is dead");
    if (unit.hasMovedThisTurn) return this.failAction("Unit has already acted this turn");
    if (!unit.actions.some((a) => a.id === actionId)) return this.failAction("Unknown action");
    if (actionId === "EscapeScroll") return this.useEscapeScroll(unit);
    if (actionId === "TransmuteScroll") return this.useTransmuteScroll(unit, targetNode);
    if (actionId !== "PowderBarrel") return this.failAction("Unknown action");
    const item = unit.heldItem;
    if (item?.kind !== "PowderBarrel" && item?.kind !== "Bomb") return this.failAction("No powder to ignite");

    applyItemDrop(unit, item);
    unit.heldItem = null;
    unit.hasMovedThisTurn = true;

    const blast = this.breakItem(item, unit.currentNodeId);

    this.checkWin();
    const gameEnded = this.endReason !== GameEndReason.None;
    if (!gameEnded) this.endTurn();

    return {
      success: true,
      unitId,
      actionId,
      originNode: blast.origins[0] ?? unit.currentNodeId,
      explosionOrigins: blast.origins,
      blastNodes: blast.nodes,
      killedUnitIds: blast.killedUnitIds,
      destroyedItemIds: blast.destroyedItemIds,
      droppedItems: [],
      gameEnded,
      winnerId: this.winnerId,
      endReason: this.endReason,
      turnNumber: this.turnNumber,
      currentPlayerId: this.currentPlayerId,
    };
  }

  private useTransmuteScroll(unit: UnitState, targetNode?: number): ActionExecution {
    const item = unit.heldItem;
    if (item?.kind !== "TransmuteScroll") return this.failAction("No transmutation scroll");
    if (item.usedThisMatch) return this.failAction("Already used this match");
    if (targetNode == null) return this.failAction("Choose an empty square this piece can attack");
    if (!transmutationTargets(this.board, unit, this.units).includes(targetNode)) {
      return this.failAction("That square is not an empty attack target");
    }
    if (!this.board.changeType(targetNode, NodeType.Impassable)) {
      return this.failAction("Cannot transmute that square");
    }

    item.usedThisMatch = true;
    unit.hasMovedThisTurn = true;
    unit.hasEverMoved = true;

    this.checkWin();
    const gameEnded = this.endReason !== GameEndReason.None;
    if (!gameEnded) this.endTurn();

    return {
      success: true,
      unitId: unit.unitId,
      actionId: "TransmuteScroll",
      originNode: unit.currentNodeId,
      destNode: targetNode,
      explosionOrigins: [],
      blastNodes: [],
      killedUnitIds: [],
      destroyedItemIds: [],
      droppedItems: [],
      gameEnded,
      winnerId: this.winnerId,
      endReason: this.endReason,
      turnNumber: this.turnNumber,
      currentPlayerId: this.currentPlayerId,
    };
  }

  private useEscapeScroll(unit: UnitState): ActionExecution {
    const item = unit.heldItem;
    if (item?.kind !== "EscapeScroll") return this.failAction("No escape scroll");
    if (item.usedThisMatch) return this.failAction("Already used this match");
    const home = unit.homeNodeId;
    if (home == null) return this.failAction("No starting square recorded");
    if (home === unit.currentNodeId) return this.failAction("Already on the starting square");
    if (!this.board.passable(home)) return this.failAction("The starting square is gone");
    const blocker = this.units.find((other) => other.isAlive && other.currentNodeId === home);
    if (blocker) return this.failAction("The starting square is occupied");

    const from = unit.currentNodeId;
    item.usedThisMatch = true;
    unit.currentNodeId = home;
    unit.hasMovedThisTurn = true;
    unit.hasEverMoved = true;

    this.checkWin();
    const gameEnded = this.endReason !== GameEndReason.None;
    if (!gameEnded) this.endTurn();

    return {
      success: true,
      unitId: unit.unitId,
      actionId: "EscapeScroll",
      originNode: from,
      destNode: home,
      explosionOrigins: [],
      blastNodes: [],
      killedUnitIds: [],
      destroyedItemIds: [],
      droppedItems: [],
      gameEnded,
      winnerId: this.winnerId,
      endReason: this.endReason,
      turnNumber: this.turnNumber,
      currentPlayerId: this.currentPlayerId,
    };
  }

  resign(playerId: number): void {
    if (this.phase !== GamePhase.Playing) return;
    this.winnerId = playerId === 0 ? 1 : 0;
    this.endReason = GameEndReason.Resignation;
    this.phase = GamePhase.Ended;
  }

  private breakItem(item: ItemState, nodeId: number): {
    origins: number[];
    nodes: number[];
    killedUnitIds: number[];
    destroyedItemIds: string[];
  } {
    const session = {
      origins: [] as number[],
      queued: [] as number[],
      nodes: new Set<number>(),
      killedUnitIds: [] as number[],
      destroyedItemIds: [] as string[],
      broken: new Set<string>(),
    };
    this.triggerBreak(item, nodeId, session);
    this.flushDetonations(session);
    return {
      origins: session.origins,
      nodes: [...session.nodes],
      killedUnitIds: session.killedUnitIds,
      destroyedItemIds: session.destroyedItemIds,
    };
  }

  private triggerBreak(item: ItemState, nodeId: number, session: BlastSession): void {
    if (session.broken.has(item.id)) return;
    session.broken.add(item.id);
    session.destroyedItemIds.push(item.id);
    applyItemBreak(item, {
      nodeId,
      explode: (origin) => {
        if (!session.origins.includes(origin) && !session.queued.includes(origin)) {
          session.queued.push(origin);
        }
      },
    });
  }

  private flushDetonations(session: BlastSession): void {
    while (session.queued.length) {
      const origin = session.queued.shift()!;
      if (session.origins.includes(origin)) continue;
      session.origins.push(origin);
      const wave = explosionNodes(this.board, origin);
      for (const nodeId of wave) {
        session.nodes.add(nodeId);
        const node = this.board.getNode(nodeId);
        if (node?.currentType === NodeType.Impassable) {
          this.board.changeType(nodeId, NodeType.Normal);
        }
      }
      const waveSet = new Set(wave);
      for (const victim of this.units) {
        if (!victim.isAlive || !waveSet.has(victim.currentNodeId)) continue;
        this.killUnit(victim, session);
        session.killedUnitIds.push(victim.unitId);
      }
      const taken = this.takeItemsOn(waveSet);
      for (const ground of taken) this.triggerBreak(ground, ground.nodeId, session);
    }
  }

  private takeItemsOn(nodes: Set<number>): ItemState[] {
    const taken: ItemState[] = [];
    this.items = this.items.filter((item) => {
      if (!nodes.has(item.nodeId)) return true;
      taken.push(item);
      return false;
    });
    return taken;
  }

  private killUnit(
    unit: UnitState,
    session?: BlastSession
  ): { drop?: { itemId: string; nodeId: number }; shatter?: { item: ItemState; nodeId: number } } {
    const item = unit.heldItem;
    unit.isAlive = false;
    unit.heldItem = null;
    if (!item) return {};
    applyItemDrop(unit, item);
    if (hasOnBreak(item.kind)) {
      if (session) {
        this.triggerBreak(item, unit.currentNodeId, session);
        return {};
      }
      return { shatter: { item, nodeId: unit.currentNodeId } };
    }
    if (!item.dropOnDeath) return {};
    const dropped = cloneItem(item);
    dropped.nodeId = unit.currentNodeId;
    this.items.push(dropped);
    return { drop: { itemId: dropped.id, nodeId: dropped.nodeId } };
  }

  private checkWin(): void {
    for (const playerId of [0, 1]) {
      const king = this.units.find(
        (u) => u.isAlive && u.ownerId === playerId && u.definitionId === "King"
      );
      if (!king) {
        this.winnerId = playerId === 0 ? 1 : 0;
        this.endReason = GameEndReason.KingCaptured;
        this.phase = GamePhase.Ended;
        return;
      }
      if (isCheckmate(this.board, playerId, this.units)) {
        this.winnerId = playerId === 0 ? 1 : 0;
        this.endReason = GameEndReason.Checkmate;
        this.phase = GamePhase.Ended;
        return;
      }
    }
    if (isStalemate(this.board, this.currentPlayerId, this.units)) {
      this.winnerId = null;
      this.endReason = GameEndReason.Stalemate;
      this.phase = GamePhase.Ended;
    }
  }

  private endTurn(): void {
    this.currentPlayerId = (this.currentPlayerId + 1) % 2;
    if (this.currentPlayerId === 0) this.turnNumber++;
    for (const unit of this.units.filter((u) => u.ownerId === this.currentPlayerId)) {
      unit.hasMovedThisTurn = false;
    }
  }

  private failMove(error: string): MoveExecution {
    return {
      success: false,
      error,
      unitId: -1,
      fromNode: -1,
      toNode: -1,
      isCapture: false,
      capturedUnitId: null,
      isRangedCapture: false,
      captureBlocked: false,
      forcefieldConsumedUnitId: null,
      droppedItemId: null,
      droppedItemNodeId: null,
      explosionOrigins: [],
      blastNodes: [],
      killedUnitIds: [],
      destroyedItemIds: [],
      gameEnded: this.phase === GamePhase.Ended,
      winnerId: this.winnerId,
      endReason: this.endReason,
      turnNumber: this.turnNumber,
      currentPlayerId: this.currentPlayerId,
    };
  }

  private failPickup(error: string): PickupExecution {
    return { success: false, error, unitId: -1, itemId: "", nodeId: -1, turnNumber: this.turnNumber, currentPlayerId: this.currentPlayerId };
  }

  private failAction(error: string): ActionExecution {
    return {
      success: false,
      error,
      unitId: -1,
      actionId: "",
      originNode: -1,
      explosionOrigins: [],
      blastNodes: [],
      killedUnitIds: [],
      destroyedItemIds: [],
      droppedItems: [],
      gameEnded: this.phase === GamePhase.Ended,
      winnerId: this.winnerId,
      endReason: this.endReason,
      turnNumber: this.turnNumber,
      currentPlayerId: this.currentPlayerId,
    };
  }
}
