import {
  DEFAULT_GOLD,
  DEFAULT_P1,
  DEFAULT_P2,
  DEFAULT_STYLE_P1,
  DEFAULT_STYLE_P2,
  PATTERN_IDS,
  isPatternId,
  normalizeHex,
  type PlayerStyle,
} from "./core/colors";
import { formatClock, remainingNow, type TimeControlId } from "./core/clock";
import { Draft, DRAFT_PICK_MODES, clampBanCount, normalizeDraftConfig, type DraftPickModeId, type DraftUniq } from "./core/draft";
import { defaultBoardForFormat, parseArmyFormat, type ArmyFormat, type MatchMode } from "./core/format";
import { Game, actionNotice, previewNodesForAction, type ActionExecution, type MoveExecution } from "./core/gameState";
import { actionCooldownLabel, actionReady } from "./core/cooldown";
import { itemIsSpent, itemUsesLabel } from "./core/items";
import {
  actionLabel,
  applyDomI18n,
  armyLabel,
  boardLabel,
  getLocale,
  initLocale,
  itemDesc,
  itemName,
  mountLangSwitch,
  setLocale,
  t,
  tx,
  type Locale,
} from "./i18n";
import { boardsForFormat, boardSupportsFormat } from "./core/board";
import { clampDecorAmount } from "./core/boardDecor";
import { allTargets, GameEndReason, GamePhase, PublicState, UnitState } from "./core/types";
import { movesForUnit } from "./core/validator";
import { NetClient } from "./net/client";
import type { BoardKind, HostedMessage, JoinedMessage, MatchSummary, PublicLobby, ResumedMessage, SeatSetupMessage } from "./net/protocol";
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
import { allSeats, lastMatchSeat, removeMatchSeat, saveMatchSeat, seatForCode } from "./persist/matchSeat";
import { GameView, boardFromState, itemOnNode, unitOnNode, type InspectHover } from "./render/GameView";
import { PortraitView } from "./render/PortraitView";
import { renderArmyEditor } from "./ui/armyEditor";
import { renderDraft } from "./ui/draft";
import { renderLobby } from "./ui/lobby";
import { bindItemIconLoader, itemIconUrl } from "./ui/itemIcon";
import { bindPieceIconLoader } from "./ui/pieceIcon";
import { pieceDisplayName, pieceMoveText } from "./ui/pieceInfo";

const canvas = document.querySelector<HTMLCanvasElement>("#game")!;
const menuCardEl = document.querySelector<HTMLElement>("#menu-card")!;
const menuStatusEl = document.querySelector<HTMLElement>("#menu-status")!;
const playNetEl = document.querySelector<HTMLElement>("#play-net")!;
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
const myGamesEl = document.querySelector<HTMLElement>("#my-games-list")!;
const timeControlEl = document.querySelector<HTMLElement>("#time-control")!;
const clocksEl = document.querySelector<HTMLElement>("#hud-clocks")!;
const clockP1El = document.querySelector("#clock-p1")!;
const clockP2El = document.querySelector("#clock-p2")!;
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
const lobbyCardEl = document.querySelector<HTMLElement>("#lobby-card")!;
const lobbyRoomEl = document.querySelector<HTMLElement>("#lobby-room")!;
const lobbyRootEl = document.querySelector<HTMLElement>("#lobby-root")!;
const colorP1El = document.querySelector<HTMLInputElement>("#color-p1")!;
const colorP1SecEl = document.querySelector<HTMLInputElement>("#color-p1-sec")!;
const colorP2El = document.querySelector<HTMLInputElement>("#color-p2")!;
const colorP2SecEl = document.querySelector<HTMLInputElement>("#color-p2-sec")!;
const patternP1El = document.querySelector<HTMLSelectElement>("#pattern-p1")!;
const patternP2El = document.querySelector<HTMLSelectElement>("#pattern-p2")!;

const view = new GameView(canvas);
bindItemIconLoader((item) => view.createItemVisual(item));
bindPieceIconLoader((unit, style) => view.createPieceVisual(unit, style));
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
initLocale(user.settings.locale);
document.documentElement.lang = getLocale();
let fightLogged = false;
let matchMode: MatchMode = user.settings.matchMode;
let armyFormat: ArmyFormat = user.settings.armyFormat;
let draftPickMode: DraftPickModeId = user.settings.draftPickMode;
let timeControl: TimeControlId = user.settings.timeControl === "24h" ? "24h" : "5+3";
let draft: Draft | null = null;
let draftSelected: string | null = null;
let clockTick: number | null = null;
let listedGames: MatchSummary[] = [];
let roomCode = "";
let matchFromDraft = false;
let playNet: "offline" | "online" = "offline";
let currentLobby: PublicLobby | null = null;
let seatStyles: [PlayerStyle, PlayerStyle] = [{ ...DEFAULT_STYLE_P1 }, { ...DEFAULT_STYLE_P2 }];

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

function setMenuStatus(text = ""): void {
  const shown = tx(text);
  menuStatusEl.textContent = shown;
  menuStatusEl.hidden = !shown;
}

function applyLocale(next: Locale): void {
  setLocale(next);
  user = setUserSettings({ locale: next });
  document.documentElement.lang = next;
  applyDomI18n();
  fillPickModes();
  applyStyleToForm(0, user.style);
  applyStyleToForm(1, user.guestStyle);
  refreshArmySelect();
  refreshBoardSelect();
  setChoice(matchModeEl, "mode", matchMode);
  setChoice(armyFormatEl, "format", armyFormat);
  setChoice(draftPickModeEl, "pick", draftPickMode);
  setChoice(timeControlEl, "time", timeControl);
  renderMyGames();
  if (!lobbyCardEl.hidden && currentLobby) showLobbyScreen(currentLobby);
  else if (!draftCardEl.hidden && draft) showDraftScreen(draft);
  if (publicState && document.body.classList.contains("playing")) refreshHud();
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
  if (value === "unique" || value === "unique-pieces" || value === "unique-per-player" || value === "free") return value;
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
    timeControl,
    ...chosenScatter(),
  });
}

function refreshBoardSelect(): void {
  const options = boardsForFormat(armyFormat);
  const current = boardEl.value || user.settings.board;
  const selected = options.some((option) => option.id === current) ? current : defaultBoardForFormat(armyFormat);
  fillSelect(
    boardEl,
    options.map((option) => ({ ...option, label: boardLabel(option.id) })),
    selected
  );
}

function refreshPlayNet(): void {
  setChoice(playNetEl, "net", playNet);
  menuCardEl.classList.toggle("is-online", playNet === "online");
  menuCardEl.classList.toggle("is-offline", playNet === "offline");
  if (playNet === "online") setArmyEditorOpen(false);
}

function refreshModeForm(): void {
  setChoice(matchModeEl, "mode", matchMode);
  setChoice(armyFormatEl, "format", armyFormat);
  setChoice(draftPickModeEl, "pick", draftPickMode);
  setChoice(timeControlEl, "time", timeControl);
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
  draftSelected = null;
}

function hideLobby(): void {
  document.body.classList.remove("lobbying");
  lobbyCardEl.hidden = true;
}

function pendingLobby(): PublicLobby {
  return {
    format: armyFormat,
    matchMode: matchMode === "draft" ? "draft" : "normal",
    seats: [
      {
        connected: true,
        ready: false,
        style: user.style,
        hasRoster: true,
        armyLabel: activeArmy(user).name,
      },
      { connected: false, ready: false, style: DEFAULT_STYLE_P2, hasRoster: false },
    ],
  };
}

function applyLobby(lobby: PublicLobby): void {
  currentLobby = lobby;
  seatStyles = [lobby.seats[0].style, lobby.seats[1].style];
  view.setColors(seatStyles);
}

function persistMyStyle(style: PlayerStyle): void {
  seatStyles[localPlayerId] = style;
  user = setUserStyles(style, user.guestStyle);
  applyStyleToForm(0, style);
}

function pushSeatSetup(opts?: { ready?: boolean }): void {
  if (mode !== "online" || !net.connected || !roomCode) return;
  const style = seatStyles[localPlayerId] ?? user.style;
  persistMyStyle(style);
  const payload: SeatSetupMessage = { type: "seat-setup", style };
  if (!draft) {
    ensureLobbyArmy();
    payload.roster = chosenRoster();
    const army = activeArmy(user);
    payload.armyLabel = army.isDefault ? armyLabel(army.basedOn) : army.name;
    if (opts?.ready !== undefined) payload.ready = opts.ready;
  }
  try {
    net.send(payload);
  } catch (err) {
    const text = tx(err instanceof Error ? err.message : "tip.loadoutFailed");
    if (!lobbyCardEl.hidden) lobbyRoomEl.textContent = text;
    else if (!draftCardEl.hidden) draftRoomEl.textContent = text;
    else setMenuStatus(text);
  }
}

function ensureLobbyArmy(): void {
  const format = currentLobby?.format ?? armyFormat;
  const options = armyOptions(user, false, format);
  if (options.length && !options.some((option) => option.id === user.activeArmyId)) {
    user = setActiveArmy(options[0].id);
  }
}

function onSeatStyle(playerId: 0 | 1, style: PlayerStyle): void {
  seatStyles[playerId] = style;
  view.setColors(seatStyles);
  if (mode === "offline") {
    applyStyleToForm(playerId, style);
    persistStylesFromForm();
    if (draft) showDraftScreen(draft);
    return;
  }
  if (playerId === localPlayerId) persistMyStyle(style);
  if (draft) showDraftScreen(draft);
  pushSeatSetup();
}

function showDraftScreen(next: Draft, code?: string): void {
  hideLobby();
  draft = next;
  roomCode = code ?? roomCode;
  document.body.classList.add("drafting");
  document.body.classList.remove("playing");
  gameHudEl.hidden = true;
  draftCardEl.hidden = false;
  if (roomCode) draftRoomEl.textContent = t("draft.room", { code: roomCode });
  else draftRoomEl.textContent = mode === "offline" ? t("draft.hotseat") : "";
  view.setColors(seatStyles);
  renderDraft(draftRootEl, draft, localPlayerId, mode === "offline", {
    selected: draftSelected,
    onSelect: (piece) => {
      draftSelected = piece;
      if (draft) showDraftScreen(draft);
    },
    onChoose: (piece) => chooseDraftPiece(piece),
    styles: seatStyles,
    onStyle: onSeatStyle,
  });
}

function showLobbyScreen(lobby: PublicLobby): void {
  hideDraft();
  applyLobby(lobby);
  seatStyles[localPlayerId] = user.style;
  const seats = [
    localPlayerId === 0 ? { ...lobby.seats[0], style: user.style } : lobby.seats[0],
    localPlayerId === 1 ? { ...lobby.seats[1], style: user.style } : lobby.seats[1],
  ] as PublicLobby["seats"];
  const viewLobby: PublicLobby = { ...lobby, seats };
  armyFormat = parseArmyFormat(lobby.format);
  ensureLobbyArmy();
  refreshArmySelect();
  mode = "online";
  document.body.classList.add("lobbying");
  document.body.classList.remove("playing");
  gameHudEl.hidden = true;
  lobbyCardEl.hidden = false;
  lobbyRoomEl.textContent = roomCode ? t("draft.room", { code: roomCode }) : "";
  renderLobby(lobbyRootEl, viewLobby, localPlayerId, {
    armies: armyOptions(user, false, lobby.format),
    selectedArmyId: user.activeArmyId,
    onArmy: (id) => {
      user = setActiveArmy(id);
      refreshArmySelect();
      pushSeatSetup();
    },
    onEditArmy: () => setArmyEditorOpen(true),
    onStyle: (style) => onSeatStyle(localPlayerId as 0 | 1, style),
    onReady: () => {
      const next = currentLobby?.seats[localPlayerId].ready !== true;
      if (currentLobby) {
        currentLobby.seats[localPlayerId] = { ...currentLobby.seats[localPlayerId], ready: next };
        showLobbyScreen(currentLobby);
      }
      pushSeatSetup({ ready: next });
    },
  });
}

function chooseDraftPiece(piece: string): void {
  if (!draft) return;
  const action = draft.phase === "ban" ? "ban" : "pick";
  if (mode === "offline") {
    const playerId = draft.currentPlayerId;
    const result = action === "ban" ? draft.tryBan(playerId, piece) : draft.tryPick(playerId, piece);
    if (!result.ok) {
      setMenuStatus(result.error ?? "err.invalidDraft");
      showDraftScreen(draft);
      return;
    }
    draftSelected = null;
    if (draft.phase === "done") {
      beginMatchFromDraft(draft);
      return;
    }
    showDraftScreen(draft);
    return;
  }
  try {
    draftSelected = null;
    net.send({ type: "draft-action", action, piece });
  } catch (err) {
    setMenuStatus(err instanceof Error ? err.message : "tip.draftFailed");
  }
}

function beginMatchFromDraft(finished: Draft): void {
  matchFromDraft = true;
  fightLogged = false;
  pendingAction = null;
  game = new Game(finished.toArmy(0), seatStyles, boardKind(), finished.toArmy(1), {
    autoPickupItems: user.settings.autoPickupItems,
    timeControl,
    ...chosenScatter(),
  });
  game.startClock();
  hideDraft();
  showGame();
  netInfoEl.textContent = t("net.hotseatDraft");
  view.refreshAtmosphere();
  applyState(game.toPublic());
}

function leaveRoom(): void {
  const wasOnline = mode === "online";
  const wasDraft = !draftCardEl.hidden;
  mode = "menu";
  localPlayerId = 0;
  hideDraft();
  hideLobby();
  currentLobby = null;
  roomCode = "";
  draftRoomEl.textContent = "";
  lobbyRoomEl.textContent = "";
  setArmyEditorOpen(false);
  if (wasOnline) net.close();
  setMenuStatus(wasDraft ? "draft.cancelled" : "net.leftRoom");
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
  armyEditorBtn.textContent = canEditArmy(army) ? t("menu.editArmy") : t("menu.viewArmy");
  armyEditorTitle.textContent = canEditArmy(army) ? t("menu.editArmy") : t("menu.army");
  renderArmyEditor(armyEditorEl, user, {
    onRename: (name) => {
      user = renameArmy(user.activeArmyId, name);
      refreshArmySelect();
      if (!lobbyCardEl.hidden) pushSeatSetup();
    },
    onSetSlot: (row, x, definitionId) => {
      const result = setArmySlot(user.activeArmyId, row, x, definitionId);
      user = result.user;
      if (result.error === "no-king") setMenuStatus("tip.needKing");
      if (result.error === "wrong-row") setMenuStatus("tip.wrongRow");
      refreshArmySelect();
      if (!lobbyCardEl.hidden) pushSeatSetup();
    },
    onDelete: () => {
      const result = deleteArmy(user.activeArmyId);
      user = result.user;
      if (result.error === "default") setMenuStatus("tip.cantDeleteDefault");
      if (result.error === "last-army") setMenuStatus("tip.keepOneArmy");
      refreshArmySelect();
      if (!lobbyCardEl.hidden) pushSeatSetup();
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
    opt.textContent = t(`pattern.${id}`);
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
  hideLobby();
  document.body.classList.add("playing");
  gameHudEl.hidden = false;
}

function statusText(state: PublicState): string {
  if (state.phase === GamePhase.Ended) {
    if (state.endReason === GameEndReason.Stalemate) return t("hud.drawStalemate");
    const who = state.winnerId === 0 ? t("hud.player1") : t("hud.player2");
    if (state.endReason === GameEndReason.Timeout) return t("hud.winsTime", { who });
    return t("hud.winsReason", { who, reason: t(`end.${state.endReason}`) });
  }
  if (mode === "online") {
    return state.currentPlayerId === localPlayerId
      ? t("hud.yourMove", { n: state.turnNumber })
      : t("hud.theirMove", { n: state.turnNumber });
  }
  return t("hud.turn", { n: state.turnNumber, player: state.currentPlayerId + 1 });
}

function canSelect(unit: UnitState): boolean {
  return unit.isAlive && unit.ownerId === actorId();
}

function pickupUnitId(): number | null {
  const state = publicState;
  if (!state || state.phase !== GamePhase.Playing || !canAct()) return null;
  const selected = selectedUnit();
  if (selected && canSelect(selected) && !selected.heldItem && !selected.inShadow && itemOnNode(state, selected.currentNodeId)) {
    return selected.unitId;
  }
  const standing = state.units.find(
    (u) => canSelect(u) && !u.inShadow && !u.heldItem && itemOnNode(state, u.currentNodeId)
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
  if (publicState && !unit.heldItem && !unit.inShadow) {
    const item = itemOnNode(publicState, unit.currentNodeId);
    if (item) {
      const can = canAct() && canSelect(unit);
      actions.push({
        id: "pickup",
        label:
          item.kind === "ForceFieldGenerator"
            ? t("action.pickupForcefield")
            : t("action.pickup", { name: itemName(item.kind) }),
        run: can ? () => tryPickup(unit.unitId) : undefined,
      });
    }
  }
  for (const action of unit.actions ?? []) {
    const can = canAct() && canSelect(unit);
    const spent = action.id === unit.heldItem?.kind && !!unit.heldItem && itemIsSpent(unit.heldItem);
    const uses = action.id === unit.heldItem?.kind && unit.heldItem ? itemUsesLabel(unit.heldItem) : null;
    const cd = actionCooldownLabel(action);
    const targeted = action.id === "TransmuteScroll" || action.id === "SwapCharm";
    const run = targeted
      ? () => beginTargetedAction(unit.unitId, action.id)
      : () => tryAction(unit.unitId, action.id);
    let label = actionLabel(action.id);
    if (uses) label = `${label} (${uses})`;
    if (cd) label = `${label} (${cd})`;
    if (action.free) label = `${label} · ${t("hud.free")}`;
    const victim = publicState ? publicState.units.find((u) => u.isAlive && !u.inShadow && u.currentNodeId === unit.currentNodeId && u.ownerId !== unit.ownerId) : undefined;
    const situational =
      (action.id === "EnterShadow" && !!unit.inShadow) ||
      (action.id === "Stab" && (!unit.inShadow || !victim));
    actions.push({
      id: action.id,
      label,
      run: can && !spent && actionReady(action) && !situational ? run : undefined,
    });
  }
  if (unit.heldItem && !unit.inShadow) {
    const blocked = !!publicState && !!itemOnNode(publicState, unit.currentNodeId);
    const can = canAct() && canSelect(unit) && !blocked;
    actions.push({
      id: "drop",
      label: t("action.drop"),
      run: can ? () => tryDrop(unit.unitId) : undefined,
    });
  }
  return actions;
}

function actionPortraitItem(unit: UnitState, actionId: string) {
  if (actionId === "pickup") {
    return publicState ? itemOnNode(publicState, unit.currentNodeId) : undefined;
  }
  if (actionId === "drop" || actionId === "Bomb" || actionId === "EscapeScroll" || actionId === "TransmuteScroll" || actionId === "SwapCharm") {
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
    if (action.id === "Bomb") btn.className = "action-ignite";
    if (action.id === "EscapeScroll") btn.className = "action-escape";
    if (action.id === "TransmuteScroll" || action.id === "SwapCharm") {
      btn.className = action.id === "SwapCharm" ? "action-swap" : "action-transmute";
      if (pendingAction?.unitId === unit.unitId && pendingAction.actionId === action.id) {
        btn.classList.add("is-armed");
      }
    }
    if (action.id === "EnterShadow") btn.className = "action-shadow";
    if (action.id === "Stab") btn.className = "action-stab";
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
  view.setAbilityHighlights(nodes, actionId === "TransmuteScroll" ? "#9a8b6e" : actionId === "SwapCharm" ? "#c45ec8" : "#ff6a3d");
}

function hideActionPreview(): void {
  if (pendingAction) return;
  view.setAbilityHighlights(null);
}

function beginTargetedAction(unitId: number, actionId: string): void {
  if (!publicState) return;
  const unit = publicState.units.find((item) => item.unitId === unitId);
  if (!unit) return;
  if (unit.heldItem?.kind === actionId && itemIsSpent(unit.heldItem)) {
    refreshHud("tip.noUses");
    return;
  }
  const spots = previewNodesForAction(
    actionId,
    boardFromState(publicState),
    unit.currentNodeId,
    unit,
    publicState.units
  );
  if (!spots.length) {
    refreshHud(actionId === "SwapCharm" ? "tip.noSwapTarget" : "tip.noTransmuteTarget");
    return;
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
  refreshHud(actionId === "SwapCharm" ? "tip.chooseAdjacent" : "tip.chooseEmptyAttack");
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
    hudHoverNameEl.textContent = itemName(item.kind);
    hudHoverMoveEl.textContent = itemDesc(item.kind);
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
      refreshHud(err instanceof Error ? err.message : "tip.moveFailed");
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
    hudNameEl.textContent = t("hud.selectPiece");
    hudMoveEl.textContent = t("hud.selectHint");
    renderActions(selectedActionsEl, undefined);
    portraitWrap.dataset.empty = "true";
    portrait.show(null);
  }
  if (notice) {
    hudNoticeEl.hidden = false;
    hudNoticeEl.textContent = tx(notice);
  } else {
    hudNoticeEl.hidden = true;
    hudNoticeEl.textContent = "";
  }
  refreshHover();
  renderClocks();
}

function renderClocks(): void {
  const clock = publicState?.clock;
  if (!clock || !publicState) {
    clocksEl.hidden = true;
    if (clockTick != null) {
      window.clearInterval(clockTick);
      clockTick = null;
    }
    return;
  }
  clocksEl.hidden = false;
  const now = Date.now();
  clockP1El.textContent = formatClock(remainingNow(clock, 0, now));
  clockP2El.textContent = formatClock(remainingNow(clock, 1, now));
  if (mode === "offline" && game && publicState.phase === GamePhase.Playing && game.applyFlag()) {
    applyState(game.toPublic(), "tip.timeExpired");
    return;
  }
  clocksEl.querySelectorAll(".clock").forEach((el) => {
    const player = Number((el as HTMLElement).dataset.player);
    el.classList.toggle("is-on", publicState!.phase === GamePhase.Playing && clock.runningPlayerId === player);
  });
  if (clockTick == null && publicState.phase === GamePhase.Playing && clock.runningPlayerId != null) {
    clockTick = window.setInterval(renderClocks, 200);
  }
  if (publicState.phase !== GamePhase.Playing && clockTick != null) {
    window.clearInterval(clockTick);
    clockTick = null;
  }
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
  let extra = notice ? tx(notice) : undefined;
  if (move.success && !move.isRangedCapture && !move.captureBlocked) {
    const landed = itemOnNode(next, move.toNode);
    const unit = next.units.find((u) => u.unitId === move.unitId);
    if (move.pickedItemId) {
      const name = unit?.heldItem ? itemName(unit.heldItem.kind) : "";
      const tip = name ? t("tip.pickedUp", { name }) : t("tip.pickedUpItem");
      extra = extra ? `${extra}\n${tip}` : tip;
    } else if (landed && unit && !unit.heldItem) {
      const tip = t("tip.landedItem", { name: itemName(landed.kind) });
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
    const spots = previewNodesForAction(
      pendingAction.actionId,
      board,
      unit.currentNodeId,
      unit,
      publicState.units
    );
    view.setAbilityHighlights(
      spots,
      pendingAction.actionId === "SwapCharm" ? "#c45ec8" : "#9a8b6e"
    );
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
    seatStyles = chosenColors();
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
    timeControl,
    ...chosenScatter(),
  });
  game.startClock();
  showGame();
  netInfoEl.textContent = t("net.hotseatItems");
  view.refreshAtmosphere();
  applyState(game.toPublic());
}

function ago(ts: number): string {
  const sec = Math.max(0, Math.floor((Date.now() - ts) / 1000));
  if (sec < 60) return t("games.justNow");
  if (sec < 3600) return t("games.min", { n: Math.floor(sec / 60) });
  if (sec < 86400) return t("games.h", { n: Math.floor(sec / 3600) });
  return t("games.d", { n: Math.floor(sec / 86400) });
}

function renderMyGames(games = listedGames): void {
  listedGames = games;
  const seats = allSeats();
  const last = lastMatchSeat();
  if (last && !joinCodeEl.value.trim()) joinCodeEl.value = last.code;
  if (!seats.length) {
    myGamesEl.replaceChildren();
    const empty = document.createElement("p");
    empty.className = "my-games-empty";
    empty.textContent = t("games.empty");
    myGamesEl.append(empty);
    return;
  }
  const byCode = new Map(games.map((game) => [game.code, game]));
  myGamesEl.replaceChildren();
  for (const seat of seats) {
    const info = byCode.get(seat.code);
    const row = document.createElement("div");
    row.className = "my-game";
    const meta = document.createElement("div");
    meta.className = "my-game-meta";
    const code = document.createElement("div");
    code.className = "my-game-code";
    code.textContent = seat.code;
    const sub = document.createElement("div");
    sub.className = "my-game-sub";
    const bits = [`P${seat.playerId + 1}`];
    if (info?.status === "ended") bits.push(info.endReason === "Timeout" ? t("games.timeout") : t("games.ended"));
    else if (info?.myTurn) bits.push(t("games.yourTurn"));
    else if (info?.status === "waiting") bits.push(t("games.waiting"));
    else if (info?.status === "draft") bits.push(t("games.draft"));
    else if (info) bits.push(t("games.theirTurn"));
    if (info?.timeControl) bits.push(info.timeControl);
    if (info?.clockLabel) bits.push(info.clockLabel);
    bits.push(ago(info?.updatedAt ?? Date.now()));
    sub.textContent = bits.join(" · ");
    meta.append(code, sub);
    const actions = document.createElement("div");
    actions.className = "my-game-actions";
    const btn = document.createElement("button");
    btn.type = "button";
    btn.className = "ghost";
    btn.textContent = t("games.reopen");
    btn.addEventListener("click", () => void reopenSeat(seat.code));
    const del = document.createElement("button");
    del.type = "button";
    del.className = "ghost";
    del.textContent = t("games.delete");
    del.addEventListener("click", () => forgetSeat(seat.code));
    actions.append(btn, del);
    row.append(meta, actions);
    myGamesEl.append(row);
  }
}

function forgetSeat(code: string): void {
  removeMatchSeat(code);
  listedGames = listedGames.filter((game) => game.code !== code);
  if (joinCodeEl.value.trim().toUpperCase() === code) {
    joinCodeEl.value = lastMatchSeat()?.code ?? "";
  }
  renderMyGames();
}

async function refreshMyGames(): Promise<void> {
  renderMyGames();
  if (mode !== "menu") return;
  const seats = allSeats();
  if (!seats.length) return;
  try {
    if (!net.connected) await net.connect();
    net.send({ type: "list", seats: seats.map(({ code, token }) => ({ code, token })) });
  } catch {
    /* local list is enough */
  }
}

function inviteUrl(code: string): string {
  const url = new URL(location.href);
  url.search = "";
  url.hash = "";
  url.searchParams.set("code", code.trim().toUpperCase());
  return url.toString();
}

function readInviteCode(): string {
  const params = new URLSearchParams(location.search);
  const raw = params.get("code") ?? params.get("c") ?? location.hash.replace(/^#/, "");
  const code = raw.trim().toUpperCase();
  return /^[A-Z0-9]{4,8}$/.test(code) ? code : "";
}

function rememberInvite(code: string): void {
  const next = inviteUrl(code);
  if (`${location.pathname}${location.search}` !== `${new URL(next).pathname}${new URL(next).search}`) {
    history.replaceState(null, "", next);
  }
}

async function copyInvite(code = joinCodeEl.value.trim().toUpperCase() || roomCode || lastMatchSeat()?.code || ""): Promise<void> {
  if (code.length < 4) {
    setMenuStatus("tip.hostCodeFirst");
    return;
  }
  const url = inviteUrl(code);
  rememberInvite(code);
  joinCodeEl.value = code;
  try {
    await navigator.clipboard.writeText(url);
    setMenuStatus(t("tip.inviteCopied", { url }));
    if (!draftCardEl.hidden) draftRoomEl.textContent = t("draft.inviteCopied", { code });
    if (!lobbyCardEl.hidden) lobbyRoomEl.textContent = t("draft.inviteCopied", { code });
  } catch {
    setMenuStatus(url);
  }
}

function rememberSeat(msg: HostedMessage | JoinedMessage | ResumedMessage): void {
  saveMatchSeat({ code: msg.code, token: msg.token, playerId: msg.playerId });
  void refreshMyGames();
}

function takeOnlineSeat(msg: HostedMessage | JoinedMessage | ResumedMessage, notice: string): void {
  rememberSeat(msg);
  rememberInvite(msg.code);
  mode = "online";
  localPlayerId = msg.playerId;
  roomCode = msg.code;
  if (msg.lobby) applyLobby(msg.lobby);
  seatStyles[localPlayerId] = user.style;
  if (msg.draft) {
    matchFromDraft = true;
    showDraftScreen(Draft.fromPublic(msg.draft), msg.code);
    pushSeatSetup();
    if (msg.type === "hosted") void copyInvite(msg.code);
    return;
  }
  if (!msg.state || msg.type === "hosted") {
    matchFromDraft = false;
    showLobbyScreen(msg.lobby ?? pendingLobby());
    pushSeatSetup();
    if (msg.type === "hosted") void copyInvite(msg.code);
    return;
  }
  matchFromDraft = false;
  showGame();
  netInfoEl.textContent = t("net.roomYou", { code: msg.code, n: msg.playerId + 1 });
  view.refreshAtmosphere();
  applyState(msg.state, notice);
}

async function hostOnline(): Promise<void> {
  try {
    persistStylesFromForm();
    fightLogged = false;
    matchFromDraft = matchMode === "draft";
    mode = "online";
    localPlayerId = 0;
    roomCode = "";
    if (matchMode !== "draft") {
      showLobbyScreen(pendingLobby());
      lobbyRoomEl.textContent = "Creating room…";
    }
    await net.connect();
    net.send({
      type: "host",
      board: boardKind(),
      autoPickupItems: user.settings.autoPickupItems,
      matchMode,
      format: armyFormat,
      draft: matchMode === "draft" ? chosenDraftConfig() : undefined,
      timeControl,
      ...chosenScatter(),
    });
  } catch (err) {
    hideLobby();
    mode = "menu";
    setMenuStatus(err instanceof Error ? err.message : "tip.hostFailed");
  }
}

async function joinOnline(): Promise<void> {
  const code = joinCodeEl.value.trim().toUpperCase();
  if (code.length < 4) {
    setMenuStatus("tip.enterCode");
    return;
  }
  try {
    await net.connect();
    const stored = seatForCode(code);
    if (stored) net.send({ type: "resume", code: stored.code, token: stored.token });
    else net.send({ type: "join", code });
  } catch (err) {
    setMenuStatus(err instanceof Error ? err.message : "tip.joinFailed");
  }
}

async function reopenSeat(code: string): Promise<void> {
  const stored = seatForCode(code) ?? lastMatchSeat();
  if (!stored) {
    setMenuStatus("tip.noSavedRoom");
    return;
  }
  joinCodeEl.value = stored.code;
  try {
    await net.connect();
    net.send({ type: "resume", code: stored.code, token: stored.token });
  } catch (err) {
    setMenuStatus(err instanceof Error ? err.message : "tip.reopenFailed");
  }
}

function resign(): void {
  if (mode === "offline" && game) {
    game.resign(game.currentPlayerId);
    applyState(game.toPublic(), "notice.resignation");
    return;
  }
  if (mode === "online" && net.connected) net.send({ type: "resign" });
}

function tryAction(unitId: number, actionId: string, targetNode?: number): void {
  pendingAction = null;
  const actor = publicState?.units.find((u) => u.unitId === unitId);
  const free = actor?.actions.some((a) => a.id === actionId && a.free);
  if (!free) selectedUnitId = null;
  if (mode === "offline" && game && publicState) {
    const prev = publicState;
    const result = game.tryAction(unitId, actionId, actorId(), targetNode);
    if (!result.success) {
      applyState(game.toPublic(), result.error);
      return;
    }
    if (!result.free) selectedUnitId = null;
    const notice = actionNotice(result);
    void applyAction(prev, game.toPublic(), result, notice);
    return;
  }
  if (mode === "online") {
    try {
      net.send({ type: "action", unitId, actionId, targetNode });
    } catch (err) {
      refreshHud(err instanceof Error ? err.message : "tip.actionFailed");
    }
  }
}

function tryDrop(unitId: number): void {
  if (mode === "offline" && game && publicState) {
    const result = game.tryDrop(unitId, actorId());
    const dropped = publicState.units.find((u) => u.unitId === unitId)?.heldItem;
    const ok = dropped ? t("tip.dropped", { name: itemName(dropped.kind) }) : t("tip.droppedItem");
    applyState(game.toPublic(), result.success ? ok : result.error);
    return;
  }
  if (mode === "online") {
    try {
      net.send({ type: "drop", unitId });
    } catch (err) {
      refreshHud(err instanceof Error ? err.message : "tip.dropFailed");
    }
  }
}

function tryPickup(unitId: number): void {
  if (mode === "offline" && game && publicState) {
    const result = game.tryPickup(unitId, publicState.currentPlayerId);
    selectedUnitId = null;
    const picked = game.units.find((u) => u.unitId === unitId)?.heldItem;
    const ok = picked ? t("tip.pickedUp", { name: itemName(picked.kind) }) : t("tip.pickedUpItem");
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
  if (unit && !unit.inShadow && canSelect(unit) && canAct() && unit.currentNodeId === nodeId && !unit.heldItem) {
    tryPickup(unit.unitId);
    return;
  }
  view.onTileClick?.(nodeId);
  void itemId;
};

view.onUnitClick = (unitId) => {
  if (!publicState || view.busy) return;
  if (publicState.phase === GamePhase.Setup) return;
  const unit = publicState.units.find((u) => u.unitId === unitId);
  if (!unit || !unit.isAlive || !canSelect(unit)) return;
  pendingAction = null;
  if (selectedUnitId === unitId) selectedUnitId = null;
  else selectedUnitId = unitId;
  refreshHighlights();
};

view.onTileClick = (nodeId) => {
  if (!publicState || view.busy) return;
  if (publicState.phase === GamePhase.Setup) return;

  const occupant = unitOnNode(publicState, nodeId);
  const item = itemOnNode(publicState, nodeId);
  const selected = selectedUnit();

  if (pendingAction && selected && selected.unitId === pendingAction.unitId) {
    const spots = previewNodesForAction(
      pendingAction.actionId,
      boardFromState(publicState),
      selected.currentNodeId,
      selected,
      publicState.units
    );
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
    refreshHud(
      pendingAction.actionId === "SwapCharm"
        ? "tip.chooseAdjacent"
        : "tip.chooseEmptyAttack"
    );
    return;
  }

  if (occupant) {
    if (canAct() && selected && canSelect(selected) && selected.unitId !== occupant.unitId && isLegalTarget(selected, nodeId)) {
      issueMove(selected.unitId, nodeId);
      return;
    }
    if (selected?.unitId === occupant.unitId) {
      pendingAction = null;
      selectedUnitId = null;
      refreshHighlights();
      return;
    }
    pendingAction = null;
    selectedUnitId = occupant.unitId;
    refreshHighlights();
    if (canAct() && item && !occupant.heldItem) {
      refreshHud("tip.clickToPickup");
    }
    return;
  }

  if (canAct() && selected && canSelect(selected)) {
    if (selected.currentNodeId === nodeId) {
      pendingAction = null;
      selectedUnitId = null;
      refreshHighlights();
      return;
    }
    issueMove(selected.unitId, nodeId);
    return;
  }

  selectedUnitId = null;
  refreshHighlights();
};

net.onMessage = (msg) => {
  if (msg.type === "hosted" || msg.type === "joined" || msg.type === "resumed") {
    const notice =
      msg.type === "resumed" ? "notice.reopened" : msg.type === "joined" ? "notice.connected" : "notice.waitingOpponent";
    takeOnlineSeat(msg, notice);
    return;
  }
  if (msg.type === "draft-state") {
    showDraftScreen(Draft.fromPublic(msg.draft), roomCode);
    return;
  }
  if (msg.type === "lobby-state") {
    applyLobby(msg.lobby);
    if (draft && !draftCardEl.hidden) showDraftScreen(draft, roomCode);
    else if (!document.body.classList.contains("playing")) showLobbyScreen(msg.lobby);
    return;
  }
  if (msg.type === "state") {
    if (!draftCardEl.hidden || !lobbyCardEl.hidden) {
      showGame();
      netInfoEl.textContent = roomCode ? t("net.roomYou", { code: roomCode, n: localPlayerId + 1 }) : "";
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
  if (msg.type === "games") {
    renderMyGames(msg.games);
    return;
  }
  if (msg.type === "error") {
    if (!draftCardEl.hidden) draftRoomEl.textContent = tx(msg.message);
    else if (!lobbyCardEl.hidden) lobbyRoomEl.textContent = tx(msg.message);
    else if (mode === "menu") setMenuStatus(msg.message);
    else refreshHud(msg.message);
    return;
  }
  if (msg.type === "opponent-left" || msg.type === "opponent-disconnected") {
    const text =
      msg.type === "opponent-disconnected"
        ? t("net.oppDisconnected")
        : t("net.oppLeft");
    if (!draftCardEl.hidden) draftRoomEl.textContent = text;
    else if (!lobbyCardEl.hidden) lobbyRoomEl.textContent = text;
    else refreshHud(text);
  }
};

net.onClose = () => {
  if (!draftCardEl.hidden) draftRoomEl.textContent = t("net.disconnected");
  else if (!lobbyCardEl.hidden) lobbyRoomEl.textContent = t("net.disconnected");
  else if (mode === "online") refreshHud("net.disconnected");
};

document.querySelector("#btn-play")!.addEventListener("click", () => {
  if (playNet === "online") void hostOnline();
  else playOffline();
});
document.querySelector("#btn-join")!.addEventListener("click", () => void joinOnline());
document.querySelector("#btn-copy-invite")!.addEventListener("click", () => void copyInvite());
document.querySelector("#btn-resign")!.addEventListener("click", resign);
document.querySelector("#btn-draft-leave")!.addEventListener("click", leaveRoom);
document.querySelector("#btn-lobby-leave")!.addEventListener("click", leaveRoom);

function fillPickModes(): void {
  draftPickModeEl.replaceChildren();
  for (const option of DRAFT_PICK_MODES) {
    const btn = document.createElement("button");
    btn.type = "button";
    btn.className = "choice";
    btn.dataset.pick = option.id;
    btn.textContent = t(`draftMode.${option.id}`);
    btn.title = t(`draftMode.${option.id}.hint`);
    draftPickModeEl.append(btn);
  }
}

fillPickModes();
applyDomI18n();
for (const host of document.querySelectorAll<HTMLElement>("[data-lang-switch]")) {
  mountLangSwitch(host, applyLocale);
}
if (user.settings.locale !== getLocale()) user = setUserSettings({ locale: getLocale() });
draftBanCountEl.value = String(user.settings.draftBanCount);
draftUniqEl.value = user.settings.draftUniqueness;
itemAmountEl.value = String(user.settings.itemAmount);
obstacleAmountEl.value = String(user.settings.obstacleAmount);
obstacleSymmetricEl.checked = user.settings.symmetricObstacles !== false;
syncScatterLabels();
refreshPlayNet();
refreshModeForm();
refreshArmySelect();
applyStyleToForm(0, user.style);
applyStyleToForm(1, user.guestStyle);
playNetEl.addEventListener("click", (event) => {
  const btn = (event.target as HTMLElement).closest<HTMLButtonElement>("button[data-net]");
  const next = btn?.dataset.net;
  if (next !== "offline" && next !== "online") return;
  playNet = next;
  refreshPlayNet();
  if (playNet === "online") void refreshMyGames();
});
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
  armyFormat = parseArmyFormat(btn.dataset.format);
  refreshArmySelect();
  refreshModeForm();
});
timeControlEl.addEventListener("click", (event) => {
  const btn = (event.target as HTMLElement).closest<HTMLButtonElement>("button[data-time]");
  const id = btn?.dataset.time;
  if (id !== "5+3" && id !== "24h") return;
  timeControl = id;
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
  if (!draftCardEl.hidden || !lobbyCardEl.hidden) {
    leaveRoom();
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
const inviteCode = readInviteCode();
if (inviteCode) {
  playNet = "online";
  refreshPlayNet();
  joinCodeEl.value = inviteCode;
  void joinOnline();
} else {
  void refreshMyGames();
}
