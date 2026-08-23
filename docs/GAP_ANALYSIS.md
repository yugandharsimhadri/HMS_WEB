# Gap analysis — HMS_WPF vs HMS_WEB

Method: enumerated all 41 WPF viewmodels and 46 views, mapped each to a web
page, dialog or controller, then checked the API for a *write* path behind
every read. Read-only masters are the theme that emerges — several screens
display seeded data with no way to change it, which compiles and renders
perfectly and is why a viewmodel-name checklist alone would have missed them.

**Coverage: 38 of 41 viewmodels ported** (30 when this was first written; the
eight master editors have since been built — see 1.2).

---

## 1. Blocking for a real clinic

### 1.1 ~~No user management~~ — **DONE**

*Closed. Settings → Staff logins, Admin-only via `ClinicAdminPolicy`.*

Two things worth carrying forward from building it:

- The admin-only gate needed a **new policy**, not `[Authorize(Roles = "Admin")]`.
  A bare `Roles` attribute *replaces* the default clinic policy rather than
  adding to it, which would have dropped the tenant requirement that keeps a
  support token out. `ClinicAdminPolicy` requires both.
- It surfaced a **guard that had silently died**: `SaveUserAsync` compared the
  whole username against `"EnterpriseAdmin"`, which stopped matching anything
  once usernames gained their `@clinic` suffix, so `enterpriseadmin@twinkle`
  was accepted. It granted nothing — support is matched before any tenant is
  resolved — but existed only to be mistaken for the support account. Now
  checks the local part, pinned by `ReservedUsernameTests`.

The original finding, kept for the reasoning:

Registration creates exactly one Admin. There is no second account and no way
to make one.

`AuthService.GetUsersAsync` and `SaveUserAsync` are fully ported and
**unreachable** — no controller calls either. The Settings page has a Doctors
section but no Users section, though the desktop's `SettingsViewModel` has
both (`NewUser`, `CanManageUsers`, `Users`, `SaveSecurityAsync`).

Consequence: a receptionist and a pharmacist share the owner's password, so
`CreatedBy`/`UpdatedBy` on every row names the owner regardless of who did
the work, and the role system (`UserRole`) does nothing. The audit trail is
the point of those columns.

This is the largest gap in the product. Everything needed sits behind it
already; it needs a `UsersController` and a Settings section.

When building it, the form should take only the **local part** and append
`@clinic-code` automatically — see `UserName.TryNormaliseLocalPart`, which
already validates and explains.

### 1.2 ~~Master data is read-only — eight editors missing~~ — **DONE**

*Closed. A `Masters` screen now composes every master, gated by the module
switches, mirroring the desktop's `GeneralMasterViewModel` +
`PathologyLabMasterViewModel`.*

What the original finding said, kept because the failure mode is worth
remembering: every clinic was stuck with exactly what `TenantProvisioner`
seeded, unable to add a vaccine their state schedule required, price a
dental package, or define a lab analyte.

**Every service method was already ported** — `SaveVaccineAsync`,
`SavePackageAsync`, `SaveAnalyteAsync`, `SaveReportAsync` and the rest, along
with their delete guards. Only the controllers and the UI were missing, which
is exactly why the gap was invisible: the domain layer looked complete
because it *was* complete.

One correction to the original list: **dentist procedures were never
missing**. `SaveProcedure` already took a `Department`, because the procedure
master is shared across departments and filtered by a pill rather than split
into three catalogues. It simply had no caller — an orphan write endpoint.

Delete is refused everywhere a master has been used, with "deactivate it
instead" as the message; anaesthesia types have no delete at all, matching
the desktop.

The buttons on the Dentist, Lab and Pediatrics pages — "Add sitting", "Add
report", "Add procedure" — remain *transactional* (a line on a case, order or
bill). They were never master editors, and still aren't.

### 1.3 ~~Clinic logo missing end to end~~ — **DONE**

*Closed. Uploaded under Settings → Document branding, rendered on all nine
patient-facing documents.*

Three things worth carrying forward:

- **A bad logo must never stop a clinic printing.** Decoding returns null on
  any failure — bad base64, a truncated upload, a file that is not an image —
  so the document loses its letterhead and keeps its prescription. Verified
  by storing deliberate rubbish and checking the receipt still generated, at
  exactly its pre-logo byte size.
- **Bounded at 200 KB**, because the image is embedded in every document the
  clinic prints, forever.
- **Report PDFs deliberately have none.** That was already a considered
  decision in `ReportPdfBuilder` — a letterhead belongs on what a patient is
  handed, not on a twenty-page internal register — and it still stands.

The original finding:

`DocumentTheme.LogoBase64` and `LogoContentType` exist on the web entity and
are never read or written by anything:

- No upload UI (the desktop has `UploadLogo` / `RemoveLogo`).
- **No logo rendering in any web PDF.** `SivayaanHMS.Printing` contains no
  reference to it, whereas the desktop's `DocumentBuilder` gives the logo a
  28%-wide letterhead column.

Consequence: every prescription, invoice, receipt and report a web clinic
prints has a text-only letterhead. This is the most visible difference to a
customer holding both products' output side by side, and the reason it was
missed is that the *data* was ported faithfully — only the two ends were not.

---

## 2. ~~Ported but unreachable~~ — **DONE**

*Both closed. Settings → Data health, and Inventory → Import a bill.*

Import is **two uploads of the same file**, not a cached preview: the server
re-parses on commit so what it writes is always something it derived itself.
Data health repair takes **product ids only** and re-scans server-side —
posting findings back would let a caller name any units-per-pack it liked and
have stock repacked to it.

Both are Admin-only; each moves stock in bulk.

The original finding:

Both services are registered in `Program.cs` and called by nothing — dead DI
registrations. The hard part (parsing, diagnosis logic) is already done; each
needs a controller and a page.

| Feature | WPF | Web state |
|---|---|---|
| Bill import | `ImportViewModel` (153 lines) | `PurchaseImportService` registered, no endpoint |
| Data health | `DataHealthViewModel` (138 lines) | `DataHealthService` registered, no endpoint |

Data health matters more than its size suggests: it repairs pack-size
mismatches, and `InventoryPage` already warns about exactly that condition
without offering the fix.

---

## 3. Minor

- **Dark theme.** `GeneralSettings.Theme` (`Light`/`Dark`) is stored, typed in
  `api/types.ts`, and never applied — there is no dark CSS and no setting to
  toggle it. The desktop ships `Theme.Dark.xaml` / `Theme.Light.xaml`.
  (`QueueLayout`, the neighbouring setting, *is* honoured by `OpdQueuePage`.)
- **About screen.** `AboutViewModel` / `AboutWindow` — version and build
  information. No web equivalent.

---

## 4. Deliberately not ported — and one open question

These are correct omissions, listed so nobody "fixes" them:

| WPF | Why not |
|---|---|
| `PrintPreviewWindow` | Replaced by `openPdf` and the browser's own viewer |
| `MessageWindow` | Replaced by inline page errors |
| Open backup folder / log folder | Local file-system paths on a clinic PC |
| `LoginWindow` shutdown handling | A WPF lifecycle quirk with no web analogue |

**Open question — backup.** The desktop gives a clinic a "Back up now" button
and a folder they own. On SaaS the vendor takes over that duty, and nothing
in HMS_WEB currently discharges it: there is no scheduled backup, no restore
path, and no way for a clinic to export its own records. Customers who ran
the desktop had their data on their own disk; web customers have none of it.
That is a business decision rather than a porting gap, but it is a
commitment, and right now nothing anywhere makes it.

---

## Suggested order

1. ~~**Masters**~~ — done.
2. ~~**User management**~~ — done.
3. ~~**Data health**, **bill import**~~ — done.
4. ~~Dark theme~~ — done (Ctrl+J; `GeneralSettings.Theme` is finally read).
5. ~~**Logo**~~ — done.
   every document a clinic hands a patient.
6. About screen.

Still open beyond this list: **backup/export** (section 4), which is a
policy decision rather than a queue item.

Backup/export should be decided in parallel — it is a policy question, not a
queue item.
