import type { UnitState } from "./types";

export function isShadow(unit: UnitState): boolean {
  return !!unit.inShadow;
}

export function principalOn(all: UnitState[], nodeId: number): UnitState | undefined {
  return all.find((u) => u.isAlive && u.currentNodeId === nodeId && !u.inShadow);
}

export function hasPrincipal(all: UnitState[], nodeId: number): boolean {
  return all.some((u) => u.isAlive && !u.inShadow && u.currentNodeId === nodeId);
}

export function hasEnemyPrincipal(all: UnitState[], nodeId: number, ownerId: number): boolean {
  return all.some((u) => u.isAlive && !u.inShadow && u.currentNodeId === nodeId && u.ownerId !== ownerId);
}

export function hasOwnShadow(all: UnitState[], nodeId: number, ownerId: number, exceptUnitId?: number): boolean {
  return all.some(
    (u) =>
      u.isAlive &&
      u.inShadow &&
      u.currentNodeId === nodeId &&
      u.ownerId === ownerId &&
      (exceptUnitId == null || u.unitId !== exceptUnitId)
  );
}

/** Hidden units exist only for their owner. `viewerId` omitted = local/hotseat, show all. */
export function unitVisibleTo(unit: UnitState, viewerId?: number): boolean {
  if (!unit.inShadow) return true;
  if (viewerId == null) return true;
  return unit.ownerId === viewerId;
}

export function filterUnitsForViewer(units: UnitState[], viewerId?: number): UnitState[] {
  if (viewerId == null) return units;
  return units.filter((unit) => unitVisibleTo(unit, viewerId));
}
