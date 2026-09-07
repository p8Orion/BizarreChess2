import {
  DEFAULT_GOLD,
  DEFAULT_P1,
  DEFAULT_P2,
  PATTERN_IDS,
  PATTERN_LABELS,
  isPatternId,
  normalizeHex,
  type PlayerStyle,
} from "./core/colors";
import { Draft, DRAFT_PICK_MODES, clampBanCount, normalizeDraftConfig, type DraftPickModeId, type DraftUniq } from "./core/draft";
import { defaultBoardForFormat, type ArmyFormat, type MatchMode } from "./core/format";
import { Game, actionNotice, previewNodesForAction, type ActionExecution, type MoveExecution } from "./core/gameState";
import { pickupActionLabel } from "./core/items";
import { boardsForFormat, boardSupportsFormat } from "./core/board";
import { clampDecorAmount } from "./core/boardDecor";
import { allTargets, GameEndReason, GamePhase, PublicState, UnitState } from "./core/types";
import { movesForUnit, transmutationTargets } from "./core/validator";
import { NetClient } from "./net/client";
import type { BoardKind } from "./net/protocol";
import {
  activeArmy,
  armyOptions,
  canEditArmy,
  deleteArmy,
  duplicateArmy,
  loadUser,
  recordArmyFight,
  renameArmy,
  resolveArmy,
  setActiveArmy,
  setArmySlot,
  setGuestArmy,
  setUserSettings,
  setUserStyles,
  syncArmyFromUnits,
} from "./persist/store";
import { GameView, boardFromState, itemOnNode, unitOnNode, type InspectHover } from "./render/GameView";
import { PortraitView } from "./render/PortraitView";
import { renderArmyEditor } from "./ui/armyEditor";
import { renderDraft } from "./ui/draft";
import { bindItemIconLoader, itemIconUrl } from "./ui/itemIcon";
import { bindPieceIconLoader } from "./ui/pieceIcon";
import { pieceDisplayName, pieceMoveText } from "./ui/pieceInfo";

const canvas = document.querySelector<HTMLCanvasElement>("#game")!;
const menuStatusEl = document.querySelector("#menu-status")!;
const gameHudEl = document.querySelector<HTMLElement>("#game-hud")!;
const portraitWrap = document.querySelector<HTMLElement>(".hud-portrait-wrap")!;
const portraitCanvas = document.querySelector<HTMLCanvasElement>("#hud-portrait")!;
const hoverSlotEl = document.querySelector<HTMLElement>("#hud-hover-slot")!;
const hoverPortraitWrap = document.querySelector<HTMLElement>("#hud-hover-portrait-wrap")!;
const hoverPortraitCanvas = document.querySelector<HTMLCanvasElement>("#hud-hover-portrait")!;
const hudNameEl = document.querySelector("#hud-name")!;
const hudMoveEl = document.querySelector("#hud-move")!;
const hudHoverNameEl = document.querySelector("#hud-hover-name")!;
const hudHoverMoveEl = document.querySelector("#hud-hover-move")!;
const selectedActionsEl = document.querySelector<HTMLElement>("#hud-selected-actions")!;
const hoverActionsEl = document.querySelector<HTMLElement>("#hud-hover-actions")!;
const hudTurnEl = document.querySelector("#hud-turn")!;
const hudNoticeEl = document.querySelector<HTMLElement>("#hud-notice")!;
const netInfoEl = document.querySelector("#net-info")!;
const joinCodeEl = document.querySelector<HTMLInputElement>("#join-code")!;
const armyEl = document.querySelector<HTMLSelectElement>("#army-kind")!;
const armyP2El = document.querySelector<HTMLSelectElement>("#army-p2")!;
const armyEditorBtn = document.querySelector<HTMLButtonElement>("#btn-army-editor")!;
const armyEditorWindow = document.querySelector<HTMLElement>("#army-editor-window")!;
const armyEditorTitle = document.querySelector("#army-editor-title")!;
const armyEditorEl = document.querySelector<HTMLElement>("#army-editor")!;
const settingsBtn = document.querySelector<HTMLButtonElement>("#btn-settings")!;
const settingsPanel = document.querySelector<HTMLElement>("#settings-panel")!;
const autoPickupEl = document.querySelector<HTMLInputElement>("#setting-auto-pickup")!;
const boardEl = document.querySelector<HTMLSelectElement>("#board-kind")!;
const itemAmountEl = document.querySelector<HTMLInputElement>("#item-amount")!;
const itemAmountValEl = document.querySelector<HTMLElement>("#item-amount-val")!;
const obstacleAmountEl = document.querySelector<HTMLInputElement>("#obstacle-amount")!;
const obstacleAmountValEl = document.querySelector<HTMLElement>("#obstacle-amount-val")!;
const obstacleSymmetricEl = document.querySelector<HTMLInputElement>("#obstacle-symmetric")!;
const armyRostersEl = document.querySelector<HTMLElement>("#army-rosters")!;
const matchModeEl = document.querySelector<HTMLElement>("#match-mode")!;
const armyFormatEl = document.querySelector<HTMLElement>("#army-format")!;
const draftOptionsEl = document.querySelector<HTMLElement>("#draft-options")!;
const draftBanCountEl = document.querySelector<HTMLInputElement>("#draft-ban-count")!;
const draftUniqEl = document.querySelector<HTMLSelectElement>("#draft-uniq")!;
const draftPickModeEl = document.querySelector<HTMLElement>("#draft-pick-mode")!;
const draftCardEl = document.querySelector<HTMLElement>("#draft-card")!;
const draftRoomEl = document.querySelector<HTMLElement>("#draft-room")!;
const draftRootEl = document.querySelector<HTMLElement>("#draft-root")!;
const colorP1El = document.querySelector<HTMLInputElement>("#color-p1")!;
const colorP1SecEl = document.querySelector<HTMLInputElement>("#color-p1-sec")!;
const colorP2El = document.querySelector<HTMLInputElement>("#color-p2")!;
const colorP2SecEl = document.querySelector<HTMLInputElement>("#color-p2-sec")!;
const patternP1El = document.querySelector<HTMLSelectElement>("#pattern-p1")!;
const patternP2El = document.querySelector<HTMLSelectElement>("#pattern-p2")!;

const view = new GameView(canvas);
bindItemIconLoader((item) => view.createItemVisual(item));
bindPieceIconLoader((unit) => view.createPieceVisual(unit));
const portrait = new PortraitView(portraitCanvas, (unit) => view.createPieceVisual(unit));
const hoverPortrait = new PortraitView(
  hoverPortraitCanvas,
  (unit) => view.createPieceVisual(unit),
  (item) => view.createItemVisual(item)
);
const net = new NetClient();

let mode: "menu" | "offline" | "online" = "menu";
let game: Game | null = null;
let publicState: PublicState | null = null;
let localPlayerId = 0;
let selectedUnitId: number | null = null;
let pendingAction: { unitId: number; actionId: string } | null = null;
let inspectHover: InspectHover | null = null;
let user = loadUser();
let fightLogged = false;
let matchMode: MatchMode = user.settings.matchMode;
let armyFormat: ArmyFormat = user.settings.armyFormat;
let draftPickMode: DraftPickModeId = user.settings.draftPickMode;
let draft: Draft | null = null;
let roomCode = "";
let matchFromDraft = false;

function fillSelect(
  el: HTMLSelectElement,
  options: { id: string; label: string; isDefault?: boolean }[],
  selected: string
): void {
  el.replaceChildren();
  for (const option of options) {
    const opt = document.createElement("option");
    opt.value = option.id;
    opt.textContent = option.label;
    if (option.isDefault) {
      opt.classList.add("is-default");
      opt.style.color = "#7ec8e3";
    }
    el.append(opt);
  }
  el.value = selected;
  el.classList.toggle("is-default", !!options.find((option) => option.id === el.value)?.isDefault);
}

function setArmyEditorOpen(open: boolean): void {
  armyEditorWindow.hidden = !open;
  armyEditorBtn.setAttribute("aria-expanded", open ? "true" : "false");
}

function setSettingsOpen(open: boolean): void {
  settingsPanel.hidden = !open;
  settingsBtn.setAttribute("aria-expanded", open ? "true" : "false");
}

function refreshSettingsForm(): void {
  autoPickupEl.checked = user.settings.autoPickupItems;
}

function setChoice(row: HTMLElement, attr: string, value: string): void {
  for (const btn of Array.from(row.querySelectorAll<HTMLButtonElement>(".choice"))) {
    btn.classList.toggle("is-on", btn.dataset[attr] === value);
  }
}

function readUniqueness(): DraftUniq {
  const value = draftUniqEl.value;
  if (value === "unique" || value === "unique-per-player" || value === "free") return value;
  return "free";
}

function chosenDraftConfig() {
  return normalizeDraftConfig(
    {
      format: armyFormat,
      banCount: clampBanCount(Number(draftBanCountEl.value)),
      pickMode: draftPickMode,
      uniqueness: readUniqueness(),
    },
    armyFormat
  );
}

function chosenScatter() {
  return {
    itemAmount: clampDecorAmount(Number(itemAmountEl.value)),
    obstacleAmount: clampDecorAmount(Number(obstacleAmountEl.value)),
    symmetricObstacles: obstacleSymmetricEl.checked,
  };
}

function syncScatterLabels(): void {
  itemAmountValEl.textContent = `${clampDecorAmount(Number(itemAmountEl.value))}%`;
  obstacleAmountValEl.textContent = `${clampDecorAmount(Number(obstacleAmountEl.value))}%`;
}

function persistLobby(): void {
  user = setUserSettings({
    matchMode,
    armyFormat,
    board: boardKind(),
    draftBanCount: clampBanCount(Number(draftBanCountEl.value)),
    draftPickMode,
    draftUniqueness: readUniqueness(),
    ...chosenScatter(),
  });
}

function refreshBoardSelect(): void {
  const options = boardsForFormat(armyFormat);
  const current = boardEl.value || user.settings.board;
  const selected = options.some((option) => option.id === current) ? current : defaultBoardForFormat(armyFormat);
  fillSelect(boardEl, options, selected);
}

function refreshModeForm(): void {
  setChoice(matchModeEl, "mode", matchMode);
  setChoice(armyFormatEl, "format", armyFormat);
  setChoice(draftPickModeEl, "pick", draftPickMode);
  draftBanCountEl.value = String(clampBanCount(Number(draftBanCountEl.value)));
  draftOptionsEl.hidden = matchMode !== "draft";
  armyRostersEl.hidden = matchMode === "draft";
  if (matchMode === "draft") setArmyEditorOpen(false);
  refreshBoardSelect();
  persistLobby();
}

function hideDraft(): void {
  document.body.classList.remove("drafting");
  draftCardEl.hidden = true;
  draft = null;
}

function showDraftScreen(next: Draft, code?: string): void {
  draft = next;
  roomCode = code ?? roomCode;
  document.body.classList.add("drafting");
  document.body.classList.remove("playing");
  gameHudEl.hidden = true;
  draftCardEl.hidden = false;
  if (roomCode) draftRoomEl.textContent = `Room ${roomCode}`;
  else draftRoomEl.textContent = mode === "offline" ? "Hotseat" : "";
  view.setColors(chosenColors());
  renderDraft(draftRootEl, draft, localPlayerId, mode === "offline", {
    onChoose: (piece) => chooseDraftPiece(piece),
  });
}

function chooseDraftPiece(piece: string): void {
  if (!draft) return;
  const action = draft.phase === "ban" ? "ban" : "pick";
  if (mode === "offline") {
    const playerId = draft.currentPlayerId;
    const result = action === "ban" ? draft.tryBan(playerId, piece) : draft.tryPick(playerId, piece);
    if (!result.ok) {
      menuStatusEl.textContent = result.error ?? "Invalid draft action";
      showDraftScreen(draft);
      return;
    }
    if (draft.phase === "done") {
      beginMatchFromDraft(draft);
      return;
    }
    showDraftScreen(draft);
    return;
  }
  try {
    net.send({ type: "draft-action", action, piece });
  } catch (err) {
    menuStatusEl.textContent = err instanceof Error ? err.message : "Draft action failed";
  }
}

function beginMatchFromDraft(finished: Draft): void {
  matchFromDraft = true;
  fightLogged = false;
  pendingAction = null;
  game = new Game(finished.toArmy(0), chosenColors(), boardKind(), finished.toArmy(1), {
    autoPickupItems: user.settings.autoPickupItems,
    ...chosenScatter(),
  });
  hideDraft();
  showGame();
  netInfoEl.textContent = "Hotseat — drafted armies";
  view.refreshAtmosphere();
  applyState(game.toPublic());
}

function leaveDraft(): void {
  const wasOnline = mode === "online";
  mode = "menu";
  localPlayerId = 0;
  hideDraft();
  roomCode = "";
  draftRoomEl.textContent = "";
  if (wasOnline) net.close();
  menuStatusEl.textContent = "Draft cancelled.";
}

function refreshArmySelect(): void {
  const p1 = armyOptions(user, false, armyFormat);
  const p2 = armyOptions(user, true, armyFormat);
  if (p1.length && !p1.some((option) => option.id === user.activeArmyId)) {
    user = setActiveArmy(p1[0].id);
  }
  if (p2.length && !p2.some((option) => option.id === user.guestArmyId)) {
    user = setGuestArmy(p2[0].id);
  }
  fillSelect(armyEl, p1, user.activeArmyId);
  fillSelect(armyP2El, p2, user.guestArmyId);
  const army = activeArmy(user);
  armyEditorBtn.textContent = canEditArmy(army) ? "Edit army" : "View army";
  armyEditorTitle.textContent = canEditArmy(army) ? "Edit army" : "Army";
  renderArmyEditor(armyEditorEl, user, {
    onRename: (name) => {
      user = renameArmy(user.activeArmyId, name);
      refreshArmySelect();
    },
    onSetSlot: (row, x, definitionId) => {
      const result = setArmySlot(user.activeArmyId, row, x, definitionId);
      user = result.user;
      if (result.error === "no-king") menuStatusEl.textContent = "The army needs a King.";
      refreshArmySelect();
    },
    onDelete: () => {
      const result = deleteArmy(user.activeArmyId);
      user = result.user;
      if (result.error === "default") menuStatusEl.textContent = "Default armies can't be deleted.";
      if (result.error === "last-army") menuStatusEl.textContent = "Keep at least one army.";
      refreshArmySelect();
    },
  });
}

function chosenRoster() {
  return resolveArmy(user, armyEl.value || user.activeArmyId);
}

function chosenGuestRoster() {
  return resolveArmy(user, armyP2El.value || user.guestArmyId);
}

function persistStylesFromForm(): void {
  user = setUserStyles(chosenColors()[0], chosenColors()[1]);
}

function persistLocalRoster(): void {
  if (!game) return;
  const ownerId = mode === "online" ? localPlayerId : 0;
  if (ownerId !== 0) return;
  user = syncArmyFromUnits(user, user.activeArmyId, game.units, ownerId);
}

function boardKind(): BoardKind {
  const value = boardEl.value;
  const options = boardsForFormat(armyFormat);
  return options.some((option) => option.id === value) && boardSupportsFormat(value as BoardKind, armyFormat)
    ? (value as BoardKind)
    : defaultBoardForFormat(armyFormat);
}

function fillPatternSelect(el: HTMLSelectElement, selected: PlayerStyle["pattern"]): void {
  el.replaceChildren();
  for (const id of PATTERN_IDS) {
    const opt = document.createElement("option");
    opt.value = id;
    opt.textContent = PATTERN_LABELS[id];
    el.append(opt);
  }
  el.value = selected;
}

function applyStyleToForm(ownerId: 0 | 1, style: PlayerStyle): void {
  if (ownerId === 0) {
    colorP1El.value = style.primary;
    colorP1SecEl.value = style.secondary;
    fillPatternSelect(patternP1El, style.pattern);
  } else {
    colorP2El.value = style.primary;
    colorP2SecEl.value = style.secondary;
    fillPatternSelect(patternP2El, style.pattern);
  }
}

function readStyle(
  primaryEl: HTMLInputElement,
  secondaryEl: HTMLInputElement,
  patternEl: HTMLSelectElement,
  fallbackPrimary: string
): PlayerStyle {
  return {
    primary: normalizeHex(primaryEl.value, fallbackPrimary),
    secondary: normalizeHex(secondaryEl.value, DEFAULT_GOLD),
    pattern: isPatternId(patternEl.value) ? patternEl.value : "fleur",
  };
}

function chosenColors(): [PlayerStyle, PlayerStyle] {
  return [
    readStyle(colorP1El, colorP1SecEl, patternP1El, DEFAULT_P1),
    readStyle(colorP2El, colorP2SecEl, patternP2El, DEFAULT_P2),
  ];
}

function showGame(): void {
  hideDraft();
  document.body.classList.add("playing");
  gameHudEl.hidden = false;
}

function statusText(state: PublicState): string {
  if (state.phase === GamePhase.Ended) {
    if (state.endReason === GameEndReason.Stalemate) return "Draw — stalemate";
    const winner = state.winnerId === 0 ? "Player 1" : "Player 2";
    return `${winner} wins (${state.endReason})`;
  }
  const turn = `Turn ${state.turnNumber} — Player ${state.currentPlayerId + 1}`;
  if (mode === "online") {
    const yours = state.currentPlayerId === localPlayerId ? "Your move" : "Opponent's move";
    return `${turn}\n${yours}`;
  }
  return turn;
}

function canSelect(unit: UnitState): boolean {
  return unit.isAlive && unit.ownerId === actorId();
}

function pickupUnitId(): number | null {
  const state = publicState;
  if (!state || state.phase !== GamePhase.Playing || !canAct()) return null;
  const selected = selectedUnit();
  if (selected && canSelect(selected) && !selected.heldItem && itemOnNode(state, selected.currentNodeId)) {
    return selected.unitId;
  }
  const standing = state.units.find(
    (u) => canSelect(u) && !u.heldItem && itemOnNode(state, u.currentNodeId)
  );
  return standing?.unitId ?? null;
}

function refreshPickup(): void {
  const unitId = pickupUnitId();
  const itemIds: string[] = [];
  if (publicState && unitId != null) {
    const unit = publicState.units.find((u) => u.unitId === unitId);
    const item = unit ? itemOnNode(publicState, unit.currentNodeId) : undefined;
    if (item) itemIds.push(item.id);
  }
  view.setPickableItems(itemIds);
}

function inspectActions(unit: UnitState): { id: string; label: string; run?: () => void }[] {
  const actions: { id: string; label: string; run?: () => void }[] = [];
  if (publicState && !unit.heldItem) {
    const item = itemOnNode(publicState, unit.currentNodeId);
    if (item) {
      const can = canAct() && canSelect(unit);
      actions.push({
        id: "pickup",
        label: pickupActionLabel(item),
        run: can ? () => tryPickup(unit.unitId) : undefined,
      });
    }
  }
  for (const action of unit.actions ?? []) {
    const can = canAct() && canSelect(unit);
    const spent = action.id === unit.heldItem?.kind && !!unit.heldItem?.usedThisMatch;
    const run =
      action.id === "TransmuteScroll"
        ? () => beginTargetedAction(unit.unitId, action.id)
        : () => tryAction(unit.unitId, action.id);
    actions.push({
      id: action.id,
      label: spent ? `${action.label} (used)` : action.label,
      run: can && !spent ? run : undefined,
    });
  }
  if (unit.heldItem) {
    const blocked = !!publicState && !!itemOnNode(publicState, unit.currentNodeId);
    const can = canAct() && canSelect(unit) && !blocked;
    actions.push({
      id: "drop",
      label: "Drop",
      run: can ? () => tryDrop(unit.unitId) : undefined,
    });
  }
  return actions;
}

function actionPortraitItem(unit: UnitState, actionId: string) {
  if (actionId === "pickup") {
    return publicState ? itemOnNode(publicState, unit.currentNodeId) : undefined;
  }
  if (actionId === "drop" || actionId === "PowderBarrel" || actionId === "EscapeScroll" || actionId === "TransmuteScroll") {
    return unit.heldItem ?? undefined;
  }
  return undefined;
}

function renderActions(el: HTMLElement, unit: UnitState | undefined): void {
  el.replaceChildren();
  if (!unit) return;
  for (const action of inspectActions(unit)) {
    const btn = document.createElement("button");
    btn.type = "button";
    if (action.id === "pickup") btn.className = "action-pickup";
    if (action.id === "PowderBarrel") btn.className = "action-ignite";
    if (action.id === "EscapeScroll") btn.className = "action-escape";
    if (action.id === "TransmuteScroll") {
      btn.className = "action-transmute";
      if (pendingAction?.unitId === unit.unitId && pendingAction.actionId === action.id) {
        btn.classList.add("is-armed");
      }
    }
    if (action.id === "drop") btn.className = "action-drop";
    const portrait = actionPortraitItem(unit, action.id);
    if (portrait) {
      const img = document.createElement("img");
      img.className = "action-portrait";
      img.alt = "";
      img.width = 28;
      img.height = 28;
      void itemIconUrl(portrait).then((url) => {
        if (url && img.isConnected) img.src = url;
      });
      btn.append(img);
    }
    const label = document.createElement("span");
    label.textContent = action.label;
    btn.append(label);
    if (action.run) btn.addEventListener("click", action.run);
    else btn.disabled = true;
    btn.addEventListener("pointerenter", () => showActionPreview(unit, action.id));
    btn.addEventListener("pointerleave", hideActionPreview);
    el.append(btn);
  }
}

function showActionPreview(unit: UnitState, actionId: string): void {
  if (!publicState) return;
  const nodes = previewNodesForAction(actionId, boardFromState(publicState), unit.currentNodeId, unit, publicState.units);
  view.setAbilityHighlights(nodes, actionId === "TransmuteScroll" ? "#9a8b6e" : "#ff6a3d");
}

function hideActionPreview(): void {
  if (pendingAction) return;
  view.setAbilityHighlights(null);
}

function beginTargetedAction(unitId: number, actionId: string): void {
  if (!publicState) return;
  const unit = publicState.units.find((item) => item.unitId === unitId);
  if (!unit) return;
  if (actionId === "TransmuteScroll") {
    if (unit.heldItem?.kind === "TransmuteScroll" && unit.heldItem.usedThisMatch) {
      refreshHud("Already used this match");
      return;
    }
    const spots = transmutationTargets(boardFromState(publicState), unit, publicState.units);
    if (!spots.length) {
      refreshHud("No empty square this piece can attack");
      return;
    }
  }
  if (pendingAction?.unitId === unitId && pendingAction.actionId === actionId) {
    pendingAction = null;
    refreshHighlights();
    refreshHud();
    return;
  }
  pendingAction = { unitId, actionId };
  selectedUnitId = unitId;
  refreshHighlights();
  refreshHud("Choose an empty square this piece can attack");
}

function selectedUnit(): UnitState | undefined {
  if (!publicState || selectedUnitId == null) return undefined;
  return publicState.units.find((u) => u.unitId === selectedUnitId);
}

function hoveredUnit(): UnitState | undefined {
  const hover = inspectHover;
  if (!publicState || hover?.kind !== "unit") return undefined;
  if (hover.unitId === selectedUnitId) return undefined;
  const unit = publicState.units.find((u) => u.unitId === hover.unitId);
  return unit?.isAlive ? unit : undefined;
}

function hoveredItem() {
  const hover = inspectHover;
  if (!publicState || hover?.kind !== "item") return undefined;
  return publicState.items.find((item) => item.id === hover.itemId);
}

function clearHoverHud(): void {
  hoverSlotEl.hidden = true;
  hoverPortraitWrap.dataset.empty = "true";
  hudHoverNameEl.textContent = "";
  hudHoverMoveEl.textContent = "";
  hoverPortrait.show(null);
  renderActions(hoverActionsEl, undefined);
  view.setHoverHighlights(null);
}

function refreshHover(): void {
  const unit = hoveredUnit();
  const item = hoveredItem();
  if (!publicState || (!unit && !item)) {
    clearHoverHud();
    return;
  }
  hoverSlotEl.hidden = false;
  hoverPortraitWrap.dataset.empty = "false";
  if (item) {
    hudHoverNameEl.textContent = item.displayName;
    hudHoverMoveEl.textContent = item.description;
    renderActions(hoverActionsEl, undefined);
    hoverPortrait.showItem(item);
    view.setHoverHighlights(null);
    return;
  }
  hudHoverNameEl.textContent = pieceDisplayName(unit!.definitionId);
  hudHoverMoveEl.textContent = pieceMoveText(unit!);
  renderActions(hoverActionsEl, unit);
    hoverPortrait.show(unit!, ownerPrimary(unit!));
  view.setHoverHighlights(movesForUnit(boardFromState(publicState), unit!, publicState.units), unit!.ownerId);
}

function ownerPrimary(unit: UnitState): string | undefined {
  return publicState?.playerColors[unit.ownerId]?.primary;
}

function actorId(): number {
  return mode === "offline" ? (publicState?.currentPlayerId ?? 0) : localPlayerId;
}

function canAct(): boolean {
  if (!publicState || publicState.phase !== GamePhase.Playing || view.busy) return false;
  return mode !== "online" || publicState.currentPlayerId === localPlayerId;
}

function isLegalTarget(unit: UnitState, nodeId: number): boolean {
  if (!publicState) return false;
  return allTargets(movesForUnit(boardFromState(publicState), unit, publicState.units)).includes(nodeId);
}

function issueMove(unitId: number, nodeId: number): void {
  selectedUnitId = null;
  refreshHighlights();
  if (mode === "offline" && game && publicState) {
    const prev = publicState;
    const result = game.tryMove(unitId, nodeId, actorId());
    if (!result.success) {
      applyState(game.toPublic(), result.error);
      return;
    }
    void applyMove(prev, game.toPublic(), result);
    return;
  }
  if (mode === "online") {
    try {
      net.send({ type: "move", unitId, targetNode: nodeId });
    } catch (err) {
      refreshHud(err instanceof Error ? err.message : "Move failed");
    }
  }
}

function refreshHud(notice?: string): void {
  hideActionPreview();
  if (!publicState) return;
  hudTurnEl.textContent = statusText(publicState);
  const unit = selectedUnit();
  if (unit?.isAlive) {
    hudNameEl.textContent = pieceDisplayName(unit.definitionId);
    hudMoveEl.textContent = pieceMoveText(unit);
    renderActions(selectedActionsEl, unit);
    portraitWrap.dataset.empty = "false";
    portrait.show(unit, ownerPrimary(unit));
  } else {
    hudNameEl.textContent = "Select a piece";
    hudMoveEl.textContent = "Hover a piece or item to inspect. Select yours to move.";
    renderActions(selectedActionsEl, undefined);
    portraitWrap.dataset.empty = "true";
    portrait.show(null);
  }
  if (notice) {
    hudNoticeEl.hidden = false;
    hudNoticeEl.textContent = notice;
  } else {
    hudNoticeEl.hidden = true;
    hudNoticeEl.textContent = "";
  }
  refreshHover();
}

function applyState(state: PublicState, notice?: string): void {
  publicState = state;
  view.setState(state, localPlayerId);
  persistLocalRoster();
  recordFightIfNeeded(state);
  refreshHighlights();
  refreshPickup();
  refreshHud(notice);
}

function recordFightIfNeeded(state: PublicState): void {
  if (fightLogged || state.phase !== GamePhase.Ended) return;
  if (matchFromDraft) return;
  if (mode === "online" && localPlayerId !== 0) return;
  fightLogged = true;
  const result =
    state.endReason === GameEndReason.Stalemate ? "draw" : state.winnerId === 0 ? "win" : "loss";
  user = recordArmyFight(user.activeArmyId, result);
}

async function applyMove(prev: PublicState, next: PublicState, move: MoveExecution, notice?: string): Promise<void> {
  await view.playOutcome(prev, next, move);
  let extra = notice;
  if (move.success && !move.isRangedCapture && !move.captureBlocked) {
    const landed = itemOnNode(next, move.toNode);
    const unit = next.units.find((u) => u.unitId === move.unitId);
    if (move.pickedItemId) {
      const name = unit?.heldItem?.displayName;
      const tip = name ? `Picked up ${name}` : "Picked up item";
      extra = extra ? `${extra}\n${tip}` : tip;
    } else if (landed && unit && !unit.heldItem) {
      const tip = `Landed on a ${landed.displayName} — pick it up next turn`;
      extra = extra ? `${extra}\n${tip}` : tip;
    }
  }
  applyState(next, extra);
}

async function applyAction(prev: PublicState, next: PublicState, action: ActionExecution, notice?: string): Promise<void> {
  await view.playAction(prev, next, action);
  applyState(next, notice);
}

function refreshHighlights(): void {
  refreshPickup();
  if (!publicState || selectedUnitId == null) {
    pendingAction = null;
    view.setHighlights(null);
    view.setAbilityHighlights(null);
    view.selectedNode(null);
    if (publicState) refreshHud();
    return;
  }
  const unit = publicState.units.find((u) => u.unitId === selectedUnitId);
  if (!unit || !unit.isAlive) {
    selectedUnitId = null;
    pendingAction = null;
    view.setHighlights(null);
    view.setAbilityHighlights(null);
    view.selectedNode(null);
    refreshPickup();
    refreshHud();
    return;
  }
  if (pendingAction && pendingAction.unitId === unit.unitId) {
    view.setHighlights(null);
    const board = boardFromState(publicState);
    const spots =
      pendingAction.actionId === "TransmuteScroll"
        ? transmutationTargets(board, unit, publicState.units)
        : previewNodesForAction(pendingAction.actionId, board, unit.currentNodeId, unit, publicState.units);
    view.setAbilityHighlights(spots, "#9a8b6e");
  } else if (canSelect(unit)) {
    view.setAbilityHighlights(null);
    const board = boardFromState(publicState);
    view.setHighlights(movesForUnit(board, unit, publicState.units), unit.ownerId);
  } else {
    view.setHighlights(null);
    view.setAbilityHighlights(null);
  }
  view.selectedNode(unit.currentNodeId);
  refreshHud();
}

function playOffline(): void {
  persistStylesFromForm();
  if (matchMode === "draft") {
    mode = "offline";
    localPlayerId = 0;
    matchFromDraft = true;
    roomCode = "";
    showDraftScreen(new Draft(chosenDraftConfig(), false));
    return;
  }
  mode = "offline";
  fightLogged = false;
  matchFromDraft = false;
  pendingAction = null;
  localPlayerId = 0;
  user = setActiveArmy(armyEl.value);
  game = new Game(chosenRoster(), chosenColors(), boardKind(), chosenGuestRoster(), {
    autoPickupItems: user.settings.autoPickupItems,
    ...chosenScatter(),
  });
  showGame();
  netInfoEl.textContent = "Hotseat — cyan orb is a Force Field; bronze cube is a Crossbow";
  view.refreshAtmosphere();
  applyState(game.toPublic());
}

async function hostOnline(): Promise<void> {
  try {
    await net.connect();
    fightLogged = false;
    matchFromDraft = matchMode === "draft";
    persistStylesFromForm();
    if (matchMode !== "draft") user = setActiveArmy(armyEl.value);
    net.send({
      type: "host",
      army: matchMode === "draft" ? undefined : activeArmy(user).basedOn,
      roster: matchMode === "draft" ? undefined : chosenRoster(),
      opponentRoster: matchMode === "draft" ? undefined : chosenGuestRoster(),
      board: boardKind(),
      colors: chosenColors(),
      autoPickupItems: user.settings.autoPickupItems,
      matchMode,
      format: armyFormat,
      draft: matchMode === "draft" ? chosenDraftConfig() : undefined,
      ...chosenScatter(),
    });
  } catch (err) {
    menuStatusEl.textContent = err instanceof Error ? err.message : "Host failed";
  }
}

async function joinOnline(): Promise<void> {
  const code = joinCodeEl.value.trim().toUpperCase();
  if (code.length < 4) {
    menuStatusEl.textContent = "Enter a room code";
    return;
  }
  try {
    await net.connect();
    net.send({ type: "join", code, roster: chosenRoster() });
  } catch (err) {
    menuStatusEl.textContent = err instanceof Error ? err.message : "Join failed";
  }
}

function resign(): void {
  if (mode === "offline" && game) {
    game.resign(game.currentPlayerId);
    applyState(game.toPublic(), "Resignation");
    return;
  }
  if (mode === "online" && net.connected) net.send({ type: "resign" });
}

function tryAction(unitId: number, actionId: string, targetNode?: number): void {
  pendingAction = null;
  if (mode === "offline" && game && publicState) {
    const prev = publicState;
    const result = game.tryAction(unitId, actionId, actorId(), targetNode);
    selectedUnitId = null;
    if (!result.success) {
      applyState(game.toPublic(), result.error);
      return;
    }
    const notice = actionNotice(result);
    void applyAction(prev, game.toPublic(), result, notice);
    return;
  }
  if (mode === "online") {
    selectedUnitId = null;
    try {
      net.send({ type: "action", unitId, actionId, targetNode });
    } catch (err) {
      refreshHud(err instanceof Error ? err.message : "Action failed");
    }
  }
}

function tryDrop(unitId: number): void {
  if (mode === "offline" && game && publicState) {
    const result = game.tryDrop(unitId, actorId());
    const dropped = publicState.units.find((u) => u.unitId === unitId)?.heldItem;
    const ok = dropped ? `Dropped ${dropped.displayName}` : "Dropped item";
    applyState(game.toPublic(), result.success ? ok : result.error);
    return;
  }
  if (mode === "online") {
    try {
      net.send({ type: "drop", unitId });
    } catch (err) {
      refreshHud(err instanceof Error ? err.message : "Drop failed");
    }
  }
}

function tryPickup(unitId: number): void {
  if (mode === "offline" && game && publicState) {
    const result = game.tryPickup(unitId, publicState.currentPlayerId);
    selectedUnitId = null;
    const picked = game.units.find((u) => u.unitId === unitId)?.heldItem;
    const ok = picked ? `Picked up ${picked.displayName}` : "Picked up item";
    applyState(game.toPublic(), result.success ? ok : result.error);
    return;
  }
  if (mode === "online") {
    selectedUnitId = null;
    net.send({ type: "pickup", unitId });
  }
}

function sameInspect(a: InspectHover | null, b: InspectHover | null): boolean {
  if (a === b) return true;
  if (!a || !b || a.kind !== b.kind) return false;
  if (a.kind === "unit" && b.kind === "unit") return a.unitId === b.unitId;
  if (a.kind === "item" && b.kind === "item") return a.itemId === b.itemId;
  return false;
}

view.onInspectHover = (target) => {
  const next = target?.kind === "unit" && target.unitId === selectedUnitId ? null : target;
  if (sameInspect(inspectHover, next)) return;
  inspectHover = next;
  refreshHover();
};

view.onItemClick = (itemId, nodeId) => {
  if (!publicState || publicState.phase !== GamePhase.Playing || view.busy) return;
  const unit =
    selectedUnitId != null
      ? publicState.units.find((u) => u.unitId === selectedUnitId)
      : unitOnNode(publicState, nodeId);
  if (unit && canSelect(unit) && canAct() && unit.currentNodeId === nodeId && !unit.heldItem) {
    tryPickup(unit.unitId);
    return;
  }
  view.onTileClick?.(nodeId);
  void itemId;
};

view.onTileClick = (nodeId) => {
  if (!publicState || view.busy) return;
  if (publicState.phase === GamePhase.Setup) return;

  const occupant = unitOnNode(publicState, nodeId);
  const item = itemOnNode(publicState, nodeId);
  const selected = selectedUnit();

  if (pendingAction && selected && selected.unitId === pendingAction.unitId) {
    const spots = transmutationTargets(boardFromState(publicState), selected, publicState.units);
    if (spots.includes(nodeId)) {
      tryAction(pendingAction.unitId, pendingAction.actionId, nodeId);
      return;
    }
    if (occupant && canSelect(occupant) && occupant.unitId !== selected.unitId) {
      pendingAction = null;
      selectedUnitId = occupant.unitId;
      refreshHighlights();
      return;
    }
    refreshHud("Choose an empty square this piece can attack");
    return;
  }

  if (occupant) {
    if (canAct() && canSelect(occupant) && selected?.unitId === occupant.unitId && item && !occupant.heldItem) {
      tryPickup(occupant.unitId);
      return;
    }
    if (canAct() && selected && canSelect(selected) && !canSelect(occupant) && isLegalTarget(selected, nodeId)) {
      issueMove(selected.unitId, nodeId);
      return;
    }
    pendingAction = null;
    selectedUnitId = occupant.unitId;
    refreshHighlights();
    if (canAct() && item && !occupant.heldItem) {
      refreshHud("Click the item or the action button to pick it up");
    }
    return;
  }

  if (canAct() && selected && canSelect(selected)) {
    issueMove(selected.unitId, nodeId);
    return;
  }

  selectedUnitId = null;
  refreshHighlights();
};

net.onMessage = (msg) => {
  if (msg.type === "hosted" || msg.type === "joined") {
    mode = "online";
    localPlayerId = msg.playerId;
    roomCode = msg.code;
    if (msg.draft) {
      matchFromDraft = true;
      showDraftScreen(Draft.fromPublic(msg.draft), msg.code);
      return;
    }
    if (!msg.state) {
      menuStatusEl.textContent = "Room opened without a match";
      return;
    }
    matchFromDraft = false;
    showGame();
    netInfoEl.textContent = `Room ${msg.code} — you are Player ${msg.playerId + 1}`;
    view.refreshAtmosphere();
    applyState(msg.state, msg.type === "joined" ? "Connected" : "Waiting for opponent");
    return;
  }
  if (msg.type === "draft-state") {
    showDraftScreen(Draft.fromPublic(msg.draft), roomCode);
    return;
  }
  if (msg.type === "state") {
    if (!draftCardEl.hidden) {
      showGame();
      netInfoEl.textContent = roomCode ? `Room ${roomCode} — you are Player ${localPlayerId + 1}` : "";
      view.refreshAtmosphere();
    }
    if (msg.lastMove && publicState && msg.lastMove.success) {
      void applyMove(publicState, msg.state, msg.lastMove, msg.notice);
      return;
    }
    if (msg.lastAction && publicState && msg.lastAction.success) {
      void applyAction(publicState, msg.state, msg.lastAction, msg.notice);
      return;
    }
    applyState(msg.state, msg.notice);
    return;
  }
  if (msg.type === "error") {
    if (!draftCardEl.hidden) draftRoomEl.textContent = msg.message;
    else if (mode === "menu") menuStatusEl.textContent = msg.message;
    else refreshHud(msg.message);
    return;
  }
  if (msg.type === "opponent-left") {
    if (!draftCardEl.hidden) draftRoomEl.textContent = "Opponent left the room";
    else refreshHud("Opponent left the room");
  }
};

net.onClose = () => {
  if (!draftCardEl.hidden) draftRoomEl.textContent = "Disconnected from server";
  else if (mode === "online") refreshHud("Disconnected from server");
};

document.querySelector("#btn-offline")!.addEventListener("click", playOffline);
document.querySelector("#btn-host")!.addEventListener("click", () => void hostOnline());
document.querySelector("#btn-join")!.addEventListener("click", () => void joinOnline());
document.querySelector("#btn-resign")!.addEventListener("click", resign);
document.querySelector("#btn-draft-leave")!.addEventListener("click", leaveDraft);

function fillPickModes(): void {
  draftPickModeEl.replaceChildren();
  for (const option of DRAFT_PICK_MODES) {
    const btn = document.createElement("button");
    btn.type = "button";
    btn.className = "choice";
    btn.dataset.pick = option.id;
    btn.textContent = option.label;
    btn.title = option.hint;
    draftPickModeEl.append(btn);
  }
}

fillPickModes();
draftBanCountEl.value = String(user.settings.draftBanCount);
draftUniqEl.value = user.settings.draftUniqueness;
itemAmountEl.value = String(user.settings.itemAmount);
obstacleAmountEl.value = String(user.settings.obstacleAmount);
obstacleSymmetricEl.checked = user.settings.symmetricObstacles !== false;
syncScatterLabels();
refreshModeForm();
refreshArmySelect();
applyStyleToForm(0, user.style);
applyStyleToForm(1, user.guestStyle);
armyEl.addEventListener("change", () => {
  user = setActiveArmy(armyEl.value);
  refreshArmySelect();
});
armyP2El.addEventListener("change", () => {
  user = setGuestArmy(armyP2El.value);
  refreshArmySelect();
});
matchModeEl.addEventListener("click", (event) => {
  const btn = (event.target as HTMLElement).closest<HTMLButtonElement>("button[data-mode]");
  if (!btn?.dataset.mode) return;
  matchMode = btn.dataset.mode === "draft" ? "draft" : "normal";
  refreshModeForm();
});
armyFormatEl.addEventListener("click", (event) => {
  const btn = (event.target as HTMLElement).closest<HTMLButtonElement>("button[data-format]");
  if (!btn?.dataset.format) return;
  armyFormat = btn.dataset.format === "normal" ? "normal" : "mini";
  refreshArmySelect();
  refreshModeForm();
});
draftPickModeEl.addEventListener("click", (event) => {
  const btn = (event.target as HTMLElement).closest<HTMLButtonElement>("button[data-pick]");
  const id = btn?.dataset.pick;
  if (id !== "pieces-first" && id !== "pawns-first") return;
  draftPickMode = id;
  refreshModeForm();
});
draftBanCountEl.addEventListener("change", () => {
  draftBanCountEl.value = String(clampBanCount(Number(draftBanCountEl.value)));
  persistLobby();
});
draftUniqEl.addEventListener("change", persistLobby);
boardEl.addEventListener("change", persistLobby);
for (const el of [itemAmountEl, obstacleAmountEl]) {
  el.addEventListener("input", () => {
    syncScatterLabels();
    persistLobby();
  });
}
obstacleSymmetricEl.addEventListener("change", persistLobby);
for (const el of [colorP1El, colorP1SecEl, colorP2El, colorP2SecEl, patternP1El, patternP2El]) {
  el.addEventListener("change", persistStylesFromForm);
}
document.querySelector("#btn-copy-army")!.addEventListener("click", () => {
  user = duplicateArmy(armyEl.value || user.activeArmyId);
  refreshArmySelect();
  setArmyEditorOpen(true);
});
armyEditorBtn.addEventListener("click", () => {
  setArmyEditorOpen(armyEditorWindow.hidden);
});
document.querySelector("#btn-close-army-editor")!.addEventListener("click", () => {
  setArmyEditorOpen(false);
});
window.addEventListener("keydown", (event) => {
  if (event.key !== "Escape") return;
  if (!draftCardEl.hidden) {
    leaveDraft();
    return;
  }
  if (mode !== "menu") return;
  if (!armyEditorWindow.hidden) {
    setArmyEditorOpen(false);
    return;
  }
  if (!settingsPanel.hidden) setSettingsOpen(false);
});
settingsBtn.addEventListener("click", () => setSettingsOpen(settingsPanel.hidden));
autoPickupEl.addEventListener("change", () => {
  user = setUserSettings({ autoPickupItems: autoPickupEl.checked });
});
refreshSettingsForm();
menuStatusEl.textContent = "Colors and armies save on this device. Pieces stay default until they pick up an item.";
