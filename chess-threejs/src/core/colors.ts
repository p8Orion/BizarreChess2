export const DEFAULT_P1 = "#f2ebd9";
export const DEFAULT_P2 = "#33261f";
export const DEFAULT_GOLD = "#c4a15a";

export const DEFAULT_STYLE_P1: PlayerStyle = {
  primary: DEFAULT_P1,
  secondary: DEFAULT_GOLD,
  pattern: "fleur",
};

export const DEFAULT_STYLE_P2: PlayerStyle = {
  primary: DEFAULT_P2,
  secondary: "#8a5a2b",
  pattern: "dot",
};

export const PATTERN_IDS = ["fleur", "heart", "dot", "check", "diamond", "cross"] as const;
export type PatternId = (typeof PATTERN_IDS)[number];

export const PATTERN_LABELS: Record<PatternId, string> = {
  fleur: "Fleur-de-lis",
  heart: "Heart",
  dot: "Dot",
  check: "Check",
  diamond: "Diamond",
  cross: "Cross",
};

export interface PlayerStyle {
  primary: string;
  secondary: string;
  pattern: PatternId;
}

export function normalizeHex(value: string, fallback: string): string {
  const v = value.trim();
  if (/^#[0-9a-fA-F]{6}$/.test(v)) return v.toLowerCase();
  if (/^#[0-9a-fA-F]{3}$/.test(v)) {
    return `#${v[1]}${v[1]}${v[2]}${v[2]}${v[3]}${v[3]}`.toLowerCase();
  }
  return fallback;
}

export function isPatternId(value: string): value is PatternId {
  return (PATTERN_IDS as readonly string[]).includes(value);
}

export function normalizeStyle(raw: unknown, fallback: PlayerStyle): PlayerStyle {
  if (typeof raw === "string") {
    return { primary: normalizeHex(raw, fallback.primary), secondary: fallback.secondary, pattern: fallback.pattern };
  }
  if (!raw || typeof raw !== "object") return { ...fallback };
  const o = raw as Partial<PlayerStyle>;
  return {
    primary: normalizeHex(o.primary ?? fallback.primary, fallback.primary),
    secondary: normalizeHex(o.secondary ?? fallback.secondary, fallback.secondary),
    pattern: o.pattern && isPatternId(o.pattern) ? o.pattern : fallback.pattern,
  };
}

export function normalizeStyles(raw?: unknown): [PlayerStyle, PlayerStyle] {
  const a = Array.isArray(raw) ? raw : [];
  return [normalizeStyle(a[0], DEFAULT_STYLE_P1), normalizeStyle(a[1], DEFAULT_STYLE_P2)];
}

function hslHex(h: number, s: number, l: number): string {
  const c = (1 - Math.abs(2 * l - 1)) * s;
  const x = c * (1 - Math.abs(((h / 60) % 2) - 1));
  const m = l - c / 2;
  let r = 0, g = 0, b = 0;
  if (h < 60) [r, g, b] = [c, x, 0];
  else if (h < 120) [r, g, b] = [x, c, 0];
  else if (h < 180) [r, g, b] = [0, c, x];
  else if (h < 240) [r, g, b] = [0, x, c];
  else if (h < 300) [r, g, b] = [x, 0, c];
  else [r, g, b] = [c, 0, x];
  const hex = (n: number) => Math.round((n + m) * 255).toString(16).padStart(2, "0");
  return `#${hex(r)}${hex(g)}${hex(b)}`;
}

export function randomStyle(ownerId = 0): PlayerStyle {
  const hue = (ownerId * 160 + Math.random() * 80 + Math.random() * 360) % 360;
  const primary = hslHex(hue, 0.42 + Math.random() * 0.2, ownerId === 0 ? 0.62 : 0.28);
  const secondary = hslHex((hue + 130 + Math.random() * 50) % 360, 0.62, 0.48);
  const pattern = PATTERN_IDS[Math.floor(Math.random() * PATTERN_IDS.length)];
  return { primary, secondary, pattern };
}

/** Light pastel wash of a player primary, for portrait backdrops. */
export function portraitWash(primary: string): string {
  const n = primary.replace("#", "");
  const r = parseInt(n.slice(0, 2), 16) / 255;
  const g = parseInt(n.slice(2, 4), 16) / 255;
  const b = parseInt(n.slice(4, 6), 16) / 255;
  const max = Math.max(r, g, b);
  const min = Math.min(r, g, b);
  const l = (max + min) / 2;
  const d = max - min;
  let h = 0;
  let s = 0;
  if (d > 1e-6) {
    s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
    if (max === r) h = ((g - b) / d + (g < b ? 6 : 0)) / 6;
    else if (max === g) h = ((b - r) / d + 2) / 6;
    else h = ((r - g) / d + 4) / 6;
  }
  return hslHex(h * 360, Math.min(0.26, s * 0.42 + 0.05), 0.9);
}

