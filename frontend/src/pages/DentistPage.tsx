import { useCallback, useEffect, useState } from 'react';
import { api, ApiError, openPdf } from '../api/client';
import type {
  AnesthesiaTypeMaster,
  DentalCase,
  DentalCaseStatus,
  DentalPackageMaster,
  DentalPayment,
  DentalReplacementMaster,
  Doctor,
  PaymentMode,
  Patient,
  Procedure,
} from '../api/types';
import { PatientPicker } from '../shell/PatientPicker';
import { ShortcutHints } from '../shell/ShortcutHints';
import { useHotkey } from '../shell/hotkeys';
import { PatientEditorDialog } from '../opd/PatientEditorDialog';
import { usePatientSearch } from '../shell/usePatientSearch';
import { useLoadedList } from '../shell/useLoadedList';

const PAYMENT_MODES: PaymentMode[] = ['Cash', 'Upi', 'Card'];

const STATUS_LABELS: Record<DentalCaseStatus, string> = {
  Planned: 'Planned',
  InProgress: 'In progress',
  Completed: 'Completed',
  Cancelled: 'Cancelled',
};

type Tab = 'cases' | 'treatment' | 'payments';

const money = (n: number) => `₹${n.toFixed(2)}`;

export function DentistPage() {
  const [tab, setTab] = useState<Tab>('cases');

  // ── Patient ────────────────────────────────────────────────────────────
  const [patient, setPatient] = useState<Patient | null>(null);
  const [addingPatient, setAddingPatient] = useState(false);

  // ── Catalogues ─────────────────────────────────────────────────────────
  // Loaded through useLoadedList so an empty list and a failed request stay
  // tellable apart. The empty states below say "none set up yet", which is a
  // statement about the clinic's data and must not be printed when the truth
  // is that the request did not arrive.
  const { items: procedures } = useLoadedList<Procedure>('/api/dentist/procedures');
  const { items: packages, failed: packagesFailed } = useLoadedList<DentalPackageMaster>('/api/dentist/packages');
  const { items: replacements, failed: replacementsFailed } = useLoadedList<DentalReplacementMaster>('/api/dentist/replacements');
  const { items: anesthesiaTypes, failed: anesthesiaFailed } = useLoadedList<AnesthesiaTypeMaster>('/api/dentist/anesthesia-types');
  const [doctors, setDoctors] = useState<Doctor[]>([]);

  // ── Cases ──────────────────────────────────────────────────────────────
  const [cases, setCases] = useState<DentalCase[]>([]);
  const [selectedCaseId, setSelectedCaseId] = useState<string | null>(null);

  // New case form
  const [doctorId, setDoctorId] = useState('');
  const [newProcedureId, setNewProcedureId] = useState('');
  const [newPackageId, setNewPackageId] = useState('');
  const [tooth, setTooth] = useState('');
  const [notes, setNotes] = useState('');

  // Sitting form
  const [workDone, setWorkDone] = useState('');
  const [anesthesiaTypeId, setAnesthesiaTypeId] = useState('');
  const [anesthesiaCost, setAnesthesiaCost] = useState('');
  const [nextSittingOn, setNextSittingOn] = useState('');

  // Replacement form
  const [replacementId, setReplacementId] = useState('');
  const [replacementQty, setReplacementQty] = useState('1');

  // Payment form
  const [amount, setAmount] = useState('');
  const [paymentMode, setPaymentMode] = useState<PaymentMode>('Cash');
  const [transactionNo, setTransactionNo] = useState('');

  const [status, setStatus] = useState('');
  const [error, setError] = useState<string | null>(null);

  // The case as it stands in the list right now — never a held object. The
  // same lesson Appointments recorded: a captured row goes stale the moment
  // the list behind it reloads.
  const selectedCase = cases.find((c) => c.id === selectedCaseId) ?? null;

  useEffect(() => {
    void api.get<Doctor[]>('/api/doctors').then((d) => {
      setDoctors(d);
      setDoctorId((current) => (d.some((x) => x.id === current) ? current : (d[0]?.id ?? '')));
    }).catch(() => {
      // Optional enrichment: an empty list here costs a little typing, not
      // correctness, and no screen states anything about it being empty.
    });
  }, []);

  const loadCases = useCallback(async (patientId: string) => {
    try {
      setCases(await api.get<DentalCase[]>(`/api/dentist/cases/by-patient/${patientId}`));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not load the cases.');
    }
  }, []);

  const selectPatient = useCallback((p: Patient) => {
    setPatient(p);
    setError(null);
    setSelectedCaseId(null);
    void loadCases(p.id);
  }, [loadCases]);

  // One hook for the debounce, the fetch, the cancel and the single-match
  // rule - see usePatientSearch for why this stopped being written per page.
  const { search, setSearch, matches, reset: resetSearch } = usePatientSearch({
    onSingleMatch: selectPatient,
    enabled: !patient,
  });

  /** A case belonging to the previous patient must not survive the switch. */
  const changePatient = () => {
    setPatient(null);
    resetSearch();
    setCases([]);
    setSelectedCaseId(null);
  };

  const refreshKeepingSelection = async () => {
    if (patient) await loadCases(patient.id);
  };

  const run = async (fn: () => Promise<string>) => {
    setError(null);
    try {
      setStatus(await fn());
      await refreshKeepingSelection();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'That did not go through.');
    }
  };

  // Same meanings as the other counters where they apply. A dental case is
  // opened rather than billed, so F2 opens the case and F7 adds a sitting.
  const G = 'Dentist';

  // ── Actions ────────────────────────────────────────────────────────────

  const openCase = () => {
    if (!patient) { setError('Select a patient first.'); return; }
    if (!doctorId) { setError('Select a doctor.'); return; }
    if (!newProcedureId && !newPackageId) {
      setError('Pick a procedure or a package to open the case against.');
      return;
    }
    if (newProcedureId && newPackageId) {
      setError('Pick either a procedure or a package, not both.');
      return;
    }

    void run(async () => {
      const opened = await api.post<DentalCase>('/api/dentist/cases', {
        patientId: patient.id,
        doctorId,
        procedureId: newProcedureId || null,
        packageId: newPackageId || null,
        toothNumber: tooth.trim() || null,
        notes: notes.trim() || null,
      });
      setNewProcedureId('');
      setNewPackageId('');
      setTooth('');
      setNotes('');
      setSelectedCaseId(opened.id);
      return `Case opened against ${opened.procedureName ?? opened.packageName}, base cost ${money(opened.baseCost)}.`;
    });
  };

  const setCaseStatus = (next: DentalCaseStatus) => {
    if (!selectedCase) return;
    void run(async () => {
      await api.post(`/api/dentist/cases/${selectedCase.id}/status`, { status: next });
      return next === 'Completed' ? 'Case marked completed.' : 'Case cancelled.';
    });
  };

  const addSitting = () => {
    if (!selectedCase) return;
    void run(async () => {
      const sitting = await api.post<{ sittingNumber: number }>(
        `/api/dentist/cases/${selectedCase.id}/sittings`,
        {
          workDone: workDone.trim() || null,
          anesthesiaTypeId: anesthesiaTypeId || null,
          anesthesiaCost: anesthesiaCost === '' ? null : Number(anesthesiaCost),
          nextSittingOn: nextSittingOn ? `${nextSittingOn}T00:00:00` : null,
        },
      );
      setWorkDone('');
      setAnesthesiaTypeId('');
      setAnesthesiaCost('');
      setNextSittingOn('');
      return `Sitting ${sitting.sittingNumber} added.`;
    });
  };

  const addReplacement = () => {
    if (!selectedCase) return;
    if (!replacementId) { setError('Pick a replacement first.'); return; }

    void run(async () => {
      const added = await api.post<{ name: string; amount: number }>(
        `/api/dentist/cases/${selectedCase.id}/replacements`,
        { replacementId, quantity: Number(replacementQty) || 1 },
      );
      setReplacementId('');
      setReplacementQty('1');
      return `${added.name} added — ${money(added.amount)}.`;
    });
  };

  const recordPayment = (print: boolean) => {
    if (!selectedCase) return;
    void run(async () => {
      const payment = await api.post<DentalPayment>(
        `/api/dentist/cases/${selectedCase.id}/payments`,
        {
          amount: Number(amount) || 0,
          paymentMode,
          transactionNo: transactionNo.trim() || null,
        },
      );
      setAmount('');
      setTransactionNo('');
      if (print) await openPdf(`/api/print/dental-receipt/${payment.id}`);
      return `${payment.receiptNo} · ${money(payment.amount)} recorded.`;
    });
  };

  /** Choosing an anesthesia type pre-fills its default cost — still
   * editable, since a longer sitting can cost more. */
  const pickAnesthesia = (id: string) => {
    setAnesthesiaTypeId(id);
    const type = anesthesiaTypes.find((a) => a.id === id);
    if (type) setAnesthesiaCost(String(type.defaultCost));
  };

  const canWork = selectedCase !== null
    && selectedCase.status !== 'Completed'
    && selectedCase.status !== 'Cancelled';

  /** Shown at the top of Treatment and Payments so which case is active is
   * never lost switching tabs. */
  const caseStrip = selectedCase ? (
    <section className="card sub-card">
      <div className="counter-selected">
        <strong>{selectedCase.procedureName ?? selectedCase.packageName}</strong>
        <span className="hint">
          {selectedCase.toothNumber && ` · tooth ${selectedCase.toothNumber}`}
          {' · '}<span className="badge">{STATUS_LABELS[selectedCase.status]}</span>
          {' · total '}{money(selectedCase.totalCost)}
          {' · paid '}{money(selectedCase.amountPaid)}
          {' · balance '}<strong>{money(selectedCase.balance)}</strong>
        </span>
        <button type="button" className="ghost" onClick={() => setTab('cases')}>Switch case</button>
      </div>
    </section>
  ) : (
    <p className="hint">Pick a case on the Cases tab first.</p>
  );

  // Registered after the actions they call, so no binding refers to a
  // function declared further down the file.
  useHotkey('f2', 'Open a case', G, () => { if (patient) openCase(); });
  useHotkey('f7', 'Add a sitting', G, () => { if (selectedCase) addSitting(); });

  return (
    <div className="page">
      <div className="page-head">
        <div>
          <h1>Dentist</h1>
          <p className="hint">{cases.length === 0 ? 'No cases for this patient' : `${cases.length} case(s)`}</p>
        </div>
      </div>

      <section className="card">
        {patient ? (
          <div className="counter-selected">
            <strong>{patient.name}</strong>
            <span className="hint"> · {patient.patientNo} · {patient.age}{patient.gender.charAt(0)}</span>
            <button type="button" className="ghost" onClick={changePatient}>Change patient</button>
          </div>
        ) : (
          <PatientPicker
            group={G}
            search={search}
            onSearchChange={setSearch}
            matches={matches}
            onPick={(p) => { selectPatient(p); resetSearch(); }}
            onNewPatient={() => setAddingPatient(true)}
          />
        )}
      </section>

      <div className="tabs">
        <button type="button" className={tab === 'cases' ? 'tab active' : 'tab'} onClick={() => setTab('cases')}>Cases</button>
        <button type="button" className={tab === 'treatment' ? 'tab active' : 'tab'} onClick={() => setTab('treatment')}>Treatment</button>
        <button type="button" className={tab === 'payments' ? 'tab active' : 'tab'} onClick={() => setTab('payments')}>Payments</button>
      </div>

      {error && <p className="auth-error">{error}</p>}
      {status && <p className="hint status-line">{status}</p>}

      {tab === 'cases' && (
        <>
          <section className="card">
            <h2>Open a new case</h2>
            <div className="settings-form">
              <div className="settings-row">
                <label>Doctor</label>
                <select value={doctorId} onChange={(e) => setDoctorId(e.target.value)}>
                  <option value="">Doctor…</option>
                  {doctors.map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}
                </select>
              </div>
              {/* Either/or, never both — the base cost has to come from
                  exactly one place, and the server refuses otherwise. */}
              <div className="settings-row">
                <label>Procedure</label>
                <select
                  value={newProcedureId}
                  onChange={(e) => { setNewProcedureId(e.target.value); if (e.target.value) setNewPackageId(''); }}
                >
                  <option value="">Procedure…</option>
                  {procedures.map((p) => <option key={p.id} value={p.id}>{p.name} — {money(p.price)}</option>)}
                </select>
              </div>
              <div className="settings-row">
                <label>…or package</label>
                <select
                  value={newPackageId}
                  onChange={(e) => { setNewPackageId(e.target.value); if (e.target.value) setNewProcedureId(''); }}
                >
                  <option value="">Package…</option>
                  {packages.map((p) => <option key={p.id} value={p.id}>{p.name} — {money(p.packagePrice)}</option>)}
                </select>
                {packages.length === 0 && (
                  <p className={packagesFailed ? "hint warn" : "hint"}>{packagesFailed ? "Packages could not be loaded." : "No packages set up yet — they arrive with the Masters module."}</p>
                )}
              </div>
              <div className="settings-row">
                <label>Tooth</label>
                <input value={tooth} onChange={(e) => setTooth(e.target.value)} placeholder="e.g. 36" />
              </div>
              <div className="settings-row">
                <label>Notes</label>
                <input value={notes} onChange={(e) => setNotes(e.target.value)} />
              </div>
            </div>
            <div className="settings-actions">
              <button type="button" onClick={openCase}>Open case</button>
            </div>
          </section>

          <section className="card">
            <h2>Cases</h2>
            <table>
              <thead>
                <tr>
                  <th>Against</th><th>Tooth</th><th>Started</th><th>Status</th>
                  <th>Total</th><th>Paid</th><th>Balance</th><th></th>
                </tr>
              </thead>
              <tbody>
                {cases.map((c) => (
                  <tr
                    key={c.id}
                    onClick={() => setSelectedCaseId(c.id)}
                    className={selectedCaseId === c.id ? 'selected-row' : undefined}
                  >
                    <td>
                      {c.procedureName ?? c.packageName}
                      {c.packageName && <div className="hint">package</div>}
                    </td>
                    <td>{c.toothNumber ?? '—'}</td>
                    <td>{new Date(c.startedOn).toLocaleDateString()}</td>
                    <td><span className="badge">{STATUS_LABELS[c.status]}</span></td>
                    <td>{money(c.totalCost)}</td>
                    <td>{money(c.amountPaid)}</td>
                    <td><strong>{money(c.balance)}</strong></td>
                    <td className="row-actions">
                      {c.status !== 'Completed' && c.status !== 'Cancelled' && (
                        <>
                          <button
                            type="button"
                            onClick={(e) => { e.stopPropagation(); setSelectedCaseId(c.id); setCaseStatus('Completed'); }}
                          >
                            Complete
                          </button>
                          <button
                            type="button"
                            className="ghost"
                            onClick={(e) => { e.stopPropagation(); setSelectedCaseId(c.id); setCaseStatus('Cancelled'); }}
                          >
                            Cancel
                          </button>
                        </>
                      )}
                    </td>
                  </tr>
                ))}
                {cases.length === 0 && <tr><td colSpan={8}>No cases for this patient.</td></tr>}
              </tbody>
            </table>
          </section>
        </>
      )}

      {tab === 'treatment' && (
        <>
          {caseStrip}

          {selectedCase && (
            <>
              <section className="card">
                <h2>Add a sitting</h2>
                {!canWork && (
                  <p className="hint">
                    This case is {STATUS_LABELS[selectedCase.status]} — no more sittings can be added.
                  </p>
                )}
                <div className="settings-form">
                  <div className="settings-row">
                    <label>Work done</label>
                    <input value={workDone} disabled={!canWork} onChange={(e) => setWorkDone(e.target.value)} />
                  </div>
                  <div className="settings-row">
                    <label>Anesthesia</label>
                    <select value={anesthesiaTypeId} disabled={!canWork} onChange={(e) => pickAnesthesia(e.target.value)}>
                      <option value="">None</option>
                      {anesthesiaTypes.map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}
                    </select>
                    {anesthesiaTypes.length === 0 && (
                      <p className={anesthesiaFailed ? "hint warn" : "hint"}>{anesthesiaFailed ? "Anesthesia types could not be loaded." : "No anesthesia types set up yet — they arrive with the Masters module."}</p>
                    )}
                  </div>
                  <div className="settings-row">
                    <label>Anesthesia cost</label>
                    <input
                      type="number"
                      min={0}
                      step="0.01"
                      value={anesthesiaCost}
                      disabled={!canWork}
                      onChange={(e) => setAnesthesiaCost(e.target.value)}
                    />
                  </div>
                  <div className="settings-row">
                    <label>Next sitting on</label>
                    <input type="date" value={nextSittingOn} disabled={!canWork} onChange={(e) => setNextSittingOn(e.target.value)} />
                  </div>
                </div>
                <div className="settings-actions">
                  <button type="button" disabled={!canWork} onClick={addSitting}>Add sitting</button>
                </div>

                <table>
                  <thead><tr><th>#</th><th>Date</th><th>Work done</th><th>Anesthesia</th><th>Cost</th><th>Next on</th></tr></thead>
                  <tbody>
                    {selectedCase.sittings.map((s) => (
                      <tr key={s.id}>
                        <td>{s.sittingNumber}</td>
                        <td>{new Date(s.sittingDate).toLocaleDateString()}</td>
                        <td>{s.workDone ?? '—'}</td>
                        <td>{s.anesthesiaTypeName ?? '—'}</td>
                        <td>{s.anesthesiaCost != null ? money(s.anesthesiaCost) : '—'}</td>
                        <td>{s.nextSittingOn ? new Date(s.nextSittingOn).toLocaleDateString() : '—'}</td>
                      </tr>
                    ))}
                    {selectedCase.sittings.length === 0 && <tr><td colSpan={6}>No sittings yet.</td></tr>}
                  </tbody>
                </table>
              </section>

              <section className="card">
                <h2>Replacements</h2>
                <div className="inline-form">
                  <select value={replacementId} disabled={!canWork} onChange={(e) => setReplacementId(e.target.value)}>
                    <option value="">Replacement…</option>
                    {replacements.map((r) => <option key={r.id} value={r.id}>{r.name} — {money(r.unitCost)}</option>)}
                  </select>
                  <input
                    type="number"
                    min={1}
                    value={replacementQty}
                    disabled={!canWork}
                    onChange={(e) => setReplacementQty(e.target.value)}
                    style={{ width: '5rem' }}
                  />
                  <button type="button" disabled={!canWork} onClick={addReplacement}>Add</button>
                </div>
                {replacements.length === 0 && (
                  <p className={replacementsFailed ? "hint warn" : "hint"}>{replacementsFailed ? "Replacements could not be loaded." : "No replacements set up yet — they arrive with the Masters module."}</p>
                )}

                <table>
                  <thead><tr><th>Replacement</th><th>Unit cost</th><th>Qty</th><th>Amount</th></tr></thead>
                  <tbody>
                    {selectedCase.replacements.map((r) => (
                      <tr key={r.id}>
                        <td>{r.name}</td>
                        <td>{money(r.unitCost)}</td>
                        <td>{r.quantity}</td>
                        <td>{money(r.amount)}</td>
                      </tr>
                    ))}
                    {selectedCase.replacements.length === 0 && <tr><td colSpan={4}>No replacements yet.</td></tr>}
                  </tbody>
                </table>
              </section>
            </>
          )}
        </>
      )}

      {tab === 'payments' && (
        <>
          {caseStrip}

          {selectedCase && (
            <>
              <section className="card">
                <h2>Record a payment</h2>
                {selectedCase.status === 'Cancelled' && (
                  <p className="hint">This case is cancelled — no further payment can be taken against it.</p>
                )}
                <div className="settings-form">
                  <div className="settings-row">
                    <label>Amount</label>
                    <input
                      type="number"
                      min={0}
                      step="0.01"
                      value={amount}
                      disabled={selectedCase.status === 'Cancelled'}
                      onChange={(e) => setAmount(e.target.value)}
                    />
                    <p className="hint">Balance outstanding: {money(selectedCase.balance)}</p>
                  </div>
                  <div className="settings-row">
                    <label>Mode</label>
                    <select
                      value={paymentMode}
                      disabled={selectedCase.status === 'Cancelled'}
                      onChange={(e) => setPaymentMode(e.target.value as PaymentMode)}
                    >
                      {PAYMENT_MODES.map((m) => <option key={m} value={m}>{m}</option>)}
                    </select>
                  </div>
                  {(paymentMode === 'Upi' || paymentMode === 'Card') && (
                    <div className="settings-row">
                      <label>Reference no.</label>
                      <input
                        value={transactionNo}
                        disabled={selectedCase.status === 'Cancelled'}
                        onChange={(e) => setTransactionNo(e.target.value)}
                      />
                    </div>
                  )}
                </div>
                <div className="settings-actions">
                  <button
                    type="button"
                    disabled={selectedCase.status === 'Cancelled'}
                    onClick={() => recordPayment(false)}
                  >
                    Record payment
                  </button>
                  <button
                    type="button"
                    disabled={selectedCase.status === 'Cancelled'}
                    onClick={() => recordPayment(true)}
                  >
                    Record &amp; print
                  </button>
                </div>
              </section>

              <section className="card">
                <h2>Payments</h2>
                <table>
                  <thead><tr><th>Receipt</th><th>Paid on</th><th>Amount</th><th>Mode</th><th></th></tr></thead>
                  <tbody>
                    {selectedCase.payments.map((p) => (
                      <tr key={p.id}>
                        <td>{p.receiptNo}</td>
                        <td>{new Date(p.paidOn).toLocaleString()}</td>
                        <td>{money(p.amount)}</td>
                        <td>{p.paymentMode}{p.transactionNo && <div className="hint">{p.transactionNo}</div>}</td>
                        <td className="row-actions">
                          <button
                            type="button"
                            className="ghost"
                            onClick={() => void openPdf(`/api/print/dental-receipt/${p.id}?reprint=true`)}
                          >
                            Reprint
                          </button>
                        </td>
                      </tr>
                    ))}
                    {selectedCase.payments.length === 0 && <tr><td colSpan={5}>Nothing paid yet.</td></tr>}
                  </tbody>
                </table>
              </section>
            </>
          )}
        </>
      )}

      <ShortcutHints keys={[['f2', 'open case'], ['f3', 'find patient'], ['f7', 'add sitting']]} />

      {addingPatient && (
        <PatientEditorDialog
          existing={null}
          onClose={() => setAddingPatient(false)}
          onSaved={(_message, saved) => {
            if (saved) selectPatient(saved);
            setAddingPatient(false);
          }}
        />
      )}
    </div>
  );
}
