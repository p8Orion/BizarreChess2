import { catalogStartingItem } from "../core/items";
import { catalogPieces } from "../core/pieces";
import { activeArmy, armyRecord, armyStats, canDeleteArmy, canEditArmy } from "../persist/store";
import type { PersistedPiece, PersistedSlot, PersistedUser } from "../persist/types";

const FILES = "abcdefghij";

export interface ArmyEditorHooks {
  onRename: (name: string) => void;
  onSetSlot: (row: "back" | "front", x: number, definitionId: string | null) => void;
  onDelete: () => void;
}

function appendTag(parent: HTMLElement, text: string, kind?: "item" | "king" | "skill"): void {
  const tag = document.createElement("span");
  tag.className = kind ? `editor-tag is-${kind}` : "editor-tag";
  tag.textContent = text;
  parent.append(tag);
}

function fillProps(el: HTMLElement, slot: PersistedSlot | undefined, pieces: PersistedPiece[]): void {
  el.replaceChildren();
  if (!slot) return;
  const def = catalogPieces().find((p) => p.id === slot.definitionId);
  if (def?.isKing) appendTag(el, "King", "king");
  if (def?.canPromote) appendTag(el, "Promotes");
  for (const skill of def?.skills ?? []) appendTag(el, skill.id, "skill");
  for (const action of def?.actions ?? []) appendTag(el, action.label || action.id);
  const inst = slot.pieceId ? pieces.find((p) => p.id === slot.pieceId) : undefined;
  if (inst?.item) appendTag(el, inst.item.displayName, "item");
  else {
    const innate = catalogStartingItem(slot.definitionId);
    if (innate) appendTag(el, innate.displayName, "item");
    else if (slot.pieceId) appendTag(el, "Veteran");
  }
}

function fillPieceSelect(select: HTMLSelectElement, current: string, locked: boolean): void {
  select.replaceChildren();
  const empty = document.createElement("option");
  empty.value = "";
  empty.textContent = "— empty —";
  select.append(empty);
  for (const def of catalogPieces()) {
    const opt = document.createElement("option");
    opt.value = def.id;
    opt.textContent = def.displayName;
    select.append(opt);
  }
  select.value = current;
  select.disabled = locked;
}

export function renderArmyEditor(root: HTMLElement, user: PersistedUser, hooks: ArmyEditorHooks): void {
  const army = activeArmy(user);
  const locked = !canEditArmy(army);
  const width = Math.max(4, ...army.slots.map((s) => s.x + 1));

  root.replaceChildren();
  root.hidden = false;
  root.classList.toggle("is-locked", locked);

  const head = document.createElement("div");
  head.className = "editor-head";
  const name = document.createElement("input");
  name.type = "text";
  name.id = "army-name";
  name.value = army.name;
  name.maxLength = 48;
  name.disabled = locked;
  name.addEventListener("change", () => hooks.onRename(name.value));
  const record = document.createElement("span");
  record.className = "editor-record";
  record.textContent = armyRecord(army);
  const stats = armyStats(army);
  const statRow = document.createElement("div");
  statRow.className = "editor-stats";
  for (const [label, value] of [
    ["Games", stats.games],
    ["Wins", stats.wins],
    ["Draws", stats.draws],
    ["Losses", stats.losses],
  ] as const) {
    const cell = document.createElement("div");
    cell.className = "editor-stat";
    const num = document.createElement("strong");
    num.textContent = String(value);
    const caption = document.createElement("span");
    caption.textContent = label;
    cell.append(num, caption);
    statRow.append(cell);
  }
  head.append(name, record, statRow);
  root.append(head);

  if (locked) {
    const lock = document.createElement("p");
    lock.className = "editor-lock";
    lock.textContent = army.isDefault
      ? "Default army — anyone can pick it. Copy it to make your own."
      : "This army already fought. Copy it to edit a fresh roster.";
    root.append(lock);
  }

  const ranks = document.createElement("div");
  ranks.className = "editor-ranks";
  for (const row of ["back", "front"] as const) {
    const block = document.createElement("div");
    block.className = "editor-rank";
    const label = document.createElement("div");
    label.className = "editor-rank-label";
    label.textContent = row === "back" ? "Back" : "Front";
    const list = document.createElement("div");
    list.className = "editor-list";
    for (let x = 0; x < width; x++) {
      const slot = army.slots.find((s) => s.row === row && s.x === x);
      const cell = document.createElement("label");
      cell.className = "editor-cell";
      if (slot?.pieceId) cell.classList.add("has-instance");
      const file = document.createElement("span");
      file.className = "editor-file";
      file.textContent = FILES[x] ?? String(x + 1);
      const select = document.createElement("select");
      select.setAttribute("aria-label", `${row} ${file.textContent}`);
      fillPieceSelect(select, slot?.definitionId ?? "", locked);
      select.addEventListener("change", () => hooks.onSetSlot(row, x, select.value || null));
      const props = document.createElement("span");
      props.className = "editor-props";
      fillProps(props, slot, user.pieces);
      cell.append(file, select, props);
      list.append(cell);
    }
    block.append(label, list);
    ranks.append(block);
  }
  root.append(ranks);

  const del = document.createElement("button");
  del.type = "button";
  del.className = "ghost editor-delete";
  const canDelete = canDeleteArmy(army, user.armies);
  del.disabled = !canDelete;
  del.textContent = army.isDefault ? "Default armies can't be deleted" : canDelete ? "Delete army" : "Can't delete the last army";
  let pending = false;
  del.addEventListener("click", () => {
    if (!canDelete) return;
    if (!pending) {
      pending = true;
      del.textContent = "Confirm delete";
      del.classList.add("is-danger");
      return;
    }
    hooks.onDelete();
  });
  root.append(del);
}
