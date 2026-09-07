import { Board } from "./board";
import { getCategorizedTargets, mergeTargets } from "./movement";
import { MoveTargets, UnitState, allTargets, emptyTargets } from "./types";

export interface ValidationResult {
  isValid: boolean;
  error?: string;
  isCapture: boolean;
  isRangedCapture: boolean;
  capturedUnitId: number | null;
}

export function transmutationTargets(board: Board, unit: UnitState, all: UnitState[]): number[] {
  const cat = movesForUnit(board, unit, all);
  const attack = new Set([...cat.captureOnly, ...cat.both, ...cat.rangedCapture]);
  return [...attack].filter(
    (id) => board.passable(id) && !all.some((other) => other.isAlive && other.currentNodeId === id)
  );
}

export function movesForUnit(board: Board, unit: UnitState, all: UnitState[]): MoveTargets {
  const occupied = (id: number) => all.some((u) => u.isAlive && u.currentNodeId === id);
  const enemy = (id: number) =>
    all.some((u) => u.isAlive && u.currentNodeId === id && u.ownerId !== unit.ownerId);
  const result = emptyTargets();
  const seen = new Set<number>();
  for (const pattern of unit.patterns) {
    if (pattern.firstMoveOnly && unit.hasEverMoved) continue;
    mergeTargets(
      result,
      getCategorizedTargets(pattern, board, unit.currentNodeId, unit.ownerId, occupied, enemy),
      seen
    );
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
  if (!unit.isAlive) return fail("Unit is dead");
  if (unit.ownerId !== currentPlayerId) return fail("Not your unit");
  if (unit.hasMovedThisTurn) return fail("Unit has already moved this turn");
  if (!board.passable(targetNode)) return fail("Target node is not passable");

  const categorized = movesForUnit(board, unit, all);
  const valid = allTargets(categorized);
  if (!valid.includes(targetNode)) return fail("Invalid move for this unit type");

  const isRanged = categorized.rangedCapture.includes(targetNode);
  const isCaptureOnly = categorized.captureOnly.includes(targetNode);
  const canMoveEmpty = categorized.moveOnly.includes(targetNode);
  const occupant = all.find((u) => u.isAlive && u.currentNodeId === targetNode);

  if (occupant) {
    if (occupant.ownerId === currentPlayerId) return fail("Cannot capture your own unit");
    return { isValid: true, isCapture: true, isRangedCapture: isRanged, capturedUnitId: occupant.unitId };
  }
  if ((isCaptureOnly || isRanged) && !canMoveEmpty) {
    return fail("Can only capture here, not move to empty square");
  }
  return { isValid: true, isCapture: false, isRangedCapture: false, capturedUnitId: null };
}

function fail(error: string): ValidationResult {
  return { isValid: false, error, isCapture: false, isRangedCapture: false, capturedUnitId: null };
}

function kingOf(playerId: number, all: UnitState[]): UnitState | undefined {
  return all.find((u) => u.isAlive && u.ownerId === playerId && u.definitionId === "King");
}

export function isKingInCheck(board: Board, playerId: number, all: UnitState[]): boolean {
  const king = kingOf(playerId, all);
  if (!king) return false;
  for (const enemy of all.filter((u) => u.isAlive && u.ownerId !== playerId)) {
    if (allTargets(movesForUnit(board, enemy, all)).includes(king.currentNodeId)) return true;
  }
  return false;
}

function hasEscape(board: Board, playerId: number, all: UnitState[]): boolean {
  for (const unit of all.filter((u) => u.isAlive && u.ownerId === playerId)) {
    const cat = movesForUnit(board, unit, all);
    for (const move of allTargets(cat)) {
      const isRanged = cat.rangedCapture.includes(move);
      const isCaptureOnly = cat.captureOnly.includes(move);
      const canMoveEmpty = cat.moveOnly.includes(move);
      const captured = all.find((u) => u.isAlive && u.currentNodeId === move);
      if ((isCaptureOnly || isRanged) && !captured && !canMoveEmpty) continue;
      const original = unit.currentNodeId;
      const actualRanged = isRanged && !!captured;
      if (!actualRanged) unit.currentNodeId = move;
      if (captured) captured.isAlive = false;
      const still = isKingInCheck(board, playerId, all);
      unit.currentNodeId = original;
      if (captured) captured.isAlive = true;
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
    if (cat.moveOnly.length || cat.both.length) return false;
    for (const nodeId of [...cat.captureOnly, ...cat.rangedCapture]) {
      const occ = all.find((u) => u.isAlive && u.currentNodeId === nodeId);
      if (occ && occ.ownerId !== playerId) return false;
    }
  }
  return true;
}
