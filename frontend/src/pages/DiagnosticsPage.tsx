import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { api, ApiError, openPdf } from '../api/client';
import type {
  DiagnosticBill,
  DiagnosticBillStatus,
  DiagnosticTest,
  PaymentMode,
  Patient,
  Visit,
} from '../api/types';
import { PatientPicker } from '../shell/PatientPicker';
import { ShortcutHints } from '../shell/ShortcutHints';
import { useHotkey } from '../shell/hotkeys';
import { PatientEditorDialog } from '../opd/PatientEditorDialog';
import { TestEditorDialog } from '../diagnostics/TestEditorDialog';
import { TestPickerDialog } from '../diagnostics/TestPickerDialog';
import { usePatientSearch } from '../shell/usePatientSearch';

/** One line on the bill being built. `testId` is null for a test requested
 * as free text during a consultation — one we do not run in-house, still
 * worth billing on its own name. */
interface BillRow {
  key: string;
  testId: string | null;
  testName: string;
  price: number;
  quantity: number;
}

const PAYMENT_MODES: PaymentMode[] = ['Cash', 'Upi', 'Card'];

const BILL_STATUSES: DiagnosticBillStatus[] = [
  'Ordered', 'SampleCollected', 'ResultReceived', 'Completed',
];

const STATUS_LABELS: Record<DiagnosticBillStatus, string> = {
  Ordered: 'Ordered',
  SampleCollected: 'Sample collected',
  ResultReceived: 'Result received',
  Completed: 'Completed',
};

type Tab = 'billing' | 'master';

export function DiagnosticsPage() {
  const [tab, setTab] = useState<Tab>('billing');

  // ── Billing ────────────────────────────────────────────────────────────
  const [patient, setPatient] = useState<Patient | null>(null);
  const [addingPatient, setAddingPatient] = useState(false);

  const [lines, setLines] = useState<BillRow[]>([]);
  const [discount, setDiscount] = useState('0');
  const [paymentMode, setPaymentMode] = useState<PaymentMode>('Cash');
  const [transactionNo, setTransactionNo] = useState('');
  const [remarks, setRemarks] = useState('');
  const [referredBy, setReferredBy] = useState('');

  const [billId, setBillId] = useState<string | null>(null);
  const [billNo, setBillNo] = useState('');
  const [billStatus, setBillStatus] = useState<DiagnosticBillStatus>('Ordered');
  const [visitId, setVisitId] = useState<string | null>(null);

  const [pendingVisits, setPendingVisits] = useState<Visit[]>([]);
  const [pendingVisitId, setPendingVisitId] = useState('');

  // The desktop reaches an existing bill through the Patients screen, which
  // calls LoadBillAsync. There is no such entry point here yet, and a bill
  // you cannot reopen is one you cannot move through the lab workflow or
  // reprint — so today's bills are listed on the screen that writes them.
  const [todaysBills, setTodaysBills] = useState<DiagnosticBill[]>([]);

  const [picking, setPicking] = useState(false);
  const [status, setStatus] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  // ── Test master ────────────────────────────────────────────────────────
  const [masterSearch, setMasterSearch] = useState('');
  const [tests, setTests] = useState<DiagnosticTest[]>([]);
  const [selectedTestId, setSelectedTestId] = useState<string | null>(null);
  const [editingTest, setEditingTest] = useState<DiagnosticTest | null>(null);
  const [testEditorOpen, setTestEditorOpen] = useState(false);
  const [masterStatus, setMasterStatus] = useState('');
  const [masterError, setMasterError] = useState<string | null>(null);

  const loadTests = useCallback(async (term: string) => {
    try {
      // No activeOnly here: the master is where a retired test is found
      // again to bring it back.
      setTests(await api.get<DiagnosticTest[]>(`/api/diagnostics/tests?term=${encodeURIComponent(term.trim())}`));
      setMasterError(null);
    } catch (err) {
      setMasterError(err instanceof ApiError ? err.message : 'Could not load the test master.');
    }
  }, []);

  const loadPending = useCallback(async () => {
    try {
      setPendingVisits(await api.get<Visit[]>('/api/diagnostics/pending-requests'));
    } catch {
      setPendingVisits([]);
    }
  }, []);

  const loadTodaysBills = useCallback(async () => {
    try {
      setTodaysBills(await api.get<DiagnosticBill[]>('/api/diagnostics/bills'));
    } catch {
      setTodaysBills([]);
    }
  }, []);

  useEffect(() => {
    const handle = setTimeout(() => void loadTests(masterSearch), 250);
    return () => clearTimeout(handle);
  }, [masterSearch, loadTests]);

  useEffect(() => {
    void loadPending();
    void loadTodaysBills();
  }, [loadPending, loadTodaysBills]);

  // ── Patient search ─────────────────────────────────────────────────────

  const selectPatient = useCallback((p: Patient) => {
    setPatient(p);
    // "Select a patient first." must not still be on screen once one is
    // selected — an error that contradicts the page is worse than none.
    setError(null);
  }, []);

  // One hook for the debounce, the fetch, the cancel and the single-match
  // rule - see usePatientSearch for why this stopped being written per page.
  const { search, setSearch, matches, reset: resetSearch } = usePatientSearch({
    onSingleMatch: selectPatient,
  });

  /** Search, selection and the match list all reset together. Clearing only
   * the search box left stale matches ready to reappear the moment the
   * picker became visible again. */
  const changePatient = () => {
    setPatient(null);
    resetSearch();
  };

  // ── Totals ─────────────────────────────────────────────────────────────

  const total = useMemo(() => lines.reduce((sum, l) => sum + l.price * l.quantity, 0), [lines]);
  const finalAmount = Math.max(0, total - (Number(discount) || 0));

  /** Editing is refused once a bill is Completed — the same rule the service
   * enforces, mirrored here so the fields go read-only rather than the
   * operator finding out only after Save fails. */
  const canEdit = billStatus !== 'Completed';

  // F3 here means the test search, not the patient box: on this screen the
  // tests are what gets typed over and over. The patient picker keeps its
  // own arrows and Enter.
  const G = 'Diagnostics';
  const testSearchRef = useRef<HTMLInputElement>(null);
  useHotkey('f3', 'Search tests', G, () => {
    testSearchRef.current?.focus();
    testSearchRef.current?.select();
  }, { whileTyping: true });

  const newBill = () => {
    setLines([]);
    setPatient(null);
    resetSearch();
    setDiscount('0');
    setRemarks('');
    setPaymentMode('Cash');
    setTransactionNo('');
    setBillId(null);
    setBillNo('');
    setBillStatus('Ordered');
    setVisitId(null);
    setReferredBy('');
    setPendingVisitId('');
    setError(null);
  };

  const addTest = (test: DiagnosticTest) => {
    // A test already on the bill is refused rather than duplicated — billing
    // one test twice is a quantity of 2 on one line.
    if (lines.some((l) => l.testId === test.id)) return;
    setLines((current) => [
      ...current,
      { key: crypto.randomUUID(), testId: test.id, testName: test.name, price: test.price, quantity: 1 },
    ]);
  };

  const setLine = (key: string, patch: Partial<BillRow>) =>
    setLines((current) => current.map((l) => (l.key === key ? { ...l, ...patch } : l)));

  const removeLine = (key: string) => setLines((current) => current.filter((l) => l.key !== key));

  // ── Load tests requested during a consultation ─────────────────────────

  const loadFromConsultation = async () => {
    setError(null);
    if (!pendingVisitId) {
      setError("Choose a patient from today's OPD list first.");
      return;
    }

    try {
      const visit = await api.get<Visit>(`/api/visits/${pendingVisitId}`);
      setPatient(visit.patient);
      // Ties this bill to the visit, which is also what removes the need to
      // ask who referred them: they came through our own OPD.
      setVisitId(visit.id);

      const active = await api.get<DiagnosticTest[]>('/api/diagnostics/tests?activeOnly=true');

      let added = 0;
      const next: BillRow[] = [];

      for (const request of visit.diagnosticRequests) {
        const test =
          (request.testId ? active.find((t) => t.id === request.testId) : undefined) ??
          active.find((t) => t.name.toLowerCase() === request.testName.toLowerCase());

        if (test) {
          if (lines.some((l) => l.testId === test.id) || next.some((l) => l.testId === test.id)) continue;
          next.push({ key: crypto.randomUUID(), testId: test.id, testName: test.name, price: test.price, quantity: 1 });
          added++;
        } else {
          // Requested as free text — still worth billing, priced at zero
          // until the desk fills it in.
          const name = request.testName.toLowerCase();
          if (lines.some((l) => l.testName.toLowerCase() === name) || next.some((l) => l.testName.toLowerCase() === name)) continue;
          next.push({ key: crypto.randomUUID(), testId: null, testName: request.testName, price: 0, quantity: 1 });
          added++;
        }
      }

      setLines((current) => [...current, ...next]);
      setStatus(added === 0
        ? 'Nothing new to load from the consultation.'
        : `${added} test(s) loaded from the consultation.`);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not load the consultation.');
    }
  };

  // ── Save ───────────────────────────────────────────────────────────────

  useHotkey('f2', 'Start a new bill', G, () => newBill());
  useHotkey('f6', 'Load tests requested in consultation', G, () => { if (patient) void loadFromConsultation(); });

  const save = async (print: boolean) => {
    setError(null);

    if (!patient) {
      setError('Select a patient first.');
      return;
    }
    if (lines.length === 0) {
      setError('Add at least one test to the bill.');
      return;
    }

    setBusy(true);
    try {
      const saved = await api.post<DiagnosticBill>('/api/diagnostics/bills', {
        id: billId,
        patientId: patient.id,
        paymentMode,
        transactionNo: transactionNo.trim() || null,
        discount: Number(discount) || 0,
        remarks: remarks.trim() || null,
        visitId,
        referredBy: referredBy.trim() || null,
        lines: lines.map((l) => ({
          testId: l.testId,
          testName: l.testName,
          price: l.price,
          quantity: l.quantity,
        })),
      });

      setStatus(`Bill ${saved.billNo} saved · ₹${saved.finalAmount.toFixed(2)}`);

      // Printed from the saved bill, never from what was on screen: a bill
      // is a document, and what it says has to come from what was stored.
      if (print) await openPdf(`/api/print/diagnostic-bill/${saved.id}`);

      newBill();
      await loadPending();
      await loadTodaysBills();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not save the bill.');
    } finally {
      setBusy(false);
    }
  };

  // Registered after `save`, so the binding does not refer to a function
  // declared further down the file.
  useHotkey('f4', 'Save the bill', G, () => { if (!busy && canEdit) void save(false); });
  useHotkey('f8', 'Save and print', G, () => { if (!busy && canEdit) void save(true); });

  const changeStatus = async (next: DiagnosticBillStatus) => {
    if (!billId) return;
    setError(null);
    try {
      await api.post(`/api/diagnostics/bills/${billId}/status`, { status: next });
      setBillStatus(next);
      setStatus(`Bill ${billNo} moved to ${STATUS_LABELS[next]}.`);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not change the status.');
    }
  };

  const loadBill = async (id: string) => {
    setError(null);
    try {
      const bill = await api.get<DiagnosticBill>(`/api/diagnostics/bills/${id}`);
      setBillId(bill.id);
      setBillNo(bill.billNo);
      setBillStatus(bill.status);
      setDiscount(String(bill.discount));
      setPaymentMode(bill.paymentMode);
      setTransactionNo(bill.transactionNo ?? '');
      setRemarks(bill.remarks ?? '');
      setVisitId(bill.visitId);
      setReferredBy(bill.referredBy ?? '');
      setLines(bill.items.map((i) => ({
        key: crypto.randomUUID(),
        testId: i.testId,
        testName: i.testName,
        price: i.price,
        quantity: i.quantity,
      })));

      const found = await api.get<Patient[]>(`/api/patients?term=${encodeURIComponent(bill.patientNo)}&take=5`);
      setPatient(found.find((p) => p.id === bill.patientId) ?? null);
      setStatus(`Bill ${bill.billNo} loaded.`);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not load the bill.');
    }
  };

  // ── Test master actions ────────────────────────────────────────────────

  const selectedTest = tests.find((t) => t.id === selectedTestId) ?? null;

  const toggleActive = async (test: DiagnosticTest) => {
    setMasterError(null);
    try {
      await api.post(`/api/diagnostics/tests/${test.id}/active`, { active: !test.active });
      setMasterStatus(`${test.name} ${test.active ? 'deactivated' : 'reactivated'}.`);
      await loadTests(masterSearch);
    } catch (err) {
      setMasterError(err instanceof ApiError ? err.message : 'Could not change the test.');
    }
  };

  const subtitle = lines.length === 0
    ? 'No tests on this bill'
    : `${lines.length} test(s) · ₹${finalAmount.toFixed(2)}`;

  return (
    <div className="page">
      <div className="page-head">
        <div>
          <h1>Diagnostics</h1>
          <p className="hint">{tab === 'billing' ? subtitle : `${tests.length} test(s) in the master`}</p>
        </div>
      </div>

      <div className="tabs">
        <button type="button" className={tab === 'billing' ? 'tab active' : 'tab'} onClick={() => setTab('billing')}>
          Billing
        </button>
        <button
          type="button"
          className={tab === 'master' ? 'tab active' : 'tab'}
          onClick={() => { setTab('master'); void loadTests(masterSearch); }}
        >
          Test Master
        </button>
      </div>

      {tab === 'billing' && (
        <>
          {error && <p className="auth-error">{error}</p>}
          {status && <p className="hint status-line">{status}</p>}

          {/* Hidden entirely when nothing is pending: unlike the pharmacy
              counter's prescription list, a patient with requested-but-
              unbilled tests is the exception, so an always-visible block
              would spend real estate on a dead control most of the time. */}
          {pendingVisits.length > 0 && (
            <section className="card">
              <h2>Tests requested in a consultation today</h2>
              <div className="inline-form">
                <select value={pendingVisitId} onChange={(e) => setPendingVisitId(e.target.value)}>
                  <option value="">Choose a patient…</option>
                  {pendingVisits.map((v) => (
                    <option key={v.id} value={v.id}>
                      Token {v.tokenNo} · {v.patient.name} · {v.diagnosticRequests.length} test(s)
                    </option>
                  ))}
                </select>
                <button type="button" onClick={() => void loadFromConsultation()}>Load tests</button>
              </div>
            </section>
          )}

          <section className="card">
            <h2>1 · Who is being billed</h2>
            {patient ? (
              <div className="counter-selected">
                <strong>{patient.name}</strong>
                <span className="hint"> · {patient.patientNo} · {patient.age}{patient.gender.charAt(0)}</span>
                {canEdit && (
                  <button type="button" className="ghost" onClick={changePatient}>Change patient</button>
                )}
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

          <section className="card">
            <h2>
              2 · Tests
              {billNo && <span className="hint"> · {billNo} · {STATUS_LABELS[billStatus]}</span>}
            </h2>

            {canEdit && (
              <div className="inline-form">
                <button
                  type="button"
                  onClick={() => {
                    if (!patient) { setError('Select a patient first.'); return; }
                    setPicking(true);
                  }}
                >
                  + Add tests
                </button>
              </div>
            )}

            <table>
              <thead><tr><th>Test</th><th>Price</th><th>Qty</th><th>Amount</th><th></th></tr></thead>
              <tbody>
                {lines.map((l) => (
                  <tr key={l.key}>
                    <td>{l.testName}</td>
                    <td>
                      {/* Overridable: a concession or a package rate is a
                          real thing the desk does. */}
                      <input
                        type="number"
                        min={0}
                        step="0.01"
                        value={l.price}
                        disabled={!canEdit}
                        onChange={(e) => setLine(l.key, { price: Number(e.target.value) })}
                        style={{ width: '6rem' }}
                      />
                    </td>
                    <td>
                      <input
                        type="number"
                        min={1}
                        value={l.quantity}
                        disabled={!canEdit}
                        onChange={(e) => setLine(l.key, { quantity: Number(e.target.value) })}
                        style={{ width: '4rem' }}
                      />
                    </td>
                    <td>{(l.price * l.quantity).toFixed(2)}</td>
                    <td className="row-actions">
                      {canEdit && (
                        <button type="button" className="ghost" onClick={() => removeLine(l.key)}>Remove</button>
                      )}
                    </td>
                  </tr>
                ))}
                {lines.length === 0 && <tr><td colSpan={5}>No tests on this bill.</td></tr>}
              </tbody>
            </table>

            <div className="totals">
              <div><span>Total</span><span>{total.toFixed(2)}</span></div>
              <div>
                <span>Discount</span>
                <input
                  type="number"
                  min={0}
                  step="0.01"
                  value={discount}
                  disabled={!canEdit}
                  onChange={(e) => setDiscount(e.target.value)}
                  style={{ width: '7rem' }}
                />
              </div>
              <div><strong>Payable</strong><strong>₹{finalAmount.toFixed(2)}</strong></div>
            </div>
          </section>

          <section className="card">
            <h2>3 · Payment</h2>
            <div className="settings-form">
              <div className="settings-row">
                <label>Mode</label>
                <select
                  value={paymentMode}
                  disabled={!canEdit}
                  onChange={(e) => setPaymentMode(e.target.value as PaymentMode)}
                >
                  {PAYMENT_MODES.map((m) => <option key={m} value={m}>{m}</option>)}
                </select>
              </div>

              {/* Only for UPI and card — cash has nothing to reconcile
                  against, so the field would be dead weight. */}
              {(paymentMode === 'Upi' || paymentMode === 'Card') && (
                <div className="settings-row">
                  <label>Reference no.</label>
                  <input value={transactionNo} disabled={!canEdit} onChange={(e) => setTransactionNo(e.target.value)} />
                </div>
              )}

              {/* Only for a patient who did not come through our own OPD —
                  one who did was referred by the clinic itself. */}
              {!visitId && (
                <div className="settings-row">
                  <label>Referred by</label>
                  <input value={referredBy} disabled={!canEdit} onChange={(e) => setReferredBy(e.target.value)} />
                </div>
              )}

              <div className="settings-row">
                <label>Remarks</label>
                <input value={remarks} disabled={!canEdit} onChange={(e) => setRemarks(e.target.value)} />
              </div>

              {/* Only once the bill exists — a new, unsaved bill is always
                  Ordered, so there is nothing to move it through yet. */}
              {billId && (
                <div className="settings-row">
                  <label>Status</label>
                  <select value={billStatus} onChange={(e) => void changeStatus(e.target.value as DiagnosticBillStatus)}>
                    {BILL_STATUSES.map((s) => <option key={s} value={s}>{STATUS_LABELS[s]}</option>)}
                  </select>
                </div>
              )}
            </div>

            <div className="settings-actions">
              <button type="button" className="primary" disabled={busy || !canEdit} onClick={() => void save(false)}>Save</button>
              <button type="button" disabled={busy || !canEdit} onClick={() => void save(true)}>Save &amp; print</button>
              {billId && (
                <button type="button" className="ghost" onClick={() => void openPdf(`/api/print/diagnostic-bill/${billId}?reprint=true`)}>
                  Reprint
                </button>
              )}
              <button type="button" className="ghost" onClick={newBill}>New bill</button>
            </div>

            {!canEdit && (
              <p className="hint">
                This bill is Completed and can no longer be edited. Start a new bill instead.
              </p>
            )}
          </section>

          {todaysBills.length > 0 && (
            <section className="card">
              <h2>Today&rsquo;s bills</h2>
              <table>
                <thead><tr><th>Bill</th><th>Patient</th><th>Amount</th><th>Status</th><th></th></tr></thead>
                <tbody>
                  {todaysBills.map((b) => (
                    <tr key={b.id}>
                      <td>{b.billNo}</td>
                      <td>{b.patientName}<div className="hint">{b.patientNo}</div></td>
                      <td>₹{b.finalAmount.toFixed(2)}</td>
                      <td><span className="badge">{STATUS_LABELS[b.status]}</span></td>
                      <td className="row-actions">
                        <button type="button" onClick={() => void loadBill(b.id)}>Open</button>
                        <button
                          type="button"
                          className="ghost"
                          onClick={() => void openPdf(`/api/print/diagnostic-bill/${b.id}?reprint=true`)}
                        >
                          Reprint
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </section>
          )}
        </>
      )}

      {tab === 'master' && (
        <>
          {masterError && <p className="auth-error">{masterError}</p>}
          {masterStatus && <p className="hint status-line">{masterStatus}</p>}

          <div className="inline-form">
            <input
              ref={testSearchRef}
              placeholder="Search tests"
              value={masterSearch}
              onChange={(e) => setMasterSearch(e.target.value)}
            />
            <button
              type="button"
              disabled={!selectedTest}
              onClick={() => { if (selectedTest) { setEditingTest(selectedTest); setTestEditorOpen(true); } }}
            >
              Edit
            </button>
            <button type="button" onClick={() => { setEditingTest(null); setTestEditorOpen(true); }}>
              + New test
            </button>
          </div>

          <section className="card">
            <table>
              <thead><tr><th>Test</th><th>Category</th><th>Price</th><th>Status</th><th></th></tr></thead>
              <tbody>
                {tests.map((t) => (
                  <tr
                    key={t.id}
                    onClick={() => setSelectedTestId(t.id)}
                    className={selectedTestId === t.id ? 'selected-row' : undefined}
                  >
                    <td>{t.name}</td>
                    <td>{t.category}</td>
                    <td>{t.price.toFixed(2)}</td>
                    <td><span className="badge">{t.active ? 'Active' : 'Inactive'}</span></td>
                    <td className="row-actions">
                      <button
                        type="button"
                        className="ghost"
                        onClick={(e) => { e.stopPropagation(); void toggleActive(t); }}
                      >
                        {t.active ? 'Deactivate' : 'Reactivate'}
                      </button>
                    </td>
                  </tr>
                ))}
                {tests.length === 0 && <tr><td colSpan={5}>No tests match.</td></tr>}
              </tbody>
            </table>
          </section>
        </>
      )}

      {picking && (
        <TestPickerDialog
          patientName={patient?.name ?? ''}
          billedTestIds={lines.map((l) => l.testId).filter((id): id is string => id !== null)}
          addedCount={lines.length}
          addedTotal={finalAmount}
          onAdd={addTest}
          onDone={() => {
            setPicking(false);
            setStatus(lines.length === 0 ? 'No tests added yet.' : `${lines.length} test(s) on this bill.`);
          }}
        />
      )}

      {testEditorOpen && (
        <TestEditorDialog
          existing={editingTest}
          onClose={() => setTestEditorOpen(false)}
          onSaved={async (message) => {
            setTestEditorOpen(false);
            setMasterStatus(message);
            await loadTests(masterSearch);
          }}
        />
      )}

      <ShortcutHints keys={[['f2', 'new bill'], ['f3', 'search tests'], ['f6', 'load requested tests'], ['f4', 'save'], ['f8', 'save & print']]} />

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
