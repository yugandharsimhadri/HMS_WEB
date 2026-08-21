# Operations

Where an installed clinic keeps its things, and what to do when something is
wrong. Written for whoever supports a machine, not for whoever builds the code.

Installing and packaging are separate: [INSTALLING.md](../INSTALLING.md) and
[BUILDING_THE_INSTALLER.md](../BUILDING_THE_INSTALLER.md).

---

## Where everything lives

| What | Default path |
|---|---|
| **The database** | `C:\ProgramData\TwinkleHMS\twinkle.db` |
| Backups | `C:\ProgramData\TwinkleHMS\backups` |
| Logs | `C:\HMS\Logs` |
| Configuration | `appsettings.json`, beside the executable |

**One file holds the whole clinic.** Copy `twinkle.db` and you have copied the
practice — patients, stock, bills, settings, users, every module. That is the
single most useful fact on this page: it makes backup, migration between
machines, and support triage trivial.

SQLite writes `twinkle.db-shm` and `twinkle.db-wal` alongside it. When copying a
database from a machine where the app may be running, **take all three**, or
close the application first.

## Configuration

`appsettings.json` sits next to the executable and can be edited in Notepad. It
is read once at startup.

```json
{
  "LogDirectory":   "C:\\HMS\\Logs",
  "DatabasePath":   null,
  "BackupDirectory": null,
  "BackupsToKeep":  14,
  "LogDaysToKeep":  30,
  "LogFileMaxMb":   10,
  "TraceMethods":   true
}
```

| Key | Default | Notes |
|---|---|---|
| `LogDirectory` | `C:\HMS\Logs` | |
| `DatabasePath` | `null` | `null` means `C:\ProgramData\TwinkleHMS\twinkle.db` |
| `BackupDirectory` | `null` | `null` means `…\TwinkleHMS\backups` |
| `BackupsToKeep` | 14 | Daily backups kept before the oldest is deleted |
| `LogDaysToKeep` | 30 | |
| `LogFileMaxMb` | 10 | Rolls at this size |
| `TraceMethods` | true | Method-level entry/exit tracing |

Use double backslashes. **Restart after editing.**

> **A bad path never stops the application.** If a folder cannot be written to —
> a locked-down PC, an offline network drive — it falls back to
> `C:\ProgramData\TwinkleHMS\logs`, and the first line of the log records which
> folder it wanted and which it used. A malformed `appsettings.json` is handled
> the same way: built-in defaults are used and the log says so, via
> `AppConfig.LoadError`. Look in the log before assuming a setting took effect.

Environment overrides exist for automated runs — `DbBootstrapper.PathOverrideVariable`
and `AppLog.DirectoryOverrideVariable` — which is how the UI tests point a real
application at a throwaway database.

## Backups

One backup per day, taken at first launch of the day by `DbBootstrapper`, into
the backups folder, keeping the most recent 14.

**This protects against mistakes, not against the PC dying.** Nothing copies the
folder off the machine. Tell the clinic to copy it to a pen drive or a cloud
folder weekly — and say plainly that if the disk fails, everything since the
last off-machine copy is gone.

> Restores are untested by any automated check. Anyone supporting an
> installation should restore a backup onto a spare machine at least once, so
> the first attempt is not during an emergency.

## Logs

One file per day in `C:\HMS\Logs`, 30 days kept, rolling at 10 MB. Settings has
an **Open log folder** button and shows the resolved path — use it rather than
guessing, since the path may have fallen back.

Each entry is a scope from `AppLog.Enter(scope, detail)` with its outcome — `Ok`
with a result, or `Skip` with a reason — and its duration. Screen changes are
logged (`Shell.Go to=pediatrics`), so an error can be placed against what the
user was looking at.

**Ask for the day's file when reporting a problem.** It usually contains the
answer.

## Startup sequence

`DbBootstrapper.InitialiseAsync` runs before the main window:

1. Resolve the database path (override → `appsettings.json` → default).
2. `MigrateAsync()` — applies any pending EF migrations.
3. Seeders, all insert-if-missing: users, vaccines, dental procedures, lab
   analytes and reports, diagnostic tests, import profiles.
4. `EnsureSettingsSplitSeedAsync` — one-time legacy `shop.*` → `clinic.*` /
   `pharmacy.*` migration, guarded by `migrations.settingssplit.done`.
5. The daily backup.
6. `DataHealthService.DailyCheckAsync` — surfaces anything needing attention.

An upgrade is therefore "replace the executable and start it". Database upgrades
are covered in [DATABASE_UPGRADES.md](../DATABASE_UPGRADES.md).

> **Startup gotcha worth knowing.** WPF's default `ShutdownMode` is
> `OnLastWindowClose`. Showing any dialog before `MainWindow` exists — the login
> window, gated by `auth.requirelogin` — will silently exit the process the
> moment that dialog closes, *after* logging "Main window shown". `App.xaml.cs`
> sets `ShutdownMode.OnExplicitShutdown` first and switches to
> `OnMainWindowClose` once the real window exists. Any future splash screen or
> onboarding wizard needs the same treatment. If a startup UI test times out
> with no visible error, check the log for an `Exiting` line straight after
> `Main window shown` — that pairing is this bug's signature.

## Data health

`DataHealthService` runs a check at startup and offers repairs from the Data
health window: merging a medicine entered twice, and repointing orphaned
references. Merging is the common one — two spellings of the same brand, each
carrying stock.

## Security

Sign-in is **off by default**. Switching on **Settings → Security → Require
sign-in** asks for credentials at next start; the default `Admin` account can
always sign in.

Passwords are salted hashes. A new user must change their password at first
sign-in.

> **Roles are attribution, not permission.** Every signed-in role currently has
> full access — the role exists so actions are attributed sensibly. Do not
> describe it to a customer as access control. See
> [BUSINESS_RULES.md](BUSINESS_RULES.md#10--users-and-roles).

## Common questions

**"Can I move it to another PC?"** Install, copy `twinkle.db` (with `-shm` and
`-wal`, or after closing the app) into `C:\ProgramData\TwinkleHMS\`, start it.
Migrations run automatically.

**"Two computers at the counter?"** Not supported. One machine, one database
file; there is no locking or synchronisation between installations, and the
document-numbering and stock-deduction code assumes a single writer.

**"Where did today's numbers go?"** Reports and the dashboard use the machine's
own date. Check the PC's clock and timezone before assuming data loss.

**"It says the medicine has none but the shelf does."** That is what **Stock came
in — add it** at the counter is for; it adds a provisional batch and lists it on
**Stock to reconcile** until a supplier bill is matched to it.
