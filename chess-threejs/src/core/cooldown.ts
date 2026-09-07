import type { UnitAction, UnitState } from "./types";

/** Locked until a reset trigger. Does not tick down. */
export const COOLDOWN_LOCKED = -1;

export function actionReady(action: UnitAction): boolean {
  return action.cooldown == null || action.cooldown === 0;
}

export function actionCooldownLabel(action: UnitAction): string | null {
  if (action.cooldown === COOLDOWN_LOCKED) return "∞";
  if (action.cooldown != null && action.cooldown > 0) return `CD ${action.cooldown}`;
  return null;
}

export function tickUnitCooldowns(unit: UnitState): void {
  for (const action of unit.actions ?? []) {
    if (action.cooldown == null || action.cooldown === COOLDOWN_LOCKED) continue;
    if (action.cooldown > 0) action.cooldown -= 1;
  }
}

export function resetActionCooldown(unit: UnitState, actionId: string, to: number | "max"): boolean {
  const action = unit.actions.find((a) => a.id === actionId);
  if (!action) return false;
  if (to === "max") action.cooldown = action.maxCooldown ?? 0;
  else action.cooldown = to;
  return true;
}

/** Apply this action's after-use cooldown and any reset trigger on another action. */
export function applyCooldownTriggers(unit: UnitState, action: UnitAction): void {
  if (action.cooldownAfterUse != null) action.cooldown = action.cooldownAfterUse;
  if (!action.resetActionId) return;
  const to = action.resetActionTo ?? "max";
  resetActionCooldown(unit, action.resetActionId, to);
}
