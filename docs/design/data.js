// Sample data for the CineScout prototype — deterministic, so renders are stable.

export const ACTIVE_MATCHES = [
  {
    id: 1,
    title: "Perfect Days",
    cinema: "Kino am Rathaus", room: "Saal 2",
    datetime: "Fri, Aug 1 · 20:15",
    price: "€9.50",
    matchReasons: ["4 seats free", "Fri evening"],
    posterSlot: "poster-match-1",
    bookingLink: "#",
  },
  {
    id: 2,
    title: "The Zone of Interest",
    cinema: "Lichtspielhaus Altstadt", room: "Saal 1",
    datetime: "Sat, Aug 2 · 19:00",
    price: "€8.00",
    matchReasons: ["2 seats free", "Sat evening"],
    posterSlot: "poster-match-2",
    bookingLink: "#",
  },
];

export const TRACKED_MOVIES_HOME = [
  { id: 11, title: "Anatomy of a Fall", next: { id: 201, datetime: "Sun, Aug 3 · 18:30", room: "Saal 3", status: null } },
  { id: 12, title: "Past Lives", next: null },
  { id: 13, title: "Poor Things", next: { id: 203, datetime: "Mon, Aug 4 · 21:00", room: "Saal 1", status: "soldout" } },
];

export const RECENT_ACTIVITY = [
  { film: "Poor Things", when: "2 hours ago" },
  { film: "Anatomy of a Fall", when: "1 day ago" },
  { film: "Past Lives", when: "3 days ago" },
  { film: "The Holdovers", when: "5 days ago" },
];

const TRACKED_TITLES = ["Anatomy of a Fall", "Poor Things", "Perfect Days"];

const FILM_POOL = [
  { title: "Perfect Days", room: "Saal 2", time: "18:00", status: "available" },
  { title: "Anatomy of a Fall", room: "Saal 3", time: "20:30", status: null },
  { title: "The Zone of Interest", room: "Saal 1", time: "19:00", status: "available" },
  { title: "Poor Things", room: "Saal 1", time: "21:00", status: "soldout" },
  { title: "Killers of the Flower Moon", room: "Saal 4", time: "17:45", status: "cancelled" },
  { title: "The Holdovers", room: "Saal 2", time: "20:00", status: "available" },
  { title: "Dune: Part Two", room: "Saal 1", time: "16:30", status: null },
];

const fmtDay = (d) => d.toLocaleDateString("en-US", { weekday: "short", month: "short", day: "numeric" });
const fmtShort = (d) => d.toLocaleDateString("en-US", { month: "short", day: "numeric" });

// 14-day window, `offset` weeks from today. Days with no performances are omitted entirely.
export function scheduleWindow(offset) {
  const base = new Date();
  base.setHours(0, 0, 0, 0);
  base.setDate(base.getDate() + offset * 7);

  const days = [];
  for (let i = 0; i < 14; i++) {
    const day = new Date(base);
    day.setDate(base.getDate() + i);
    const seed = Math.abs(Math.round(day.getTime() / 86400000) + offset * 3);
    const count = [2, 3, 0, 2, 1, 3, 0][seed % 7];
    if (count === 0) continue;

    const performances = [];
    for (let k = 0; k < count; k++) {
      const f = FILM_POOL[(seed + k * 3) % FILM_POOL.length];
      performances.push({
        id: seed * 10 + k,
        title: f.title,
        datetime: f.time,
        fullDatetime: `${fmtDay(day)} · ${f.time}`,
        cinema: k % 2 === 0 ? "Kino am Rathaus" : "Lichtspielhaus Altstadt",
        room: f.room,
        status: f.status,
        tracking: TRACKED_TITLES.includes(f.title),
      });
    }
    performances.sort((a, b) => a.datetime.localeCompare(b.datetime));

    let label = fmtDay(day);
    if (offset === 0 && i === 0) label = "Today";
    else if (offset === 0 && i === 1) label = "Tomorrow";
    days.push({ label, performances });
  }
  return days;
}

export function windowLabel(offset) {
  const start = new Date();
  start.setHours(0, 0, 0, 0);
  start.setDate(start.getDate() + offset * 7);
  const end = new Date(start);
  end.setDate(start.getDate() + 13);
  return `${fmtShort(start)} – ${fmtShort(end)}`;
}

export const TRACKED_FILMS = [
  {
    id: 11, title: "Anatomy of a Fall",
    performances: [
      { id: 201, datetime: "Sun, Aug 3 · 18:30", cinema: "Kino am Rathaus", room: "Saal 3", status: null },
      { id: 202, datetime: "Tue, Aug 5 · 20:00", cinema: "Kino am Rathaus", room: "Saal 1", status: "available" },
      { id: 205, datetime: "Thu, Aug 7 · 17:15", cinema: "Lichtspielhaus Altstadt", room: "Saal 2", status: "available" },
    ],
  },
  { id: 12, title: "Past Lives", performances: [] },
  {
    id: 13, title: "Poor Things",
    performances: [
      { id: 203, datetime: "Mon, Aug 4 · 21:00", cinema: "Lichtspielhaus Altstadt", room: "Saal 1", status: "soldout" },
    ],
  },
];

// Films with at least one upcoming performance — finished runs are excluded.
export const ALL_FILMS = [
  { id: 21, title: "Dune: Part Two" },
  { id: 22, title: "The Holdovers" },
  { id: 23, title: "Killers of the Flower Moon" },
  { id: 24, title: "The Zone of Interest" },
];

export const TIME_WINDOWS = [
  { id: 1, days: ["Fr", "Sa"], start: "18:00", end: "23:00" },
  { id: 2, days: ["Su"], start: "14:00", end: "20:00" },
];

export const CINEMAS = ["Kino am Rathaus", "Lichtspielhaus Altstadt"];

// Cinema.cs / Room.cs — Konfiguration der gecrawlten Kinos.
export const CINEMA_CONFIG = [
  {
    id: 1,
    externalCinemaId: "kino-am-rathaus",
    name: "Kino am Rathaus",
    crawlBaseUrl: "https://www.kino-am-rathaus.de",
    isActive: true,
    kinoheldCinemaId: "4711",
    lastCrawlAt: "Today, 14:02",
    rooms: [
      { id: 1, name: "Saal 1", externalAuditoriumId: "aud-1001" },
      { id: 2, name: "Saal 2", externalAuditoriumId: "aud-1002" },
      { id: 3, name: "Saal 3", externalAuditoriumId: "aud-1003" },
    ],
  },
  {
    id: 2,
    externalCinemaId: "lichtspielhaus-altstadt",
    name: "Lichtspielhaus Altstadt",
    crawlBaseUrl: "https://www.lichtspielhaus-altstadt.de",
    isActive: true,
    kinoheldCinemaId: "5288",
    lastCrawlAt: "Today, 13:47",
    rooms: [
      { id: 4, name: "Saal 1", externalAuditoriumId: "aud-2001" },
      { id: 5, name: "Saal 2", externalAuditoriumId: "aud-2002" },
    ],
  },
  {
    id: 3,
    externalCinemaId: "traumpalast-nord",
    name: "Traumpalast Nord",
    crawlBaseUrl: "https://www.traumpalast-nord.de",
    isActive: false,
    kinoheldCinemaId: null,
    lastCrawlAt: "Never",
    rooms: [],
  },
];

// Ein Saal ist nur innerhalb seines Kinos eindeutig — deshalb immer als Paar.
export const ROOMS = [
  { cinema: "Kino am Rathaus", room: "Saal 1" },
  { cinema: "Kino am Rathaus", room: "Saal 2" },
  { cinema: "Kino am Rathaus", room: "Saal 3" },
  { cinema: "Lichtspielhaus Altstadt", room: "Saal 1" },
  { cinema: "Lichtspielhaus Altstadt", room: "Saal 2" },
];

export const SEAT_MATRICES_GENERAL = [
  { cinema: "Kino am Rathaus", room: "Saal 1", name: "Sweet spot", rowStart: "A", rowEnd: "D", seatStart: 1, seatEnd: 10, partySize: 4, enabled: true },
  { cinema: "Kino am Rathaus", room: "Saal 2", name: "Back rows", rowStart: "A", rowEnd: "F", seatStart: 1, seatEnd: 14, partySize: 2, enabled: false },
  { cinema: "Lichtspielhaus Altstadt", room: "Saal 1", name: "Centre block", rowStart: "C", rowEnd: "O", seatStart: 1, seatEnd: 20, partySize: 6, enabled: true },
];

export const SEAT_MATRICES_OVERRIDES = [
  { id: 1, film: "Poor Things", cinema: "Lichtspielhaus Altstadt", room: "Saal 1", name: "Premiere seats", rowStart: "B", rowEnd: "C", seatStart: 4, seatEnd: 8, partySize: 4, enabled: true },
  { id: 2, film: "Perfect Days", cinema: "Kino am Rathaus", room: "Saal 2", name: "Quiet corner", rowStart: "D", rowEnd: "F", seatStart: 2, seatEnd: 9, partySize: 2, enabled: false },
];

// Live-availability grid — per-seat status, gaps where the adjacency chain breaks.
export const LIVE_SEAT_GRID = [
  ["free", "free", "sold", "sold", null, "free", "free", "other", "free"],
  ["free", "sold", "sold", "sold", "sold", "free", "free", "free", "free"],
  ["sold", "sold", null, "free", "free", "free", "other", "free", "sold"],
  ["free", "free", "free", "free", null, null, "free", "free", "free"],
];
export const LIVE_SEAT_SUMMARY = { free: 19, total: 32 };

export const PERFORMANCE_DETAIL = {
  id: 203,
  title: "Poor Things",
  cinema: "Lichtspielhaus Altstadt", room: "Saal 1",
  datetime: "Mon, Aug 4 · 21:00",
  price: "€9.00",
  posterSlot: "poster-detail",
  bookingLink: "#",
  isMatch: false,
  isTracked: true,
  matchReasons: [],
};
