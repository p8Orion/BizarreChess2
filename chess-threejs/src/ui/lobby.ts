import type { PublicLobby } from "../net/protocol";
import { createStyleFields } from "./styleFields";
import type { PlayerStyle } from "../core/colors";
import { t } from "../i18n";

export interface LobbyHooks {
  armies: { id: string; label: string }[];
  selectedArmyId: string;
  onArmy: (id: string) => void;
  onEditArmy: () => void;
  onStyle: (style: PlayerStyle) => void;
  onReady: () => void;
}

export function renderLobby(root: HTMLElement, lobby: PublicLobby, localPlayerId: number, hooks: LobbyHooks): void {
  const mine = lobby.seats[localPlayerId];
  const otherId = localPlayerId === 0 ? 1 : 0;
  const other = lobby.seats[otherId];
  root.replaceChildren();
  root.hidden = false;

  const hint = document.createElement("p");
  hint.className = "draft-hint";
  if (!lobby.seats[1].connected && localPlayerId === 0) hint.textContent = t("lobby.waitingJoin");
  else if (!other.connected) hint.textContent = t("lobby.oppDisconnected");
  else if (mine.ready && other.ready) hint.textContent = t("lobby.starting");
  else if (mine.ready) hint.textContent = t("lobby.readyWait");
  else hint.textContent = t("lobby.pickReady");
  root.append(hint);

  const you = document.createElement("section");
  you.className = "lobby-seat";
  const youHead = document.createElement("div");
  youHead.className = "sep";
  youHead.textContent = t("lobby.you");
  const army = document.createElement("label");
  army.className = "army";
  const armyTag = document.createElement("span");
  armyTag.textContent = t("lobby.army");
  const select = document.createElement("select");
  for (const option of hooks.armies) {
    const opt = document.createElement("option");
    opt.value = option.id;
    opt.textContent = option.label;
    select.append(opt);
  }
  select.value = hooks.selectedArmyId;
  select.disabled = mine.ready;
  select.addEventListener("change", () => hooks.onArmy(select.value));
  army.append(armyTag, select);
  const edit = document.createElement("button");
  edit.type = "button";
  edit.className = "ghost";
  edit.textContent = t("lobby.editArmy");
  edit.disabled = mine.ready;
  edit.addEventListener("click", hooks.onEditArmy);
  you.append(youHead, army, edit, createStyleFields(mine.style, hooks.onStyle, t("lobby.colors"), !mine.ready));
  root.append(you);

  const opp = document.createElement("section");
  opp.className = "lobby-seat is-opp";
  const oppHead = document.createElement("div");
  oppHead.className = "sep";
  oppHead.textContent = t("lobby.opponent");
  const oppStatus = document.createElement("p");
  oppStatus.className = "lobby-opp";
  const bits = [other.connected ? t("lobby.inRoom") : t("lobby.notConnected")];
  if (other.armyLabel) bits.push(other.armyLabel);
  bits.push(other.ready ? t("lobby.ready") : t("lobby.picking"));
  oppStatus.textContent = bits.join(" · ");
  opp.append(oppHead, oppStatus, createStyleFields(other.style, () => undefined, t("lobby.colors"), false));
  root.append(opp);

  const ready = document.createElement("button");
  ready.type = "button";
  ready.textContent = mine.ready ? t("lobby.readyOn") : t("lobby.readyBtn");
  ready.classList.toggle("is-on", mine.ready);
  ready.setAttribute("aria-pressed", mine.ready ? "true" : "false");
  ready.addEventListener("click", hooks.onReady);
  root.append(ready);
}
