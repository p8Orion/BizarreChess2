export type TimeControlId = "5+3" | "24h";

export interface TimeControl {
  id: TimeControlId;
  label: string;
  initialMs: number;
  incrementMs: number;
  /** Clock resets to initialMs when the turn starts. */
  perMove: boolean;
}

export const TIME_CONTROLS: Record<TimeControlId, TimeControl> = {
  "5+3": { id: "5+3", label: "5+3", initialMs: 5 * 60 * 1000, incrementMs: 3000, perMove: false },
  "24h": { id: "24h", label: "24 h", initialMs: 24 * 60 * 60 * 1000, incrementMs: 0, perMove: true },
};

export interface PublicClock {
  control: TimeControl;
  remainingMs: [number, number];
  runningPlayerId: number | null;
  updatedAt: number | null;
}

export function normalizeTimeControlId(raw: unknown): TimeControlId {
  return raw === "24h" ? "24h" : "5+3";
}

export function createClock(id: TimeControlId = "5+3"): PublicClock {
  const control = TIME_CONTROLS[normalizeTimeControlId(id)];
  return {
    control: { ...control },
    remainingMs: [control.initialMs, control.initialMs],
    runningPlayerId: null,
    updatedAt: null,
  };
}

export function startClock(clock: PublicClock, playerId: number, now: number): void {
  clock.runningPlayerId = playerId;
  clock.updatedAt = now;
}

export function syncClock(clock: PublicClock, now: number): void {
  if (clock.runningPlayerId == null || clock.updatedAt == null) return;
  const pid = clock.runningPlayerId;
  clock.remainingMs[pid] = Math.max(0, clock.remainingMs[pid] - (now - clock.updatedAt));
  clock.updatedAt = now;
}

export function stopClock(clock: PublicClock, now: number): void {
  syncClock(clock, now);
  clock.runningPlayerId = null;
  clock.updatedAt = null;
}

export function afterTurn(clock: PublicClock, previousPlayerId: number, nextPlayerId: number, now: number): void {
  syncClock(clock, now);
  if (clock.control.perMove) clock.remainingMs[nextPlayerId] = clock.control.initialMs;
  else clock.remainingMs[previousPlayerId] += clock.control.incrementMs;
  clock.runningPlayerId = nextPlayerId;
  clock.updatedAt = now;
}

export function flaggedPlayer(clock: PublicClock, now: number): number | null {
  syncClock(clock, now);
  if (clock.runningPlayerId == null) return null;
  return clock.remainingMs[clock.runningPlayerId] <= 0 ? clock.runningPlayerId : null;
}

export function remainingNow(clock: PublicClock, playerId: number, now: number): number {
  let ms = clock.remainingMs[playerId];
  if (clock.runningPlayerId === playerId && clock.updatedAt != null) ms -= now - clock.updatedAt;
  return Math.max(0, ms);
}

export function formatClock(ms: number): string {
  const total = Math.max(0, Math.ceil(ms / 1000));
  const hours = Math.floor(total / 3600);
  const minutes = Math.floor((total % 3600) / 60);
  const seconds = total % 60;
  if (hours > 0) return `${hours}:${String(minutes).padStart(2, "0")}:${String(seconds).padStart(2, "0")}`;
  if (ms < 10_000 && ms > 0) return `${(ms / 1000).toFixed(1)}`;
  return `${minutes}:${String(seconds).padStart(2, "0")}`;
}
