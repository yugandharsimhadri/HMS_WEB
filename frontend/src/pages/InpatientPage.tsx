import { useCallback, useEffect, useMemo, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import type { AdmissionRow, BedBoardItem, Patient } from '../api/types';
import { AdmitDialog } from '../inpatient/AdmitDialog';
import { dateTime, dayOfStay, money, todayInput } from '../inpatient/ip';
import { useHotkey } from '../shell/hotkeys';
import { ShortcutHints } from '../shell/ShortcutHints';

type View = 'beds' | 'admitted' | 'discharged';

/**
 * In-patients home: the bed board, the list of who is in, and who has left.
 * The board is the first screen because "which bed is free?" is the question
 * the desk asks most.
 */
export function InpatientPage() {
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();

  const [view, setView] = useState<View>('beds');
  const [board, setBoard] = useState<BedBoardItem[]>([]);
  const [rows, setRows] = useState<AdmissionRow[]>([]);
  const [term, setTerm] = useState('');
  const [from, setFrom] = useState(() => {
    const d = new Date(); d.setDate(d.getDate() - 30);
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
  });
  const [to, setTo] = useState(todayInput());
  const [duesOnly, setDuesOnly] = useState(false);

  const [admitting, setAdmitting] = useState<{ bedId?: string | null; patient?: Patient | null; doctorId?: string | null; visitId?: string | null; reason?: string | null } | null>(null);
  const [error, setError] = useState<string | null>(null);

  const loadBoard = useCallback(async () => {
    try {
      setBoard(await api.get<BedBoardItem[]>('/api/inpatient/board'));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not load the beds.');
    }
  }, []);

  const loadRows = useCallback(async () => {
    if (view === 'beds') return;
    const p = new URLSearchParams({ status: view === 'admitted' ? 'Admitted' : 'Discharged' });
    if (view === 'discharged') { p.set('from', from); p.set('to', to); }
    if (term.trim()) p.set('term', term.trim());
    try {
      setRows(await api.get<AdmissionRow[]>(`/api/inpatient/admissions?${p}`));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not load the list.');
    }
  }, [view, from, to, term]);

  useEffect(() => { void loadBoard(); }, [loadBoard]);
  useEffect(() => {
    const t = setTimeout(() => { void loadRows(); }, 250);
    return () => clearTimeout(t);
  }, [loadRows]);

  // Admit from an OPD visit: /ip?admit=1&patientNo=P00012&doctor=…&visit=…
  useEffect(() => {
    if (params.get('admit') !== '1') return;
    const patientNo = params.get('patientNo');
    const open = (patient: Patient | null) => setAdmitting({
      patient, doctorId: params.get('doctor'), visitId: params.get('visit'), reason: params.get('reason'),
    });
    if (patientNo) {
      void api.get<Patient[]>(`/api/patients?term=${encodeURIComponent(patientNo)}&take=5`)
        .then((found) => open(found.find((p) => p.patientNo === patientNo) ?? null));
    } else {
      open(null);
    }
    setParams({}, { replace: true });
  }, [params, setParams]);

  const setBedStatus = async (bed: BedBoardItem, status: 'Available' | 'Blocked') => {
    let note: string | null = null;
    if (status === 'Blocked') {
      note = window.prompt(`Why is bed ${bed.bedNo} being blocked?`);
      if (!note?.trim()) return;
    }
    setError(null);
    try {
      await api.post(`/api/inpatient/beds/${bed.bedId}/status`, { status, note });
      await loadBoard();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not change the bed.');
    }
  };

  const counts = useMemo(() => {
    const active = board;
    return {
      total: active.length,
      occupied: active.filter((b) => b.status === 'Occupied').length,
      available: active.filter((b) => b.status === 'Available').length,
      cleaning: active.filter((b) => b.status === 'Cleaning').length,
      blocked: active.filter((b) => b.status === 'Blocked').length,
      billed: active.filter((b) => b.hasFinalBill).length,
    };
  }, [board]);

  const wards = useMemo(() => [...new Set(board.map((b) => b.ward))], [board]);

  useHotkey('f2', 'Admit a patient', 'In-patients', () => setAdmitting({}));

  return (
    <div className="page">
      <div className="page-head">
        <div>
          <h1>In-patients</h1>
          <p className="hint">{counts.occupied} of {counts.total} beds occupied · {counts.available} free</p>
        </div>
        <div className="inline-form">
          <button type="button" onClick={() => setAdmitting({})}>Admit patient <span className="kbd">F2</span></button>
        </div>
      </div>

      <div className="tabs">
        {(['beds', 'admitted', 'discharged'] as View[]).map((v) => (
          <button key={v} type="button" className={view === v ? 'tab active' : 'tab'} onClick={() => setView(v)}>
            {v === 'beds' ? 'Beds' : v === 'admitted' ? `Admitted (${counts.occupied})` : 'Discharged'}
          </button>
        ))}
      </div>

      {error && <p className="auth-error">{error}</p>}

      {view === 'beds' && (
        <>
          {board.length === 0 && (
            <section className="card">
              <p>No beds yet. Add room types and beds under <strong>Masters → In-patient</strong>.</p>
              <div className="inline-form"><button type="button" onClick={() => navigate('/masters?tab=ip-beds')}>Set up beds</button></div>
            </section>
          )}
          {wards.map((ward) => (
            <section key={ward} className="ip-ward">
              <h2 className="ip-ward-title">{ward}</h2>
              <div className="ip-beds">
                {board.filter((b) => b.ward === ward).map((b) => (
                  <BedTile
                    key={b.bedId}
                    bed={b}
                    onOpen={() => b.admissionId && navigate(`/ip/${b.admissionId}`)}
                    onAdmit={() => setAdmitting({ bedId: b.bedId })}
                    onReady={() => void setBedStatus(b, 'Available')}
                    onBlock={() => void setBedStatus(b, 'Blocked')}
                  />
                ))}
              </div>
            </section>
          ))}
        </>
      )}

      {view !== 'beds' && (
        <section className="card">
          <div className="inline-form">
            <input placeholder="Name, phone, patient or IP number" value={term} onChange={(e) => setTerm(e.target.value)} />
            {view === 'discharged' && (
              <>
                <label>From</label>
                <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
                <label>To</label>
                <input type="date" value={to} onChange={(e) => setTo(e.target.value)} />
                <label className="checkbox-label"><input type="checkbox" checked={duesOnly} onChange={() => setDuesOnly(!duesOnly)} /><span>Only with balance due</span></label>
              </>
            )}
          </div>
          <table>
            <thead>
              <tr>
                <th>IP no.</th><th>Patient</th><th>Bed</th><th>Doctor</th><th>Admitted</th>
                {view === 'discharged' ? <th>Discharged</th> : <th style={{ textAlign: 'right' }}>Day</th>}
                <th style={{ textAlign: 'right' }}>Balance</th>
              </tr>
            </thead>
            <tbody>
              {rows.filter((r) => !duesOnly || view !== 'discharged' || r.balance > 0).map((r) => (
                <tr key={r.id} className="clickable" onClick={() => navigate(`/ip/${r.id}`)}>
                  <td className="mono">{r.ipNo}</td>
                  <td><strong>{r.patientName}</strong> <span className="hint">{r.age} {r.gender.charAt(0)} · {r.patientNo}</span>
                    {r.payerType === 'Insurance' && <span className="badge"> Insurance</span>}</td>
                  <td>{r.bedNo} <span className="hint">{r.ward}</span></td>
                  <td>{r.doctorName}</td>
                  <td>{dateTime(r.admittedOn)}</td>
                  {view === 'discharged'
                    ? <td>{dateTime(r.dischargedOn)}</td>
                    : <td style={{ textAlign: 'right' }}>{dayOfStay(r.admittedOn)}</td>}
                  <td style={{ textAlign: 'right' }} className={r.balance > 0 ? 'ip-due' : r.balance < 0 ? 'ip-credit' : ''}>
                    {r.balance === 0 ? '—' : r.balance > 0 ? money(r.balance) : `${money(-r.balance)} cr`}
                  </td>
                </tr>
              ))}
              {rows.length === 0 && <tr><td colSpan={7} className="hint">{view === 'admitted' ? 'No one is admitted.' : 'No discharges in these dates.'}</td></tr>}
            </tbody>
          </table>
        </section>
      )}

      {admitting && (
        <AdmitDialog
          {...admitting}
          onClose={() => setAdmitting(null)}
          onAdmitted={(id) => navigate(`/ip/${id}`)}
        />
      )}

      <ShortcutHints keys={[['f2', 'admit']]} />
    </div>
  );
}

function BedTile({ bed, onOpen, onAdmit, onReady, onBlock }: {
  bed: BedBoardItem;
  onOpen: () => void;
  onAdmit: () => void;
  onReady: () => void;
  onBlock: () => void;
}) {
  const cls = `ip-bed ip-bed-${bed.status.toLowerCase()}`;

  if (bed.status === 'Occupied') {
    return (
      <button type="button" className={cls} onClick={onOpen}>
        <span className="ip-bed-no">{bed.bedNo}</span>
        <span className="ip-bed-who">{bed.patientName}</span>
        <span className="ip-bed-meta">{bed.patientAge} {bed.gender?.charAt(0)} · day {dayOfStay(bed.admittedOn!)}</span>
        {bed.hasFinalBill && <span className="ip-bed-flag">Bill made</span>}
      </button>
    );
  }

  return (
    <div className={cls}>
      <span className="ip-bed-no">{bed.bedNo}</span>
      <span className="ip-bed-meta">{bed.roomType}</span>
      <span className="ip-bed-state">
        {bed.status === 'Available' && 'Free'}
        {bed.status === 'Cleaning' && 'Cleaning'}
        {bed.status === 'Blocked' && (bed.note ?? 'Blocked')}
      </span>
      <span className="ip-bed-actions">
        {bed.status === 'Available' && <><button type="button" onClick={onAdmit}>Admit</button><button type="button" className="ghost" onClick={onBlock}>Block</button></>}
        {bed.status === 'Cleaning' && <button type="button" onClick={onReady}>Mark ready</button>}
        {bed.status === 'Blocked' && <button type="button" className="ghost" onClick={onReady}>Unblock</button>}
      </span>
    </div>
  );
}
