import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { api, ApiError, openPdf } from '../api/client';
import type {
  AdmissionDetail, BedBoardItem, DischargeSummary, DischargeType, Doctor, IpBillView,
  IpChargeCategory, IpPaymentKind, IpService, PayerType, PaymentMode,
} from '../api/types';
import {
  CATEGORY_LABEL, DISCHARGE_LABEL, MANUAL_CATEGORIES, PAYMENT_KIND_LABEL,
  dateTime, dayOfStay, money, nowInput, shortDate, todayInput, toLocalInput,
} from '../inpatient/ip';
import { useHotkey } from '../shell/hotkeys';

type Tab = 'bill' | 'payments' | 'notes' | 'discharge' | 'details';

/**
 * One admission, start to end. The header stays put; the tabs follow the order
 * the work happens in - charges while the patient is in, money as it comes,
 * notes, then discharge.
 */
export function AdmissionPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [data, setData] = useState<AdmissionDetail | null>(null);
  const [tab, setTab] = useState<Tab>('bill');
  const [error, setError] = useState<string | null>(null);
  const [status, setStatus] = useState('');
  const [transferring, setTransferring] = useState(false);

  const load = useCallback(async () => {
    try {
      setData(await api.get<AdmissionDetail>(`/api/inpatient/admissions/${id}`));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not load the admission.');
    }
  }, [id]);

  useEffect(() => { void load(); }, [load]);

  /** Every action on this screen: run it, show what went wrong in words, reload. */
  const act = useCallback(async (fn: () => Promise<unknown>, done?: string) => {
    setError(null);
    setStatus('');
    try {
      await fn();
      await load();
      if (done) setStatus(done);
      return true;
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'That did not work.');
      return false;
    }
  }, [load]);

  const print = (path: string) => { void openPdf(path).catch((err) => setError(err instanceof ApiError ? err.message : 'Could not print.')); };

  if (!data) return <div className="page">{error ? <p className="auth-error">{error}</p> : <p className="hint">Loading…</p>}</div>;

  const { admission: a, bill } = data;
  const isIn = a.status === 'Admitted';
  const isFinal = bill.isFinal;

  return (
    <div className="page">
      <div className="page-head">
        <div>
          <button type="button" className="ghost ip-back" onClick={() => navigate('/ip')}>← In-patients</button>
          <h1>{a.patient.name} <span className="hint mono">{a.ipNo}</span></h1>
        </div>
        <div className="inline-form">
          {isIn && !isFinal && <button type="button" className="ghost" onClick={() => setTransferring(true)}>Transfer bed</button>}
          <button type="button" className="ghost" onClick={() => print(`/api/print/ip/${a.id}/slip`)}>Admission slip</button>
        </div>
      </div>

      <div className="ip-strip">
        <div><small>Patient</small><b>{a.patient.ageText} {a.patient.gender} · {a.patient.patientNo}</b></div>
        <div><small>Bed</small><b>{a.bed.bedNo} · {a.bed.ward}</b></div>
        <div><small>Doctor</small><b>{a.doctor.name}</b></div>
        <div><small>Admitted</small><b>{dateTime(a.admittedOn)}</b></div>
        <div><small>{isIn ? 'Day' : 'Discharged'}</small><b>{isIn ? dayOfStay(a.admittedOn) : dateTime(a.dischargedOn)}</b></div>
        <div><small>Payer</small><b>{a.payerType === 'Insurance' ? (a.insurerName || 'Insurance') : 'Self'}</b></div>
        <div><small>Status</small><b><span className={`badge ip-status-${a.status.toLowerCase()}`}>{a.status}{a.isMlc ? ' · MLC' : ''}</span></b></div>
      </div>

      <MoneyStrip bill={bill} />

      <div className="tabs">
        {([
          ['bill', 'Bill'], ['payments', 'Payments'], ['notes', `Notes (${data.notes.length})`],
          ['discharge', isIn ? 'Discharge' : 'Final bill'], ['details', 'Details'],
        ] as [Tab, string][]).map(([t, label]) => (
          <button key={t} type="button" className={tab === t ? 'tab active' : 'tab'} onClick={() => setTab(t)}>{label}</button>
        ))}
      </div>

      {error && <p className="auth-error">{error}</p>}
      {status && <p className="hint status-line">{status}</p>}

      {tab === 'bill' && <BillTab data={data} act={act} print={print} />}
      {tab === 'payments' && <PaymentsTab data={data} act={act} print={print} />}
      {tab === 'notes' && <NotesTab data={data} act={act} />}
      {tab === 'discharge' && <DischargeTab data={data} act={act} print={print} />}
      {tab === 'details' && <DetailsTab data={data} act={act} onCancelled={() => navigate('/ip')} />}

      {transferring && (
        <TransferDialog
          admissionId={a.id}
          currentBed={a.bed.bedNo}
          onClose={() => setTransferring(false)}
          onDone={async () => { setTransferring(false); await load(); setStatus('Bed changed.'); }}
        />
      )}
    </div>
  );
}

type Act = (fn: () => Promise<unknown>, done?: string) => Promise<boolean>;

function MoneyStrip({ bill }: { bill: IpBillView }) {
  const m = bill.money;
  return (
    <div className="ip-money">
      <div><small>{bill.isFinal ? `Final bill ${bill.billNo}` : `Bill so far · ${bill.bedDays} bed day${bill.bedDays === 1 ? '' : 's'}`}</small><b>{money(m.net)}</b></div>
      <div><small>Paid</small><b>{money(m.paid)}</b></div>
      {m.insuranceAdjustment > 0 && <div><small>Insurance adjustment</small><b>{money(m.insuranceAdjustment)}</b></div>}
      <div className={m.balance > 0 ? 'ip-due' : m.balance < 0 ? 'ip-credit' : ''}>
        <small>{m.balance > 0 ? 'Balance due' : m.balance < 0 ? 'Refund due' : 'Balance'}</small>
        <b>{money(Math.abs(m.balance))}</b>
      </div>
    </div>
  );
}

// ── Bill ──────────────────────────────────────────────────────────────────

function BillTab({ data, act, print }: { data: AdmissionDetail; act: Act; print: (path: string) => void }) {
  const { admission: a, bill } = data;
  const canEdit = !bill.isFinal && a.status !== 'Cancelled';

  const [services, setServices] = useState<IpService[]>([]);
  const [doctors, setDoctors] = useState<Doctor[]>([]);
  const [name, setName] = useState('');
  const [category, setCategory] = useState<IpChargeCategory>('Procedure');
  const [serviceId, setServiceId] = useState<string | null>(null);
  const [qty, setQty] = useState('1');
  const [rate, setRate] = useState('');
  const [date, setDate] = useState(todayInput());
  const [doctorId, setDoctorId] = useState(a.doctorId);
  const nameRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    void api.get<IpService[]>('/api/inpatient/services?activeOnly=true').then(setServices);
    void api.get<Doctor[]>('/api/doctors').then((d) => setDoctors(d.filter((x) => x.isActive)));
  }, []);

  /** Typing a service's name fills its category and rate; anything else is a free-text charge. */
  const onName = (value: string) => {
    setName(value);
    const match = services.find((s) => s.name.toLowerCase() === value.trim().toLowerCase());
    if (match) {
      setServiceId(match.id);
      setCategory(match.category);
      setRate(String(match.rate));
    } else {
      setServiceId(null);
    }
  };

  const add = async (e: FormEvent) => {
    e.preventDefault();
    const ok = await act(() => api.post(`/api/inpatient/admissions/${a.id}/charges`, {
      ipServiceId: serviceId,
      category,
      description: name.trim(),
      chargeDate: date,
      doctorId: category === 'DoctorVisit' ? doctorId : null,
      quantity: Number(qty) || 1,
      rate: Number(rate) || 0,
    }));
    if (ok) {
      setName(''); setServiceId(null); setQty('1'); setRate('');
      nameRef.current?.focus();
    }
  };

  const remove = (chargeId: string, description: string) => {
    if (!window.confirm(`Remove "${description}" from the bill?`)) return;
    void act(() => api.del(`/api/inpatient/admissions/${a.id}/charges/${chargeId}`), 'Charge removed.');
  };

  const groups = useMemo(() => {
    const order: IpChargeCategory[] = ['Room', 'Nursing', 'DoctorVisit', 'Procedure', 'Investigation', 'Medicine', 'Other'];
    return order
      .map((c) => ({ category: c, lines: bill.lines.filter((l) => l.category === c) }))
      .filter((g) => g.lines.length > 0);
  }, [bill.lines]);

  return (
    <section className="card">
      {canEdit && (
        <form className="ip-add" onSubmit={add}>
          <label className="ip-add-name">Charge
            <input ref={nameRef} list="ip-services" value={name} onChange={(e) => onName(e.target.value)}
                   placeholder="Type a service, or anything" autoFocus />
            <datalist id="ip-services">
              {services.map((s) => <option key={s.id} value={s.name}>{CATEGORY_LABEL[s.category]} · ₹{s.rate}</option>)}
            </datalist>
          </label>
          <label>Type
            <select value={category} onChange={(e) => setCategory(e.target.value as IpChargeCategory)} disabled={serviceId !== null}>
              {MANUAL_CATEGORIES.map((c) => <option key={c} value={c}>{CATEGORY_LABEL[c]}</option>)}
            </select>
          </label>
          {category === 'DoctorVisit' && (
            <label>Doctor
              <select value={doctorId} onChange={(e) => setDoctorId(e.target.value)}>
                {doctors.map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}
              </select>
            </label>
          )}
          <label className="ip-add-num">Qty<input type="number" min="1" value={qty} onChange={(e) => setQty(e.target.value)} /></label>
          <label className="ip-add-num">Rate ₹<input type="number" min="0" step="0.01" value={rate} onChange={(e) => setRate(e.target.value)} /></label>
          <label className="ip-add-date">Date<input type="date" value={date} onChange={(e) => setDate(e.target.value)} /></label>
          <button type="submit" disabled={!name.trim() && category !== 'DoctorVisit'}>Add</button>
        </form>
      )}
      {bill.isFinal && <p className="hint">The final bill {bill.billNo} is made, so the charges are fixed. Cancel the bill on the Final bill tab to change them.</p>}

      <table className="ip-lines">
        <thead><tr><th>Date</th><th>Item</th><th className="num">Qty</th><th className="num">Rate</th><th className="num">Amount</th><th /></tr></thead>
        <tbody>
          {groups.map((g) => (
            <GroupRows key={g.category} label={CATEGORY_LABEL[g.category]} lines={g.lines}
                       onRemove={canEdit ? remove : undefined} />
          ))}
          {bill.lines.length === 0 && <tr><td colSpan={6} className="hint">No charges yet.</td></tr>}
        </tbody>
        <tfoot>
          <tr><td colSpan={4}>Total</td><td className="num">{money(bill.money.gross)}</td><td /></tr>
          {bill.money.gst > 0 && <tr><td colSpan={4}>GST</td><td className="num">{money(bill.money.gst)}</td><td /></tr>}
          {bill.money.discount > 0 && <tr><td colSpan={4}>Discount</td><td className="num">−{money(bill.money.discount)}</td><td /></tr>}
        </tfoot>
      </table>

      <div className="inline-form">
        <button type="button" className="ghost" onClick={() => print(`/api/print/ip/${a.id}/bill`)}>
          {bill.isFinal ? 'Print final bill' : 'Print interim bill'}
        </button>
      </div>
    </section>
  );
}

/** Where each counter keeps the print of a bill that went on the IP bill. */
const SOURCE_PRINT: Record<string, string> = {
  Pharmacy: 'bill',
  Diagnostics: 'diagnostic-bill',
  Lab: 'lab-report',
  Procedures: 'procedure-bill',
};

function GroupRows({ label, lines, onRemove }: {
  label: string;
  lines: IpBillView['lines'];
  onRemove?: (chargeId: string, description: string) => void;
}) {
  const subtotal = lines.reduce((s, l) => s + l.amount, 0);
  return (
    <>
      <tr className="ip-group"><td colSpan={4}>{label}</td><td className="num">{money(subtotal)}</td><td /></tr>
      {lines.map((l, i) => (
        <tr key={l.chargeId ?? `${label}-${i}`}>
          <td>{shortDate(l.chargeDate)}</td>
          <td>{l.description}
            {l.source && l.sourceId && (
              <button type="button" className="ghost ip-source" title={`Print the ${l.source.toLowerCase()} bill`}
                      onClick={() => void openPdf(`/api/print/${SOURCE_PRINT[l.source!]}/${l.sourceId}?reprint=true`)}>
                {l.source} bill
              </button>
            )}</td>
          <td className="num">{l.quantity}</td>
          <td className="num">{l.rate.toLocaleString('en-IN')}</td>
          <td className="num">{l.amount.toLocaleString('en-IN', { minimumFractionDigits: 2 })}</td>
          <td className="ip-x">
            {onRemove && l.chargeId && (
              <button type="button" className="ghost" aria-label={`Remove ${l.description}`} onClick={() => onRemove(l.chargeId!, l.description)}>×</button>
            )}
          </td>
        </tr>
      ))}
    </>
  );
}

// ── Payments ──────────────────────────────────────────────────────────────

function PaymentsTab({ data, act, print }: { data: AdmissionDetail; act: Act; print: (path: string) => void }) {
  const { admission: a, bill } = data;
  const balance = bill.money.balance;

  // Before the final bill there is only one kind of money: an advance. After it,
  // the choice follows the balance: collect what is due, or refund what is over.
  const kinds: IpPaymentKind[] = !bill.isFinal
    ? ['Advance']
    : balance < 0 ? ['Refund'] : ['Settlement', 'Insurance'];

  const [kind, setKind] = useState<IpPaymentKind>(kinds[0]);
  const [amount, setAmount] = useState('');
  const [mode, setMode] = useState<PaymentMode>('Cash');
  const [reference, setReference] = useState('');
  const [remarks, setRemarks] = useState('');

  useEffect(() => {
    if (!kinds.includes(kind)) setKind(kinds[0]);
    // A settlement or refund is almost always the whole balance.
    if (bill.isFinal && balance !== 0) setAmount(String(Math.abs(balance)));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [bill.isFinal, balance]);

  const canTake = a.status !== 'Cancelled' && (!bill.isFinal || balance !== 0);

  const save = async (e: FormEvent) => {
    e.preventDefault();
    const ok = await act(() => api.post(`/api/inpatient/admissions/${a.id}/payments`, {
      kind,
      amount: Number(amount) || 0,
      mode: kind === 'Insurance' ? null : mode,
      reference: reference.trim() || null,
      remarks: remarks.trim() || null,
    }), `${PAYMENT_KIND_LABEL[kind]} saved.`);
    if (ok) { setAmount(''); setReference(''); setRemarks(''); }
  };

  return (
    <section className="card">
      {canTake && (
        <form className="ip-add" onSubmit={save}>
          <label>Type
            <select value={kind} onChange={(e) => setKind(e.target.value as IpPaymentKind)}>
              {kinds.map((k) => <option key={k} value={k}>{k === 'Insurance' ? 'Insurance payment received' : PAYMENT_KIND_LABEL[k]}</option>)}
            </select>
          </label>
          <label className="ip-add-num">Amount ₹<input type="number" min="1" step="0.01" value={amount} onChange={(e) => setAmount(e.target.value)} autoFocus /></label>
          {kind !== 'Insurance' && (
            <label>{kind === 'Refund' ? 'Refund by' : 'Paid by'}
              <select value={mode} onChange={(e) => setMode(e.target.value as PaymentMode)}>
                <option value="Cash">Cash</option><option value="Upi">UPI</option><option value="Card">Card</option>
              </select>
            </label>
          )}
          {(kind === 'Insurance' || mode !== 'Cash') && (
            <label>{kind === 'Insurance' ? 'Claim / UTR no.' : 'Transaction no.'}<input value={reference} onChange={(e) => setReference(e.target.value)} /></label>
          )}
          <label className="ip-add-name">Remarks<input value={remarks} onChange={(e) => setRemarks(e.target.value)} /></label>
          <button type="submit" disabled={!Number(amount)}>Save</button>
        </form>
      )}
      {bill.isFinal && balance === 0 && <p className="hint">The bill is settled.</p>}

      <table>
        <thead><tr><th>Receipt</th><th>Date</th><th>Type</th><th>Mode / reference</th><th className="num">Amount</th><th /></tr></thead>
        <tbody>
          {a.payments.map((p) => (
            <tr key={p.id}>
              <td className="mono">{p.receiptNo}</td>
              <td>{dateTime(p.paidOn)}</td>
              <td>{PAYMENT_KIND_LABEL[p.kind]}</td>
              <td>{p.mode ?? ''}{p.reference ? ` · ${p.reference}` : ''}{p.remarks ? <span className="hint"> · {p.remarks}</span> : null}</td>
              <td className="num">{p.kind === 'Refund' ? '−' : ''}{money(p.amount)}</td>
              <td><button type="button" className="ghost" onClick={() => print(`/api/print/ip/${a.id}/receipt/${p.id}`)}>Receipt</button></td>
            </tr>
          ))}
          {a.payments.length === 0 && <tr><td colSpan={6} className="hint">No payments yet.</td></tr>}
        </tbody>
      </table>
    </section>
  );
}

// ── Notes ─────────────────────────────────────────────────────────────────

function NotesTab({ data, act }: { data: AdmissionDetail; act: Act }) {
  const { admission: a, notes } = data;
  const [text, setText] = useState('');
  const [author, setAuthor] = useState(a.doctor.name);
  const [temp, setTemp] = useState('');
  const [pulse, setPulse] = useState('');
  const [bp, setBp] = useState('');
  const [spo2, setSpo2] = useState('');
  const [rr, setRr] = useState('');

  const num = (v: string) => (v.trim() === '' ? null : Number(v));

  const save = async (e: FormEvent) => {
    e.preventDefault();
    const ok = await act(() => api.post(`/api/inpatient/admissions/${a.id}/notes`, {
      text, author, temperatureF: num(temp), pulse: num(pulse), bloodPressure: bp.trim() || null,
      spo2: num(spo2), respiratoryRate: num(rr),
    }), 'Note saved.');
    if (ok) { setText(''); setTemp(''); setPulse(''); setBp(''); setSpo2(''); setRr(''); }
  };

  return (
    <section className="card">
      {a.status !== 'Cancelled' && (
        <form className="ip-note-form" onSubmit={save}>
          <textarea rows={3} value={text} onChange={(e) => setText(e.target.value)} placeholder="Note" autoFocus />
          <div className="ip-add">
            <label>By<input value={author} onChange={(e) => setAuthor(e.target.value)} /></label>
            <label className="ip-add-num">Temp °F<input type="number" step="0.1" value={temp} onChange={(e) => setTemp(e.target.value)} /></label>
            <label className="ip-add-num">Pulse<input type="number" value={pulse} onChange={(e) => setPulse(e.target.value)} /></label>
            <label className="ip-add-num">BP<input value={bp} onChange={(e) => setBp(e.target.value)} placeholder="120/80" /></label>
            <label className="ip-add-num">SpO₂ %<input type="number" value={spo2} onChange={(e) => setSpo2(e.target.value)} /></label>
            <label className="ip-add-num">RR<input type="number" value={rr} onChange={(e) => setRr(e.target.value)} /></label>
            <button type="submit">Save note</button>
          </div>
        </form>
      )}

      <div className="ip-notes">
        {notes.map((n) => (
          <div key={n.id} className="ip-note">
            <small>{dateTime(n.writtenOn)}{n.author && ` · ${n.author}`}</small>
            {n.text && <p>{n.text}</p>}
            <small>
              {[n.temperatureF != null && `T ${n.temperatureF}°F`, n.pulse != null && `P ${n.pulse}`,
                n.bloodPressure && `BP ${n.bloodPressure}`, n.spo2 != null && `SpO₂ ${n.spo2}%`,
                n.respiratoryRate != null && `RR ${n.respiratoryRate}`].filter(Boolean).join(' · ')}
            </small>
          </div>
        ))}
        {notes.length === 0 && <p className="hint">No notes yet. Notes cannot be edited once saved; a correction is a new note.</p>}
      </div>
    </section>
  );
}

// ── Discharge ─────────────────────────────────────────────────────────────

function DischargeTab({ data, act, print }: { data: AdmissionDetail; act: Act; print: (path: string) => void }) {
  const { admission: a, bill, bills } = data;
  const isIn = a.status === 'Admitted';

  return (
    <div className="ip-discharge">
      <SummaryCard data={data} act={act} print={print} />
      {!bill.isFinal ? <FinalBillCard data={data} act={act} /> : <SettleCard data={data} act={act} print={print} />}
      {isIn && bill.isFinal && <DischargeCard data={data} act={act} />}

      {bills.length > 0 && (
        <section className="card">
          <h2>Bills on this admission</h2>
          <table>
            <thead><tr><th>Bill no.</th><th>Made</th><th className="num">Net</th><th>Status</th><th /></tr></thead>
            <tbody>
              {bills.map((b) => (
                <tr key={b.id}>
                  <td className="mono">{b.billNo}</td>
                  <td>{dateTime(b.billedOn)}</td>
                  <td className="num">{money(b.net)}</td>
                  <td>{b.status === 'Final'
                    ? <span className="badge ip-status-admitted">Final</span>
                    : <span className="hint">Cancelled {dateTime(b.cancelledOn)} · {b.cancelReason}</span>}</td>
                  <td>{b.status === 'Final' && data.canDiscount && (
                    <button type="button" className="ghost" onClick={() => {
                      const reason = window.prompt(`Cancel bill ${b.billNo}? It is kept on record and a new bill can be made.\n\nReason:`);
                      if (reason?.trim()) void act(() => api.post(`/api/inpatient/admissions/${a.id}/bills/${b.id}/cancel`, { reason }), `Bill ${b.billNo} cancelled. Make a new bill when ready.`);
                    }}>Cancel bill</button>
                  )}</td>
                </tr>
              ))}
            </tbody>
          </table>
          {!data.canDiscount && <p className="hint">Only an admin can cancel a bill.</p>}
        </section>
      )}
    </div>
  );
}

function SummaryCard({ data, act, print }: { data: AdmissionDetail; act: Act; print: (path: string) => void }) {
  const { admission: a, summary, notes } = data;

  // Course in hospital starts from the notes, oldest first - the doctor edits it down.
  const draftCourse = useMemo(() => [...notes].reverse().filter((n) => n.text)
    .map((n) => `${shortDate(n.writtenOn)}: ${n.text}`).join('\n'), [notes]);

  const [form, setForm] = useState<DischargeSummary>(() => ({
    finalDiagnosis: summary?.finalDiagnosis ?? a.reason ?? '',
    courseInHospital: summary?.courseInHospital ?? draftCourse,
    treatment: summary?.treatment ?? '',
    conditionAtDischarge: summary?.conditionAtDischarge ?? '',
    medicines: summary?.medicines ?? '',
    advice: summary?.advice ?? '',
    followUpOn: summary?.followUpOn?.slice(0, 10) ?? '',
    doctorName: summary?.doctorName ?? a.doctor.name,
  }));
  const set = (k: keyof DischargeSummary) => (e: { target: { value: string } }) => setForm({ ...form, [k]: e.target.value });

  const save = (e: FormEvent) => {
    e.preventDefault();
    void act(() => api.put(`/api/inpatient/admissions/${a.id}/summary`, { ...form, followUpOn: form.followUpOn || null }), 'Discharge summary saved.');
  };

  // The follow-up date on the summary becomes an OPD appointment with the treating doctor,
  // at the start of the morning list; the desk can move it from Appointments.
  const bookFollowUp = () => {
    if (!summary?.followUpOn) return;
    void act(() => api.post('/api/appointments', {
      patientId: a.patientId,
      doctorId: a.doctorId,
      scheduledOn: `${summary.followUpOn!.slice(0, 10)}T10:00:00`,
      durationMinutes: 15,
      moduleContext: 'General',
      reason: `Follow-up after discharge (${a.ipNo})`,
      notes: null,
    }), `Follow-up booked for ${new Date(summary.followUpOn).toLocaleDateString()} at 10:00 with ${a.doctor.name}.`);
  };

  return (
    <form className="card ip-summary" onSubmit={save}>
      <h2>1 · Discharge summary {summary && <span className="badge ip-status-admitted">Saved</span>}</h2>
      <label>Final diagnosis<input value={form.finalDiagnosis ?? ''} onChange={set('finalDiagnosis')} /></label>
      <label>Course in hospital<textarea rows={3} value={form.courseInHospital ?? ''} onChange={set('courseInHospital')} /></label>
      <label>Treatment given<textarea rows={2} value={form.treatment ?? ''} onChange={set('treatment')} /></label>
      <div className="ip-grid">
        <label>Condition at discharge<input value={form.conditionAtDischarge ?? ''} onChange={set('conditionAtDischarge')} placeholder="Stable" /></label>
        <label>Follow-up on<input type="date" value={form.followUpOn ?? ''} onChange={set('followUpOn')} /></label>
        <label>Doctor<input value={form.doctorName ?? ''} onChange={set('doctorName')} /></label>
      </div>
      <label>Medicines on discharge<textarea rows={2} value={form.medicines ?? ''} onChange={set('medicines')} /></label>
      <label>Advice<textarea rows={2} value={form.advice ?? ''} onChange={set('advice')} /></label>
      <div className="inline-form">
        <button type="submit">Save summary</button>
        {summary && <button type="button" className="ghost" onClick={() => print(`/api/print/ip/${a.id}/summary`)}>Print summary</button>}
        {summary?.followUpOn && (
          <button type="button" className="ghost" onClick={bookFollowUp}>
            Book follow-up {new Date(summary.followUpOn).toLocaleDateString(undefined, { day: 'numeric', month: 'short' })}
          </button>
        )}
      </div>
    </form>
  );
}

function FinalBillCard({ data, act }: { data: AdmissionDetail; act: Act }) {
  const { admission: a } = data;
  const [dischargeOn, setDischargeOn] = useState(a.dischargedOn ? toLocalInput(new Date(a.dischargedOn)) : nowInput());
  const [preview, setPreview] = useState<IpBillView | null>(null);
  const [discount, setDiscount] = useState('');
  const [reason, setReason] = useState('');

  // The bill as it would be at the chosen discharge time - bed days move with it.
  useEffect(() => {
    const t = setTimeout(() => {
      void api.get<IpBillView>(`/api/inpatient/admissions/${a.id}/bill?upTo=${encodeURIComponent(dischargeOn)}`)
        .then(setPreview).catch(() => setPreview(null));
    }, 200);
    return () => clearTimeout(t);
  }, [a.id, dischargeOn]);

  const d = Number(discount) || 0;
  const totals = useMemo(() => {
    if (!preview) return [];
    const by = new Map<IpChargeCategory, number>();
    for (const l of preview.lines) by.set(l.category, (by.get(l.category) ?? 0) + l.amount);
    return [...by.entries()];
  }, [preview]);

  const make = (e: FormEvent) => {
    e.preventDefault();
    void act(() => api.post(`/api/inpatient/admissions/${a.id}/final-bill`, {
      dischargeOn, discount: d, discountReason: reason.trim() || null,
    }), 'Final bill made. Collect or refund the balance, then discharge.');
  };

  return (
    <form className="card" onSubmit={make}>
      <h2>2 · Final bill</h2>
      <div className="ip-grid">
        <label>Discharge on
          <input type="datetime-local" value={dischargeOn} onChange={(e) => setDischargeOn(e.target.value)} disabled={a.status !== 'Admitted'} />
        </label>
        {data.canDiscount && (
          <>
            <label>Discount ₹<input type="number" min="0" step="1" value={discount} onChange={(e) => setDiscount(e.target.value)} /></label>
            {d > 0 && <label>Reason<input value={reason} onChange={(e) => setReason(e.target.value)} required /></label>}
          </>
        )}
      </div>
      {preview && (
        <table className="ip-totals">
          <tbody>
            {totals.map(([c, amount]) => <tr key={c}><td>{CATEGORY_LABEL[c]}{c === 'Room' ? ` · ${preview.bedDays} day${preview.bedDays === 1 ? '' : 's'}` : ''}</td><td className="num">{money(amount)}</td></tr>)}
            {preview.money.gst > 0 && <tr><td>GST</td><td className="num">{money(preview.money.gst)}</td></tr>}
            {d > 0 && <tr><td>Discount</td><td className="num">−{money(d)}</td></tr>}
            <tr className="ip-total"><td>Net bill</td><td className="num">{money(preview.money.gross + preview.money.gst - d)}</td></tr>
            <tr><td>Paid so far</td><td className="num">{money(preview.money.paid)}</td></tr>
            <tr className="ip-total"><td>{preview.money.gross + preview.money.gst - d - preview.money.paid >= 0 ? 'Balance due' : 'Refund due'}</td>
              <td className="num">{money(Math.abs(preview.money.gross + preview.money.gst - d - preview.money.paid))}</td></tr>
          </tbody>
        </table>
      )}
      {!data.canDiscount && <p className="hint">Only an admin can give a discount.</p>}
      <div className="inline-form"><button type="submit">Make final bill</button></div>
    </form>
  );
}

function SettleCard({ data, act, print }: { data: AdmissionDetail; act: Act; print: (path: string) => void }) {
  const { admission: a, bill } = data;
  const m = bill.money;
  const isInsurance = a.payerType === 'Insurance' || m.insurance > 0;

  return (
    <section className="card">
      <h2>2 · Final bill {bill.billNo} · settle</h2>
      <table className="ip-totals">
        <tbody>
          <tr><td>Net bill</td><td className="num">{money(m.net)}</td></tr>
          {m.advances > 0 && <tr><td>Advances</td><td className="num">−{money(m.advances)}</td></tr>}
          {m.insurance > 0 && <tr><td>Insurance paid</td><td className="num">−{money(m.insurance)}</td></tr>}
          {m.settlements > 0 && <tr><td>Collected at settlement</td><td className="num">−{money(m.settlements)}</td></tr>}
          {m.refunds > 0 && <tr><td>Refunded</td><td className="num">{money(m.refunds)}</td></tr>}
          {m.insuranceAdjustment > 0 && <tr><td>Adjusted to insurance payment</td><td className="num">−{money(m.insuranceAdjustment)}</td></tr>}
          <tr className="ip-total"><td>{m.balance > 0 ? 'Balance due' : m.balance < 0 ? 'Refund due' : 'Balance'}</td><td className="num">{money(Math.abs(m.balance))}</td></tr>
        </tbody>
      </table>

      <p className="hint">
        {m.balance > 0 && 'Collect it on the Payments tab (Settlement). For insurance, record what the insurer paid there first.'}
        {m.balance < 0 && 'Refund it on the Payments tab.'}
        {m.balance === 0 && 'Settled.'}
      </p>

      {isInsurance && (
        <div className="ip-insurance">
          <strong>Insurance</strong>
          <p className="hint">When the insurer has paid: either collect the rest from the patient (Payments → Settlement), or, if the hospital accepts the insurer&rsquo;s amount as full payment, adjust the bill to it so the balance is zero.</p>
          <div className="inline-form">
            {m.insurance > 0 && m.balance > 0 && (
              <button type="button" className="ghost" onClick={() => {
                if (window.confirm(`Write off ${money(m.balance)} so the bill stands at what was paid? The balance becomes zero.`))
                  void act(() => api.post(`/api/inpatient/admissions/${a.id}/bills/${bill.billId}/adjust-to-insurance`), 'Bill adjusted to the insurance payment.');
              }}>Adjust bill to insurance payment</button>
            )}
            {m.insuranceAdjustment > 0 && (
              <button type="button" className="ghost" onClick={() => void act(() => api.post(`/api/inpatient/admissions/${a.id}/bills/${bill.billId}/clear-adjustment`), 'Adjustment removed.')}>Undo adjustment</button>
            )}
          </div>
        </div>
      )}

      <div className="inline-form">
        <button type="button" className="ghost" onClick={() => print(`/api/print/ip/${a.id}/bill`)}>Print final bill</button>
      </div>
    </section>
  );
}

function DischargeCard({ data, act }: { data: AdmissionDetail; act: Act }) {
  const { admission: a, bill } = data;
  const [type, setType] = useState<DischargeType>('Normal');
  const due = bill.money.balance;

  return (
    <section className="card">
      <h2>3 · Discharge</h2>
      <div className="ip-grid">
        <label>Discharge type
          <select value={type} onChange={(e) => setType(e.target.value as DischargeType)}>
            {(Object.keys(DISCHARGE_LABEL) as DischargeType[]).map((t) => <option key={t} value={t}>{DISCHARGE_LABEL[t]}</option>)}
          </select>
        </label>
        <label>At<input value={dateTime(bill.upTo)} readOnly /></label>
      </div>
      {due > 0 && <p className="hint ip-due">{money(due)} is still due. It will show on the IP dues report.</p>}
      <div className="inline-form">
        <button type="button" disabled={due < 0} onClick={() => {
          if (window.confirm(`Discharge ${a.patient.name}? Bed ${a.bed.bedNo} goes for cleaning.`))
            void act(() => api.post(`/api/inpatient/admissions/${a.id}/discharge`, { dischargeType: type }), 'Discharged.');
        }}>Discharge</button>
      </div>
    </section>
  );
}

// ── Details ───────────────────────────────────────────────────────────────

function DetailsTab({ data, act, onCancelled }: { data: AdmissionDetail; act: Act; onCancelled: () => void }) {
  const { admission: a } = data;
  const [doctors, setDoctors] = useState<Doctor[]>([]);
  const [f, setF] = useState({
    doctorId: a.doctorId,
    admittedOn: toLocalInput(new Date(a.admittedOn)),
    reason: a.reason ?? '',
    attendantName: a.attendantName ?? '',
    attendantRelation: a.attendantRelation ?? '',
    attendantPhone: a.attendantPhone ?? '',
    payerType: a.payerType as PayerType,
    insurerName: a.insurerName ?? '',
    policyNo: a.policyNo ?? '',
    isMlc: a.isMlc,
  });
  const set = (k: keyof typeof f) => (e: { target: { value: string } }) => setF({ ...f, [k]: e.target.value });

  useEffect(() => { void api.get<Doctor[]>('/api/doctors').then(setDoctors); }, []);

  const save = (e: FormEvent) => {
    e.preventDefault();
    void act(() => api.put(`/api/inpatient/admissions/${a.id}`, f), 'Details saved.');
  };

  const canCancel = a.status === 'Admitted' && a.charges.length === 0 && a.payments.length === 0;

  return (
    <form className="card ip-summary" onSubmit={save}>
      <div className="ip-grid">
        <label>Treating doctor
          <select value={f.doctorId} onChange={set('doctorId')}>
            {doctors.map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}
          </select>
        </label>
        <label>Admitted on<input type="datetime-local" value={f.admittedOn} onChange={set('admittedOn')} /></label>
        <label className="checkbox-label"><input type="checkbox" checked={f.isMlc} onChange={() => setF({ ...f, isMlc: !f.isMlc })} /><span>MLC</span></label>
      </div>
      <label>Reason / provisional diagnosis<input value={f.reason} onChange={set('reason')} /></label>
      <div className="ip-grid">
        <label>Attendant<input value={f.attendantName} onChange={set('attendantName')} /></label>
        <label>Relation<input value={f.attendantRelation} onChange={set('attendantRelation')} /></label>
        <label>Phone<input value={f.attendantPhone} onChange={set('attendantPhone')} /></label>
      </div>
      <div className="ip-grid">
        <label>Paid by
          <select value={f.payerType} onChange={set('payerType')}>
            <option value="Self">Self (patient)</option><option value="Insurance">Insurance / TPA</option>
          </select>
        </label>
        {f.payerType === 'Insurance' && <>
          <label>Insurance / TPA<input value={f.insurerName} onChange={set('insurerName')} /></label>
          <label>Policy / card no.<input value={f.policyNo} onChange={set('policyNo')} /></label>
        </>}
      </div>

      <h3>Bed history</h3>
      <table>
        <thead><tr><th>Bed</th><th>Room</th><th className="num">Rent/day</th><th>From</th><th>To</th></tr></thead>
        <tbody>
          {a.stays.map((s) => (
            <tr key={s.id}><td>{s.bedNo}</td><td>{s.roomTypeName}</td><td className="num">{money(s.dailyRent)}</td><td>{dateTime(s.from)}</td><td>{s.to ? dateTime(s.to) : 'now'}</td></tr>
          ))}
        </tbody>
      </table>

      <div className="inline-form">
        <button type="submit" disabled={a.status === 'Cancelled'}>Save details</button>
        {canCancel && (
          <button type="button" className="danger" onClick={() => {
            const reason = window.prompt('Cancel this admission? Use this only for an admission entered by mistake.\n\nReason:');
            if (reason?.trim()) void act(() => api.post(`/api/inpatient/admissions/${a.id}/cancel`, { reason })).then((ok) => ok && onCancelled());
          }}>Cancel admission</button>
        )}
      </div>
    </form>
  );
}

// ── Transfer ──────────────────────────────────────────────────────────────

function TransferDialog({ admissionId, currentBed, onClose, onDone }: {
  admissionId: string; currentBed: string; onClose: () => void; onDone: () => void;
}) {
  const [beds, setBeds] = useState<BedBoardItem[]>([]);
  const [bed, setBed] = useState('');
  const [at, setAt] = useState(nowInput());
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    void api.get<BedBoardItem[]>('/api/inpatient/board').then((b) => setBeds(b.filter((x) => x.status === 'Available')));
  }, []);

  useHotkey('escape', 'Close', 'Transfer', onClose, { whileTyping: true });

  const save = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    try {
      await api.post(`/api/inpatient/admissions/${admissionId}/transfer`, { bedId: bed, at });
      onDone();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not move the patient.');
    }
  };

  return (
    <div className="overlay" role="dialog" aria-modal="true" aria-label="Transfer bed">
      <div className="overlay-card">
        <div className="overlay-head"><h2>Move from {currentBed}</h2><button type="button" className="ghost" onClick={onClose}>Close</button></div>
        <form className="overlay-body ip-form" onSubmit={save}>
          <label>To bed
            <select value={bed} onChange={(e) => setBed(e.target.value)} required autoFocus>
              <option value="">Choose a free bed</option>
              {beds.map((b) => <option key={b.bedId} value={b.bedId}>{b.bedNo} · {b.ward} · {b.roomType} · ₹{b.dailyRent}/day</option>)}
            </select>
          </label>
          <label>Moved at<input type="datetime-local" value={at} onChange={(e) => setAt(e.target.value)} /></label>
          <p className="hint">{currentBed} goes for cleaning. A day spent in two beds is charged at the higher rate.</p>
          {error && <p className="auth-error">{error}</p>}
          <div className="settings-actions"><button type="submit" disabled={!bed}>Move</button></div>
        </form>
      </div>
    </div>
  );
}
