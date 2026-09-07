import { FORMAT_SIZE, isBackPiece, isFrontPiece } from "../core/format";
import { backRowWithKing, type Draft, type DraftPickRow, type PublicDraft } from "../core/draft";
import type { PlayerStyle } from "../core/colors";
import { DEFAULT_STYLE_P1, DEFAULT_STYLE_P2 } from "../core/colors";
import type { UnitDefinition } from "../core/types";
import { catalogPieces } from "../core/pieces";
import { pieceDisplayName, pieceMoveText } from "./pieceInfo";
import { pieceIconUrl, stubUnit } from "./pieceIcon";
import { createStyleFields } from "./styleFields";
import { t } from "../i18n";

export interface DraftHooks {
  onChoose: (piece: string) => void;
  onSelect: (piece: string) => void;
  selected?: string | null;
  styles?: [PlayerStyle, PlayerStyle];
  onStyle?: (playerId: 0 | 1, style: PlayerStyle) => void;
}

function pieceName(id: string): string {
  return pieceDisplayName(id);
}

function slotList(ids: string[], size: number): (string | null)[] {
  return Array.from({ length: size }, (_, i) => ids[i] ?? null);
}

function styleOf(styles: [PlayerStyle, PlayerStyle] | undefined, ownerId: 0 | 1): PlayerStyle {
  return styles?.[ownerId] ?? (ownerId === 0 ? DEFAULT_STYLE_P1 : DEFAULT_STYLE_P2);
}

function attachPortrait(img: HTMLImageElement, definitionId: string, ownerId: 0 | 1, style?: PlayerStyle): void {
  img.alt = pieceName(definitionId);
  void pieceIconUrl(definitionId, ownerId, style).then((url) => {
    if (url && img.isConnected) img.src = url;
  });
}

function phaseHint(draft: PublicDraft, hotseat: boolean, localPlayerId: number): string {
  if (draft.phase === "waiting") return t("draft.waiting");
  if (draft.phase === "done") return t("draft.done");
  const whose =
    hotseat || draft.currentPlayerId === localPlayerId
      ? hotseat
        ? t("draft.playerN", { n: draft.currentPlayerId + 1 })
        : t("draft.yourTurn")
      : t("draft.opponentTurn");
  if (draft.phase === "ban") {
    const left = draft.config.banCount * 2 - draft.bans.length;
    return left === 1 ? t("draft.banHint1", { whose }) : t("draft.banHint", { whose, n: left });
  }
  return draft.pickRow === "front" ? t("draft.pickFront", { whose }) : t("draft.pickBack", { whose });
}

function nextPickSlot(state: PublicDraft, ownerId: 0 | 1): { row: DraftPickRow; index: number } | null {
  if (state.phase !== "pick" || state.pickRow == null || state.currentPlayerId !== ownerId) return null;
  const row = state.pickRow;
  const format = state.config.format;
  if (row === "back") {
    const index = backRowWithKing(state.picks[ownerId].back, format).findIndex((id) => !id);
    return index >= 0 ? { row, index } : null;
  }
  const index = state.picks[ownerId].front.length;
  return index < FORMAT_SIZE[format].front ? { row, index } : null;
}

function renderSlot(id: string | null, ownerId: 0 | 1, locked = false, style?: PlayerStyle, next = false): HTMLElement {
  const slot = document.createElement("span");
  slot.className = "draft-slot";
  slot.classList.toggle("is-next", next);
  if (!id) {
    slot.classList.add("is-empty");
    slot.textContent = "—";
    return slot;
  }
  slot.classList.toggle("is-king", id === "King");
  slot.classList.toggle("is-fixed", locked || id === "King");
  const img = document.createElement("img");
  img.className = "draft-slot-portrait";
  attachPortrait(img, id, ownerId, style);
  const name = document.createElement("em");
  name.textContent = pieceName(id);
  slot.append(img, name);
  return slot;
}

function renderRoster(
  picks: PublicDraft["picks"][0],
  format: PublicDraft["config"]["format"],
  title: string,
  active: boolean,
  ownerId: 0 | 1,
  style?: PlayerStyle,
  next?: { row: DraftPickRow; index: number } | null
): HTMLElement {
  const size = FORMAT_SIZE[format];
  const col = document.createElement("div");
  col.className = "draft-roster";
  col.classList.toggle("is-active", active);
  const head = document.createElement("strong");
  head.textContent = title;
  col.append(head);
  for (const row of ["back", "front"] as DraftPickRow[]) {
    const label = document.createElement("div");
    label.className = "draft-row-label";
    label.textContent = row === "back" ? t("draft.pieces") : t("draft.pawns");
    const list = document.createElement("div");
    list.className = "draft-slots";
    const ids = row === "back" ? backRowWithKing(picks.back, format) : slotList(picks.front, size.front);
    ids.forEach((id, index) => {
      list.append(renderSlot(id, ownerId, id === "King", style, next?.row === row && next.index === index));
    });
    col.append(label, list);
  }
  return col;
}

function renderInspect(definitionId: string | null | undefined, ownerId: 0 | 1, style?: PlayerStyle): HTMLElement {
  const bar = document.createElement("div");
  bar.className = "draft-inspect";
  if (!definitionId) {
    bar.classList.add("is-empty");
    bar.textContent = t("draft.inspectEmpty");
    return bar;
  }
  const wrap = document.createElement("div");
  wrap.className = "draft-inspect-portrait";
  const img = document.createElement("img");
  attachPortrait(img, definitionId, ownerId, style);
  wrap.append(img);
  const body = document.createElement("div");
  body.className = "draft-inspect-body";
  const name = document.createElement("div");
  name.className = "draft-inspect-name";
  name.textContent = pieceDisplayName(definitionId);
  const move = document.createElement("div");
  move.className = "draft-inspect-move";
  move.textContent = pieceMoveText(stubUnit(definitionId, ownerId));
  body.append(name, move);
  bar.append(wrap, body);
  return bar;
}

function renderPieceButton(
  piece: UnitDefinition,
  opts: {
    banned: boolean;
    unavailable: boolean;
    selected: boolean;
    ownerId: 0 | 1;
    style?: PlayerStyle;
    onSelect: (id: string) => void;
  }
): HTMLButtonElement {
  const btn = document.createElement("button");
  btn.type = "button";
  btn.className = "draft-piece";
  const img = document.createElement("img");
  img.className = "draft-piece-portrait";
  attachPortrait(img, piece.id, opts.ownerId, opts.style);
  const name = document.createElement("span");
  name.textContent = piece.id === "King" ? t("draft.kingFixed") : pieceDisplayName(piece.id);
  btn.append(img, name);
  btn.classList.toggle("is-banned", opts.banned);
  btn.classList.toggle("is-unavailable", opts.unavailable);
  btn.classList.toggle("is-king", piece.id === "King");
  btn.classList.toggle("is-fixed", piece.id === "King");
  btn.classList.toggle("is-selected", opts.selected);
  btn.title = piece.id === "King" ? t("draft.kingTitle") : opts.unavailable ? t("draft.taken") : "";
  btn.addEventListener("click", () => opts.onSelect(piece.id));
  return btn;
}

function renderGroup(
  title: string,
  pieces: UnitDefinition[],
  state: PublicDraft,
  legalSet: Set<string>,
  rowActive: boolean,
  ownerId: 0 | 1,
  hooks: DraftHooks
): HTMLElement {
  const group = document.createElement("div");
  group.className = "draft-group";
  group.classList.toggle("is-dim", state.phase === "pick" && !rowActive);
  const head = document.createElement("div");
  head.className = "draft-group-label";
  head.textContent = title;
  const grid = document.createElement("div");
  grid.className = "draft-pool";
  const uniq = state.config.uniqueness !== "free";
  for (const piece of pieces) {
    const banned = state.bans.includes(piece.id);
    const inRow =
      state.phase !== "pick" ||
      (state.pickRow === "front" ? isFrontPiece(piece.id) : isBackPiece(piece.id));
    const unavailable =
      uniq && state.phase === "pick" && inRow && piece.id !== "King" && !banned && !legalSet.has(piece.id);
    grid.append(
      renderPieceButton(piece, {
        banned,
        unavailable,
        selected: hooks.selected === piece.id,
        ownerId,
        style: styleOf(hooks.styles, ownerId),
        onSelect: hooks.onSelect,
      })
    );
  }
  group.append(head, grid);
  return group;
}

export function renderDraft(root: HTMLElement, draft: Draft | PublicDraft, localPlayerId: number, hotseat: boolean, hooks: DraftHooks): void {
  const state = "toPublic" in draft ? draft.toPublic() : draft;
  const engine = "legalBans" in draft ? draft : null;
  const activeId = state.currentPlayerId;
  const mine = hotseat || activeId === localPlayerId;
  const legal = engine
    ? state.phase === "ban"
      ? engine.legalBans()
      : engine.legalPicks(activeId)
    : [];
  const legalSet = new Set(legal);

  root.replaceChildren();
  root.hidden = false;

  root.append(renderInspect(hooks.selected, localPlayerId as 0 | 1, styleOf(hooks.styles, localPlayerId as 0 | 1)));

  const status = document.createElement("p");
  status.className = "draft-hint";
  status.textContent = phaseHint(state, hotseat, localPlayerId);
  root.append(status);

  if (hooks.styles && hooks.onStyle) {
    const colors = document.createElement("div");
    colors.className = "colors draft-colors";
    if (hotseat) {
      colors.append(
        createStyleFields(hooks.styles[0], (style) => hooks.onStyle?.(0, style), "P1"),
        createStyleFields(hooks.styles[1], (style) => hooks.onStyle?.(1, style), "P2")
      );
    } else {
      colors.append(
        createStyleFields(hooks.styles[localPlayerId], (style) => hooks.onStyle?.(localPlayerId as 0 | 1, style), t("draft.colors"))
      );
    }
    root.append(colors);
  }

  const bans = document.createElement("div");
  bans.className = "draft-bans";
  const banLabel = document.createElement("span");
  banLabel.textContent = state.bans.length ? t("draft.banned") : t("draft.noBans");
  bans.append(banLabel);
  for (const id of state.bans) {
    const tag = document.createElement("span");
    tag.className = "draft-ban";
    const img = document.createElement("img");
    img.className = "draft-ban-portrait";
    attachPortrait(img, id, activeId, styleOf(hooks.styles, activeId));
    const name = document.createElement("span");
    name.textContent = pieceName(id);
    tag.append(img, name);
    bans.append(tag);
  }
  root.append(bans);

  const rosters = document.createElement("div");
  rosters.className = "draft-rosters";
  const p1Active = state.phase !== "waiting" && state.phase !== "done" && state.currentPlayerId === 0;
  const p2Active = state.phase !== "waiting" && state.phase !== "done" && state.currentPlayerId === 1;
  const p1Title = hotseat || localPlayerId === 0 ? t("hud.player1") : t("draft.opponent");
  const p2Title = hotseat || localPlayerId === 1 ? t("hud.player2") : t("draft.opponent");
  rosters.append(
    renderRoster(state.picks[0], state.config.format, p1Title, p1Active, 0, styleOf(hooks.styles, 0), nextPickSlot(state, 0)),
    renderRoster(state.picks[1], state.config.format, p2Title, p2Active, 1, styleOf(hooks.styles, 1), nextPickSlot(state, 1))
  );
  if (!hotseat) {
    const first = rosters.children[localPlayerId === 0 ? 0 : 1] as HTMLElement;
    const second = rosters.children[localPlayerId === 0 ? 1 : 0] as HTMLElement;
    if (first && second) {
      first.querySelector("strong")!.textContent = t("draft.you");
      second.querySelector("strong")!.textContent = t("draft.opponent");
    }
  }
  root.append(rosters);

  const catalog = catalogPieces();
  const canAct = state.phase === "ban" || state.phase === "pick";
  const groups = document.createElement("div");
  groups.className = "draft-groups";
  groups.append(
    renderGroup(
      t("draft.pieces"),
      catalog.filter((piece) => isBackPiece(piece.id)),
      state,
      legalSet,
      state.phase !== "pick" || state.pickRow === "back",
      activeId,
      hooks
    ),
    renderGroup(
      t("draft.pawns"),
      catalog.filter((piece) => isFrontPiece(piece.id)),
      state,
      legalSet,
      state.phase !== "pick" || state.pickRow === "front",
      activeId,
      hooks
    )
  );
  if (state.phase === "waiting" || state.phase === "done") groups.hidden = true;
  root.append(groups);

  const act = document.createElement("button");
  act.type = "button";
  act.id = "btn-draft-act";
  act.classList.toggle("is-ban", state.phase === "ban");
  act.textContent = state.phase === "ban" ? t("draft.ban") : t("draft.pick");
  const selected = hooks.selected;
  const canConfirm =
    canAct &&
    mine &&
    !!selected &&
    selected !== "King" &&
    legalSet.has(selected) &&
    !state.bans.includes(selected);
  act.disabled = !canConfirm;
  if (canConfirm && selected) act.addEventListener("click", () => hooks.onChoose(selected));
  if (state.phase === "waiting" || state.phase === "done") act.hidden = true;
  root.append(act);
}
