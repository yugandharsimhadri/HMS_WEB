# Appointments — feature parity with HMS_WPF

Every behaviour the desktop has in this module, enumerated from the source
(not from memory), with its port status. This is the acceptance checklist:
HMS_WEB is not done until every ☑ below is genuinely ☑.

Derived from: `AppointmentsViewModel` (402 lines), `AppointmentsService`
(already ported), the `Appointment` / `ReminderItem` / `ReminderLog`
entities, and `AppointmentSlipDocument`.

> **Spec source.** `AppointmentsViewModel.cs` does not exist on HMS_WPF's
> `main`. The four clinical modules landed on the unmerged branch
> `origin/Dentist_Pathology` (`3603677`, 19 Aug 2026), which is the
> specification for this module and every one after it. Read it without
> switching HMS_WPF's working tree:
>
> ```
> git show origin/Dentist_Pathology:src/Pharma.App/ViewModels/AppointmentsViewModel.cs
> ```

Status: ☑ done · ◻ not started · ◐ partial

**52 of 58 items are ☑ as of 22 Aug 2026**, each driven against the running
app in a browser — not just compiled. The 6 remaining are ◐ and each says
why. The bugs that testing surfaced are listed at the end.

---

## 1 · Module gating (`GeneralSettings`)

| # | Feature | Why it exists | Status |
|---|---|---|---|
| 1.1 | Appointments is **off by default** (`AppointmentsEnabled`) | A walk-in-only clinic has no use for advance booking; the desktop makes it a deliberate opt-in | ☑ |
| 1.2 | Nav entry appears only when the module is on | An empty screen behind a permanent nav item reads as a broken feature | ☑ |
| 1.3 | Booking **context** picker lists only enabled modules — General (needs `OpdEnabled`), Pediatrics, Dentist, Pathology Lab | Offering a choice the server will refuse is a dead end the user cannot diagnose | ☑ |
| 1.4 | Server refuses a booking against a switched-off context | Defence in depth — `EnsureModuleEnabledAsync`, the same shape as every status-gated save here | ☑ |
| 1.5 | Selected context falls back to the first enabled one when the current pick is switched off | Otherwise the picker keeps a stale selection that cannot be booked | ◐ built; the switch-off-while-selected path was not driven |

## 2 · Today's appointments tab

| # | Feature | Why | Status |
|---|---|---|---|
| 2.1 | **Date picker** — any day, not just today (a real date control, not a typed text field) | Same shape as `OpdViewModel.Date`; the desk books ahead and looks back | ☑ |
| 2.2 | Changing the date reloads the list | ☑ |
| 2.3 | List ordered by scheduled time | The desk reads the day top to bottom | ☑ |
| 2.4 | **Rescheduled rows are excluded** from the day's list | The replacement row carries the live slot; showing both double-books the day visually | ☑ |
| 2.5 | Cancelled and checked-in rows **stay visible**, with their status | "What happened today" is the question, not "what is left" | ☑ |
| 2.6 | Subtitle — `N appointment(s)`, or `Nothing booked for this day` when empty | ☑ |
| 2.7 | Row shows time, patient, doctor, context, status and reason | ☑ |
| 2.8 | A status line reports the outcome of each action | ☑ |

## 3 · Check-in

| # | Feature | Why | Status |
|---|---|---|---|
| 3.1 | Check-in on a **General** appointment books a real OPD `Visit` | An appointment is an intent; the queue entry is created only on arrival | ☑ |
| 3.2 | The visit is booked at **now**, not at the scheduled time | The token reflects when they actually walked in, so the queue orders by arrival | ☑ |
| 3.3 | Fee auto-fills from the doctor's `ConsultationFee` | ☑ |
| 3.4 | The appointment's reason carries over as the visit complaint | ☑ |
| 3.5 | The visit is linked back via `BookVisitAsync(..., appointmentId)` | ☑ |
| 3.6 | Appointment marked `CheckedIn` with `LinkedRecordId` set to the visit | Closes the loop; lets "the record this turned into" be found later | ☑ |
| 3.7 | Outcome: `{patient} checked in — token {N}.` | The token is the one thing the desk must read back to the patient | ☑ |
| 3.8 | Pediatrics / Dentist / Pathology Lab check-in refuses with "not available yet" | Those modules have no record to create yet; doing nothing silently would look like a failed click | ☑ |
| 3.9 | Server refuses check-in unless the status is `Scheduled` | A double check-in would mint a second token for one arrival | ☑ |
| 3.10 | A failed check-in surfaces the server's message and leaves the row untouched | ☑ |

## 4 · Cancel

| # | Feature | Why | Status |
|---|---|---|---|
| 4.1 | **A reason is required** — refuses with "Say why the appointment is being cancelled." | A cancellation without a reason is unauditable | ☑ |
| 4.2 | Cancel **never deletes the row** — the status becomes `Cancelled` | A cancellation is still a fact; the same reasoning that makes `StockAdjustment` write a document | ☑ |
| 4.3 | The reason is appended to notes as `Cancelled: {reason}`, preserving anything already there | ☑ |
| 4.4 | Server refuses to cancel an already **checked-in** appointment | It became a visit; cancel that instead, or the two records disagree | ☑ |
| 4.5 | The reason field clears after a successful cancel | ☑ |
| 4.6 | Outcome names the patient whose appointment was cancelled | ☑ |
| 4.7 | Cancel asks for confirmation | Carried from the desktop's `MessageBox`; matches OPD's cancel-visit, which stays a real confirm by decision | ☑ |

## 5 · Reschedule

| # | Feature | Why | Status |
|---|---|---|---|
| 5.1 | New date required — refuses with "Enter the new date." | ☑ |
| 5.2 | New time **optional** — falls back to the original appointment's time | Moving a slot a week out usually keeps the hour | ☑ |
| 5.3 | Reschedule **creates a new appointment** and marks the old one `Rescheduled` — it never mutates `ScheduledOn` in place | The original time stays on record instead of being silently overwritten | ☑ |
| 5.4 | The replacement carries `RescheduledFromId` back to the original | The trail is followable both ways | ☑ |
| 5.5 | The replacement gets its **own** `AppointmentNo` | ☑ |
| 5.6 | Patient, doctor, context, reason and duration carry over | ☑ |
| 5.7 | Server refuses to reschedule an already **checked-in** appointment | ☑ |
| 5.8 | Outcome names the new time **and** the new number | The desk writes the new number on the card | ☑ |
| 5.9 | Date and time fields clear after a successful reschedule | ☑ |

## 6 · Book appointment tab

| # | Feature | Why | Status |
|---|---|---|---|
| 6.1 | Patient search by name **or phone**, at most 20 matches | ☑ |
| 6.2 | Search runs as you type | ☑ |
| 6.3 | **A single match auto-selects** | The overwhelmingly common case is one person | ☑ |
| 6.4 | Choosing a patient clears the search box but keeps the selection | ☑ |
| 6.5 | **Change patient** resets selection, search and matches | Picking the wrong sibling must be undoable without leaving the tab | ☑ |
| 6.6 | **New patient** inline, and the created patient becomes the selection | ☑ |
| 6.7 | Doctor picker, defaulting to the first doctor | ☑ |
| 6.8 | Doctor selection **survives a reload by Id**, not by object reference | `GetDoctorsAsync` returns fresh `AsNoTracking` instances; matching by reference silently blanks the picker on a second visit | ☑ |
| 6.9 | Booking date defaults to **tomorrow** | An appointment is by definition not today's walk-in | ☑ |
| 6.10 | Booking time defaults to 10:00 | ☑ |
| 6.11 | Duration defaults to 15 minutes; a non-positive value is coerced to 15 | A zero-length slot is meaningless | ☑ |
| 6.12 | Reason and notes, both optional, trimmed, empty becomes null | ☑ |
| 6.13 | Refuses without a patient: "Select a patient, or add a new one." | ☑ |
| 6.14 | Refuses without a doctor: "Select a doctor." | ☑ |
| 6.15 | Refuses an unparseable date: "Enter a valid date." | ◐ the check exists, but `<input type="date">` cannot produce an unparseable value — structurally prevented rather than caught |
| 6.16 | An unparseable time falls back to midnight rather than refusing | ◐ same: `<input type="time">` cannot produce one. The fallback is kept for a blank field |
| 6.17 | Patient name and phone and the doctor name are **denormalized onto the appointment** | The daily list and a reminder must read correctly without a join, and survive a later name edit | ☑ |
| 6.18 | `AppointmentNo` is allocated server-side by `NumberService` | ☑ |
| 6.19 | Outcome: `{AppointmentNo} booked for {date time}.` | ☑ |
| 6.20 | The form resets after a successful booking — patient, matches, reason, notes, duration | ☑ |
| 6.21 | Date, time and doctor **persist** across bookings | Booking a family into consecutive slots is the common case | ☑ |
| 6.22 | If the booked date matches the date on the Today tab, that list refreshes | ☑ |
| 6.23 | **Book** and **Book & print** are separate actions | ☑ |

## 7 · Reminders tab

| # | Feature | Why | Status |
|---|---|---|---|
| 7.1 | Two sources today: OPD **follow-ups** (`Visit.FollowUpOn`) and **upcoming appointments** | Pediatrics' vaccine-due and Dentist's next-sitting plug in the same way later | ☑ appointments driven; no follow-up existed to exercise that source |
| 7.2 | **Due within** lead-day picker — 1 / 3 / 7 / 14 / 30, default 3, and free entry outside that list | ☑ |
| 7.3 | Changing the lead days reloads | ☑ |
| 7.4 | A negative lead-day count is clamped to 0 | ☑ |
| 7.5 | Already-reminded rows **stay on the list**, flagged, rather than vanishing | The desk needs to see what has been done, not only what is left | ☑ |
| 7.6 | Not-yet-reminded rows sort first, then by due date | ☑ |
| 7.7 | The action label flips: **Mark reminded** / **Mark not reminded** | One button that reads correctly either way | ☑ |
| 7.8 | Marking writes a `ReminderLog` with channel `OnScreen` | The other channels exist so that WhatsApp or email later is "send as well as log", not a migration | ☑ |
| 7.9 | Unmarking **removes** the log row | ☑ |
| 7.10 | Actioned-by is the signed-in user (the desktop wrote the literal "front desk") | Deliberate divergence — the web has a real authenticated user | ☑ stamped `admin@demo-clinic` |
| 7.11 | The selection clears after toggling | ◐ n/a by design — the web screen acts per row, so it has no selection to clear (see deliberate differences) |
| 7.12 | Row shows patient, phone, due date, description and status | The phone number is the point — this is a call list | ☑ |

## 8 · Tabs and refresh

| # | Feature | Why | Status |
|---|---|---|---|
| 8.1 | Three tabs — Today's appointments, Book appointment, Reminders | Each is a handful of fields, not a destination of its own | ☑ |
| 8.2 | Switching **to** Today's appointments reloads it | Without this, a booking made on the Book tab never appears until you leave the page and come back | ☑ |
| 8.3 | Switching **to** Reminders reloads it | The same bug, the same fix | ☑ |

## 9 · Printing

| # | Feature | Why | Status |
|---|---|---|---|
| 9.1 | **Appointment slip PDF** — port `AppointmentSlipDocument` into `SivayaanHMS.Printing` | ☑ |
| 9.2 | The slip carries the clinic header and document theme from Settings | ☑ |
| 9.3 | Returned inline, fetched as a blob so the bearer token is sent (`openPdf`) | ◐ endpoint returns a valid 28 KB `application/pdf` and the text extracts correctly; the new tab itself is blocked by the automation browser's pop-up blocker, and `openPdf` reports that correctly |
| 9.4 | Reprint an existing appointment's slip from the daily list | The desktop only prints at booking time; a patient who loses the slip is the obvious gap | ☑ |

---

## Deliberate differences from HMS_WPF

Not gaps — decisions, recorded so they are not "fixed" by mistake later.

- **Three tabs → three tabs on one page.** The same information
  architecture; the desktop's `SelectedTabIndex` reload behaviour is kept
  exactly (see 8.2 and 8.3, which exist for a real bug).
- **`Dialog.Show` warnings → inline messages**, except cancel, which stays a
  real confirm — destroying a booked slot deserves acknowledgement, the same
  call OPD's cancel-visit made.
- **New-patient overlay → the existing `PatientEditorDialog`**, reused rather
  than implemented a second time. Its `onSaved` now hands back the saved row
  as well as the message, so a caller that opened it to *pick* somebody can
  select the patient it just created.
- **Reminders act per row, not on a selection.** The desktop has one button
  driven by `SelectedReminder`, which is why it needs `ReminderActionLabel`
  and a selection reset. A table row on the web carries its own button, so
  the label still flips per row (7.7) but there is no selection to clear.
- **"front desk" → the signed-in user** for `ReminderLog.ActionedBy`,
  matching the `Environment.UserName` decision already taken.
- **Print preview → PDF in a new tab.**
- **Names are read server-side, not accepted from the client.** The desktop
  copies `PatientName` / `DoctorName` off the objects it has selected;
  `BookAppointmentRequest` carries only ids and the controller reads the
  names, so a client cannot book a slot that prints one patient's name
  against another's record.

---

## What browser testing caught

Both of these compiled cleanly and would have shipped unnoticed.

| Bug | How it showed up | Why it mattered |
|---|---|---|
| **The sidebar never re-read Features** | Switching Appointments on saved correctly but the nav did not change; only a full page reload revealed it | `AppShell` read `/api/settings/general` once on mount. Pre-existing, but Appointments is the first module that is **off** by default — until now you could only hit this turning something *off*, so switching a module *on* looked like it had silently failed. Fixed by having Settings announce the save and the shell re-read it |
| **Stale selected row after any refresh** | Checking a patient in left Cancel and Reschedule on screen for an appointment that was now `CheckedIn` — both of which the server refuses | The page held the selected row as an *object*, so it kept the version captured at click time while the list underneath was replaced. Now it holds the id and derives the row from the current list — the same "match by id, not by reference" lesson item 6.8 records for the doctor picker |

Two smaller things worth knowing:

- The clinic's default footer text ("Get well soon. Medicines once sold are
  not returnable.") prints on the appointment slip, because the slip uses
  `clinic.FooterText` exactly as the desktop does. It reads oddly on a
  booking confirmation, but it is the clinic's configured text and changing
  it is a Settings decision, not a code one.
- `OpdQueuePage`'s `today()` uses `toISOString().slice(0,10)`, which is UTC
  and so opens on *yesterday* between midnight and 05:30 IST. This page uses
  a local-date helper instead. The OPD one is untouched here — it is a
  separate, already-signed-off module — but it is a real latent bug.
