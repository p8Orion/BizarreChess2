export interface MatchSeat {
  code: string;
  token: string;
  playerId: number;
}

const KEY = "bizarre-chess.match-seat.v1";
const MAX_CODES = 20;

interface SeatBook {
  last?: MatchSeat;
  byCode: Record<string, MatchSeat>;
}

function emptyBook(): SeatBook {
  return { byCode: {} };
}

function readBook(): SeatBook {
  try {
    const raw = localStorage.getItem(KEY);
    if (!raw) return emptyBook();
    const parsed = JSON.parse(raw) as Partial<SeatBook>;
    const byCode =
      parsed.byCode && typeof parsed.byCode === "object" ? (parsed.byCode as Record<string, MatchSeat>) : {};
    const last = parsed.last && typeof parsed.last.code === "string" ? parsed.last : undefined;
    return { last, byCode };
  } catch {
    return emptyBook();
  }
}

function writeBook(book: SeatBook): void {
  const codes = Object.keys(book.byCode);
  if (codes.length > MAX_CODES) {
    const keep = new Set(book.last ? [book.last.code] : []);
    for (const code of codes.slice(-MAX_CODES + keep.size)) keep.add(code);
    for (const code of codes) {
      if (!keep.has(code)) delete book.byCode[code];
    }
  }
  localStorage.setItem(KEY, JSON.stringify(book));
}

export function allSeats(): MatchSeat[] {
  const book = readBook();
  const last = book.last?.code;
  return Object.values(book.byCode).sort((a, b) => {
    if (a.code === last) return -1;
    if (b.code === last) return 1;
    return a.code.localeCompare(b.code);
  });
}

export function lastMatchSeat(): MatchSeat | null {
  return readBook().last ?? null;
}

export function seatForCode(code: string): MatchSeat | null {
  return readBook().byCode[code.trim().toUpperCase()] ?? null;
}

export function saveMatchSeat(seat: MatchSeat): MatchSeat {
  const book = readBook();
  const next = { code: seat.code.trim().toUpperCase(), token: seat.token, playerId: seat.playerId };
  book.last = next;
  book.byCode[next.code] = next;
  writeBook(book);
  return next;
}

export function removeMatchSeat(code: string): void {
  const book = readBook();
  const key = code.trim().toUpperCase();
  delete book.byCode[key];
  if (book.last?.code === key) {
    const rest = Object.values(book.byCode);
    book.last = rest[rest.length - 1];
  }
  writeBook(book);
}
