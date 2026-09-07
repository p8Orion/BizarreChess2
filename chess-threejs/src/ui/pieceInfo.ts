import { itemUsesLabel } from "../core/items";
import { PIECES } from "../core/pieces";
import { MovementPattern, MovementType, UnitState } from "../core/types";
import { hasMessage, itemName, t } from "../i18n";

function distLabel(n: number): string {
  if (n < 0) return t("move.anySquares");
  if (n === 1) return t("move.oneSquare");
  return t("move.nSquares", { n });
}

function describePattern(pattern: MovementPattern): string {
  const flags: string[] = [];
  if (pattern.moveOnly) flags.push(t("move.flag.moveOnly"));
  if (pattern.passesUnits) flags.push(t("move.flag.passesUnits"));
  if (pattern.captureOnly) flags.push(t("move.flag.captureOnly"));
  if (pattern.rangedCapture) flags.push(t("move.flag.staysPut"));
  if (pattern.firstMoveOnly) flags.push(t("move.flag.firstMove"));
  if (pattern.converts) flags.push(t("move.flag.converts"));
  if (pattern.pushDistance) {
    flags.push(
      pattern.pushDistance < 0 || pattern.pushDistance >= 99
        ? t("move.flag.pushFar")
        : pattern.pushDistance === 1
          ? t("move.flag.pushN", { n: pattern.pushDistance })
          : t("move.flag.pushNs", { n: pattern.pushDistance })
    );
  }
  const extra = flags.length ? ` (${flags.join(", ")})` : "";
  if (pattern.hopBeyond > 0) {
    const land = pattern.hopBeyond === 1 ? t("move.landImmediate") : t("move.landN", { n: pattern.hopBeyond });
    const dir =
      pattern.type === MovementType.Orthogonal
        ? t("move.dir.ortho")
        : pattern.type === MovementType.Diagonal
          ? t("move.dir.diag")
          : t("move.dir.line");
    return t("move.hop", { dir, land, extra });
  }
  switch (pattern.type) {
    case MovementType.Orthogonal:
      return t("move.ortho", { dist: distLabel(pattern.maxDistance), extra });
    case MovementType.Diagonal:
      return t("move.diag", { dist: distLabel(pattern.maxDistance), extra });
    case MovementType.Adjacent:
      return t("move.adjacent", { extra });
    case MovementType.Forward:
      return t("move.forward", { dist: distLabel(pattern.maxDistance), extra });
    case MovementType.Backward:
      return t("move.backward", { dist: distLabel(pattern.maxDistance), extra });
    case MovementType.Sideways:
      return t("move.sideways", { dist: distLabel(pattern.maxDistance), extra });
    case MovementType.DiagonalCapture:
      return t("move.diagCapture");
    case MovementType.Leaper:
      return t("move.leaper", { x: pattern.leapX, y: pattern.leapY, extra });
    case MovementType.DiagonalLeaper:
      return t("move.diagLeaper", { dist: distLabel(pattern.maxDistance), extra });
    default:
      return "";
  }
}

export function pieceDisplayName(definitionId: string): string {
  const key = `piece.${definitionId}.name`;
  return hasMessage(key) ? t(key) : PIECES[definitionId]?.displayName ?? definitionId;
}

export function pieceMoveText(unit: UnitState): string {
  const key = `piece.${unit.definitionId}.blurb`;
  const base = hasMessage(key) ? t(key) : unit.patterns.map(describePattern).filter(Boolean).join(" ");
  const extras: string[] = [];
  if (unit.skills.some((s) => s.id === "Forcefield" && s.isActive)) {
    extras.push(t("hud.forcefield"));
  }
  for (const granted of unit.patterns.filter((p) => p.grantedBy)) {
    extras.push(describePattern(granted));
  }
  if (unit.heldItem) {
    const uses = itemUsesLabel(unit.heldItem);
    extras.push(t("hud.holding", { name: itemName(unit.heldItem.kind), uses: uses ? t("hud.uses", { uses }) : "" }));
  }
  return extras.length ? `${base} ${extras.join(" ")}` : base;
}
