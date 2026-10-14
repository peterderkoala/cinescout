repo: peterderkoala/cinescout
branch: dev
path: docs/design

## Last sync
date: 2026-08-10T07:09:31Z
commit: 14121d0c4889

### Updated in this project
- Vokabular-Umbenennung ([Wayfinder-Ticket #87](https://github.com/peterderkoala/cinescout/issues/87)):
  „Site" → „Cinema", „WatchedMovie" → „TrackedMovie" durchgehend in data-entity/data-field-Annotationen,
  sichtbaren Labels, Nav-Links und Sample-Daten, gepusht in CineScout Prototype.dc.html,
  FilmPerformanceCard.dc.html, Technical Design Spec.md und data.js. Diese Umbenennung kehrt eine frühere
  Entscheidung um: „Cinema" war der ursprüngliche Name, wurde in einem späteren Entwurf zu „Site" geändert,
  und wird jetzt zurückgeführt — „Site" war im Web-Entwicklungskontext zu überladen (Website, Deployment-Site,
  statische Site), während die Domäne immer einen physischen Kinostandort meinte. Begründung in Technical
  Design Spec.md §1 und im Repo unter docs/adr/0002-rename-site-to-cinema-and-watchedmovie-to-trackedmovie.md.
- Technical Design Spec.md ergänzt: Design-zu-Code-Vertrag (Tokens, Screens, Komponenten, abgeleitete Werte, bekannte Modell-Lücken) gegen src/cinescout.model/*.cs.
- Prototyp konsistent an das Modell benannt: „Cinema" durchgehend zu „Cinema", Matrix-Name als Pflichtfeld; alle Screens und Formularfelder tragen data-entity / data-field mit exakten C#-Property-Namen.
- Neuer Screen „Cinemas": Konfiguration der gecrawlten Kinos direkt nach src/cinescout.model/Cinema.cs (ExternalCinemaId, Name, CrawlBaseUrl, IsActive, KinoheldCinemaId als aufgelöstes Read-only-Feld) plus Raumliste nach Room.cs (Name, ExternalAuditoriumId).
- Seat Matrices: Dropdown-Filter für Cinema und Room über beiden Tabs, inkl. Trefferzähler und Leerzustand.
- Seat Matrices an das echte Modell angeglichen: Saal ist jetzt an ein Kino gebunden (cinema + room), Matrizen tragen ein Name-Feld, und die Präzedenz „film-spezifisch schlägt allgemein" ist auf beiden Kartentypen sichtbar.
- Design Manual.dc.html ergänzt: Farbrollen, Abstufungen, Dark-Mode-Token, Schriften und Skala.
- Gestaltungs-Feinschliff: Contentbreite 1536 px, Navbar-Padding/Logo, Dark-Mode-Akzent #D1AB5B, Success #328168, neutrale Outline-Buttons im Dark Mode.

## Sync history
### 2026-08-10T07:09:31Z
- Vokabular-Umbenennung „Site" → „Cinema", „WatchedMovie" → „TrackedMovie" (Wayfinder-Ticket #87) über
  CineScout Prototype.dc.html, FilmPerformanceCard.dc.html, Technical Design Spec.md und data.js gepusht.

### 2026-08-07T19:19:31Z
- Seat Matrices an das echte Modell angeglichen (cinema + room, Name-Feld, Präzedenz sichtbar).
- Design Manual.dc.html ergänzt; gestalterischer Feinschliff (Contentbreite, Navbar, Dark-Mode-Token).

### 2026-07-28T20:07:00Z
- Built CineScout Prototype.dc.html: navbar shell + Home, Schedule, Tracked Movies, Time Preferences, Seat Matrices, Performance Detail, Login, Setup — light mode, Bootstrap 5.3 + custom burgundy/gold accents, Montserrat/Open Sans.
- Split shared FilmPerformanceCard.dc.html (compact-row + featured variants) and SeatGrid.dc.html (zone-preview + live-availability modes) per components/*.md.
- Sample data in data.js (films, performances, tracked list, time windows, seat matrices, live seat grid).

## Screen map
| Screen | Source docs |
|---|---|
| Navbar shell | docs/design/layout-navigation.md |
| Home | docs/design/home.md |
| Schedule | docs/design/schedule.md |
| Tracked Movies | docs/design/tracked-movies.md |
| Time Preferences | docs/design/time-preferences.md |
| Cinemas | src/cinescout.model/Cinema.cs, src/cinescout.model/Room.cs |
| Seat Matrices | docs/design/seat-matrices.md, docs/design/components/seat-grid.md |
| Performance Detail | docs/design/performance-detail.md, docs/design/components/seat-grid.md |
| Login / Setup | docs/design/auth.md |
| FilmPerformanceCard | docs/design/components/film-performance-card.md |
| SeatGrid | docs/design/components/seat-grid.md |
| Colors/type/spacing | docs/design/00-design-system.md |
| Technical Design Spec.md | src/cinescout.model/*.cs |
