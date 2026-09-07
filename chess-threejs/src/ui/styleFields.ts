import {
  DEFAULT_GOLD,
  PATTERN_IDS,
  isPatternId,
  normalizeHex,
  type PlayerStyle,
} from "../core/colors";
import { t } from "../i18n";

export function createStyleFields(
  style: PlayerStyle,
  onChange: (next: PlayerStyle) => void,
  label: string,
  enabled = true
): HTMLElement {
  const row = document.createElement("div");
  row.className = "player-style";
  const tag = document.createElement("span");
  tag.textContent = label;
  const primary = document.createElement("input");
  primary.type = "color";
  primary.title = t("menu.primary");
  primary.value = style.primary;
  primary.disabled = !enabled;
  const secondary = document.createElement("input");
  secondary.type = "color";
  secondary.title = t("menu.secondary");
  secondary.value = style.secondary;
  secondary.disabled = !enabled;
  const pattern = document.createElement("select");
  pattern.setAttribute("aria-label", t("menu.p1Pattern"));
  pattern.disabled = !enabled;
  for (const id of PATTERN_IDS) {
    const opt = document.createElement("option");
    opt.value = id;
    opt.textContent = t(`pattern.${id}`);
    pattern.append(opt);
  }
  pattern.value = style.pattern;
  const emit = () =>
    onChange({
      primary: normalizeHex(primary.value, style.primary),
      secondary: normalizeHex(secondary.value, DEFAULT_GOLD),
      pattern: isPatternId(pattern.value) ? pattern.value : style.pattern,
    });
  primary.addEventListener("change", emit);
  secondary.addEventListener("change", emit);
  pattern.addEventListener("change", emit);
  const wrapPrimary = document.createElement("label");
  wrapPrimary.title = t("menu.primary");
  wrapPrimary.append(primary);
  const wrapSecondary = document.createElement("label");
  wrapSecondary.title = t("menu.secondary");
  wrapSecondary.append(secondary);
  row.append(tag, wrapPrimary, wrapSecondary, pattern);
  return row;
}
