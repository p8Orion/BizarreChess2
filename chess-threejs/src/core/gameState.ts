import { afterTurn, createClock, flaggedPlayer, startClock, stopClock, type PublicClock, type TimeControlId } from "./clock";
import { Board, createBoardByKind, definitionFromPublic } from "./board";
import { applyBoardDecor, normalizeDecorAmounts } from "./boardDecor";
import { normalizeStyles, type PlayerStyle } from "./colors";
import { applyCooldownTriggers, actionReady, tickUnitCooldowns } from "./cooldown";
import { applyItemBreak, applyItemDrop, applyItemPick, cloneItem, defaultBoardItems, hasOnBreak, itemIsSpent, placeBoardItems, refillItemUses, spendItemUse, startingHeldItem } from "./items";
import { ArmyDef, BIZARRE_ARMY, PIECES, Slot } from "./pieces";
import { filterUnitsForViewer, hasOwnShadow, principalOn, unitVisibleTo } from "./occupancy";
import { BoardKind, GameEndReason, GamePhase, ItemState, NodeType, PublicState, UnitAction, UnitState } from "./types";
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
  pushedUnitId: number | null;
  pushToNode: number | null;
  pushFalls: boolean;
  convertedUnitId: number | null;
  droppedItemId: string | null;
  droppedItemNodeId: number | null;
  pickedItemId: string | null;
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
  swappedUnitId?: number;
  /** Action did not mark the piece or end the turn. */
  free?: boolean;
  captureBlocked?: boolean;
  forcefieldConsumedUnitId?: number | null;
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

export interface DropExecution {
  success: boolean;
  error?: string;
  unitId: number;
  itemId: string;
  nodeId: number;
  turnNumber: number;
  currentPlayerId: number;
}

export interface GameSettings {
  autoPickupItems?: boolean;
  itemAmount?: number;
  obstacleAmount?: number;
  symmetricObstacles?: boolean;
  seed?: number;
  timeControl?: TimeControlId;
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

export function swapTargets(board: Board, originNode: number, all: UnitState[]): number[] {
  return board.neighbors(originNode).filter((id) => !!principalOn(all, id));
}

export function previewNodesForAction(
  actionId: string,
  board: Board,
  originNode: number,
  unit?: UnitState,
  all: UnitState[] = []
): number[] {
  if (actionId === "Bomb") return explosionNodes(board, originNode);
  if (actionId === "EscapeScroll") {
    if (unit?.heldItem?.kind === "EscapeScroll" && itemIsSpent(unit.heldItem)) return [];
    if (unit?.homeNodeId != null && unit.homeNodeId !== originNode) return [unit.homeNodeId];
    return [];
  }
  if (actionId === "TransmuteScroll" && unit) {
    if (unit.heldItem?.kind === "TransmuteScroll" && itemIsSpent(unit.heldItem)) return [];
    return transmutationTargets(board, unit, all);
  }
  if (actionId === "SwapCharm") {
    if (unit?.heldItem?.kind === "SwapCharm" && itemIsSpent(unit.heldItem)) return [];
    return swapTargets(board, originNode, all);
  }
  if (actionId === "EnterShadow") {
    if (!unit || unit.inShadow) return [];
    return [originNode];
  }
  if (actionId === "Stab") {
    if (!unit?.inShadow) return [];
    const victim = principalOn(all, originNode);
    if (!victim || victim.ownerId === unit.ownerId) return [];
    return [originNode];
  }
  return [];
}

export function actionNotice(action: ActionExecution): string | undefined {
  if (!action.success) return action.error;
  if (action.gameEnded) return `end.${action.endReason}`;
  if (action.actionId === "EscapeScroll") return "notice.escapeHome";
  if (action.actionId === "TransmuteScroll") return "notice.transmute";
  if (action.actionId === "SwapCharm") return "notice.swap";
  if (action.actionId === "EnterShadow") return "notice.enterShadow";
  if (action.actionId === "Stab") {
    if (action.captureBlocked) return "notice.stabBlocked";
    return "notice.stabbed";
  }
  if (action.actionId === "Bomb") return "notice.explosion";
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
  autoPickupItems = false;
  clock: PublicClock | null = null;
  private nextUnitId = 0;

  constructor(
    army: Slot[] | ArmyDef | ArmySpec = BIZARRE_ARMY,
    colors?: unknown,
    boardKind: BoardKind = "lane11",
    opponentArmy?: Slot[] | ArmyDef | ArmySpec,
    settings?: GameSettings
  ) {
    const scatter = normalizeDecorAmounts(settings);
    const generated = applyBoardDecor(createBoardByKind(boardKind, settings?.seed), scatter, settings?.seed);
    this.board = new Board(generated.def);
    this.playerColors = normalizeStyles(colors);
    this.autoPickupItems = settings?.autoPickupItems !== false;
    const spec = toArmySpec(army);
    const opponent = opponentArmy
      ? toArmySpec(opponentArmy)
      : { slots: spec.slots.map(({ piece, x, row }) => ({ piece, x, row })), fillEmptyFront: spec.fillEmptyFront };
    this.placeArmy(spec.slots, 0, spec.fillEmptyFront);
    this.placeArmy(opponent.slots, 1, opponent.fillEmptyFront);
    this.items =
      generated.itemTiles != null
        ? placeBoardItems(generated.itemTiles, (id) => this.board.passable(id))
        : defaultBoardItems(
            (id) => this.board.passable(id),
            this.board.def.width,
            this.board.def.height,
            this.board.def.itemSpawnTiles
          );
    this.phase = GamePhase.Playing;
    this.clock = createClock(settings?.timeControl);
  }

  startClock(now = Date.now()): void {
    if (!this.clock || this.phase !== GamePhase.Playing) return;
    startClock(this.clock, this.currentPlayerId, now);
  }

  applyFlag(now = Date.now()): boolean {
    if (!this.clock || this.phase !== GamePhase.Playing) return false;
    const loser = flaggedPlayer(this.clock, now);
    if (loser == null) return false;
    this.endGame(loser === 0 ? 1 : 0, GameEndReason.Timeout);
    return true;
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
    game.autoPickupItems = state.autoPickupItems === true;
    game.clock = state.clock ? structuredClone(state.clock) : null;
    game.nextUnitId = game.units.reduce((max, unit) => Math.max(max, unit.unitId + 1), 0);
    return game;
  }

  toPublic(viewerId?: number): PublicState {
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
      units: structuredClone(filterUnitsForViewer(this.units, viewerId)),
      items: structuredClone(this.items),
      playerColors: [{ ...this.playerColors[0] }, { ...this.playerColors[1] }],
      autoPickupItems: this.autoPickupItems,
      clock: this.clock ? structuredClone(this.clock) : null,
    };
  }

  sanitizeMove(move: MoveExecution, viewerId?: number): MoveExecution {
    const vis = (id: number | null | undefined) =>
      id != null && id >= 0 && this.units.some((u) => u.unitId === id && unitVisibleTo(u, viewerId));
    const actorKnown = vis(move.unitId);
    return {
      ...move,
      unitId: actorKnown ? move.unitId : -1,
      fromNode: actorKnown ? move.fromNode : move.toNode,
      capturedUnitId: vis(move.capturedUnitId) ? move.capturedUnitId : null,
      convertedUnitId: vis(move.convertedUnitId) ? move.convertedUnitId : null,
      pushedUnitId: vis(move.pushedUnitId) ? move.pushedUnitId : null,
      forcefieldConsumedUnitId: vis(move.forcefieldConsumedUnitId) ? move.forcefieldConsumedUnitId : null,
      killedUnitIds: move.killedUnitIds.filter((id) => vis(id)),
    };
  }

  sanitizeAction(action: ActionExecution, viewerId?: number): ActionExecution | undefined {
    const actor = this.units.find((u) => u.unitId === action.unitId);
    const actorKnown = actor ? unitVisibleTo(actor, viewerId) : viewerId == null;
    const vis = (id: number | null | undefined) =>
      id != null && id >= 0 && this.units.some((u) => u.unitId === id && unitVisibleTo(u, viewerId));
    if (!actorKnown && !action.killedUnitIds.some((id) => vis(id)) && !vis(action.forcefieldConsumedUnitId) && !action.gameEnded) {
      return undefined;
    }
    return {
      ...action,
      unitId: actorKnown ? action.unitId : -1,
      swappedUnitId: vis(action.swappedUnitId) ? action.swappedUnitId : undefined,
      forcefieldConsumedUnitId: vis(action.forcefieldConsumedUnitId) ? action.forcefieldConsumedUnitId : null,
      killedUnitIds: action.killedUnitIds.filter((id) => vis(id)),
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
        inShadow: !!def.inShadow,
      };
      if (unit.heldItem) {
        refillItemUses(unit.heldItem);
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
    const blocked = this.turnError(playerId);
    if (blocked) return this.failMove(blocked);
    const unit = this.units.find((u) => u.unitId === unitId);
    if (!unit) return this.failMove("err.unitNotFound");

    const validation = validateMove(this.board, unit, targetNode, this.units, playerId);
    if (!validation.isValid) return this.failMove(validation.error ?? "err.invalidMove");

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

    if (validation.isPush && validation.pushedUnitId != null) {
      const victim = this.units.find((u) => u.unitId === validation.pushedUnitId);
      if (!victim) return this.failMove("err.nothingToPush");
      const dest = validation.pushToNode ?? victim.currentNodeId;
      if (validation.pushFalls) {
        const loot = this.killUnit(victim);
        if (loot.drop) {
          droppedItemId = loot.drop.itemId;
          droppedItemNodeId = loot.drop.nodeId;
        }
        pendingShatter = loot.shatter;
        killedUnitIds.push(victim.unitId);
      } else if (dest !== victim.currentNodeId) {
        victim.currentNodeId = dest;
      }
    } else if (validation.isConvert && validation.convertedUnitId != null) {
      const target = this.units.find((u) => u.unitId === validation.convertedUnitId);
      const field = target?.skills.find((s) => s.id === "Forcefield" && s.isActive);
      if (target && field) {
        field.isActive = false;
        captureBlocked = true;
        forcefieldConsumed = target.unitId;
        unit.hasMovedThisTurn = true;
      } else if (target) {
        target.ownerId = unit.ownerId;
        target.homeNodeId = target.currentNodeId;
        target.convertedThisMatch = true;
        target.pieceId = undefined;
        target.rosterRow = undefined;
        target.rosterX = undefined;
      }
    } else if (validation.isCapture && validation.capturedUnitId != null) {
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
      if (validation.isConvert || validation.isRangedCapture) {
        unit.hasMovedThisTurn = true;
        unit.hasEverMoved = true;
      } else {
        unit.currentNodeId = targetNode;
        unit.hasEverMoved = true;
        unit.hasMovedThisTurn = true;
      }
    }

    if (pendingShatter) {
      const blast = this.breakItem(pendingShatter.item, pendingShatter.nodeId);
      explosionOrigins = blast.origins;
      blastNodes = blast.nodes;
      killedUnitIds = [...new Set([...killedUnitIds, ...blast.killedUnitIds])];
      destroyedItemIds = blast.destroyedItemIds;
    }

    if (!captureBlocked) this.checkWin();

    const pickedItemId = !captureBlocked && unit.isAlive ? this.maybeAutoPickup(unit) : null;

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
      pushedUnitId: validation.pushedUnitId,
      pushToNode: validation.pushToNode,
      pushFalls: validation.pushFalls,
      convertedUnitId: captureBlocked ? null : validation.convertedUnitId,
      droppedItemId,
      droppedItemNodeId,
      pickedItemId,
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
    const blocked = this.turnError(playerId);
    if (blocked) return this.failPickup(blocked);
    const unit = this.units.find((u) => u.unitId === unitId);
    if (!unit) return this.failPickup("err.unitNotFound");
    if (unit.ownerId !== playerId) return this.failPickup("err.notYourUnit");
    if (unit.inShadow) return this.failPickup("err.hiddenNoPickup");
    if (unit.hasMovedThisTurn) return this.failPickup("err.alreadyActed");
    if (unit.heldItem) return this.failPickup("err.alreadyHoldsItem");
    const item = this.items.find((i) => i.nodeId === unit.currentNodeId);
    if (!item) return this.failPickup("err.noItemHere");

    this.giveItem(unit, item);

    unit.hasMovedThisTurn = true;
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

  tryDrop(unitId: number, playerId: number): DropExecution {
    const blocked = this.turnError(playerId);
    if (blocked) return this.failDrop(blocked);
    const unit = this.units.find((u) => u.unitId === unitId);
    if (!unit) return this.failDrop("err.unitNotFound");
    if (unit.ownerId !== playerId) return this.failDrop("err.notYourUnit");
    if (!unit.isAlive) return this.failDrop("err.unitDead");
    if (unit.inShadow) return this.failDrop("err.hiddenNoDrop");
    const item = unit.heldItem;
    if (!item) return this.failDrop("err.notHolding");
    if (this.items.some((ground) => ground.nodeId === unit.currentNodeId)) {
      return this.failDrop("err.itemAlreadyHere");
    }

    applyItemDrop(unit, item);
    unit.heldItem = null;
    item.nodeId = unit.currentNodeId;
    this.items.push(item);

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
    const blocked = this.turnError(playerId);
    if (blocked) return this.failAction(blocked);
    const unit = this.units.find((u) => u.unitId === unitId);
    if (!unit) return this.failAction("err.unitNotFound");
    if (unit.ownerId !== playerId) return this.failAction("err.notYourUnit");
    if (!unit.isAlive) return this.failAction("err.unitDead");
    const action = unit.actions.find((a) => a.id === actionId);
    if (!action) return this.failAction("err.unknownAction");
    if (!action.free && unit.hasMovedThisTurn) return this.failAction("err.alreadyActed");
    if (!actionReady(action)) {
      return this.failAction(action.cooldown === -1 ? "err.abilityLocked" : "err.abilityCooldown");
    }
    if (actionId === "EnterShadow") return this.useEnterShadow(unit, action);
    if (actionId === "Stab") return this.useStab(unit, action);
    if (actionId === "EscapeScroll") return this.useEscapeScroll(unit);
    if (actionId === "TransmuteScroll") return this.useTransmuteScroll(unit, targetNode);
    if (actionId === "SwapCharm") return this.useSwapCharm(unit, targetNode);
    if (actionId !== "Bomb") return this.failAction("err.unknownAction");
    const item = unit.heldItem;
    if (item?.kind !== "Bomb") return this.failAction("err.noBomb");

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

  private useEnterShadow(unit: UnitState, action: UnitAction): ActionExecution {
    if (unit.inShadow) return this.failAction("err.alreadyHidden");
    if (hasOwnShadow(this.units, unit.currentNodeId, unit.ownerId, unit.unitId)) {
      return this.failAction("err.ownShadowThere");
    }
    unit.inShadow = true;
    applyCooldownTriggers(unit, action);
    unit.hasMovedThisTurn = true;
    unit.hasEverMoved = true;
    this.checkWin();
    const gameEnded = this.endReason !== GameEndReason.None;
    if (!gameEnded) this.endTurn();
    return {
      success: true,
      unitId: unit.unitId,
      actionId: "EnterShadow",
      originNode: unit.currentNodeId,
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

  private useStab(unit: UnitState, action: UnitAction): ActionExecution {
    if (!unit.inShadow) return this.failAction("err.mustBeHidden");
    const target = principalOn(this.units, unit.currentNodeId);
    if (!target || target.ownerId === unit.ownerId) return this.failAction("err.noEnemyHere");
    const origin = unit.currentNodeId;
    const field = target.skills.find((s) => s.id === "Forcefield" && s.isActive);
    if (field) {
      field.isActive = false;
      const loot = this.killUnit(unit);
      this.checkWin();
      const gameEnded = this.endReason !== GameEndReason.None;
      if (!gameEnded) this.endTurn();
      return {
        success: true,
        unitId: unit.unitId,
        actionId: "Stab",
        originNode: origin,
        captureBlocked: true,
        forcefieldConsumedUnitId: target.unitId,
        explosionOrigins: [],
        blastNodes: [],
        killedUnitIds: [unit.unitId],
        destroyedItemIds: [],
        droppedItems: loot.drop ? [{ itemId: loot.drop.itemId, nodeId: loot.drop.nodeId }] : [],
        gameEnded,
        winnerId: this.winnerId,
        endReason: this.endReason,
        turnNumber: this.turnNumber,
        currentPlayerId: this.currentPlayerId,
      };
    }

    const loot = this.killUnit(target);
    const killedUnitIds = [target.unitId];
    let explosionOrigins: number[] = [];
    let blastNodes: number[] = [];
    let destroyedItemIds: string[] = [];
    const droppedItems = loot.drop ? [{ itemId: loot.drop.itemId, nodeId: loot.drop.nodeId }] : [];
    if (loot.shatter) {
      const blast = this.breakItem(loot.shatter.item, loot.shatter.nodeId);
      explosionOrigins = blast.origins;
      blastNodes = blast.nodes;
      destroyedItemIds = blast.destroyedItemIds;
      for (const id of blast.killedUnitIds) {
        if (!killedUnitIds.includes(id)) killedUnitIds.push(id);
      }
    }
    if (unit.isAlive) {
      unit.inShadow = false;
      applyCooldownTriggers(unit, action);
      unit.hasMovedThisTurn = true;
      unit.hasEverMoved = true;
    }

    this.checkWin();
    const gameEnded = this.endReason !== GameEndReason.None;
    if (!gameEnded) this.endTurn();
    return {
      success: true,
      unitId: unit.unitId,
      actionId: "Stab",
      originNode: origin,
      explosionOrigins,
      blastNodes,
      killedUnitIds,
      destroyedItemIds,
      droppedItems,
      gameEnded,
      winnerId: this.winnerId,
      endReason: this.endReason,
      turnNumber: this.turnNumber,
      currentPlayerId: this.currentPlayerId,
    };
  }

  private useTransmuteScroll(unit: UnitState, targetNode?: number): ActionExecution {
    const item = unit.heldItem;
    if (item?.kind !== "TransmuteScroll") return this.failAction("err.noTransmuteScroll");
    if (itemIsSpent(item)) return this.failAction("err.noUsesLeft");
    if (targetNode == null) return this.failAction("err.chooseEmptyAttack");
    if (!transmutationTargets(this.board, unit, this.units).includes(targetNode)) {
      return this.failAction("err.notEmptyAttack");
    }
    if (!this.board.changeType(targetNode, NodeType.Impassable)) {
      return this.failAction("err.cannotTransmute");
    }

    if (!spendItemUse(item)) return this.failAction("err.noUsesLeft");
    const crushed: number[] = [];
    for (const shadow of this.units.filter((u) => u.isAlive && u.inShadow && u.currentNodeId === targetNode)) {
      this.killUnit(shadow);
      crushed.push(shadow.unitId);
    }
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
      killedUnitIds: crushed,
      destroyedItemIds: [],
      droppedItems: [],
      gameEnded,
      winnerId: this.winnerId,
      endReason: this.endReason,
      turnNumber: this.turnNumber,
      currentPlayerId: this.currentPlayerId,
    };
  }

  private useSwapCharm(unit: UnitState, targetNode?: number): ActionExecution {
    const item = unit.heldItem;
    if (item?.kind !== "SwapCharm") return this.failAction("err.noSwapCharm");
    if (itemIsSpent(item)) return this.failAction("err.noUsesLeft");
    if (targetNode == null) return this.failAction("err.chooseAdjacent");
    if (!this.board.areConnected(unit.currentNodeId, targetNode)) {
      return this.failAction("err.notAdjacent");
    }
    const other = principalOn(this.units, targetNode);
    if (!other) return this.failAction("err.noPieceThere");
    if (other.unitId === unit.unitId) return this.failAction("err.cannotSwapSelf");

    if (!spendItemUse(item)) return this.failAction("err.noUsesLeft");
    const from = unit.currentNodeId;
    unit.currentNodeId = other.currentNodeId;
    other.currentNodeId = from;
    unit.hasEverMoved = true;
    other.hasEverMoved = true;

    this.checkWin();
    const gameEnded = this.endReason !== GameEndReason.None;

    return {
      success: true,
      unitId: unit.unitId,
      actionId: "SwapCharm",
      originNode: from,
      destNode: targetNode,
      swappedUnitId: other.unitId,
      free: true,
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
    if (item?.kind !== "EscapeScroll") return this.failAction("err.noEscapeScroll");
    if (itemIsSpent(item)) return this.failAction("err.noUsesLeft");
    const home = unit.homeNodeId;
    if (home == null) return this.failAction("err.noHomeSquare");
    if (home === unit.currentNodeId) return this.failAction("err.alreadyHome");
    if (!this.board.passable(home)) return this.failAction("err.homeGone");
    if (principalOn(this.units, home)) return this.failAction("err.homeOccupied");
    if (unit.inShadow && hasOwnShadow(this.units, home, unit.ownerId, unit.unitId)) {
      return this.failAction("err.ownShadowThere");
    }

    const from = unit.currentNodeId;
    if (!spendItemUse(item)) return this.failAction("err.noUsesLeft");
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
    this.endGame(playerId === 0 ? 1 : 0, GameEndReason.Resignation);
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
        this.endGame(playerId === 0 ? 1 : 0, GameEndReason.KingCaptured);
        return;
      }
      if (isCheckmate(this.board, playerId, this.units)) {
        this.endGame(playerId === 0 ? 1 : 0, GameEndReason.Checkmate);
        return;
      }
    }
    if (isStalemate(this.board, this.currentPlayerId, this.units)) {
      this.endGame(null, GameEndReason.Stalemate);
    }
  }

  private endGame(winnerId: number | null, reason: GameEndReason): void {
    this.winnerId = winnerId;
    this.endReason = reason;
    this.phase = GamePhase.Ended;
    if (this.clock) stopClock(this.clock, Date.now());
  }

  private turnError(playerId: number): string | undefined {
    if (this.applyFlag()) return "err.timeExpired";
    if (playerId !== this.currentPlayerId) return "err.notYourTurn";
    if (this.phase !== GamePhase.Playing) return "err.notPlaying";
    return undefined;
  }

  private endTurn(): void {
    const previous = this.currentPlayerId;
    this.currentPlayerId = (this.currentPlayerId + 1) % 2;
    if (this.currentPlayerId === 0) this.turnNumber++;
    for (const unit of this.units.filter((u) => u.ownerId === this.currentPlayerId)) {
      unit.hasMovedThisTurn = false;
      tickUnitCooldowns(unit);
    }
    if (this.clock) afterTurn(this.clock, previous, this.currentPlayerId, Date.now());
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
      pushedUnitId: null,
      pushToNode: null,
      pushFalls: false,
      convertedUnitId: null,
      droppedItemId: null,
      droppedItemNodeId: null,
      pickedItemId: null,
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

  private failDrop(error: string): DropExecution {
    return { success: false, error, unitId: -1, itemId: "", nodeId: -1, turnNumber: this.turnNumber, currentPlayerId: this.currentPlayerId };
  }

  private giveItem(unit: UnitState, item: ItemState): void {
    unit.heldItem = { ...item, nodeId: -1 };
    applyItemPick(unit, item);
    this.items = this.items.filter((ground) => ground.id !== item.id);
  }

  private maybeAutoPickup(unit: UnitState): string | null {
    if (!this.autoPickupItems || unit.heldItem || unit.inShadow) return null;
    const item = this.items.find((ground) => ground.nodeId === unit.currentNodeId);
    if (!item) return null;
    this.giveItem(unit, item);
    return item.id;
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
