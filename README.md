# WatchingYou — modular monolith movie rental

One solution, two front ends: ASP.NET Core (.NET 10) serving Minimal APIs and Razor Pages, with React + Tailwind islands mounted inside the Razor shell. It opens in Visual Studio 2026 and in VS Code without changing anything.

---

## Run it

### Prerequisites

| Tool | Why | Note |
|---|---|---|
| .NET 10 SDK | Builds everything | `dotnet --version` should print 10.x |
| SQL Server LocalDB | Default database | Ships with the VS "Data storage and processing" workload |
| Node 20+ | Only if you want to change the React code | The built bundle is committed, so you can skip this at first |

### Visual Studio 2026

1. Open `WatchingYou.sln`.
2. **MovieRental.Host** is already the startup project.
3. Press F5.

Solution Explorer shows `src` (with a `Modules` folder inside), plus two solution
folders: **Solution Items** for the root config files and **client** for the React
entry points. To browse the whole React tree, use the Folder View toggle at the top
of Solution Explorer.

`MovieRental.Host.csproj` has a `BuildReactClient` target that runs `npm run build`
before the .NET build, so F5 always picks up your latest React changes. It only fires
if `client/node_modules` exists, so a machine without Node still builds against the
committed bundle. Opt out with `/p:SkipClientBuild=true`.

Useful while you work: **View → SQL Server Object Explorer** to inspect the five
schemas in `MovieRentalDB`, and **Tools → NuGet Package Manager → Package Manager
Console** for the `dotnet ef` commands further down.

### VS Code

```bash
dotnet restore
dotnet run --project src/MovieRental.Host
```

Or press F5 and pick **Run the site**. The `.vscode` folder has the build task and launch configs already.

Either way the site comes up at **https://localhost:7139**, the API reference at **/swagger**.

### Signing in

The first run creates the database, the schemas and two accounts:

| Role | E-mail | Password |
|---|---|---|
| Admin | `admin@reelandrow.test` | `Admin1234` |
| Security | `security@reelandrow.test` | `Security1234` |
| Customer | `customer@reelandrow.test` | `Customer1234` |

Verification codes and e-mails are printed to the API console until you configure a real
transport — see **Verification and delivery** below.

The development bootstrapper carries a schema stamp. When an entity changes shape the stamp
moves, and the next start drops and rebuilds `MovieRentalDB` rather than leaving a schema that
is present but out of date. That is development-only; production uses real migrations.

### Working on the React side

```bash
cd client
npm install
npm run build     # writes src/MovieRental.Host/wwwroot/app/{app.js,app.css}
```

For hot reload, run `npm run dev` in one terminal and launch the API with
`Frontend__UseDevServer=true`. VS Code's **Run the site (with Vite hot reload)** config
does both for you; in Visual Studio, add the variable under Project Properties → Debug →
Open debug launch profiles UI → Environment variables. The Razor layout then loads
modules straight from the Vite dev server instead of the built bundle.

You can also work in both IDEs at once — Visual Studio for the C#, VS Code for the
React — since neither writes anything the other cares about.

---

## How it is put together

```
WatchingYou.sln
├── Directory.Build.props        net10.0, nullable, implicit usings for every project
├── Directory.Packages.props     central package versions
├── src/
│   ├── MovieRental.SharedKernel/        no module depends on another module, only on this
│   │   ├── Abstractions/                BaseEntity, ISoftDeletable
│   │   ├── Contracts/                   ICatalogApi, IUserDirectory, IEmailSender, ISmsSender
│   │   ├── Cqrs/                        ICommand, IQuery, Dispatcher, pipeline behaviours
│   │   ├── Persistence/ModuleDbContext   schema-per-module, soft delete, audit stamps
│   │   ├── Results/                     Result, Error, PagedResult
│   │   └── Security/                    ICurrentUser, AppRoles, Base64UrlText
│   ├── Modules/
│   │   ├── MovieRental.Modules.Identity/   schema "identity"
│   │   ├── MovieRental.Modules.Catalog/    schema "catalog"
│   │   ├── MovieRental.Modules.Rentals/    schema "rentals"
│   │   ├── MovieRental.Modules.Cinema/     schema "cinema"
│   │   └── MovieRental.Modules.Media/      schema "media"
│   └── MovieRental.Host/
│       ├── Program.cs                   composition root, JWT, Swagger, pipeline
│       ├── Pages/                       Razor shells, strongly-typed view models only
│       ├── Middleware/                  exception → ProblemDetails
│       ├── Infrastructure/              CurrentUser, dev DB bootstrap, analytics
│       └── wwwroot/                     shell.css + the built React bundle
└── client/                              Vite + React 18 + TypeScript + Tailwind v4
    └── src/components/ui/stack-spread.tsx
```

Each module is a folder of **vertical slices**. A slice file holds its command or query, its validator, its handler and its route — everything that changes together, changing in one place. `Program.cs` names the five modules and nothing else about them:

```csharp
builder.Services.AddModules(
    builder.Configuration,
    new IdentityModule(), new CatalogModule(), new RentalsModule(),
    new CinemaModule(), new MediaModule());
```

### Architecture notes

**Why a hand-rolled dispatcher.** `SharedKernel/Cqrs/Dispatcher.cs` is about 80 lines and resolves handlers through DI with a cached reflection lookup, wrapping each in the registered pipeline behaviours. A third-party mediator would add a dependency for roughly the same code and hide the pipeline behind an extension method.

**ACID.** Every module writes through EF Core against one SQL Server database, so a slice is one transaction. Three places do the real work:

- `CatalogApi.TryReserveCopyAsync` decrements stock with a conditional `UPDATE … WHERE AvailableCopies > 0` rather than read-then-write, so two customers racing for the last copy cannot both win.
- `SeatBooking` carries a filtered unique index on `(ScreeningId, Row, Number)`. Both racing bookings pass the availability read; only one survives the index, and the loser gets a clean "that seat just went".
- `SeedMoviesCommand` wraps the whole import in one transaction — a half-imported catalogue is worse than none.

**CAP.** One SQL Server instance is a CP system: during a failover the API returns errors instead of stale data. That is the right trade here, because a rental that double-books the last copy is worse than a rental that fails and can be retried. Renting crosses two schemas without a distributed transaction; if the rental row fails to write, a compensating `ReleaseCopyAsync` puts the copy back.

**Soft delete.** Entities implementing `ISoftDeletable` get `HasQueryFilter(e => !e.IsDeleted)` applied automatically in `ModuleDbContext`, and `Remove()` is rewritten into an `UPDATE`. The admin restore screen is the one caller that opts out, with `IgnoreQueryFilters()`.

**Tokens.** Access tokens are short-lived JWTs built by `JwtSecurityTokenHandler`. Refresh tokens are rows in `identity.RefreshTokens` with rotation: using one revokes it and points it at its replacement. A revoked token coming back means the chain leaked, so every live token for that user is revoked at once.

### Why some code looks repeated

**Endpoints map `Error.Code` to a result by hand** — twenty-four `switch` expressions that look
alike. They could collapse into one helper returning `IResult`, and that would read shorter.
It would also erase the return types: each endpoint declares
`Results<Ok<T>, NotFound<Error>, Conflict<Error>>`, and those typed unions are what tell
Swagger, and anyone calling the API, exactly which responses are possible. A shared `IResult`
helper documents every endpoint as "returns something". The repetition is the price of an
honest contract, and it is paid knowingly.

**Dialogs share one frame**, `components/ui/modal.tsx`: overlay, click-outside, Escape, and
locking the page scroll. Two dialogs keep their own because they are genuinely different — the
film dialog animates in, and the 3D hall preview is full-screen with no backdrop to click.

## Extracting over an old copy

A zip overwrites what changed but leaves behind what was **deleted**. A stale `.cs` file still
compiles into the project and fails against code that has moved on — that is where `CS0101`
(duplicate definition) and `CS0117` (missing member) come from after an update.

Delete the folder before extracting. When that is inconvenient:

```powershell
.\scripts\clean-stale.ps1
```

It removes the files this version no longer has, clears `bin` and `obj`, and says what it did.

## When LocalDB is rebuilt

The standard fix for a LocalDB instance that will not start is to delete and recreate it. The
new instance forgets the database, but its `.mdf` and `.ldf` stay in the user profile folder.
EF's `EnsureDeleted` only drops what the instance knows about, so it cannot see them, and the
next `CREATE DATABASE` fails with *"Cannot create file … because it already exists"*.

The bootstrapper now clears those files itself before creating the database. It removes only
files named exactly for this database, and only when the instance has no such database
registered — in that state they belong to nothing. If one is locked, the error names the file
and says what to close, instead of surfacing later as a message that points at neither.

## Migrations

The development bootstrapper creates the schemas on first run so you can start with only a connection string. For anything beyond that, each module owns its own migration history:

```bash
dotnet ef migrations add Init -c CatalogDbContext -o Persistence/Migrations \
  -p src/Modules/MovieRental.Modules.Catalog -s src/MovieRental.Host

dotnet ef database update -c CatalogDbContext -s src/MovieRental.Host
```

Repeat per context: `IdentityDbContext`, `CatalogDbContext`, `RentalsDbContext`, `CinemaDbContext`, `MediaDbContext`.

---

## Features and where they live

| Feature | Code |
|---|---|
| Registration, e-mail/SMS verification | `Modules.Identity/Features/{Register,Login,Verification}.cs`, `Infrastructure/VerificationService.cs` |
| Catalogue browsing, search, filters | `Modules.Catalog/Features/GetMovies.cs` |
| Rent, extend, return, late fees | `Modules.Rentals/Features/`, `Domain/LateFeePolicy.cs` |
| Admin inventory, soft delete, restore | `Modules.Catalog/Features/ManageMovies.cs` |
| Ratings and reviews | `Modules.Catalog/Features/AddReview.cs` |
| Due-date notifications | `Modules.Rentals/Infrastructure/DueDateNotificationService.cs` |
| Cinema seat map and booking | `Modules.Cinema/Features/{SeatMap,BookSeats}.cs` |
| Card checkout, e-mailed code, QR ticket | `Modules.Cinema/Features/BookSeats.cs`, `Domain/SeatPayment.cs` |
| Movies on Display + screening admin | `Modules.Cinema/Features/{MoviesOnDisplay,ManageScreenings}.cs` |
| Short-film upload | `Modules.Media/Features/UploadShortFilm.cs` |
| Studio workspace, visibility, threads | `Modules.Media/Features/StudioWorkspace.cs` |
| Authorised video streaming | `Modules.Media/Features/StreamShortFilm.cs` |
| AI Catalog and Human Craft galleries | `Modules.Media/Features/Galleries.cs` |
| Security review pipeline | `Modules.Media/Features/SecurityReview.cs`, `Domain/SecurityReport.cs` |
| Admin decision and verdict e-mail | `Modules.Media/Features/ReviewShortFilm.cs` |
| Four-language interface | `Host/Infrastructure/Localization/`, `Host/locales/*.json` |
| Dashboard analytics | `Modules.{Catalog,Rentals}/Infrastructure/*Analytics.cs` |

## Roles

| Role | Can do |
|---|---|
| Customer | Rent, review, book seats, upload shorts, run their own Studio |
| Security | Everything a customer can, plus inspect submissions and file reports |
| Admin | Everything, plus inventory, screenings, final approval and the API reference |

Nav items a role may not use are never rendered, and every route is independently
protected server-side — hiding a link is presentation, not security.

## Review pipeline

```
Pending ──claim──> UnderSecurityReview ──report──> SecurityCleared ──admin──> Approved
                                              \                         \
                                               ─> SecurityFlagged  ────────> Rejected
```

Security inspects against a stored eight-point checklist and cannot publish. Admin publishes
and cannot act without a filed report. Approving a flagged film requires a written reason,
which travels to the uploader in the decision e-mail. The three-day deadline covers the whole
pipeline, not each stage.

A film reaches a public gallery only when it is **Approved** *and* its author has set it to
**Public**. Which gallery is decided by the origin declared at upload — AI Catalog or Human
Craft. Video is served through an authorising endpoint, never as a static file, so a private
film cannot be reached by guessing its URL.

## Why some code looks repeated

**Endpoints map `Error.Code` to a result by hand** — twenty-four `switch` expressions that look
alike. They could collapse into one helper returning `IResult`, and that would read shorter.
It would also erase the return types: each endpoint declares
`Results<Ok<T>, NotFound<Error>, Conflict<Error>>`, and those typed unions are what tell
Swagger, and anyone calling the API, exactly which responses are possible. A shared `IResult`
helper documents every endpoint as "returns something". The repetition is the price of an
honest contract, and it is paid knowingly.

**Dialogs share one frame**, `components/ui/modal.tsx`: overlay, click-outside, Escape, and
locking the page scroll. Two dialogs keep their own because they are genuinely different — the
film dialog animates in, and the 3D hall preview is full-screen with no backdrop to click.

## Extracting over an old copy

A zip overwrites what changed but leaves behind what was **deleted**. A stale `.cs` file still
compiles into the project and fails against code that has moved on — that is where `CS0101`
(duplicate definition) and `CS0117` (missing member) come from after an update.

Delete the folder before extracting. When that is inconvenient:

```powershell
.\scripts\clean-stale.ps1
```

It removes the files this version no longer has, clears `bin` and `obj`, and says what it did.

## When LocalDB is rebuilt

The standard fix for a LocalDB instance that will not start is to delete and recreate it. The
new instance forgets the database, but its `.mdf` and `.ldf` stay in the user profile folder.
EF's `EnsureDeleted` only drops what the instance knows about, so it cannot see them, and the
next `CREATE DATABASE` fails with *"Cannot create file … because it already exists"*.

The bootstrapper now clears those files itself before creating the database. It removes only
files named exactly for this database, and only when the instance has no such database
registered — in that state they belong to nothing. If one is locked, the error names the file
and says what to close, instead of surfacing later as a message that points at neither.

## Migrations

The development bootstrapper drops and rebuilds the database whenever the model changes. That
is fine until you care about the data in it — and on this project that point arrived quickly.

```powershell
.\scripts\migrations.ps1 -Add Init      # every module
.\scripts\migrations.ps1 -Update
```

Then set `Database:UseMigrations` to `true` in `appsettings.Development.json`. The bootstrapper
steps aside and migrates instead of dropping: a new column is added to the database you
already have, rather than costing you every account and booking in it.

After that, a model change is one module's problem:

```powershell
.\scripts\migrations.ps1 -Add AddSomething -Only Catalog
.\scripts\migrations.ps1 -Update -Only Catalog
```

Each module has its own `__EFMigrations` table in its own schema. There is no single history
for the solution, which is the point of the split.

## Continuous integration

`.github/workflows/ci.yml` runs on every push: restore, build, and the unit tests on the .NET
side; `npm ci`, type-check and build on the client. Integration tests are filtered out because
they need SQL Server; the unit tests deliberately need nothing, which is what makes them worth
running every time.

The client has no test runner yet, so `tsc --noEmit` is the gate. It catches the class of bug
that actually happens here: a contract changing on the server while the client still reads the
old shape.

## Error pages

`/error/{code}` handles 401, 403, 404, 429 and everything else, in all four languages.

Two routes reach it. A missing page never throws, so `UseStatusCodePagesWithReExecute` sends
it there; a thrown exception on a page request is redirected there by the exception
middleware. Both are scoped away from `/api`, because a `fetch` expecting JSON should not be
handed a page of HTML to parse — API calls still get RFC 7807 ProblemDetails as before.

**Rendered entirely by Razor, with no React island.** The moment you most need an error page
is the moment the bundle failed to load, and a page that depends on the same JavaScript that
just broke is not an error page. It carries the half-closed eye from the wordmark, the same
travelling green beam as the search fields, and the trace identifier — quiet until somebody is
reporting the problem.

The response keeps its real status code. A 404 rendered with a 200 header tells every crawler
and monitor that the page was fine.

## One URL for the bundle

The layout used to load the entry script with `asp-append-version`, which appends `?v=hash`.
Every lazily loaded chunk imports the entry back as plain `./app.js`, with no query — and to a
browser those are two different modules. The entry evaluated twice, a second copy of React
appeared, and any page that loaded a chunk crashed on its first hook. The map on Movies on
Display was one of them; its failure was first put down to WebGL, and the error boundary
added then hid the crash rather than exposing it.

The script is now loaded under one URL, and freshness comes from `Cache-Control: no-cache`
on `/app` instead: the browser revalidates on each load, which is a cheap 304 when nothing has
changed and the new build the moment something has.

The smoke test had the same flaw — it imported `app.js?p=<page>` to get a fresh module per
page — which is how it first reported this. Each page now runs in its own process under the
real URL, the way a browser loads it.

## Smoke test

```bash
cd client && npm run smoke          # every page
npm run smoke -- globe              # one page
```

Mounts every React island in a fake DOM and fails if one throws or renders nothing.

It exists because of a real failure: a component that could not start WebGL took its whole
page down, leaving a blank screen and no message. `tsc` and `vite build` both passed — nothing
catches a crash that happens only when the code runs. jsdom has no WebGL, which makes it a
good stand-in for the machines where that actually happens.

Every island is now wrapped in an error boundary, so a widget that fails shows a contained
message and the rest of the page keeps working.

## Tests

```bash
dotnet test                                  # everything
dotnet test --filter Category!=Integration   # unit tests only, no database needed
```

| File | What it pins down |
|---|---|
| `LateFeePolicyTests` | Grace period, part-days rounding up, the ten-day cap, and that a returned rental is judged by its return date rather than by now |
| `CardValidationTests` | Luhn, both Mastercard ranges including the 2017 2-series, expiry edges, three-digit CVC |
| `CodeHashingTests` | Codes are stored hashed, salted per code, and a hash from one salt never verifies against another |
| `SlugFactoryTests` | Azerbaijani diacritics fold to ASCII, punctuation collapses, the year keeps remakes apart |
| `ValidationBehaviorTests` | An invalid message never reaches its handler, and every failure is reported at once |
| `SeatConcurrencyTests` | Two simultaneous checkouts for one seat: exactly one wins |

`SeatConcurrencyTests` needs LocalDB and creates a throwaway database per run. It has to hit
real SQL Server, because the guarantee under test *is* the filtered unique index — an
in-memory provider would accept both rows and prove nothing. That is also why the assertion
is on the database row count, not on what the handler returned.

The rest run anywhere, in well under a second, because the logic they cover was written as
pure functions with no clock and no database of their own.

## Help service

A live chat between a customer and the Security desk — the model a bank's chat window uses,
not a ticket queue. One thread per customer that they keep coming back to, rather than a new
numbered case for every question.

**Customer:** *Help Service* in the nav. Type, and the desk answers in the same window.
**Desk:** *Security → Requests*. Who is waiting on the left, the conversation on the right,
with an unread count and the last thing each person said.

Security and Admin accounts do not get the Help Service link, and the inbox excludes their own
thread. The desk **is** the Help Service; a link inviting an agent to open a conversation with
themselves is a link to nowhere.

### Reads do not write

The customer's window polls every four seconds. That poll used to mark the desk's replies as
seen — a database write on every tick, racing the customer's own send, which is where
*"Someone else changed this first. Reload and retry."* came from. A support chat that asks you
to reload is not a support chat.

The customer's read is now pure. The read receipt only ever fed the desk's unread count, so
the customer's side never needed one. The desk's side marks a thread read with a single
set-based `ExecuteUpdateAsync`: nothing is tracked, so two agents opening the same conversation
cannot collide. Sending appends one row instead of loading every message to add to it.

A 409 that does slip through is retried once in the background rather than shown.

### Two accounts in one browser

Testing a support chat means being signed in twice at once — the customer in one window, the
desk in another. The refresh token used to live under one `localStorage` key, which every tab
on the origin shares: whoever signed in last owned it, and the other tab silently became that
account the moment its short-lived access token expired. Replies then appeared to vanish,
because the page was asking on behalf of the wrong person.

The token is now kept in `sessionStorage`, which is per tab, and mirrored to `localStorage` so
an ordinary single-tab visit still survives a browser restart.

New messages arrive by polling every five seconds. A socket would be tidier, but polling one
small thread is cheap, survives a dropped connection with no reconnect logic, and needs
nothing added to the host — and nobody notices five seconds when a person is typing the reply.

### What the desk sees

The sender's **name, e-mail and what they wrote**. Taken from the signed-in session, never
from a form field, so an address cannot be forged.

Nothing else about the account crosses over: no roles, no phone, no bookings, and **no
password**. Passwords are not stored in readable form anywhere and would not be shown here if
they were. Staff answering a question do not need credentials, and a support screen that
displayed them would cost exactly the trust a help desk runs on. The page says so plainly, in
all four languages, because the worry is a reasonable one.

## The globe

Signed-in members can put themselves on a globe and find people whose taste matches theirs.

**Appearing is a choice.** `ShareOnGlobe` is off until somebody turns it on, and turning it off
does not merely hide the row — it clears the stored city, because a record that still holds
where somebody lives is still holding it. The browser is never asked for a position: the city
is typed, and coordinates come from a small gazetteer, so a pin is the city's point and
identical for everyone in it. A map that could point at somebody's street is a different and
much worse product.

**Cities are aggregated in SQL**, not sent one row per member: three faces and a count, so a
city of a million costs exactly what a city of three costs and the payload grows with the
number of cities rather than the number of people. The member list under a city is paged,
fifty at a time.

The globe itself is CSS — one image scrolled behind a circular mask, with inset shadows
standing in for a sphere. It is a disc, not a projection, so there is nowhere honest to put a
pin; the cities are a row of buttons beneath it instead. That swap removed
`@react-three/fiber`, `@react-three/drei` and two Earth textures from the project. `three`
stays, because the 3D hall preview genuinely needs it.

### The crowd

A band of people walking under the header opens the globe page, with *Meet on WatchingYou*
sliding across them. It is decoration and nothing more: `pointer-events: none` and
`aria-hidden` throughout, so clicks pass through and a screen reader is not read a moving line
on a loop. The Open Peeps artwork is black line on white; inverted, it becomes pale linework on
the site's dark ground. Figures are scaled to the band — the original drew them at the sprite
sheet's own size, which suits a full-screen canvas and cuts heads off in a strip a few hundred
pixels tall. `gsap` loads with this page only.

Adapted from **Skiper UI** (Skiper 39, by @gurvinder-singh02), itself after a CodePen by
Zadvorsky; illustrations by [Open Peeps](https://www.openpeeps.com/). The free Skiper licence
asks for attribution, which is kept in the component's header and here.

### Messaging

**Message** beside a member on the globe opens a conversation. Three rules hold it up, and
none of them are optional in a feature that lets strangers reach strangers:

- **Only people who put themselves on the globe can be written to.** Appearing there is a
  choice; being reachable follows from that, not from having registered.
- **Blocking is one-sided and instant.** It is a decision about who may reach you, not a
  negotiation. A blocked sender is told the person is not accepting messages — never that they
  were blocked, which would only invite a second account.
- **Reporting sends the message to the Security desk and blocks at the same time.** Nobody
  should keep receiving from somebody they have just reported while the desk catches up. The
  text is copied into the report, so it survives the sender deleting it.

Both controls sit in the conversation header rather than behind a menu: the moment somebody
needs them is not the moment to make them go looking. The desk reviews them under
**Security → Reports**, and resolving one leaves the row and its audit entry in place.

The pair of participants is stored sorted, with a unique index on it — otherwise two people
opening a conversation at the same moment end up talking in separate rooms.

**Compare movie categories you watched** puts the two genre histograms on one scale and gives a
match percentage. It is a Jaccard overlap over genres, not titles: two people who have watched
no film in common can still both live on westerns and documentaries, and that is the useful
thing to know before starting a conversation. Catalog computes it because Catalog owns genres;
Rentals supplies the film ids and never learns what a genre is.

The Earth texture is generated into `wwwroot/globe/` rather than pulled from a CDN — a map that
silently turns into a grey ball when somebody else's CDN moves is worse than no map. It is a
coarse land/ocean map, which is all a texture at this scale can show.

**Not built: messaging.** Finding someone is not talking to them. A chat worth shipping needs
delivery, moderation, blocking and abuse reporting, and bolting a message box onto this without
those would be worse than leaving it out.

## The map

Movies on Display opens with the four cinemas pinned on a dark MapLibre basemap. Clicking a
pin filters the listing below to that building; clicking it again clears the filter. A map you
cannot act on is a picture, so the pin is the filter control.

Adapted from the mapcn marker component, with the light/dark switching removed — this site has
one theme, and the original carried a MutationObserver and a media-query listener to follow a
class that never changes. The Carto basemap needs no API key, which is why it is preferred over
Mapbox here. MapLibre loads through `React.lazy`, so its ~280 KB gzipped stays out of every
page that has no map, exactly as Three.js does.

Coordinates live on the venue and are editable. The seeded values put each pin in the right
neighbourhood; they are approximations, not surveyed positions.

## Cinemas, halls and the 3D preview

Four cinemas, four rooms each. `Venue` is the building; `Hall` is the room, and it carries the
measurements — row pitch, rise, screen width and curvature, distance to the first row, room
dimensions. A screening belongs to a hall and snapshots its seat grid, so re-fitting a room
later never invalidates seats already sold.

Pick one or more seats, press **View from here**, and the room is rebuilt in 3D with the
camera at eye height in that seat. The readout gives the distance, the angle the screen
subtends and the sideways offset, then a verdict: *too close · close · ideal · good · far*.

The camera turns but never moves. Orbit controls would let someone drift out of the seat and
end up answering a different question from the one they asked; dragging turns the head, with
pitch clamped to roughly a neck's range. Picking several seats puts arrows in the header —
and binds the arrow keys — so you can compare A2 against A3 without closing the view.

The scene was designed in Claude Design and ported off that tool's `three-d-stage` runtime
onto plain Three.js. Two things changed in the move:

- Every dimension now comes from the hall record instead of being hardcoded. The sixteen
  rooms are genuinely different, and a preview that drew the same box for all of them would
  be decoration rather than information.
- Three.js loads through a dynamic `import()`, so its ~190 KB gzipped arrives only when
  somebody opens the preview. Vite splits it into its own chunk; every other page is
  unaffected. The scene disposes its geometries, materials and textures on close — leaving
  them behind would leak a whole room per viewing.

## Booking and payment

Choosing seats and owning them are two different things, so booking is two steps.

1. **Checkout.** The card is validated — Luhn, brand prefix, expiry, CVC length — then the
   seats are written to the database *unconfirmed*, under a `SeatPayment`, and a six-digit
   code is e-mailed. Writing the rows immediately is what makes the hold real: the unique
   index on `(ScreeningId, Row, Number)` is the only thing that can truthfully stop two
   people paying for the same seat, and an in-memory reservation would not be covered by it.
2. **Confirm.** The code turns the hold into a ticket with a reference and a QR code.

Holds last fifteen minutes, and a background sweeper releases the ones nobody confirmed.
Releasing a seat is a soft delete: the unique index is filtered to `[IsDeleted] = 0`, so the
row drops out of the constraint and the seat is sellable again while the abandoned attempt
stays on record. An unfinished checkout is also recoverable — `/api/bookings/pending` returns
it, so a reload or a different device still finds the way back to the code screen.

**The card is never stored.** The number and CVC are validated and discarded inside the
handler; only the brand, the last four digits and the cardholder name are persisted. There is
no acquirer and no money moves — this is a teaching implementation, and the UI says so. What
it does model correctly is the part students usually get wrong: never keeping the PAN.

Signing in is required before checkout. Pressing Confirm while signed out sends the visitor
to `/account?returnUrl=…` and back to the same performance afterwards.

## Profile and SMS

**Account → Profile** sets the display name and the phone number. This is what makes SMS
verification reachable at all: the phone field at registration is optional, and until this
existed anyone who skipped it had no way to add one later, so the SMS button never appeared.

Changing the number clears its confirmation — a new number is an unproven number, and carrying
the old flag across would mean a "verified" phone nobody verified.

Requesting a code while signed in uses the account already in session rather than a typed
address, so an SMS cannot be aimed at somebody else's number.

## Password reset

Two steps, both anonymous, both answering identically whether or not the address exists —
anything else turns the form into a way of discovering who has an account here.

A reset code carries a `Purpose` distinct from an account-verification code. Without that
column the two would be interchangeable, and a harmless "confirm your e-mail" message would
double as a password-reset token.

Completing a reset revokes every live refresh token. If the reset happened because somebody
else had the old password, leaving their session alive would defeat the exercise.

## Rate limiting

A six-digit code is a million guesses. Five attempts burn the code, but nothing stopped an
attacker asking for a fresh one, so the attempt counter alone was never a defence.

Fixed windows, partitioned by client address:

| Policy | Applies to | Limit |
|---|---|---|
| `auth` | login, register | 8 per minute |
| `codes` | send/confirm code, forgot/reset password | 12 per five minutes |

Rejections return 429 with a `Retry-After` header, because a user who mistyped twice deserves
a straight answer rather than a silent wall. Behind a proxy the socket address is the proxy's,
so `X-Forwarded-For` is preferred when present — configure `ForwardedHeaders` before trusting
it in production.

## Refunds

| When | What happens |
|---|---|
| More than 48 hours before the screening | Refundable, 30% of the price kept |
| 48 hours or less | No refund |
| Screening cancelled by the cinema | Everything back, whatever the clock says |
| Ticket already scanned at the door | No refund, cancelled screening or not |

`RefundPolicy` is a pure function of (booking, screening, now) — no clock of its own and no
database. The quote shown on the ticket and the amount actually refunded come from the same
call, because two implementations would eventually disagree and the customer would be the one
to find out. It is recomputed when the refund is carried out, not taken from the client: the
cut-off moves while somebody is reading the page.

A screening can override the window, the fee, and add a note of its own. That is why every
ticket carries a **Rules** section rather than the site quoting one policy everywhere — the
customer reads the rule that applies to their booking.

Refunding releases the seats back on sale, e-mails a receipt naming the fee, and writes an
audit entry. **Cancelling a screening is not deleting it**: it makes every booking on it fully
refundable and keeps the record. The money still goes back when the customer asks rather than
silently, so they keep a ticket to point at until then.

`RefundPolicyTests` pins the edges: the boundary at exactly 48 hours is inclusive, a scanned
ticket is refused even when the screening was cancelled, and a per-screening override is
honoured.

## The door

QR codes were only half a feature while nothing read them back. Security → **Ticket check-in**
takes a scanned payload or a reference typed by hand and answers with one of five states:

*Admitted · Already used · Different screening · Never confirmed · Not found*

The refusals matter more than the acceptance — a doorman needs to know **why** a ticket is
being turned away. Checking in stamps the seat, so a second scan of the same code reports when
it was first used. Scanning one seat's code admits that seat; a typed reference admits the next
unused seat on the booking, which is what happens when a group arrives together.

## Audit log

Separation of duties without a record is only half the idea: if nobody can see who approved a
flagged film or who suspended an account, the split protects nothing.

`identity.AuditEntries` records role changes, suspensions, security verdicts, admin decisions
and check-ins — with the reason, and with the actor's name and roles **as they were at the
time**. Looking up today's roles later would quietly rewrite history every time somebody is
promoted.

Append-only. There is no endpoint that edits or deletes an entry, because a log the watched can
rewrite is not evidence.

## Reviews

Stars and a comment, on the film's own dialog, under the reviews already there — where
somebody reading opinions is most likely to have one. A 403 from the server is reported as
what it is: rent the film first.



Rating a film requires having rented it. `IRentalApi.HasRentedAsync` is a contract in
SharedKernel: Catalog asks the question, Rentals answers it, and neither reaches into the
other's tables. Past rentals count — having returned the film is not a reason to lose your say.

## Users and roles

Admin → **Users**: search, grant or revoke Admin and Security, suspend and reinstate.

Customer is never listed as grantable. Everyone keeps it — it is what grants renting and the
Studio — and the server appends it regardless of what the client sends.

Two things the server refuses, because the interface offers no way back from either: removing
your own Admin role, and demoting or suspending the last active admin.

Suspension, not deletion: the rentals, reviews and bookings behind an account still have to
make sense afterwards. Suspending also revokes live refresh tokens, since blocking sign-in
alone would leave an existing session working until it happened to expire.

## Verification and delivery

Codes are six digits, hashed with a per-code salt, valid ten minutes, single use, five
attempts, one resend per minute. An account cannot sign in until its e-mail is confirmed.

`Notifications:Email:Provider` and `Notifications:Sms:Provider` choose the transport:

- `Console` writes the code to the log — the default, so a fresh clone runs with no credentials.
- `Smtp` sends for real. Set host, port, from-address in `appsettings.json`; put the username
  and password in user secrets.
- `Twilio` sends SMS. Account SID and auth token likewise belong in user secrets.

```bash
dotnet user-secrets set "Notifications:Email:UserName" "you@gmail.com" --project src/MovieRental.Host
dotnet user-secrets set "Notifications:Email:Password" "your-app-password" --project src/MovieRental.Host
```

Gmail needs an App Password, not your account password, and two-factor must be on.
An unrecognised provider name throws at startup rather than falling back to the console —
a silent fallback in production means codes nobody ever receives.

## Languages

Azerbaijani (default), English, Russian, Turkish. One JSON file per locale in
`src/MovieRental.Host/locales/`, read once at startup and used by **both** Razor and React —
Razor through `ILanguageContext`, React through the same dictionary inlined into the document
so the first paint is already translated.

The brief asked for `.resx` on the Razor side and a separate bundle for React. Two stores of
the same sentences drift: a key gets translated on one side and not the other, and nobody
notices until a page renders half in English. One file feeding both costs a small loader and
removes that whole class of bug.

202 keys, identical across all four files. Every visitor-facing string in the shell and in
every React page comes from a key — nothing is hardcoded.

To add one: put the key in all four files, then use `@T["your.key"]` in Razor or
`t("your.key")` in React. Missing keys render as the key itself, which is a visible bug
report rather than a blank button.

Dates and numbers go through `Intl` with the active language, so 12 September reads
"12 sen" in Azerbaijani and "12 сент." in Russian without a second format table.

---

## Front end

Razor owns routing and the page shell; React owns everything dynamic. Each page renders `<section id="root" data-page="…">` and `client/src/main.tsx` mounts the matching island — one bundle, six entry points, no client router fighting the server for the URL.

No `ViewBag` or `ViewData` anywhere. Every page exposes a strongly-typed `AppPageViewModel`, and `_Layout.cshtml` reads it off the page model.

**Palette** — `#0D0B0A` ink, `#1A1614` raised, `#C5A059` brass, `#EDE6DA` paper. Defined once in `wwwroot/css/shell.css` for the Razor chrome and once in `client/src/styles/app.css` as Tailwind v4 `@theme` tokens. **Type** — Bodoni Moda for display, Inter for everything else.

The hero is `components/ui/stack-spread.tsx`: eight cards clustered at rest, scattering on scroll, with pointer parallax once they settle. Two changes from the original component — the card faces render inline SVG posters from `PosterArt.tsx` instead of remote photographs, so the hero has no image requests to wait on, and the headline and subtitle are props. It respects `prefers-reduced-motion` and drops to a stacked column on touch devices.

Motion elsewhere is deliberate and sparse: one staggered entrance for the catalogue grid, spring feedback on seat selection, layout animation when a rental leaves the list. Nothing moves on its own after the page settles.

### Notes

- `client/` has no `index.html` on purpose. The Razor page *is* the document; Vite is configured to build `src/main.tsx` directly into `app.js` and `app.css` with fixed names, so `_Layout.cshtml` can hard-code the script tag instead of reading a manifest.
- The API client keeps the access token in memory and the refresh token in `localStorage`, and retries a 401 once after refreshing. Keeping the short-lived token out of `localStorage` limits what an XSS bug can reach.

---

## Before this goes anywhere real

- Move `Jwt:SecretKey` into user secrets or environment variables. The value in `appsettings.json` is a placeholder and the app refuses to start if it is under 32 characters.
- Replace `ConsoleEmailSender` and `ConsoleSmsSender` with real transports. They are registered in `IdentityModule.RegisterServices`, so nothing else changes.
- Swap the development bootstrapper for real migrations.
- Uploaded films land on the local disk under `src/MovieRental.Host/uploads/`. Point that at blob storage before any real traffic.
- Add rate limiting on `/api/auth/login`.
- The benchmark numbers on the admin dashboard are sample data for the charts, not measured results.
