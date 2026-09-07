import { Board } from "./board";
import { getCategorizedTargets, mergeTargets, pushDistanceOf, resolvePush } from "./movement";
import { hasOwnShadow, hasPrincipal, principalOn } from "./occupancy";
import { MoveTargets, UnitState, allTargets, emptyTargets } from "./types";

export interface ValidationResult {
  isValid: boolean;
  error?: string;
  isCapture: boolean;
  isRangedCapture: boolean;
  capturedUnitId: number | null;
  isPush: boolean;
  pushedUnitId: number | null;
  pushToNode: number | null;
  pushFalls: boolean;
  isConvert: boolean;
  convertedUnitId: number | null;
}

export function transmutationTargets(board: Board, unit: UnitState, all: UnitState[]): number[] {
  const cat = movesForUnit(board, unit, all);
  const attack = new Set([...cat.captureOnly, ...cat.both, ...cat.rangedCapture]);
  return [...attack].filter(
    (id) => board.passable(id) && !hasPrincipal(all, id)
  );
}

export function movesForUnit(board: Board, unit: UnitState, all: UnitState[]): MoveTargets {
  const occupied = (id: number) => all.some((u) => u.isAlive && !u.inShadow && u.currentNodeId === id);
  const enemy = (id: number) =>
    all.some((u) => u.isAlive && !u.inShadow && u.currentNodeId === id && u.ownerId !== unit.ownerId);
  const result = emptyTargets();
  const seen = new Set<number>();
  for (const pattern of unit.patterns) {
    if (pattern.firstMoveOnly && unit.hasEverMoved) continue;
    const live = unit.inShadow ? { ...pattern, canShare: true } : pattern;
    mergeTargets(
      result,
      getCategorizedTargets(live, board, unit.currentNodeId, unit.ownerId, occupied, enemy),
      seen
    );
  }
  result.convert = result.convert.filter((id) => {
    const target = principalOn(all, id);
    return !!target && target.definitionId !== "King";
  });
  if (unit.inShadow) {
    const blocked = (id: number) => hasOwnShadow(all, id, unit.ownerId, unit.unitId);
    result.moveOnly = result.moveOnly.filter((id) => !blocked(id));
    result.captureOnly = result.captureOnly.filter((id) => !blocked(id));
    result.both = result.both.filter((id) => !blocked(id));
  }
  return result;
}

export function validateMove(
  board: Board,
  unit: UnitState,
  targetNode: number,
  all: UnitState[],
  currentPlayerId: number
): ValidationResult {
  if (!unit.isAlive) return fail("err.unitDead");
  if (unit.ownerId !== currentPlayerId) return fail("err.notYourUnit");
  if (unit.hasMovedThisTurn) return fail("err.alreadyMoved");
  if (!board.passable(targetNode)) return fail("err.notPassable");

  const categorized = movesForUnit(board, unit, all);
  const valid = allTargets(categorized);
  if (!valid.includes(targetNode)) return fail("err.invalidMove");

  const isRanged = categorized.rangedCapture.includes(targetNode);
  const isCaptureOnly = categorized.captureOnly.includes(targetNode);
  const canMoveEmpty = categorized.moveOnly.includes(targetNode);
  const occupant = principalOn(all, targetNode);

  if (unit.inShadow && hasOwnShadow(all, targetNode, unit.ownerId, unit.unitId)) {
    return fail("err.ownShadowThere");
  }

  if (categorized.push.includes(targetNode)) {
    if (!occupant) return fail("err.nothingToPush");
    const outcome = resolvePush(
      board,
      unit.currentNodeId,
      targetNode,
      pushDistanceOf(unit),
      (id) => all.some((other) => other.isAlive && !other.inShadow && other.currentNodeId === id)
    );
    if (!outcome.vacated) return fail("err.cannotPush");
    return {
      isValid: true,
      isCapture: false,
      isRangedCapture: false,
      capturedUnitId: null,
      isPush: true,
      pushedUnitId: occupant.unitId,
      pushToNode: outcome.dest,
      pushFalls: outcome.falls,
      isConvert: false,
      convertedUnitId: null,
    };
  }

  if (categorized.convert.includes(targetNode)) {
    if (!occupant) return fail("err.nothingToConvert");
    if (occupant.ownerId === currentPlayerId) return fail("err.alreadyYours");
    if (occupant.definitionId === "King") return fail("err.cannotConvertKing");
    return {
      isValid: true,
      isCapture: false,
      isRangedCapture: false,
      capturedUnitId: null,
      isPush: false,
      pushedUnitId: null,
      pushToNode: null,
      pushFalls: false,
      isConvert: true,
      convertedUnitId: occupant.unitId,
    };
  }

  if (occupant) {
    if (unit.inShadow && canMoveEmpty) {
      return {
        isValid: true,
        isCapture: false,
        isRangedCapture: false,
        capturedUnitId: null,
        isPush: false,
        pushedUnitId: null,
        pushToNode: null,
        pushFalls: false,
        isConvert: false,
        convertedUnitId: null,
      };
    }
    if (occupant.ownerId === currentPlayerId) return fail("err.cannotCaptureOwn");
    return {
      isValid: true,
      isCapture: true,
      isRangedCapture: isRanged,
      capturedUnitId: occupant.unitId,
      isPush: false,
      pushedUnitId: null,
      pushToNode: null,
      pushFalls: false,
      isConvert: false,
      convertedUnitId: null,
    };
  }
  if ((isCaptureOnly || isRanged) && !canMoveEmpty) {
    return fail("err.captureOnly");
  }
  return {
    isValid: true,
    isCapture: false,
    isRangedCapture: false,
    capturedUnitId: null,
    isPush: false,
    pushedUnitId: null,
    pushToNode: null,
    pushFalls: false,
    isConvert: false,
    convertedUnitId: null,
  };
}

function fail(error: string): ValidationResult {
  return {
    isValid: false,
    error,
    isCapture: false,
    isRangedCapture: false,
    capturedUnitId: null,
    isPush: false,
    pushedUnitId: null,
    pushToNode: null,
    pushFalls: false,
    isConvert: false,
    convertedUnitId: null,
  };
}

function kingOf(playerId: number, all: UnitState[]): UnitState | undefined {
  return all.find((u) => u.isAlive && u.ownerId === playerId && u.definitionId === "King");
}

export function isKingInCheck(board: Board, playerId: number, all: UnitState[]): boolean {
  const king = kingOf(playerId, all);
  if (!king || king.inShadow) return false;
  const occupied = (id: number) => all.some((u) => u.isAlive && !u.inShadow && u.currentNodeId === id);
  for (const enemy of all.filter((u) => u.isAlive && u.ownerId !== playerId && !u.inShadow)) {
    const cat = movesForUnit(board, enemy, all);
    if ([...cat.captureOnly, ...cat.both, ...cat.rangedCapture].includes(king.currentNodeId)) return true;
    if (cat.push.includes(king.currentNodeId)) {
      const outcome = resolvePush(board, enemy.currentNodeId, king.currentNodeId, pushDistanceOf(enemy), occupied);
      if (outcome.falls) return true;
    }
  }
  return false;
}

function hasEscape(board: Board, playerId: number, all: UnitState[]): boolean {
  const occupied = (id: number) => all.some((u) => u.isAlive && !u.inShadow && u.currentNodeId === id);
  for (const unit of all.filter((u) => u.isAlive && u.ownerId === playerId)) {
    const cat = movesForUnit(board, unit, all);
    for (const move of allTargets(cat)) {
      if (cat.push.includes(move)) {
        const occupant = principalOn(all, move);
        if (!occupant) continue;
        const outcome = resolvePush(board, unit.currentNodeId, move, pushDistanceOf(unit), occupied);
        if (!outcome.vacated) continue;
        const pusherFrom = unit.currentNodeId;
        const victimFrom = occupant.currentNodeId;
        const victimAlive = occupant.isAlive;
        occupant.currentNodeId = outcome.dest;
        if (outcome.falls) occupant.isAlive = false;
        unit.currentNodeId = move;
        const still = isKingInCheck(board, playerId, all);
        unit.currentNodeId = pusherFrom;
        occupant.currentNodeId = victimFrom;
        occupant.isAlive = victimAlive;
        if (!still) return true;
        continue;
      }
      if (cat.convert.includes(move)) {
        const occupant = principalOn(all, move);
        if (!occupant || occupant.definitionId === "King") continue;
        const prevOwner = occupant.ownerId;
        occupant.ownerId = unit.ownerId;
        const still = isKingInCheck(board, playerId, all);
        occupant.ownerId = prevOwner;
        if (!still) return true;
        continue;
      }
      const isRanged = cat.rangedCapture.includes(move);
      const isCaptureOnly = cat.captureOnly.includes(move);
      const canMoveEmpty = cat.moveOnly.includes(move);
      const captured = principalOn(all, move);
      if ((isCaptureOnly || isRanged) && !captured && !canMoveEmpty) continue;
      const original = unit.currentNodeId;
      const actualRanged = isRanged && !!captured;
      const sharing = unit.inShadow && canMoveEmpty && !!captured;
      if (!actualRanged) unit.currentNodeId = move;
      if (captured && !sharing) captured.isAlive = false;
      const still = isKingInCheck(board, playerId, all);
      unit.currentNodeId = original;
      if (captured && !sharing) captured.isAlive = true;
      if (!still) return true;
    }
  }
  return false;
}

export function isCheckmate(board: Board, playerId: number, all: UnitState[]): boolean {
  return isKingInCheck(board, playerId, all) && !hasEscape(board, playerId, all);
}

export function isStalemate(board: Board, playerId: number, all: UnitState[]): boolean {
  if (isKingInCheck(board, playerId, all)) return false;
  for (const unit of all.filter((u) => u.isAlive && u.ownerId === playerId)) {
    const cat = movesForUnit(board, unit, all);
    if (cat.moveOnly.length || cat.both.length || cat.push.length || cat.convert.length) return false;
    for (const nodeId of [...cat.captureOnly, ...cat.rangedCapture]) {
      const occ = principalOn(all, nodeId);
      if (occ && occ.ownerId !== playerId) return false;
    }
  }
  return true;
}
