import { useGeneralSettings } from '../settings/SettingsContext';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api, ApiError, openPdf } from '../api/client';
import type { ClinicProfile, ClinicSession, Doctor, Visit, VisitStatus } from '../api/types';
import { describeSession, isInSession, SESSIONS } from '../opd/session';
import { BookVisitDialog } from '../opd/BookVisitDialog';
import { CollectFeeDialog } from '../opd/CollectFeeDialog';
import { describeCombo, useHotkey } from '../shell/hotkeys';
import { ShortcutHints } from '../shell/ShortcutHints';

/** How a status should read at a glance. The word still appears — colour
 *  alone would fail a colour-blind receptionist and a monochrome screen. */
const STATUS_CLASS: Record<VisitStatus, string> = {
  Booked: 's-booked',
  Waiting: 's-waiting',
  InConsultation: 's-consult',
  Completed: 's-done',
  Cancelled: 's-cancelled',
};

const STATUS_TEXT: Record<VisitStatus, string> = {
  Booked: 'Booked',
  Waiting: 'Waiting',
  InConsultation: 'In consultation',
  Completed: 'Completed',
  Cancelled: 'Cancelled',
};

const StatusChip = ({ status }: { status: VisitStatus }) => (
  <span className={`status-chip ${STATUS_CLASS[status]}`}>{STATUS_TEXT[status]}</span>
);

/** Still to be seen — everything that has not finished or been cancelled.
 * Mirrors the desktop's Visit.IsWaiting. */
const isWaiting = (v: Visit) =>
  v.status === 'Booked' || v.status === 'Waiting' || v.status === 'InConsultation';

/** A visit can be cancelled only while it is Booked or Waiting and nothing
 * has been taken for it. Mirrors Visit.CanCancel — the server refuses too. */
const canCancel = (v: Visit) => !v.feePaid && (v.status === 'Booked' || v.status === 'Waiting');

const today = () => new Date().toISOString().slice(0, 10);

export function OpdQueuePage() {
  const navigate = useNavigate();

  const [date, setDate] = useState(today);
  const [all, setAll] = useState<Visit[]>([]);
  const [doctors, setDoctors] = useState<Doctor[]>([]);
  const [clinic, setClinic] = useState<ClinicProfile | null>(null);
  // Tiles vs rows is a Settings choice. Derived rather than held in state:
  // it is a straight read of a value the provider already has, and copying
  // it into local state only created a window where the two disagreed.
  const general = useGeneralSettings();
  const useTiles = general ? general.queueLayout === 'Tiles' : true;

  const [doctorTab, setDoctorTab] = useState<string | null>(null); // null = All
  const [session, setSession] = useState<ClinicSession>('FullDay');

  const [status, setStatus] = useState('');
  const [error, setError] = useState<string | null>(null);

  const [booking, setBooking] = useState(false);
  const [collectingFor, setCollectingFor] = useState<Visit | null>(null);

  // The keyboard's cursor. Held as an id, never as a row object: the list is
  // refetched after every action, and a held object goes stale the moment
  // anything changes underneath it.
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const refresh = useCallback(async (forDate: string) => {
    try {
      setAll(await api.get<Visit[]>(`/api/visits?date=${forDate}`));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not load the queue.');
    }
  }, []);

  useEffect(() => {
    void api.get<Doctor[]>('/api/doctors').then(setDoctors).catch(() => {
      // Optional enrichment: an empty list here costs a little typing, not
      // correctness, and no screen states anything about it being empty.
    });
    void api.get<ClinicProfile>('/api/settings/clinic').then(setClinic).catch(() => {
      // Optional enrichment: an empty list here costs a little typing, not
      // correctness, and no screen states anything about it being empty.
    });
  }, []);

  useEffect(() => {
    void refresh(date);
  }, [date, refresh]);

  // Split into the two columns for the chosen doctor and sitting. Visits
  // outside the sitting are counted rather than dropped: an afternoon
  // walk-in belongs to neither, and a queue that quietly loses somebody is
  // worse than one that says it is filtered.
  const { waiting, completed, hidden } = useMemo(() => {
    const w: Visit[] = [];
    const c: Visit[] = [];
    let h = 0;

    for (const v of all) {
      if (doctorTab && v.doctorId !== doctorTab) continue;

      if (clinic && !isInSession(clinic, session, v.scheduledOn)) {
        if (isWaiting(v) || v.status === 'Completed') h++;
        continue;
      }

      if (isWaiting(v)) w.push(v);
      else if (v.status === 'Completed') c.push(v);
    }

    return { waiting: w, completed: c, hidden: h };
  }, [all, doctorTab, session, clinic]);

  // Waiting first, then completed — the order the eye travels and therefore
  // the order Up and Down should travel too.
  const ordered = useMemo(() => [...waiting, ...completed], [waiting, completed]);

  const selected = useMemo(
    () => ordered.find((v) => v.id === selectedId) ?? null,
    [ordered, selectedId],
  );

  // Keep a cursor on screen. If the selected visit filtered away — a doctor
  // tab changed, a sitting changed, it was cancelled — fall back to the top
  // rather than leaving the shortcuts pointed at nothing.
  useEffect(() => {
    if (ordered.length === 0) {
      if (selectedId !== null) setSelectedId(null);
    } else if (!ordered.some((v) => v.id === selectedId)) {
      setSelectedId(ordered[0].id);
    }
  }, [ordered, selectedId]);

  const move = useCallback(
    (delta: number) => {
      if (ordered.length === 0) return;
      const at = ordered.findIndex((v) => v.id === selectedId);
      const next = at === -1 ? 0 : (at + delta + ordered.length) % ordered.length;
      setSelectedId(ordered[next].id);
      document
        .querySelector(`[data-visit="${ordered[next].id}"]`)
        ?.scrollIntoView({ block: 'nearest' });
    },
    [ordered, selectedId],
  );

  const subtitle = useMemo(() => {
    const when = new Date(`${date}T00:00:00`).toLocaleDateString(undefined, {
      weekday: 'short', day: '2-digit', month: 'short',
    });
    let line = `${waiting.length} waiting · ${completed.length} completed · ${when}`;
    if (session !== 'FullDay' && clinic) {
      line += ` · ${session} sitting, ${describeSession(clinic, session)}`;
      if (hidden > 0) line += ` · ${hidden} more today outside these hours`;
    }
    return line;
  }, [waiting.length, completed.length, date, session, clinic, hidden]);

  const act = async (fn: () => Promise<void>, ok: string) => {
    setError(null);
    try {
      await fn();
      await refresh(date);
      setStatus(ok);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'That did not work.');
    }
  };

  const setStatusOf = (visit: Visit, next: VisitStatus, message: string) =>
    act(() => api.post(`/api/visits/${visit.id}/status`, { status: next }), message);

  const cancel = async (visit: Visit) => {
    if (!canCancel(visit)) {
      setStatus(
        visit.feePaid
          ? `Token ${visit.tokenNo} has already been paid and cannot be cancelled.`
          : `Token ${visit.tokenNo} is already ${visit.status.toLowerCase()}.`,
      );
      await refresh(date);
      return;
    }

    if (!window.confirm(`Cancel token ${visit.tokenNo} for ${visit.patient.name}?`)) return;
    await setStatusOf(visit, 'Cancelled', `Token ${visit.tokenNo} cancelled.`);
  };

  const consult = async (visit: Visit) => {
    // Moving to InConsultation before opening is what takes the tile out of
    // the "not started" state for anyone else looking at the queue.
    await act(
      () => api.post(`/api/visits/${visit.id}/status`, { status: 'InConsultation' }),
      `Token ${visit.tokenNo} in consultation.`,
    );
    navigate(`/consultation/${visit.id}`);
  };

  const collectFee = (visit: Visit) => {
    if (visit.feePaid) {
      setStatus(`Token ${visit.tokenNo} has already paid — use Receipt to reprint.`);
      return;
    }
    setCollectingFor(visit);
  };

  const print = async (path: string, unavailable: string) => {
    setError(null);
    try {
      await openPdf(path);
    } catch (err) {
      setStatus(err instanceof ApiError && err.status === 400 ? unavailable : String(err));
    }
  };

  // The counter's keys. Bound to the same functions the buttons call, so a
  // shortcut can never drift away from what the button does — and every one
  // of them refuses politely rather than silently when it does not apply.
  const G = 'OPD queue';
  useHotkey('f2', 'Book a visit', G, () => setBooking(true));
  useHotkey('arrowdown', 'Next in queue', G, () => move(1));
  useHotkey('arrowup', 'Previous in queue', G, () => move(-1));
  useHotkey('enter', 'Open consultation', G, () => {
    if (selected && isWaiting(selected)) void consult(selected);
  });
  useHotkey('f4', 'Collect the fee', G, () => {
    if (selected) collectFee(selected);
  });
  useHotkey('f6', 'Mark arrived', G, () => {
    if (selected && selected.status === 'Booked') {
      void setStatusOf(selected, 'Waiting', `Token ${selected.tokenNo} marked arrived.`);
    }
  });
  useHotkey('f8', 'Move to completed', G, () => {
    if (selected && isWaiting(selected)) {
      void setStatusOf(selected, 'Completed', `Token ${selected.tokenNo} moved to completed.`);
    }
  });

  const actions = (visit: Visit) => (
    <div className="row-actions">
      {visit.status === 'Booked' && (
        <button type="button" onClick={() => setStatusOf(visit, 'Waiting', `Token ${visit.tokenNo} marked arrived.`)}>
          Arrived
        </button>
      )}
      {isWaiting(visit) && (
        <button type="button" onClick={() => consult(visit)}>Consult</button>
      )}
      {isWaiting(visit) && (
        <button
          type="button"
          onClick={() => setStatusOf(visit, 'Completed', `Token ${visit.tokenNo} moved to completed.`)}
        >
          Complete
        </button>
      )}
      {visit.status === 'Completed' && (
        <button
          type="button"
          onClick={() => setStatusOf(visit, 'Waiting', `Token ${visit.tokenNo} moved back to waiting.`)}
        >
          Reopen
        </button>
      )}
      {!visit.feePaid && visit.status !== 'Cancelled' && (
        <button type="button" onClick={() => collectFee(visit)}>Fee</button>
      )}
      {visit.feePaid && (
        <button
          type="button"
          className="ghost"
          onClick={() => print(`/api/print/receipt/${visit.id}?reprint=true`, 'No receipt to print.')}
        >
          Receipt
        </button>
      )}
      <button
        type="button"
        className="ghost"
        onClick={() => print(`/api/print/prescription/${visit.id}`, `Token ${visit.tokenNo} has no prescription yet.`)}
      >
        Rx
      </button>
      {canCancel(visit) && (
        <button type="button" className="danger" onClick={() => cancel(visit)}>Cancel</button>
      )}
    </div>
  );

  const tile = (visit: Visit) => (
    <div
      className={visit.id === selectedId ? 'tile selected-row' : 'tile'}
      key={visit.id}
      data-visit={visit.id}
      onClick={() => setSelectedId(visit.id)}
    >
      <div className="tile-head">
        <span className="token">{visit.tokenNo}</span>
        <div>
          <strong>{visit.patient.name}</strong>
          <div className="hint">
            {visit.patient.age}
            {visit.patient.gender.charAt(0)} ·{' '}
            {new Date(visit.scheduledOn).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })} ·{' '}
            {visit.doctor.name}
          </div>
        </div>
        <span className={`badge ${visit.feePaid ? 'paid' : 'unpaid'}`}>
          ₹{visit.fee.toFixed(2)}{visit.feePaid ? ' paid' : ''}
        </span>
      </div>
      <StatusChip status={visit.status} />
      {visit.complaint && <p className="hint complaint">{visit.complaint}</p>}
      {actions(visit)}
    </div>
  );

  const rows = (list: Visit[]) => (
    <table>
      <thead>
        <tr>
          <th>Token</th><th>Patient</th><th>Time</th><th>Doctor</th><th>Status</th><th>Fee</th><th>Actions</th>
        </tr>
      </thead>
      <tbody>
        {list.map((v) => (
          <tr
            key={v.id}
            data-visit={v.id}
            className={v.id === selectedId ? 'selected-row' : undefined}
            onClick={() => setSelectedId(v.id)}
          >
            <td>{v.tokenNo}</td>
            <td>
              {v.patient.name}
              <span className="hint"> · {v.patient.age}{v.patient.gender.charAt(0)}</span>
            </td>
            <td>{new Date(v.scheduledOn).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}</td>
            <td>{v.doctor.name}</td>
            <td><StatusChip status={v.status} /></td>
            <td>{v.fee.toFixed(2)}{v.feePaid ? ' (paid)' : ''}</td>
            <td>{actions(v)}</td>
          </tr>
        ))}
        {list.length === 0 && <tr><td colSpan={7}>Nobody here.</td></tr>}
      </tbody>
    </table>
  );

  const column = (title: string, list: Visit[]) => (
    <section className="card queue-column">
      <h2>{title} <span className="hint">({list.length})</span></h2>
      {useTiles ? (
        list.length === 0 ? <p className="hint">Nobody here.</p> : <div className="tiles">{list.map(tile)}</div>
      ) : (
        rows(list)
      )}
    </section>
  );

  return (
    <div className="page wide">
      <div className="page-head">
        <div>
          <h1>OPD Queue</h1>
          <p className="hint">{subtitle}</p>
        </div>
        <div className="inline-form">
          <input type="date" value={date} onChange={(e) => setDate(e.target.value)} />
          <select value={session} onChange={(e) => setSession(e.target.value as ClinicSession)}>
            {SESSIONS.map((s) => <option key={s.id} value={s.id}>{s.label}</option>)}
          </select>
          <button type="button" className="primary" onClick={() => setBooking(true)}>
            Book visit <span className="kbd">{describeCombo('f2')}</span>
          </button>
        </div>
      </div>

      {/* One tab per doctor rather than a column repeated on every row — the
          duplication that dominated the old desktop screen. */}
      <div className="tabs">
        <button
          type="button"
          className={doctorTab === null ? 'tab active' : 'tab'}
          onClick={() => setDoctorTab(null)}
        >
          All doctors
        </button>
        {doctors.map((d) => (
          <button
            key={d.id}
            type="button"
            className={doctorTab === d.id ? 'tab active' : 'tab'}
            onClick={() => setDoctorTab(d.id)}
          >
            {d.name}
          </button>
        ))}
      </div>

      {error && <p className="auth-error">{error}</p>}
      {status && <p className="hint status-line">{status}</p>}

      <div className="queue-columns">
        {column('Waiting', waiting)}
        {column('Completed', completed)}
      </div>

      <ShortcutHints keys={[['arrowup arrowdown', 'move'], ['enter', 'consult'], ['f4', 'fee'], ['f6', 'arrived'], ['f8', 'complete'], ['f2', 'book visit']]} />

      {booking && (
        <BookVisitDialog
          doctors={doctors}
          preferredDoctorId={doctorTab}
          date={date}
          onClose={() => setBooking(false)}
          onBooked={async (message) => {
            setBooking(false);
            await refresh(date);
            setStatus(message);
          }}
        />
      )}

      {collectingFor && (
        <CollectFeeDialog
          visit={collectingFor}
          onClose={() => setCollectingFor(null)}
          onCollected={async (message) => {
            setCollectingFor(null);
            await refresh(date);
            setStatus(message);
          }}
        />
      )}
    </div>
  );
}
