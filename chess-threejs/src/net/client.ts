import type { ClientMessage, ServerMessage } from "./protocol";

export function wsUrl(): string {
  const proto = location.protocol === "https:" ? "wss:" : "ws:";
  const path = new URL(".", location.href).pathname.replace(/\/$/, "");
  return `${proto}//${location.host}${path}/ws`;
}

export class NetClient {
  private socket: WebSocket | null = null;
  onMessage: ((msg: ServerMessage) => void) | null = null;
  onClose: (() => void) | null = null;

  get connected(): boolean {
    return this.socket?.readyState === WebSocket.OPEN;
  }

  connect(): Promise<void> {
    this.close();
    return new Promise((resolve, reject) => {
      const socket = new WebSocket(wsUrl());
      this.socket = socket;
      socket.onopen = () => resolve();
      socket.onerror = () => reject(new Error("Could not connect to the game server"));
      socket.onclose = () => this.onClose?.();
      socket.onmessage = (event) => {
        try {
          this.onMessage?.(JSON.parse(String(event.data)) as ServerMessage);
        } catch {
          /* ignore malformed frames */
        }
      };
    });
  }

  send(message: ClientMessage): void {
    if (!this.socket || this.socket.readyState !== WebSocket.OPEN) {
      throw new Error("Not connected");
    }
    this.socket.send(JSON.stringify(message));
  }

  close(): void {
    if (!this.socket) return;
    this.socket.onclose = null;
    this.socket.close();
    this.socket = null;
  }
}
