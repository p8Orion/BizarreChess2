import type { ArmyKind } from "./pieces";
import type { BoardKind } from "./types";

export type ArmyFormat = "normal" | "mini";
export type MatchMode = "normal" | "draft";

export const FORMAT_SIZE: Record<ArmyFormat, { back: number; front: number }> = {
  mini: { back: 4, front: 4 },
  normal: { back: 8, front: 8 },
};

const MINI_KINDS = new Set<ArmyKind>(["mini-classic", "mini-court", "mini-bizarre", "mini-leap"]);

export function formatOfArmyKind(kind: ArmyKind): ArmyFormat {
  return MINI_KINDS.has(kind) ? "mini" : "normal";
}

export function isFrontPiece(definitionId: string): boolean {
  return definitionId === "Pawn" || definitionId === "Lancer" || definitionId === "Defender" || definitionId === "Bomber";
}

export function isBackPiece(definitionId: string): boolean {
  return !isFrontPiece(definitionId);
}

export function kingFile(format: ArmyFormat): number {
  return Math.floor(FORMAT_SIZE[format].back / 2);
}

export function draftRowNeed(format: ArmyFormat, row: "back" | "front"): number {
  const size = FORMAT_SIZE[format][row];
  return row === "back" ? size - 1 : size;
}

/** Lane boards are mini-only. Wider boards can host a mini army centered, or a full one. */
export function defaultBoardForFormat(format: ArmyFormat): BoardKind {
  return format === "mini" ? "lane11" : "classic";
}
