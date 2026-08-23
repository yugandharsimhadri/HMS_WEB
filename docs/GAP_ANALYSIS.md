# Gap analysis — HMS_WPF vs HMS_WEB

Method: enumerated all 41 WPF viewmodels and 46 views, mapped each to a web
page, dialog or controller, then checked the API for a *write* path behind
every read. Read-only masters are the theme that emerges — several screens
display seeded data with no way to change it, which compiles and renders
perfectly and is why a viewmodel-name checklist alone would have missed them.

**Coverage: 30 of 41 viewmodels ported.** The eleven gaps below cluster into
three groups, only one of which is genuinely blocking.

---

## 1. Blocking for a real clinic

### 1.1 No user management — a clinic cannot give staff a login

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

### 1.2 Master data is read-only — eight editors missing

Every clinic is stuck with exactly what `TenantProvisioner` seeded. They
cannot add a vaccine their state schedule requires, price a dental package,
or define a lab analyte.

| Master | WPF editor | Web API |
|---|---|---|
| Vaccines | `VaccineEditorViewModel` | GET only |
| Dental packages | `DentalPackageEditorViewModel` | GET only |
| Dental replacements | `DentalReplacementEditorViewModel` | GET only |
| Anaesthesia types | `AnesthesiaTypeEditorViewModel` | GET only |
| Dentist procedures | `ProcedureEditorViewModel` | GET only |
| Lab reports | `LabReportEditorViewModel` | GET only |
| Lab analytes | `LabAnalyteEditorViewModel` | GET only |
| Lab packages | `LabPackageEditorViewModel` | GET only |

Two masters *are* writable, which is what makes the rest look finished:
diagnostic tests (`SaveTest`/`SetTestActive`/`DeleteTest`) and pediatric
procedures (`SaveProcedure`). Copy those two.

The buttons that exist on the Dentist, Lab and Pediatrics pages — "Add
sitting", "Add report", "Add procedure" — are *transactional* (adding a line
to a case, order or bill). None of them define a master.

Corresponds to `GeneralMasterViewModel` (vaccines, procedures, replacements,
anaesthesia, packages) and `PathologyLabMasterViewModel` (analytes, reports,
packages).

### 1.3 Clinic logo missing end to end

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

## 2. Ported but unreachable

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

1. **User management** — blocks multi-staff use, which is most clinics.
2. **Logo** (upload + printing) — visible on every document a clinic hands a
   patient.
3. **Masters** — unblocks Dentist and Pathology Lab, which are otherwise
   fixed at their seed data.
4. **Data health**, then **bill import**.
5. Dark theme, About.

Backup/export should be decided in parallel — it is a policy question, not a
queue item.
