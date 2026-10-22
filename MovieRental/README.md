# Reel & Row — modular monolith movie rental

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

1. Open `MovieRental.sln`.
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
MovieRental.sln
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

### Migrations

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
