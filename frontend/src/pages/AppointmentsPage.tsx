import { useGeneralSettings } from '../settings/SettingsContext';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { api, ApiError, openPdf } from '../api/client';
import type {
  Appointment,
  AppointmentModuleContext,
  CheckInResult,
  Doctor,
  Patient,
  ReminderItem,
} from '../api/types';
import { PatientPicker } from '../shell/PatientPicker';
import { ShortcutHints } from '../shell/ShortcutHints';
import { useHotkey } from '../shell/hotkeys';
import { PatientEditorDialog } from '../opd/PatientEditorDialog';
import { usePatientSearch } from '../shell/usePatientSearch';

/** Today as a naive local date, deliberately NOT `toISOString().slice(0,10)`
 * — that converts to UTC first, so anywhere east of Greenwich the date
 * picker opens on yesterday for the first hours of the morning. */
function localDate(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

const today = () => localDate(new Date());
const tomorrow = () => {
  const d = new Date();
  d.setDate(d.getDate() + 1);
  return localDate(d);
};

/** The desktop's ReminderLeadDayOptions. Editable in the desktop, so the
 * field stays a number input with these as quick picks rather than a
 * closed dropdown. */
const LEAD_DAY_OPTIONS = [1, 3, 7, 14, 30];

type Tab = 'today' | 'book' | 'reminders';

const TABS: { id: Tab; label: string }[] = [
  { id: 'today', label: "Today's appointments" },
  { id: 'book', label: 'Book appointment' },
  { id: 'reminders', label: 'Reminders' },
];

const CONTEXT_LABELS: Record<AppointmentModuleContext, string> = {
  General: 'General (OPD)',
  Pediatrics: 'Pediatrics',
  Dentist: 'Dentist',
  PathologyLab: 'Pathology Lab',
};

const timeOf = (iso: string) =>
  new Date(iso).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });

export function AppointmentsPage() {
  const [tab, setTab] = useState<Tab>('today');

  const [doctors, setDoctors] = useState<Doctor[]>([]);
  const general = useGeneralSettings();

  // ── Today's appointments ───────────────────────────────────────────────
  const [date, setDate] = useState(today);
  const [list, setList] = useState<Appointment[]>([]);
  // The id, never the row object. Holding the object meant that after any
  // refresh the panel below was still reading the version captured when the
  // row was clicked — so checking somebody in left Cancel and Reschedule on
  // screen for an appointment the server would now refuse both on. Same
  // lesson as the doctor picker: match by id, not by reference.
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [cancelReason, setCancelReason] = useState('');
  const [rescheduleDate, setRescheduleDate] = useState('');
  const [rescheduleTime, setRescheduleTime] = useState('');
  const [dailyStatus, setDailyStatus] = useState('');
  const [dailyError, setDailyError] = useState<string | null>(null);

  // ── Book ───────────────────────────────────────────────────────────────
  const [patient, setPatient] = useState<Patient | null>(null);
  const [addingPatient, setAddingPatient] = useState(false);
  const [doctorId, setDoctorId] = useState('');
  const [context, setContext] = useState<AppointmentModuleContext>('General');
  const [bookingDate, setBookingDate] = useState(tomorrow);
  const [bookingTime, setBookingTime] = useState('10:00');
  const [duration, setDuration] = useState('15');
  const [reason, setReason] = useState('');
  const [notes, setNotes] = useState('');
  const [bookingStatus, setBookingStatus] = useState('');
  const [bookingError, setBookingError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  // ── Reminders ──────────────────────────────────────────────────────────
  const [leadDays, setLeadDays] = useState('3');
  const [reminders, setReminders] = useState<ReminderItem[]>([]);
  const [reminderError, setReminderError] = useState<string | null>(null);

  const loadDay = useCallback(async (forDate: string) => {
    try {
      setList(await api.get<Appointment[]>(`/api/appointments?date=${forDate}`));
      setDailyError(null);
    } catch (err) {
      setDailyError(err instanceof ApiError ? err.message : "Could not load the day's appointments.");
    }
  }, []);

  const loadReminders = useCallback(async (days: string) => {
    try {
      setReminders(await api.get<ReminderItem[]>(`/api/appointments/reminders?leadDays=${Math.max(0, Number(days) || 0)}`));
      setReminderError(null);
    } catch (err) {
      setReminderError(err instanceof ApiError ? err.message : 'Could not load reminders.');
    }
  }, []);

  useEffect(() => {
    void api
      .get<Doctor[]>('/api/doctors')
      .then((d) => {
        setDoctors(d);
        // Match the previous pick back up by id, never by object identity —
        // the doctors list is rebuilt from a fresh AsNoTracking read on every
        // load, so holding the old object silently blanks the picker.
        setDoctorId((current) => (d.some((x) => x.id === current) ? current : (d[0]?.id ?? '')));
      })
      .catch(() => {
      // Optional enrichment: an empty list here costs a little typing, not
      // correctness, and no screen states anything about it being empty.
    });
  }, []);

  useEffect(() => {
    void loadDay(date);
  }, [date, loadDay]);

  useEffect(() => {
    void loadReminders(leadDays);
  }, [leadDays, loadReminders]);

  // The desktop reloads on tab change for exactly one reason: a booking made
  // on the Book tab otherwise never appears on Today's list until you leave
  // the page and come back.
  const openTab = (next: Tab) => {
    setTab(next);
    if (next === 'today') void loadDay(date);
    else if (next === 'reminders') void loadReminders(leadDays);
  };

  /** Only a module that is switched on is offered — booking against one that
   * is off is refused server-side, so offering it is a dead end. */
  const enabledContexts = useMemo(() => {
    if (!general) return ['General'] as AppointmentModuleContext[];
    const on: AppointmentModuleContext[] = [];
    if (general.opdEnabled) on.push('General');
    if (general.pediatricsEnabled) on.push('Pediatrics');
    if (general.dentistEnabled) on.push('Dentist');
    if (general.pathologyLabEnabled) on.push('PathologyLab');
    return on;
  }, [general]);

  useEffect(() => {
    if (enabledContexts.length > 0 && !enabledContexts.includes(context)) setContext(enabledContexts[0]);
  }, [enabledContexts, context]);

  // ── Today's actions ────────────────────────────────────────────────────

  const runDaily = async (fn: () => Promise<string>) => {
    setDailyError(null);
    try {
      setDailyStatus(await fn());
      await loadDay(date);
    } catch (err) {
      setDailyError(err instanceof ApiError ? err.message : 'That did not go through.');
    }
  };

  const checkIn = (a: Appointment) =>
    runDaily(async () => {
      const result = await api.post<CheckInResult>(`/api/appointments/${a.id}/check-in`);
      return `${result.patientName} checked in — token ${result.tokenNo}.`;
    });

  const cancel = (a: Appointment) => {
    if (!cancelReason.trim()) {
      setDailyError('Say why the appointment is being cancelled.');
      return;
    }
    // Stays a real confirm, not an inline message: a booked slot is being
    // destroyed, which is the same call OPD's cancel-visit made.
    if (!window.confirm(`Cancel ${a.patientName}'s appointment at ${timeOf(a.scheduledOn)}?`)) return;

    void runDaily(async () => {
      await api.post(`/api/appointments/${a.id}/cancel`, { reason: cancelReason.trim() });
      setCancelReason('');
      return `${a.patientName}'s appointment cancelled.`;
    });
  };

  const reschedule = (a: Appointment) => {
    if (!rescheduleDate) {
      setDailyError('Enter the new date.');
      return;
    }
    // An empty time keeps the slot's original hour — moving an appointment a
    // week out usually keeps the time of day.
    const time = rescheduleTime || new Date(a.scheduledOn).toTimeString().slice(0, 5);

    void runDaily(async () => {
      const moved = await api.post<Appointment>(`/api/appointments/${a.id}/reschedule`, {
        scheduledOn: `${rescheduleDate}T${time}:00`,
        durationMinutes: null,
      });
      setRescheduleDate('');
      setRescheduleTime('');
      const when = new Date(moved.scheduledOn);
      return `${a.patientName} moved to ${when.toLocaleDateString()} ${timeOf(moved.scheduledOn)} (${moved.appointmentNo}).`;
    });
  };

  // ── Booking ────────────────────────────────────────────────────────────

  // One hook for the debounce, the fetch, the cancel and the single-match
  // rule - see usePatientSearch for why this stopped being written per page.
  const { search, setSearch, matches, reset: resetSearch } = usePatientSearch({
    onSingleMatch: setPatient,
  });

  const resetBooking = () => {
    resetSearch();
    setPatient(null);
    setReason('');
    setNotes('');
    setDuration('15');
    // Date, time and doctor deliberately persist — booking a family into
    // consecutive slots is the common case.
  };

  const printSlip = async (appointmentId: string) => {
    try {
      await openPdf(`/api/print/appointment/${appointmentId}`);
    } catch (err) {
      setDailyError(err instanceof ApiError ? err.message : 'Could not open the slip.');
    }
  };

  // F2 clears the form for the next booking, F4 books, F8 books and prints
  // the slip -- the same three meanings as every other screen.
  const G = 'Appointments';

  const book = async (print: boolean) => {
    setBookingError(null);

    if (!patient) {
      setBookingError('Select a patient, or add a new one.');
      return;
    }
    if (!doctorId) {
      setBookingError('Select a doctor.');
      return;
    }
    if (!bookingDate) {
      setBookingError('Enter a valid date.');
      return;
    }

    setBusy(true);
    try {
      // Naive local date-time, never .toISOString(): the server stores and
      // returns clinic-local times with no offset. See BookVisitDialog.
      const saved = await api.post<Appointment>('/api/appointments', {
        patientId: patient.id,
        doctorId,
        scheduledOn: `${bookingDate}T${bookingTime || '00:00'}:00`,
        durationMinutes: Number(duration) > 0 ? Number(duration) : 15,
        moduleContext: context,
        reason: reason.trim() || null,
        notes: notes.trim() || null,
      });

      const when = new Date(saved.scheduledOn);
      setBookingStatus(`${saved.appointmentNo} booked for ${when.toLocaleDateString()} ${timeOf(saved.scheduledOn)}.`);

      // Printed after the booking is saved, never before: the slip carries
      // the appointment number, and that number is allocated server-side.
      if (print) await openPdf(`/api/print/appointment/${saved.id}`);

      resetBooking();
      if (bookingDate === date) await loadDay(date);
    } catch (err) {
      setBookingError(err instanceof ApiError ? err.message : 'Could not book the appointment.');
    } finally {
      setBusy(false);
    }
  };

  // ── Reminders ──────────────────────────────────────────────────────────

  const toggleReminder = async (r: ReminderItem) => {
    setReminderError(null);
    try {
      await api.post('/api/appointments/reminders/toggle', {
        sourceKind: r.sourceKind,
        sourceId: r.sourceId,
        patientId: r.patientId,
        dueOn: r.dueOn,
        isReminded: r.isReminded,
      });
      await loadReminders(leadDays);
    } catch (err) {
      setReminderError(err instanceof ApiError ? err.message : 'Could not update the reminder.');
    }
  };

  const subtitle =
    list.length === 0 ? 'Nothing booked for this day' : `${list.length} appointment(s)`;

  // Always the row as it stands in the list right now.
  const selected = list.find((a) => a.id === selectedId) ?? null;

  // Registered after the actions they call, so no binding refers to a
  // function declared further down the file.
  useHotkey('f2', 'Start a new booking', G, () => resetBooking());
  useHotkey('f4', 'Book the appointment', G, () => { if (!busy) void book(false); });
  useHotkey('f8', 'Book and print the slip', G, () => { if (!busy) void book(true); });

  return (
    <div className="page">
      <div className="page-head">
        <div>
          <h1>Appointments</h1>
          <p className="hint">{subtitle}</p>
        </div>
      </div>

      <div className="tabs">
        {TABS.map((t) => (
          <button
            key={t.id}
            type="button"
            className={t.id === tab ? 'tab active' : 'tab'}
            onClick={() => openTab(t.id)}
          >
            {t.label}
          </button>
        ))}
      </div>

      {tab === 'today' && (
        <>
          <div className="inline-form">
            <input type="date" value={date} onChange={(e) => setDate(e.target.value)} />
          </div>

          {dailyError && <p className="auth-error">{dailyError}</p>}
          {dailyStatus && <p className="hint status-line">{dailyStatus}</p>}

          <section className="card">
            <table>
              <thead>
                <tr>
                  <th>Time</th><th>No.</th><th>Patient</th><th>Doctor</th>
                  <th>For</th><th>Status</th><th></th>
                </tr>
              </thead>
              <tbody>
                {list.map((a) => (
                  <tr
                    key={a.id}
                    onClick={() => setSelectedId(a.id)}
                    className={selectedId === a.id ? 'selected-row' : undefined}
                  >
                    <td>{timeOf(a.scheduledOn)}</td>
                    <td>{a.appointmentNo}</td>
                    <td>
                      {a.patientName}
                      {a.patientPhone && <div className="hint">{a.patientPhone}</div>}
                      {a.reason && <div className="hint">{a.reason}</div>}
                    </td>
                    <td>{a.doctorName}</td>
                    <td>{CONTEXT_LABELS[a.moduleContext]}</td>
                    <td><span className="badge">{a.status}</span></td>
                    <td className="row-actions">
                      {a.status === 'Scheduled' && (
                        <button
                          type="button"
                          // Acting on a row should not also select it — the
                          // click would otherwise bubble to the row handler.
                          onClick={(e) => { e.stopPropagation(); void checkIn(a); }}
                        >
                          Check in
                        </button>
                      )}
                      {/* Any appointment, not only a freshly booked one — a
                          patient who lost the slip is the obvious case. */}
                      <button
                        type="button"
                        className="ghost"
                        onClick={(e) => { e.stopPropagation(); void printSlip(a.id); }}
                      >
                        Slip
                      </button>
                    </td>
                  </tr>
                ))}
                {list.length === 0 && <tr><td colSpan={7}>Nothing booked for this day.</td></tr>}
              </tbody>
            </table>
          </section>

          {selected && selected.status === 'Scheduled' && (
            <section className="card">
              <h2>{selected.patientName} · {timeOf(selected.scheduledOn)} · {selected.appointmentNo}</h2>

              <div className="settings-row">
                <label>Cancel — reason</label>
                <div className="inline-form">
                  <input
                    placeholder="Why is it being cancelled?"
                    value={cancelReason}
                    onChange={(e) => setCancelReason(e.target.value)}
                  />
                  <button type="button" onClick={() => cancel(selected)}>Cancel appointment</button>
                </div>
              </div>

              <div className="settings-row">
                <label>Reschedule to</label>
                <div className="inline-form">
                  <input type="date" value={rescheduleDate} onChange={(e) => setRescheduleDate(e.target.value)} />
                  <input
                    type="time"
                    value={rescheduleTime}
                    onChange={(e) => setRescheduleTime(e.target.value)}
                    title="Leave blank to keep the original time"
                  />
                  <button type="button" onClick={() => reschedule(selected)}>Reschedule</button>
                </div>
                <p className="hint">Leave the time blank to keep {timeOf(selected.scheduledOn)}.</p>
              </div>
            </section>
          )}
        </>
      )}

      {tab === 'book' && (
        <>
          {bookingError && <p className="auth-error">{bookingError}</p>}
          {bookingStatus && <p className="hint status-line">{bookingStatus}</p>}

          <section className="card">
            <h2>1 · Who is coming</h2>
            {patient ? (
              <div className="counter-selected">
                <strong>{patient.name}</strong>
                <span className="hint"> · {patient.age}{patient.gender.charAt(0)}{patient.phone && ` · ${patient.phone}`}</span>
                <button type="button" className="ghost" onClick={() => { setPatient(null); resetSearch(); }}>
                  Change patient
                </button>
              </div>
            ) : (
              <PatientPicker
                group={G}
                search={search}
                onSearchChange={setSearch}
                matches={matches}
                onPick={(p) => { setPatient(p); resetSearch(); }}
                onNewPatient={() => setAddingPatient(true)}
                label={(p) => (
                  <>
                    {p.name} · {p.age}{p.gender.charAt(0)}{p.phone && ` · ${p.phone}`}
                  </>
                )}
              />
            )}
          </section>

          <section className="card">
            <h2>2 · When and with whom</h2>
            <div className="settings-form">
              <div className="settings-row">
                <label>For</label>
                <select value={context} onChange={(e) => setContext(e.target.value as AppointmentModuleContext)}>
                  {enabledContexts.map((c) => (
                    <option key={c} value={c}>{CONTEXT_LABELS[c]}</option>
                  ))}
                </select>
              </div>
              <div className="settings-row">
                <label>Doctor</label>
                <select value={doctorId} onChange={(e) => setDoctorId(e.target.value)}>
                  <option value="">Doctor…</option>
                  {doctors.map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}
                </select>
              </div>
              <div className="settings-row">
                <label>Date and time</label>
                <div className="inline-form">
                  <input type="date" value={bookingDate} onChange={(e) => setBookingDate(e.target.value)} />
                  <input type="time" value={bookingTime} onChange={(e) => setBookingTime(e.target.value)} />
                </div>
              </div>
              <div className="settings-row">
                <label>Duration (minutes)</label>
                <input
                  type="number"
                  min={1}
                  value={duration}
                  onChange={(e) => setDuration(e.target.value)}
                />
              </div>
            </div>
          </section>

          <section className="card">
            <h2>3 · What it is for</h2>
            <div className="settings-form">
              <div className="settings-row">
                <label>Reason</label>
                <input value={reason} onChange={(e) => setReason(e.target.value)} />
              </div>
              <div className="settings-row">
                <label>Notes</label>
                <input value={notes} onChange={(e) => setNotes(e.target.value)} />
              </div>
            </div>
            <div className="settings-actions">
              <button type="button" className="primary" disabled={busy} onClick={() => void book(false)}>Book appointment</button>
              <button type="button" disabled={busy} onClick={() => void book(true)}>Book &amp; print</button>
              <button type="button" className="ghost" onClick={resetBooking}>Clear</button>
            </div>
          </section>
        </>
      )}

      {tab === 'reminders' && (
        <>
          <div className="inline-form">
            <label>Due within</label>
            <input
              type="number"
              min={0}
              value={leadDays}
              onChange={(e) => setLeadDays(e.target.value)}
              style={{ width: '5rem' }}
            />
            <span className="hint">days</span>
            <div className="chips">
              {LEAD_DAY_OPTIONS.map((n) => (
                <button
                  key={n}
                  type="button"
                  className={String(n) === leadDays ? 'tab active' : 'tab'}
                  onClick={() => setLeadDays(String(n))}
                >
                  {n}
                </button>
              ))}
            </div>
          </div>

          {reminderError && <p className="auth-error">{reminderError}</p>}

          <section className="card">
            <table>
              <thead>
                <tr><th>Due</th><th>Patient</th><th>Phone</th><th>What</th><th>Status</th><th></th></tr>
              </thead>
              <tbody>
                {reminders.map((r) => (
                  <tr key={`${r.sourceKind}-${r.sourceId}`}>
                    <td>{new Date(r.dueOn).toLocaleDateString()}</td>
                    <td>{r.patientName}</td>
                    <td>{r.patientPhone ?? '—'}</td>
                    <td>{r.description}</td>
                    <td><span className="badge">{r.statusLabel}</span></td>
                    <td className="row-actions">
                      <button type="button" onClick={() => void toggleReminder(r)}>
                        {r.isReminded ? 'Mark not reminded' : 'Mark reminded'}
                      </button>
                    </td>
                  </tr>
                ))}
                {reminders.length === 0 && <tr><td colSpan={6}>Nothing due in this window.</td></tr>}
              </tbody>
            </table>
          </section>
        </>
      )}

      <ShortcutHints keys={[['f2', 'new booking'], ['f3', 'find patient'], ['f4', 'book'], ['f8', 'book & print']]} />

      {addingPatient && (
        <PatientEditorDialog
          existing={null}
          onClose={() => setAddingPatient(false)}
          onSaved={(_message, saved) => {
            if (saved) setPatient(saved);
            setAddingPatient(false);
            resetSearch();
          }}
        />
      )}
    </div>
  );
}
