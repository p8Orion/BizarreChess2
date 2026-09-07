import { itemUsesLabel } from "../core/items";
import { PIECES } from "../core/pieces";
import { MovementPattern, MovementType, UnitState } from "../core/types";

const BLURBS: Record<string, string> = {
  King: "One square in any direction.",
  Queen: "Any number of squares orthogonally or diagonally.",
  Rook: "Any number of squares orthogonally.",
  Bishop: "Any number of squares diagonally.",
  Knight: "Leaps in an L (2×1). Jumps over pieces.",
  Camel: "Leaps in a long L (3×1). Jumps over pieces.",
  Crossbowman: "Steps one square in any direction without capturing. Shoots diagonally up to 3 squares and stays put.",
  Cannon: "Steps one square orthogonally without capturing. Shoots orthogonally at range 2–4 and stays put.",
  Grasshopper:
    "Moves like a queen, but must hop over a piece (friend or foe) and land on the square immediately beyond. Captures the piece it lands on, not the one it hops over.",
  Pawn: "Moves one square forward (two on the first move). Captures one square diagonally forward.",
  Lancer: "Advances forward and can capture that way. May move two squares on its first move.",
  Defender: "Moves one square forward without capturing. May step two forward on its first move (that step can capture).",
  Bomber: "Moves one square forward without capturing. May move two squares forward on its first move. Cannot capture. Ignite, break, or die to explode this tile and the four orthogonal neighbors, and smash mountains there.",
};

function distLabel(n: number): string {
  if (n < 0) return "any number of squares";
  if (n === 1) return "1 square";
  return `${n} squares`;
}

function describePattern(pattern: MovementPattern): string {
  const flags: string[] = [];
  if (pattern.moveOnly) flags.push("cannot capture");
  if (pattern.captureOnly) flags.push("capture only");
  if (pattern.rangedCapture) flags.push("stays put");
  if (pattern.firstMoveOnly) flags.push("first move");
  const extra = flags.length ? ` (${flags.join(", ")})` : "";
  if (pattern.hopBeyond > 0) {
    const land = pattern.hopBeyond === 1 ? "the square immediately beyond" : `${pattern.hopBeyond} squares beyond`;
    const dir =
      pattern.type === MovementType.Orthogonal
        ? "orthogonally"
        : pattern.type === MovementType.Diagonal
          ? "diagonally"
          : "along its line";
    return `Hops ${dir} over a piece and lands on ${land}${extra}.`;
  }
  switch (pattern.type) {
    case MovementType.Orthogonal:
      return `Orthogonal ${distLabel(pattern.maxDistance)}${extra}.`;
    case MovementType.Diagonal:
      return `Diagonal ${distLabel(pattern.maxDistance)}${extra}.`;
    case MovementType.Adjacent:
      return `One square in any direction${extra}.`;
    case MovementType.Forward:
      return `Forward ${distLabel(pattern.maxDistance)}${extra}.`;
    case MovementType.Backward:
      return `Backward ${distLabel(pattern.maxDistance)}${extra}.`;
    case MovementType.Sideways:
      return `Sideways ${distLabel(pattern.maxDistance)}${extra}.`;
    case MovementType.DiagonalCapture:
      return `Captures one square diagonally forward.`;
    case MovementType.Leaper:
      return `Leaps ${pattern.leapX}×${pattern.leapY}${extra}.`;
    case MovementType.DiagonalLeaper:
      return `Diagonal leap up to ${distLabel(pattern.maxDistance)}${extra}.`;
    default:
      return "";
  }
}

export function pieceDisplayName(definitionId: string): string {
  return PIECES[definitionId]?.displayName ?? definitionId;
}

export function pieceMoveText(unit: UnitState): string {
  const base = BLURBS[unit.definitionId] ?? unit.patterns.map(describePattern).filter(Boolean).join(" ");
  const extras: string[] = [];
  if (unit.skills.some((s) => s.id === "Forcefield" && s.isActive)) {
    extras.push("Force field blocks one capture.");
  }
  for (const granted of unit.patterns.filter((p) => p.grantedBy)) {
    extras.push(describePattern(granted));
  }
  if (unit.heldItem) {
    const uses = itemUsesLabel(unit.heldItem);
    extras.push(`Holding ${unit.heldItem.displayName}${uses ? ` (${uses})` : ""}.`);
  }
  return extras.length ? `${base} ${extras.join(" ")}` : base;
}
