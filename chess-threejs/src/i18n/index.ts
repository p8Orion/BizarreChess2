import { en } from "./en";
import { es } from "./es";

export type Locale = "es" | "en";
export type MessageKey = keyof typeof en;
export type Vars = Record<string, string | number>;

const TABLES: Record<Locale, Record<string, string>> = { en, es };

let locale: Locale = "es";
const listeners = new Set<(next: Locale) => void>();

export function isLocale(value: unknown): value is Locale {
  return value === "es" || value === "en";
}

export function detectLocale(): Locale {
  const nav = typeof navigator !== "undefined" ? navigator.language.toLowerCase() : "es";
  return nav.startsWith("en") ? "en" : "es";
}

export function getLocale(): Locale {
  return locale;
}

export function setLocale(next: Locale): void {
  if (locale === next) return;
  locale = next;
  for (const fn of listeners) fn(next);
}

export function initLocale(saved?: unknown): Locale {
  locale = isLocale(saved) ? saved : detectLocale();
  return locale;
}

export function onLocaleChange(fn: (next: Locale) => void): () => void {
  listeners.add(fn);
  return () => listeners.delete(fn);
}

export function hasMessage(key: string): key is MessageKey {
  return Object.hasOwn(en, key);
}

export function t(key: string, vars?: Vars): string {
  const text = TABLES[locale][key] ?? TABLES.en[key] ?? key;
  if (!vars) return text;
  return text.replace(/\{(\w+)\}/g, (_, name: string) => (vars[name] == null ? `{${name}}` : String(vars[name])));
}

/** Translate a known key; leave unknown server leftovers as-is. */
export function tx(key: string | undefined | null, vars?: Vars): string {
  if (!key) return "";
  return hasMessage(key) ? t(key, vars) : key;
}

export function pieceName(id: string): string {
  return t(`piece.${id}.name`);
}

export function pieceBlurb(id: string): string {
  return t(`piece.${id}.blurb`);
}

export function itemName(kind: string): string {
  return t(`item.${kind}.name`);
}

export function itemDesc(kind: string): string {
  return t(`item.${kind}.desc`);
}

export function actionLabel(id: string): string {
  return t(`action.${id}`);
}

export function armyLabel(id: string): string {
  return t(`army.${id}`);
}

export function boardLabel(id: string): string {
  return t(`board.${id}`);
}

export function applyDomI18n(root: ParentNode = document): void {
  for (const el of root.querySelectorAll<HTMLElement>("[data-i18n]")) {
    const key = el.dataset.i18n;
    if (key) el.textContent = t(key);
  }
  for (const el of root.querySelectorAll<HTMLElement>("[data-i18n-placeholder]")) {
    const key = el.dataset.i18nPlaceholder;
    if (key && "placeholder" in el) (el as HTMLInputElement).placeholder = t(key);
  }
  for (const el of root.querySelectorAll<HTMLElement>("[data-i18n-title]")) {
    const key = el.dataset.i18nTitle;
    if (key) el.title = t(key);
  }
  for (const el of root.querySelectorAll<HTMLElement>("[data-i18n-aria]")) {
    const key = el.dataset.i18nAria;
    if (key) el.setAttribute("aria-label", t(key));
  }
}

const FLAG_SVG: Record<Locale, string> = {
  es: `<svg viewBox="0 0 36 24" aria-hidden="true"><rect width="36" height="8" fill="#74acdf"/><rect y="8" width="36" height="8" fill="#fff"/><rect y="16" width="36" height="8" fill="#74acdf"/><circle cx="18" cy="12" r="3.1" fill="#f6b40e"/><circle cx="18" cy="12" r="1.55" fill="#74acdf"/></svg>`,
  en: `<svg viewBox="0 0 36 24" aria-hidden="true"><rect width="36" height="24" fill="#bf0a30"/><rect y="2.4" width="36" height="2.4" fill="#fff"/><rect y="7.2" width="36" height="2.4" fill="#fff"/><rect y="12" width="36" height="2.4" fill="#fff"/><rect y="16.8" width="36" height="2.4" fill="#fff"/><rect y="21.6" width="36" height="2.4" fill="#fff"/><rect width="14.4" height="13.2" fill="#002868"/></svg>`,
};

export function mountLangSwitch(host: HTMLElement, onPick: (next: Locale) => void): void {
  host.replaceChildren();
  host.classList.add("lang-switch");
  host.setAttribute("role", "group");
  const paint = (): void => {
    host.setAttribute("aria-label", t("lang.es"));
    for (const btn of host.querySelectorAll<HTMLButtonElement>("button[data-locale]")) {
      const id = btn.dataset.locale;
      const key = id === "en" ? "lang.en" : "lang.es";
      btn.classList.toggle("is-on", id === locale);
      btn.title = t(key);
      btn.setAttribute("aria-label", t(key));
      btn.setAttribute("aria-pressed", id === locale ? "true" : "false");
    }
  };
  for (const id of ["es", "en"] as const) {
    const btn = document.createElement("button");
    btn.type = "button";
    btn.dataset.locale = id;
    btn.innerHTML = FLAG_SVG[id];
    btn.addEventListener("click", () => onPick(id));
    host.append(btn);
  }
  paint();
  onLocaleChange(paint);
}
