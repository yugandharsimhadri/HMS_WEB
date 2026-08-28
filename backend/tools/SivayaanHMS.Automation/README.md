# SivayaanHMS.Automation

Real business journeys through SivayaanHMS, driven by a real browser against the real API and a
real (throwaway) database — no mocks, no fixtures typed by hand. `SivayaanHMS.UatTests` is what
runs them; this project is where they are defined.

Built on the same tools and the same shape as **TransTrack.Automation** (`TransTrack/TransTruck_Web`)
and, one level further back, **ABPS_WEB.Automation** — Playwright for .NET, a `Workflow` object per
business journey, xUnit as the runner. Ported, not copied: SivayaanHMS speaks SQL Server rather
than SQLite and Vite rather than Next.js, and both of those change real things about how the
harness has to work. What follows explains the shape and calls out where it genuinely differs.

## Why this exists

A UI test that asserts against a fixture proves the client renders a shape someone typed into that
fixture. This proves the client, the controllers, EF Core, the tenant filter and SQL Server all
agree — because they are the actual four things running, not a stand-in for any of them.

## Layout

| | |
|---|---|
| `Workflows/` | `IWorkflow`, the base class, the catalog, the runner, and the eight scenarios themselves |
| `ClinicSession.cs` | browser + page + sign-in; what a workflow is handed |
| `ApiServer.cs` | publishes and runs the real API against a throwaway SQL Server database |
| `WebDevServer.cs` | starts the real Vite dev server, or reuses yours |
| `DemoData.cs` | the fixed cast, and the seeder that creates it through the product's own API |
| `RepoPaths.cs`, `ManagedProcess.cs`, `BrowserProvisioning.cs` | infrastructure, close ports to their TransTrack.Automation counterparts |

## Running it

```bash
dotnet test backend/tests/SivayaanHMS.UatTests
```

That is the whole interface. Every environment variable below has a working default, so this needs
no setup on a checkout that has never run it before — it publishes the API, creates a database,
starts Vite, seeds a clinic, and runs all eight workflows headless.

## What a run actually does

1. Publishes `SivayaanHMS.Api` to `backend/artifacts/uat/api-publish` (Release, once per run).
2. Starts it against a brand-new database — `SivayaanHMSUat_<timestamp>` on
   `(localdb)\MSSQLLocalDB` by default — with `ASPNETCORE_ENVIRONMENT=Development`, which is what
   makes `Program.cs`'s own startup migration create the database and apply every migration to it.
   Nothing here runs a migration tool of its own; it is the same code path the app always uses in
   development, pointed at a database nobody has used before.
3. Registers a clinic through `POST /api/tenants/register` — no token, no recovery flow, just the
   same call the sign-up form makes — signs in as its admin, turns on Diagnostics and Pediatrics
   from Settings > Features (both off by default), adds two patients and books one visit.
4. Starts Vite on its own dedicated port with `VITE_API_URL` pointed at the throwaway API.
5. Runs the eight `[Fact]`s in `SivayaanHMS.UatTests`, each opening its own headless Chromium
   session, signing in through the real login form, and running one workflow.

## Why a publish, not `dotnet run`

`appsettings.Local.json` holds a developer's own SQL password and is loaded **last** by
`Program.cs`, deliberately, so it overrides everything — including a connection string this
automation sets as an environment variable. A plain `dotnet build` still copies that file into
`bin/`; a `dotnet publish` does not, because it is marked `CopyToPublishDirectory="Never"` in
`SivayaanHMS.Api.csproj` for exactly this reason on the production deployment path. Running the
throwaway API from the publish output means it inherits that guarantee for free. `ApiServer`
checks this explicitly after every publish and refuses to start if the file is somehow present —
a UAT run must never be able to reach a real clinic's database, and that check is what makes
"never" something more than an intention.

## Why SQL Server here and not a mock

TransTrack.Automation explains its own version of this choice by pointing at ABPS_WEB.Automation,
which mocks every `/api/**` response because its API needs SQL Server and a mock is what makes the
suite runnable at all. SivayaanHMS also needs SQL Server — but by the time this project was
written, `TestDb.cs` in `SivayaanHMS.Tests` had already solved "a throwaway SQL Server database per
test run, created and dropped cleanly" for the xUnit suite. This reuses that same idea at the
process level instead of the connection level: one throwaway database per `dotnet test` run,
created by the app's own startup migration rather than by a separate tool.

The database is **left in place** after a run, exactly as TransTrack.Automation leaves its
SQLite file — a failed scenario is far easier to diagnose against the data it actually ran on.
Dropping it is not automatic, because a LocalDB database does not just disappear when a folder is
cleaned up the way a stray file would; each run logs the exact `sqlcmd` command to drop the one it
created. To sweep every leftover one at once:

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -E -C -h -1 -W -Q "SELECT name FROM sys.databases WHERE name LIKE 'SivayaanHMSUat_%';" |
  ForEach-Object { sqlcmd -S "(localdb)\MSSQLLocalDB" -E -C -Q "ALTER DATABASE [$_] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$_];" }
```

## Where this genuinely differs from TransTrack.Automation, and why

- **No `RunMode` or `Viewport`.** Both exist there to serve a video recorder (`TransTrack.DemoRunner`)
  and a real second mobile layout — a bottom tab bar with screens filed behind "More". Neither is
  true here: nothing records video, and SivayaanHMS is desktop-first with one responsive breakpoint
  that reflows two CSS properties. Building a mobile/desktop split for a layout the product does
  not have would test something that is not real.
- **No `Narrator`.** The on-screen caption overlay exists purely to make workflow steps followable
  on camera. `WorkflowContext.StepAsync` still records each step's narration — that is what lets a
  failed test's message name the business step that broke — it just never draws it.
- **Registration needs no token.** TransTrack's onboarding goes through an `EnterpriseAdmin`
  recovery token and forces a password change on first sign-in. SivayaanHMS's
  `POST /api/tenants/register` needs no auth and lets the clinic's own admin sign in immediately
  with the password chosen at registration — genuinely simpler, not simplified for this harness.
- **Vite's `--strictPort`, not a hand-rolled one.** Next.js has no equivalent flag, which is most of
  why `WebDevServer` in TransTrack.Automation exists as a large file. Vite's version is
  correspondingly smaller.

## Environment variables

| Variable | Default | |
|---|---|---|
| `SIVAYAANHMS_UAT_BASE_URL` | `http://localhost:5410` | where the client is served |
| `SIVAYAANHMS_UAT_API_BASE_URL` | `http://localhost:5411` | where the API is served |
| `SIVAYAANHMS_UAT_SQL_INSTANCE` | `(localdb)\MSSQLLocalDB` | passed straight to `sqlcmd -S` / the connection string |
| `SIVAYAANHMS_UAT_WEB_PATH` | repo discovery | absolute path, if repo discovery fails |
| `SIVAYAANHMS_UAT_MANAGE_SERVERS` | `true` | `false` to point at servers and a database you already have running |
| `SIVAYAANHMS_UAT_SKIP_API_PUBLISH` | `false` | `true` to reuse the last publish while iterating on a workflow's Playwright steps |

### Ports: 5410 and 5411

Neither the API's own development port (5130) nor its production port (6051) — both are
deliberately left alone, on the same reasoning TransTrack.Automation gives for avoiding Next's
3000 and its own product's 6041: a run that silently attached to a developer's own dev server, or
worse a real installation, would fail in a way that reads as a product bug, or would not fail at
all and would simply be pointed at the wrong thing.

## Adding a workflow

1. Add a class to `Workflows/`, deriving from `Workflow`, with a stable PascalCase `Key`.
2. Write `RunAsync` as `StepAsync("narration", …)` beats. Put the verification inside the step —
   prefer `c.ExpectVisibleAsync`, `c.Link`, `c.Button`, `c.NavigateAsync` over raw locators, and
   reach for `c.Dialog` the moment a step needs to find a control inside an open dialog: the page
   behind a dialog is never unmounted, only visually covered, and several screens keep a `<select>`
   of their own in the header that a plain role-based lookup can bind to instead.
3. Register it in `WorkflowCatalog.All`.
4. Add a `[Fact]` in `SivayaanHMS.UatTests` calling `RunWorkflowAsync("YourKey")`.

## Playwright version

Pinned to **1.61.0**, matching TransTrack.Automation and ABPS_WEB.Automation on this machine.
Playwright pins an exact Chromium build per version, so sharing the version shares the download in
`~/AppData/Local/ms-playwright` rather than adding a third build. If `frontend` ever adds
`@playwright/test`, pin it to the same major/minor for the same reason.

Chromium is installed on first launch failure, not up front — `playwright install` evicts builds no
longer referenced by the installed version, so running it routinely would keep deleting the
browsers the sibling projects on this machine depend on.

## A note on machine load while writing this

Every timing number in this file (page timeouts, publish timeouts) was set generously on purpose,
because the machine this was built on routinely has SQL Server, the API, a Vite dev server and
several unrelated `dotnet` processes running at once during a long working session. A UAT run's
own defaults reflect that reality rather than an idealised idle machine; they cost nothing extra
when the machine genuinely is idle.
