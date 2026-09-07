import { MovementPattern, MovementType, pattern } from "./types";

/** Land on the square immediately behind the hurdle (Grasshopper). */
export const HURDLE_BEYOND = 1;

export type HopSpec = {
  /** Squares after the hurdle where this hop lands. 1 = immediately behind. */
  beyond: number;
};

/**
 * Along a ray of passable squares, find the landing after hopping the first occupied
 * square (friend or foe). Returns the landing node, never the hurdle.
 */
export function hurdleLanding(
  squares: number[],
  beyond: number,
  isOccupied: (id: number) => boolean
): number | null {
  if (beyond <= 0) return null;
  let hurdle = -1;
  for (let i = 0; i < squares.length; i++) {
    if (isOccupied(squares[i])) {
      hurdle = i;
      break;
    }
  }
  if (hurdle < 0) return null;
  const land = hurdle + beyond;
  if (land >= squares.length) return null;
  for (let i = hurdle + 1; i < land; i++) {
    if (isOccupied(squares[i])) return null;
  }
  return squares[land];
}

/** Movement particularity: hop a piece and step onto the empty square beyond. */
export function hurdleMove(
  type: MovementType,
  hop: HopSpec = { beyond: HURDLE_BEYOND },
  extra?: Partial<MovementPattern>
): MovementPattern {
  return pattern({ type, hopBeyond: hop.beyond, moveOnly: true, ...extra });
}

/** Attack particularity: same hop, capture the piece on the landing square (not the hurdle). */
export function hurdleCapture(
  type: MovementType,
  hop: HopSpec = { beyond: HURDLE_BEYOND },
  extra?: Partial<MovementPattern>
): MovementPattern {
  return pattern({ type, hopBeyond: hop.beyond, captureOnly: true, ...extra });
}

export function queenHurdleMove(hop: HopSpec = { beyond: HURDLE_BEYOND }): MovementPattern[] {
  return [hurdleMove(MovementType.Orthogonal, hop), hurdleMove(MovementType.Diagonal, hop)];
}

export function queenHurdleCapture(hop: HopSpec = { beyond: HURDLE_BEYOND }): MovementPattern[] {
  return [hurdleCapture(MovementType.Orthogonal, hop), hurdleCapture(MovementType.Diagonal, hop)];
}
