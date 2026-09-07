import { Board } from "./board";
import { HEX_DIAG_DIRS, fromCube, toCube } from "./hex";
import { hurdleLanding } from "./hop";
import {
  AXIS_A,
  DIAG_AXES,
  MovementPattern,
  MovementType,
  MoveTargets,
  NodeType,
  ORTHO_AXES,
  blocksFlight,
  emptyTargets,
} from "./types";

export function getCategorizedTargets(
  pattern: MovementPattern,
  board: Board,
  fromNode: number,
  playerSide: number,
  isOccupied: (id: number) => boolean,
  isEnemy: (id: number) => boolean
): MoveTargets {
  const result = emptyTargets();
  const maxDist = pattern.maxDistance === -1 ? 100 : pattern.maxDistance;

  switch (pattern.type) {
    case MovementType.Orthogonal:
      addAxisRays(result, board, fromNode, maxDist, pattern, isOccupied, isEnemy, axesFor(pattern, board.orthoAxes));
      break;
    case MovementType.Diagonal:
      addDiagonal(result, board, fromNode, maxDist, pattern, isOccupied, isEnemy);
      break;
    case MovementType.Leaper:
      addLeaper(result, board, fromNode, pattern, isOccupied, isEnemy);
      break;
    case MovementType.Adjacent:
      addAdjacent(result, board, fromNode, pattern, isOccupied, isEnemy);
      break;
    case MovementType.Forward:
      addAxisRays(result, board, fromNode, maxDist, pattern, isOccupied, isEnemy, axesFor(pattern, board.isHex ? [AXIS_A] : ORTHO_AXES), {
        forward: playerSide === 0 ? 1 : -1,
      });
      break;
    case MovementType.Backward:
      addAxisRays(result, board, fromNode, maxDist, pattern, isOccupied, isEnemy, axesFor(pattern, board.isHex ? [AXIS_A] : ORTHO_AXES), {
        forward: playerSide === 0 ? -1 : 1,
      });
      break;
    case MovementType.Sideways:
      addAxisRays(result, board, fromNode, maxDist, pattern, isOccupied, isEnemy, axesFor(pattern, board.orthoAxes), {
        forward: 0,
      });
      break;
    case MovementType.DiagonalCapture:
      addDiagonal(result, board, fromNode, 1, pattern, isOccupied, isEnemy, {
        forward: playerSide === 0 ? 1 : -1,
      });
      break;
    case MovementType.DiagonalLeaper:
      addDiagonal(result, board, fromNode, maxDist, pattern, isOccupied, isEnemy);
      break;
  }
  return result;
}

function pushCapture(result: MoveTargets, nodeId: number, pattern: MovementPattern): void {
  if (pattern.rangedCapture) result.rangedCapture.push(nodeId);
  else if (pattern.captureOnly) result.captureOnly.push(nodeId);
  else result.both.push(nodeId);
}

function pushEmpty(result: MoveTargets, nodeId: number, pattern: MovementPattern): void {
  if (pattern.rangedCapture) result.rangedCapture.push(nodeId);
  else if (pattern.captureOnly) result.captureOnly.push(nodeId);
  else if (pattern.moveOnly) result.moveOnly.push(nodeId);
  else result.both.push(nodeId);
}

function continuesPastOccupied(pattern: MovementPattern): boolean {
  return pattern.canJump || pattern.passesUnits;
}

function applyLanding(
  result: MoveTargets,
  nodeId: number,
  pattern: MovementPattern,
  isOccupied: (id: number) => boolean,
  isEnemy: (id: number) => boolean
): void {
  if (isOccupied(nodeId)) {
    if (pattern.canShare) result.moveOnly.push(nodeId);
    else if (isEnemy(nodeId) && !pattern.moveOnly) pushCapture(result, nodeId, pattern);
    else if (pattern.canEnterOccupied && !isEnemy(nodeId)) result.moveOnly.push(nodeId);
    return;
  }
  if (pattern.captureOnly) return;
  pushEmpty(result, nodeId, pattern);
}

function applyHurdle(
  result: MoveTargets,
  squares: number[],
  pattern: MovementPattern,
  isOccupied: (id: number) => boolean,
  isEnemy: (id: number) => boolean
): void {
  const land = hurdleLanding(squares, pattern.hopBeyond, isOccupied);
  if (land == null) return;
  applyLanding(result, land, pattern, isOccupied, isEnemy);
}

/** Passable squares along an axis ray, stopping at walls, pits, and board edge. */
function collectAxisRay(board: Board, from: number, first: number, axis: string, maxDist: number): number[] {
  const squares: number[] = [];
  let prev = from;
  let current = first;
  for (let i = 1; i <= maxDist; i++) {
    const node = board.getNode(current);
    if (!node || blocksFlight(node) || !board.passable(current)) break;
    squares.push(current);
    const nexts = board.neighborsOnAxis(current, axis).filter((n) => n !== prev);
    if (!nexts[0]) break;
    prev = current;
    current = nexts[0];
  }
  return squares;
}

function axesFor(pattern: MovementPattern, fallback: string[]): string[] {
  return pattern.axes.length ? pattern.axes : fallback;
}

function deltaY(board: Board, from: number, to: number): number {
  return (board.nodeDef(to)?.y ?? 0) - (board.nodeDef(from)?.y ?? 0);
}

/** Graph walk vs terrain: walls block flight; abyss does not. Landing still needs a passable square. */
function canTraverse(board: Board, nodeId: number, landing: boolean, fly: boolean): boolean {
  const node = board.getNode(nodeId);
  if (!node) return false;
  if (landing) return board.passable(nodeId);
  if (fly) return !blocksFlight(node);
  return board.passable(nodeId);
}

function afterAxisSteps(
  board: Board,
  from: number,
  axis: string,
  steps: number,
  opts: { fly: boolean; land: boolean }
): number[] {
  if (steps <= 0) return [from];
  const found: number[] = [];
  const walk = (cur: number, prev: number, left: number) => {
    for (const next of board.neighborsOnAxis(cur, axis)) {
      if (next === prev) continue;
      const landing = opts.land && left === 1;
      if (!canTraverse(board, next, landing, opts.fly)) continue;
      if (left === 1) found.push(next);
      else walk(next, cur, left - 1);
    }
  };
  walk(from, -1, steps);
  return found;
}

function addDiagonal(
  result: MoveTargets,
  board: Board,
  fromNode: number,
  maxDist: number,
  pattern: MovementPattern,
  isOccupied: (id: number) => boolean,
  isEnemy: (id: number) => boolean,
  opts?: { forward?: number }
): void {
  if (!board.isHex) {
    addAxisRays(result, board, fromNode, maxDist, pattern, isOccupied, isEnemy, axesFor(pattern, DIAG_AXES), opts);
    return;
  }
  const origin = board.nodeDef(fromNode);
  const cube = toCube(origin.x, origin.y);
  for (const dir of HEX_DIAG_DIRS) {
    const first = fromCube(cube.q + dir.q, cube.r + dir.r);
    const firstId = board.idAt(first.x, first.y);
    if (firstId == null) continue;
    if (opts?.forward != null && Math.sign(deltaY(board, fromNode, firstId)) !== opts.forward) continue;
    if (pattern.hopBeyond > 0) {
      const squares: number[] = [];
      let qHop = cube.q + dir.q;
      let rHop = cube.r + dir.r;
      for (let i = 1; i <= maxDist; i++) {
        const pos = fromCube(qHop, rHop);
        const current = board.idAt(pos.x, pos.y);
        if (current == null) break;
        const node = board.getNode(current);
        if (!node || blocksFlight(node) || !board.passable(current)) break;
        squares.push(current);
        qHop += dir.q;
        rHop += dir.r;
      }
      applyHurdle(result, squares, pattern, isOccupied, isEnemy);
      continue;
    }
    let q = cube.q + dir.q;
    let r = cube.r + dir.r;
    for (let i = 1; i <= maxDist; i++) {
      const pos = fromCube(q, r);
      const current = board.idAt(pos.x, pos.y);
      if (current == null) break;
      const node = board.getNode(current);
      if (!node) break;
      if (blocksFlight(node)) break;
      if (!board.passable(current)) {
        if (pattern.rangedCapture) {
          q += dir.q;
          r += dir.r;
          continue;
        }
        break;
      }
      if (isOccupied(current)) {
        if (i >= pattern.minDistance) applyLanding(result, current, pattern, isOccupied, isEnemy);
        if (!continuesPastOccupied(pattern)) break;
      } else if (i >= pattern.minDistance) {
        pushEmpty(result, current, pattern);
      }
      q += dir.q;
      r += dir.r;
    }
  }
}

function addAxisRays(
  result: MoveTargets,
  board: Board,
  fromNode: number,
  maxDist: number,
  pattern: MovementPattern,
  isOccupied: (id: number) => boolean,
  isEnemy: (id: number) => boolean,
  axes: string[],
  opts?: { forward?: number }
): void {
  for (const axis of axes) {
    for (const first of board.neighborsOnAxis(fromNode, axis)) {
      if (opts?.forward != null && Math.sign(deltaY(board, fromNode, first)) !== opts.forward) continue;
      if (pattern.hopBeyond > 0) {
        applyHurdle(result, collectAxisRay(board, fromNode, first, axis, maxDist), pattern, isOccupied, isEnemy);
        continue;
      }
      let prev = fromNode;
      let current = first;
      for (let i = 1; i <= maxDist; i++) {
        const node = board.getNode(current);
        if (!node) break;
        if (blocksFlight(node)) break;
        if (!board.passable(current)) {
          if (pattern.rangedCapture) {
            const nexts = board.neighborsOnAxis(current, axis).filter((n) => n !== prev);
            if (!nexts[0]) break;
            prev = current;
            current = nexts[0];
            continue;
          }
          break;
        }
        if (isOccupied(current)) {
          if (i >= pattern.minDistance) applyLanding(result, current, pattern, isOccupied, isEnemy);
          if (!continuesPastOccupied(pattern)) break;
        } else if (i >= pattern.minDistance) {
          pushEmpty(result, current, pattern);
        }
        const nexts = board.neighborsOnAxis(current, axis).filter((n) => n !== prev);
        if (!nexts[0]) break;
        prev = current;
        current = nexts[0];
      }
    }
  }
}

function addLeaper(
  result: MoveTargets,
  board: Board,
  fromNode: number,
  pattern: MovementPattern,
  isOccupied: (id: number) => boolean,
  isEnemy: (id: number) => boolean
): void {
  const n = pattern.leapX;
  const m = pattern.leapY;
  const axes = axesFor(pattern, board.orthoAxes);
  const dest = new Set<number>();
  // N on one axis then M on another, or vice versa (swap counts and axis pair).
  const legs: [number, number][] = n === m ? [[n, m]] : [[n, m], [m, n]];
  for (const [first, second] of legs) {
    for (const axisA of axes) {
      for (const axisB of axes) {
        if (axisA === axisB) continue;
        for (const mid of afterAxisSteps(board, fromNode, axisA, first, { fly: true, land: false })) {
          for (const end of afterAxisSteps(board, mid, axisB, second, { fly: true, land: true })) dest.add(end);
        }
      }
    }
  }
  for (const nodeId of dest) {
    if (nodeId === fromNode || !board.passable(nodeId)) continue;
    if (isOccupied(nodeId)) {
      applyLanding(result, nodeId, pattern, isOccupied, isEnemy);
      continue;
    }
    if (pattern.captureOnly) result.captureOnly.push(nodeId);
    else if (pattern.moveOnly) result.moveOnly.push(nodeId);
    else result.both.push(nodeId);
  }
}

/** -1 or 99+ = until blocked. */
export function pushStepCap(distance: number): number {
  if (distance < 0 || distance >= 99) return 100;
  return distance;
}

export type PushResolution = {
  vacated: boolean;
  dest: number;
  falls: boolean;
};

/** Shove the occupant of `target` away from `from` along that axis. */
export function resolvePush(
  board: Board,
  from: number,
  target: number,
  pushDistance: number,
  isOccupied: (id: number) => boolean
): PushResolution {
  const axis = board.axisBetween(from, target);
  if (!axis || pushStepCap(pushDistance) <= 0) return { vacated: false, dest: target, falls: false };
  const cap = pushStepCap(pushDistance);
  let prev = from;
  let current = target;
  let dest = target;
  for (let i = 1; i <= cap; i++) {
    const next = board.neighborsOnAxis(current, axis).find((id) => id !== prev);
    if (next == null) break;
    const node = board.getNode(next);
    if (!node || node.currentType === NodeType.Destroyed) break;
    if (node.currentType === NodeType.Impassable) break;
    if (node.currentType === NodeType.Abyss) return { vacated: true, dest: next, falls: true };
    if (!board.passable(next) || isOccupied(next)) break;
    prev = current;
    current = next;
    dest = current;
  }
  return { vacated: dest !== target, dest, falls: false };
}

export function pushDistanceOf(unit: { patterns: MovementPattern[] }): number {
  return unit.patterns.find((pattern) => pattern.pushDistance)?.pushDistance ?? 0;
}

function addAdjacent(
  result: MoveTargets,
  board: Board,
  fromNode: number,
  pattern: MovementPattern,
  isOccupied: (id: number) => boolean,
  isEnemy: (id: number) => boolean
): void {
  for (const nodeId of board.neighbors(fromNode)) {
    if (!board.passable(nodeId)) continue;
    if (isOccupied(nodeId)) {
      if (pattern.converts && isEnemy(nodeId)) {
        result.convert.push(nodeId);
        continue;
      }
      if (pattern.pushDistance) {
        if (resolvePush(board, fromNode, nodeId, pattern.pushDistance, isOccupied).vacated) {
          result.push.push(nodeId);
        }
        continue;
      }
      if (isEnemy(nodeId) && !pattern.moveOnly) {
        if (pattern.captureOnly) result.captureOnly.push(nodeId);
        else result.both.push(nodeId);
      } else if (pattern.canShare || (pattern.canEnterOccupied && !isEnemy(nodeId))) {
        result.moveOnly.push(nodeId);
      }
      continue;
    }
    if (pattern.captureOnly) result.captureOnly.push(nodeId);
    else if (pattern.moveOnly) result.moveOnly.push(nodeId);
    else result.both.push(nodeId);
  }
}

export function mergeTargets(into: MoveTargets, other: MoveTargets, seen: Set<number>): void {
  for (const n of other.moveOnly) {
    if (!seen.has(n)) {
      seen.add(n);
      into.moveOnly.push(n);
    }
  }
  for (const n of other.captureOnly) {
    if (!seen.has(n)) {
      seen.add(n);
      into.captureOnly.push(n);
    } else if (into.moveOnly.includes(n)) {
      into.moveOnly = into.moveOnly.filter((x) => x !== n);
      into.both.push(n);
    }
  }
  for (const n of other.both) {
    if (!seen.has(n)) {
      seen.add(n);
      into.both.push(n);
    }
  }
  for (const n of other.rangedCapture) {
    if (!seen.has(n)) {
      seen.add(n);
      into.rangedCapture.push(n);
    } else if (into.moveOnly.includes(n) && !into.rangedCapture.includes(n)) {
      into.rangedCapture.push(n);
    }
  }
  for (const n of other.push) {
    if (!seen.has(n)) {
      seen.add(n);
      into.push.push(n);
    }
  }
  for (const n of other.convert) {
    if (!seen.has(n)) {
      seen.add(n);
      into.convert.push(n);
    }
  }
}
