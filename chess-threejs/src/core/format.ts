import type { ArmyKind } from "./pieces";
import type { BoardKind } from "./types";

export type ArmyFormat = "normal" | "midi" | "mini";
export type MatchMode = "normal" | "draft";

export const FORMAT_SIZE: Record<ArmyFormat, { back: number; front: number }> = {
  mini: { back: 4, front: 4 },
  midi: { back: 6, front: 6 },
  normal: { back: 8, front: 8 },
};

const MINI_KINDS = new Set<ArmyKind>(["mini-classic", "mini-court", "mini-bizarre", "mini-leap"]);
const MIDI_KINDS = new Set<ArmyKind>([
  "midi-classic",
  "midi-court",
  "midi-bizarre",
  "midi-leap",
  "midi-intrigue",
]);

export function parseArmyFormat(raw: unknown): ArmyFormat {
  return raw === "normal" || raw === "midi" || raw === "mini" ? raw : "mini";
}

export function formatOfArmyKind(kind: ArmyKind): ArmyFormat {
  if (MINI_KINDS.has(kind)) return "mini";
  if (MIDI_KINDS.has(kind)) return "midi";
  return "normal";
}

export function isFrontPiece(definitionId: string): boolean {
  return definitionId === "Pawn" || definitionId === "Lancer" || definitionId === "Defender" || definitionId === "Bomber";
}

export function isBackPiece(definitionId: string): boolean {
  return !isFrontPiece(definitionId);
}

export function pieceFitsRow(definitionId: string, row: "back" | "front"): boolean {
  return row === "front" ? isFrontPiece(definitionId) : isBackPiece(definitionId);
}

export function kingFile(format: ArmyFormat): number {
  return Math.floor(FORMAT_SIZE[format].back / 2);
}

export function draftRowNeed(format: ArmyFormat, row: "back" | "front"): number {
  const size = FORMAT_SIZE[format][row];
  return row === "back" ? size - 1 : size;
}

/** Lane boards fit mini and midi. Wider boards center a smaller army, or host a full one. */
export function defaultBoardForFormat(format: ArmyFormat): BoardKind {
  return format === "normal" ? "classic" : "lane11";
}
