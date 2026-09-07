import type { ArmyFormat } from "./format";
import { COOLDOWN_LOCKED } from "./cooldown";
import { queenHurdleCapture, queenHurdleMove } from "./hop";
import { MovementType, UnitDefinition, leaper, pattern } from "./types";

function def(
  id: string,
  extra: Partial<UnitDefinition> & { patterns: UnitDefinition["patterns"] }
): UnitDefinition {
  return {
    displayName: id,
    isKing: false,
    canPromote: false,
    model: id.toLowerCase(),
    skills: [],
    actions: [],
    inShadow: false,
    ...extra,
    id,
  };
}

const STEP_BACK = pattern({ type: MovementType.Backward, maxDistance: 1, moveOnly: true });
const STEP_SIDE = pattern({ type: MovementType.Sideways, maxDistance: 1, moveOnly: true });

export const CROSSBOW_SHOT = pattern({
  type: MovementType.DiagonalLeaper,
  maxDistance: 3,
  captureOnly: true,
  rangedCapture: true,
});

export const PIECES: Record<string, UnitDefinition> = {
  King: def("King", {
    isKing: true,
    patterns: [pattern({ type: MovementType.Adjacent, maxDistance: 1 })],
  }),
  Queen: def("Queen", {
    patterns: [
      pattern({ type: MovementType.Orthogonal, maxDistance: -1 }),
      pattern({ type: MovementType.Diagonal, maxDistance: -1 }),
    ],
  }),
  Grasshopper: def("Grasshopper", {
    model: "placeholder",
    patterns: [...queenHurdleMove(), ...queenHurdleCapture()],
  }),
  Rook: def("Rook", {
    patterns: [pattern({ type: MovementType.Orthogonal, maxDistance: -1 })],
  }),
  Bishop: def("Bishop", {
    patterns: [pattern({ type: MovementType.Diagonal, maxDistance: -1 })],
  }),
  Knight: def("Knight", {
    patterns: [leaper(2, 1)],
  }),
  Camel: def("Camel", {
    patterns: [leaper(3, 1)],
  }),
  Crossbowman: def("Crossbowman", {
    patterns: [pattern({ type: MovementType.Adjacent, maxDistance: 1, moveOnly: true })],
  }),
  Pusher: def("Pusher", {
    model: "placeholder",
    patterns: [pattern({ type: MovementType.Adjacent, maxDistance: 1, pushDistance: -1 })],
  }),
  Priest: def("Priest", {
    model: "placeholder",
    patterns: [pattern({ type: MovementType.Adjacent, maxDistance: 1, moveOnly: true, converts: true })],
  }),
  Assassin: def("Assassin", {
    model: "placeholder",
    patterns: [pattern({ type: MovementType.Diagonal, maxDistance: 2, moveOnly: true, passesUnits: true })],
    actions: [
      {
        id: "EnterShadow",
        label: "Enter shadows",
        maxCooldown: 3,
        cooldown: 0,
        cooldownAfterUse: COOLDOWN_LOCKED,
      },
      {
        id: "Stab",
        label: "Stab",
        resetActionId: "EnterShadow",
        resetActionTo: "max",
      },
    ],
  }),
  Cannon: def("Cannon", {
    patterns: [
      pattern({ type: MovementType.Orthogonal, maxDistance: 1, moveOnly: true }),
      pattern({
        type: MovementType.Orthogonal,
        maxDistance: 4,
        minDistance: 2,
        captureOnly: true,
        rangedCapture: true,
      }),
    ],
  }),
  Pawn: def("Pawn", {
    canPromote: true,
    patterns: [
      pattern({ type: MovementType.Forward, maxDistance: 1, moveOnly: true }),
      pattern({ type: MovementType.Forward, maxDistance: 2, moveOnly: true, firstMoveOnly: true }),
      STEP_BACK,
      STEP_SIDE,
      pattern({ type: MovementType.DiagonalCapture, captureOnly: true }),
    ],
  }),
  Lancer: def("Lancer", {
    canPromote: true,
    patterns: [
      pattern({ type: MovementType.Forward, maxDistance: 1 }),
      pattern({ type: MovementType.Forward, maxDistance: 2, firstMoveOnly: true }),
      STEP_BACK,
      STEP_SIDE,
    ],
  }),
  Defender: def("Defender", {
    canPromote: true,
    patterns: [
      pattern({ type: MovementType.Forward, maxDistance: 1, moveOnly: true }),
      pattern({ type: MovementType.Forward, maxDistance: 2, firstMoveOnly: true }),
      STEP_BACK,
      STEP_SIDE,
    ],
    skills: [{ id: "Forcefield", isActive: true }],
  }),
  Bomber: def("Bomber", {
    model: "pawn",
    canPromote: false,
    patterns: [
      pattern({ type: MovementType.Forward, maxDistance: 1, moveOnly: true }),
      pattern({ type: MovementType.Forward, maxDistance: 2, moveOnly: true, firstMoveOnly: true }),
      STEP_BACK,
      STEP_SIDE,
    ],
  }),
};

export type Slot = { piece: string; x: number; row: "back" | "front" };

export type ArmyKind = "bizarre" | "classic" | "mini-classic" | "mini-court" | "mini-bizarre" | "mini-leap";

export interface ArmyDef {
  id: ArmyKind;
  label: string;
  format: ArmyFormat;
  slots: Slot[];
  /** Extra pawns on empty front files (wide boards). Mini armies leave the flanks empty. */
  fillEmptyFront: boolean;
}

export const CLASSIC_ARMY: Slot[] = [
  { piece: "Rook", x: 0, row: "back" },
  { piece: "Knight", x: 1, row: "back" },
  { piece: "Bishop", x: 2, row: "back" },
  { piece: "Queen", x: 3, row: "back" },
  { piece: "King", x: 4, row: "back" },
  { piece: "Bishop", x: 5, row: "back" },
  { piece: "Knight", x: 6, row: "back" },
  { piece: "Rook", x: 7, row: "back" },
  ...Array.from({ length: 8 }, (_, x) => ({ piece: "Pawn", x, row: "front" as const })),
];

/** Army that shows the custom pieces without changing the back-row king. */
export const BIZARRE_ARMY: Slot[] = [
  { piece: "Cannon", x: 0, row: "back" },
  { piece: "Camel", x: 1, row: "back" },
  { piece: "Crossbowman", x: 2, row: "back" },
  { piece: "Grasshopper", x: 3, row: "back" },
  { piece: "King", x: 4, row: "back" },
  { piece: "Bishop", x: 5, row: "back" },
  { piece: "Knight", x: 6, row: "back" },
  { piece: "Rook", x: 7, row: "back" },
  { piece: "Lancer", x: 0, row: "front" },
  { piece: "Defender", x: 1, row: "front" },
  { piece: "Bomber", x: 2, row: "front" },
  { piece: "Pawn", x: 3, row: "front" },
  { piece: "Pawn", x: 4, row: "front" },
  { piece: "Pawn", x: 5, row: "front" },
  { piece: "Lancer", x: 6, row: "front" },
  { piece: "Defender", x: 7, row: "front" },
];

function miniArmy(...back: string[]): Slot[] {
  return [
    ...back.map((piece, x) => ({ piece, x, row: "back" as const })),
    ...back.map((_, x) => ({ piece: "Pawn", x, row: "front" as const })),
  ];
}

export const ARMIES: ArmyDef[] = [
  { id: "bizarre", label: "Bizarre — full custom", format: "normal", slots: BIZARRE_ARMY, fillEmptyFront: true },
  { id: "classic", label: "Classic — 8 + 8", format: "normal", slots: CLASSIC_ARMY, fillEmptyFront: true },
  { id: "mini-classic", label: "Mini Classic — R N B K + 4 pawns", format: "mini", slots: miniArmy("Rook", "Knight", "Bishop", "King"), fillEmptyFront: false },
  { id: "mini-court", label: "Mini Court — B Q K N + 4 pawns", format: "mini", slots: miniArmy("Bishop", "Queen", "King", "Knight"), fillEmptyFront: false },
  {
    id: "mini-bizarre",
    label: "Mini Bizarre — Cannon Crossbow King Grasshopper + Lancer Defender Bomber",
    format: "mini",
    slots: [
      ...miniArmy("Cannon", "Crossbowman", "King", "Grasshopper").filter((slot) => slot.row === "back"),
      { piece: "Lancer", x: 0, row: "front" },
      { piece: "Defender", x: 1, row: "front" },
      { piece: "Bomber", x: 2, row: "front" },
      { piece: "Pawn", x: 3, row: "front" },
    ],
    fillEmptyFront: false,
  },
  { id: "mini-leap", label: "Mini Leap — Knight Camel King Bishop + 4 pawns", format: "mini", slots: miniArmy("Knight", "Camel", "King", "Bishop"), fillEmptyFront: false },
];

export function armyByKind(id?: string): ArmyDef {
  return ARMIES.find((army) => army.id === id) ?? ARMIES[0];
}

/** Every piece in the catalog — editor and selectors should use this, not a hardcoded list. */
export function catalogPieces(): UnitDefinition[] {
  return Object.values(PIECES);
}
