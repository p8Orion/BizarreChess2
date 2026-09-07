import { FORMAT_SIZE, isBackPiece, isFrontPiece } from "../core/format";
import { backRowWithKing, type Draft, type DraftPickRow, type PublicDraft } from "../core/draft";
import type { UnitDefinition } from "../core/types";
import { catalogPieces } from "../core/pieces";
import { pieceIconUrl } from "./pieceIcon";

export interface DraftHooks {
  onChoose: (piece: string) => void;
}

function pieceName(id: string): string {
  return catalogPieces().find((piece) => piece.id === id)?.displayName ?? id;
}

function slotList(ids: string[], size: number): (string | null)[] {
  return Array.from({ length: size }, (_, i) => ids[i] ?? null);
}

function attachPortrait(img: HTMLImageElement, definitionId: string, ownerId: 0 | 1): void {
  img.alt = pieceName(definitionId);
  void pieceIconUrl(definitionId, ownerId).then((url) => {
    if (url && img.isConnected) img.src = url;
  });
}

function phaseHint(draft: PublicDraft, hotseat: boolean, localPlayerId: number): string {
  if (draft.phase === "waiting") return "Waiting for opponent to join…";
  if (draft.phase === "done") return "Draft complete.";
  const whose =
    hotseat || draft.currentPlayerId === localPlayerId
      ? hotseat
        ? `Player ${draft.currentPlayerId + 1}`
        : "Your turn"
      : "Opponent's turn";
  if (draft.phase === "ban") {
    const left = draft.config.banCount * 2 - draft.bans.length;
    return `${whose} — ban a piece (${left} ban${left === 1 ? "" : "s"} left)`;
  }
  const row = draft.pickRow === "front" ? "front row (pawns)" : "back row (pieces)";
  return `${whose} — pick ${row}`;
}

function renderSlot(id: string | null, ownerId: 0 | 1, locked = false): HTMLElement {
  const slot = document.createElement("span");
  slot.className = "draft-slot";
  if (!id) {
    slot.classList.add("is-empty");
    slot.textContent = "—";
    return slot;
  }
  slot.classList.toggle("is-king", id === "King");
  slot.classList.toggle("is-fixed", locked || id === "King");
  const img = document.createElement("img");
  img.className = "draft-slot-portrait";
  attachPortrait(img, id, ownerId);
  const name = document.createElement("em");
  name.textContent = pieceName(id);
  slot.append(img, name);
  return slot;
}

function renderRoster(picks: PublicDraft["picks"][0], format: PublicDraft["config"]["format"], title: string, active: boolean, ownerId: 0 | 1): HTMLElement {
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
    label.textContent = row === "back" ? "Pieces" : "Pawns";
    const list = document.createElement("div");
    list.className = "draft-slots";
    const ids = row === "back" ? backRowWithKing(picks.back, format) : slotList(picks.front, size.front);
    for (const id of ids) list.append(renderSlot(id, ownerId, id === "King"));
    col.append(label, list);
  }
  return col;
}

function renderPieceButton(
  piece: UnitDefinition,
  opts: {
    banned: boolean;
    allowed: boolean;
    unavailable: boolean;
    canAct: boolean;
    mine: boolean;
    ownerId: 0 | 1;
    onChoose: (id: string) => void;
  }
): HTMLButtonElement {
  const btn = document.createElement("button");
  btn.type = "button";
  btn.className = "draft-piece";
  const img = document.createElement("img");
  img.className = "draft-piece-portrait";
  attachPortrait(img, piece.id, opts.ownerId);
  const name = document.createElement("span");
  name.textContent = piece.id === "King" ? "King (fijo)" : piece.displayName;
  btn.append(img, name);
  btn.classList.toggle("is-banned", opts.banned);
  btn.classList.toggle("is-unavailable", opts.unavailable);
  btn.classList.toggle("is-king", piece.id === "King");
  btn.classList.toggle("is-fixed", piece.id === "King");
  const clickable = piece.id !== "King" && opts.allowed && opts.mine && opts.canAct && !opts.unavailable;
  btn.disabled = !clickable;
  btn.title = piece.id === "King" ? "Both armies start with a King" : opts.unavailable ? "Already taken" : "";
  if (clickable) {
    btn.addEventListener("click", () => opts.onChoose(piece.id));
  }
  return btn;
}

function renderGroup(
  title: string,
  pieces: UnitDefinition[],
  state: PublicDraft,
  legalSet: Set<string>,
  canAct: boolean,
  mine: boolean,
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
        allowed: legalSet.has(piece.id),
        unavailable,
        canAct,
        mine,
        ownerId,
        onChoose: hooks.onChoose,
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

  const status = document.createElement("p");
  status.className = "draft-hint";
  status.textContent = phaseHint(state, hotseat, localPlayerId);
  root.append(status);

  const bans = document.createElement("div");
  bans.className = "draft-bans";
  const banLabel = document.createElement("span");
  banLabel.textContent = state.bans.length ? "Banned" : "No bans yet";
  bans.append(banLabel);
  for (const id of state.bans) {
    const tag = document.createElement("span");
    tag.className = "draft-ban";
    const img = document.createElement("img");
    img.className = "draft-ban-portrait";
    attachPortrait(img, id, activeId);
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
  const p1Title = hotseat || localPlayerId === 0 ? "Player 1" : "Opponent";
  const p2Title = hotseat || localPlayerId === 1 ? "Player 2" : "Opponent";
  rosters.append(
    renderRoster(state.picks[0], state.config.format, p1Title, p1Active, 0),
    renderRoster(state.picks[1], state.config.format, p2Title, p2Active, 1)
  );
  if (!hotseat) {
    const first = rosters.children[localPlayerId === 0 ? 0 : 1] as HTMLElement;
    const second = rosters.children[localPlayerId === 0 ? 1 : 0] as HTMLElement;
    if (first && second) {
      first.querySelector("strong")!.textContent = "You";
      second.querySelector("strong")!.textContent = "Opponent";
    }
  }
  root.append(rosters);

  const catalog = catalogPieces();
  const canAct = state.phase === "ban" || state.phase === "pick";
  const groups = document.createElement("div");
  groups.className = "draft-groups";
  groups.append(
    renderGroup(
      "Piezas",
      catalog.filter((piece) => isBackPiece(piece.id)),
      state,
      legalSet,
      canAct,
      mine,
      state.phase !== "pick" || state.pickRow === "back",
      activeId,
      hooks
    ),
    renderGroup(
      "Peones",
      catalog.filter((piece) => isFrontPiece(piece.id)),
      state,
      legalSet,
      canAct,
      mine,
      state.phase !== "pick" || state.pickRow === "front",
      activeId,
      hooks
    )
  );
  if (state.phase === "waiting" || state.phase === "done") groups.hidden = true;
  root.append(groups);
}
