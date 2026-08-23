import { useCallback, useEffect, useMemo, useState } from 'react';
import { api, ApiError, openPdf } from '../api/client';
import type {
  LabAnalyte,
  LabOrder,
  LabOrderStatus,
  LabPackageMaster,
  LabReport,
  LabResultType,
  PaymentMode,
  Patient,
} from '../api/types';
import { PatientPicker } from '../shell/PatientPicker';
import { ShortcutHints } from '../shell/ShortcutHints';
import { useHotkey } from '../shell/hotkeys';
import { PatientEditorDialog } from '../opd/PatientEditorDialog';

const PAYMENT_MODES: PaymentMode[] = ['Cash', 'Upi', 'Card'];

const STATUS_LABELS: Record<LabOrderStatus, string> = {
  Ordered: 'Ordered',
  SampleCollected: 'Sample collected',
  ResultEntered: 'Result entered',
  Verified: 'Verified',
  Completed: 'Completed',
};

/** One line on the order being built, before it is persisted. */
interface OrderLine {
  key: string;
  reportId: string;
  reportName: string;
  price: number;
}

/** One analyte's entry line, flattened across every report on the order so
 * the whole panel is entered in one pass. */
interface ResultRow {
  key: string;
  orderReportId: string;
  reportName: string;
  analyteId: string;
  analyteName: string;
  units: string;
  resultType: LabResultType;
  value: string;
  referenceRange: string;
  flag: string;
}

const money = (n: number) => `₹${n.toFixed(2)}`;

export function PathologyLabPage() {
  // ── Patient ────────────────────────────────────────────────────────────
  const [search, setSearch] = useState('');
  const [matches, setMatches] = useState<Patient[]>([]);
  const [patient, setPatient] = useState<Patient | null>(null);
  const [addingPatient, setAddingPatient] = useState(false);

  // ── Catalogues ─────────────────────────────────────────────────────────
  const [reports, setReports] = useState<LabReport[]>([]);
  const [packages, setPackages] = useState<LabPackageMaster[]>([]);

  // ── Orders ─────────────────────────────────────────────────────────────
  const [orders, setOrders] = useState<LabOrder[]>([]);
  const [selectedOrderId, setSelectedOrderId] = useState<string | null>(null);

  // ── New order builder ──────────────────────────────────────────────────
  const [lines, setLines] = useState<OrderLine[]>([]);
  const [reportToAdd, setReportToAdd] = useState('');
  const [packageToApply, setPackageToApply] = useState('');
  const [packageId, setPackageId] = useState<string | null>(null);
  const [referredBy, setReferredBy] = useState('');
  const [specimenId, setSpecimenId] = useState('');
  const [discount, setDiscount] = useState('0');
  const [paymentMode, setPaymentMode] = useState<PaymentMode>('Cash');
  const [transactionNo, setTransactionNo] = useState('');
  const [remarks, setRemarks] = useState('');

  // ── Result entry ───────────────────────────────────────────────────────
  const [resultRows, setResultRows] = useState<ResultRow[]>([]);

  const [status, setStatus] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  // Derived from the live list, never a captured object.
  const selectedOrder = orders.find((o) => o.id === selectedOrderId) ?? null;

  useEffect(() => {
    void api.get<LabReport[]>('/api/lab/reports').then(setReports).catch(() => {});
    void api.get<LabPackageMaster[]>('/api/lab/packages').then(setPackages).catch(() => {});
  }, []);

  const loadOrders = useCallback(async (patientId: string) => {
    try {
      setOrders(await api.get<LabOrder[]>(`/api/lab/orders/by-patient/${patientId}`));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not load the orders.');
    }
  }, []);

  const selectPatient = useCallback((p: Patient) => {
    setPatient(p);
    setSearch('');
    setMatches([]);
    setError(null);
    setSelectedOrderId(null);
    setResultRows([]);
    void loadOrders(p.id);
  }, [loadOrders]);

  const findPatients = useCallback(async (term: string) => {
    if (!term.trim()) { setMatches([]); return; }
    try {
      const found = await api.get<Patient[]>(`/api/patients?term=${encodeURIComponent(term.trim())}&take=20`);
      setMatches(found);
      if (found.length === 1) selectPatient(found[0]);
    } catch {
      setMatches([]);
    }
  }, [selectPatient]);

  useEffect(() => {
    if (patient) return;
    const handle = setTimeout(() => void findPatients(search), 250);
    return () => clearTimeout(handle);
  }, [search, findPatients, patient]);

  const changePatient = () => {
    setPatient(null);
    setSearch('');
    setMatches([]);
    setOrders([]);
    setSelectedOrderId(null);
    setResultRows([]);
  };

  // ── Result entry rows ──────────────────────────────────────────────────

  /** Every analyte of every ordered report, flattened onto one list and
   * pre-filled with whatever has already been entered. */
  const loadResultRows = useCallback(async (orderId: string) => {
    try {
      const order = await api.get<LabOrder>(`/api/lab/orders/${orderId}`);
      const rows: ResultRow[] = [];

      for (const orderReport of order.reports) {
        if (!orderReport.reportId) continue;
        const analytes = await api.get<LabAnalyte[]>(`/api/lab/reports/${orderReport.reportId}/analytes`);

        for (const analyte of analytes) {
          const existing = orderReport.results.find((r) => r.analyteId === analyte.id);
          rows.push({
            key: `${orderReport.id}:${analyte.id}`,
            orderReportId: orderReport.id,
            reportName: orderReport.reportName,
            analyteId: analyte.id,
            analyteName: analyte.name,
            units: analyte.units,
            resultType: analyte.resultType,
            value: existing?.resultValue ?? '',
            referenceRange: existing?.referenceRangeDisplay ?? '',
            flag: existing?.flag ?? '',
          });
        }
      }

      setResultRows(rows);
    } catch {
      setResultRows([]);
    }
  }, []);

  useEffect(() => {
    if (selectedOrderId) void loadResultRows(selectedOrderId);
    else setResultRows([]);
  }, [selectedOrderId, loadResultRows]);

  // ── Order builder ──────────────────────────────────────────────────────

  const total = useMemo(() => lines.reduce((sum, l) => sum + l.price, 0), [lines]);
  const finalAmount = Math.max(0, total - (Number(discount) || 0));

  const addReport = () => {
    const report = reports.find((r) => r.id === reportToAdd);
    if (!report) return;
    if (lines.some((l) => l.reportId === report.id)) {
      setError(`${report.name} is already on this order.`);
      return;
    }
    setLines((current) => [
      ...current,
      { key: crypto.randomUUID(), reportId: report.id, reportName: report.name, price: report.price },
    ]);
    setReportToAdd('');
    setError(null);
  };

  /**
   * Expands the package into one line per constituent report, each at its
   * own catalogue price, then discounts the order down to the package's
   * flat price. Not one opaque line: the printed report still shows what
   * each report is worth, and each report's analytes still have to be known
   * for result entry.
   */
  const G = 'Pathology Lab';
  useHotkey('f2', 'Start a new order', G, () => newOrderForm());
  useHotkey('f7', 'Apply a package', G, () => { if (patient) void applyPackage(); });
  useHotkey('f4', 'Save the order', G, () => { if (patient) void saveOrder(); });
  useHotkey('f6', 'Mark sample collected', G, () => markCollected());
  useHotkey('f8', 'Save the results', G, () => saveResults());

  const applyPackage = async () => {
    const pkg = packages.find((p) => p.id === packageToApply);
    if (!pkg) return;

    setError(null);
    try {
      const pkgReports = await api.get<LabReport[]>(`/api/lab/packages/${pkg.id}/reports`);
      if (pkgReports.length === 0) {
        setError('This package has no reports configured.');
        return;
      }

      setLines(pkgReports.map((r) => ({
        key: crypto.randomUUID(), reportId: r.id, reportName: r.name, price: r.price,
      })));
      setDiscount(String(Math.max(0, pkgReports.reduce((s, r) => s + r.price, 0) - pkg.packagePrice)));
      setPackageId(pkg.id);
      setPackageToApply('');
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not apply the package.');
    }
  };

  const newOrderForm = () => {
    setLines([]);
    setReportToAdd('');
    setPackageToApply('');
    setPackageId(null);
    setReferredBy('');
    setSpecimenId('');
    setDiscount('0');
    setPaymentMode('Cash');
    setTransactionNo('');
    setRemarks('');
    setError(null);
  };

  const saveOrder = async () => {
    setError(null);
    if (!patient) { setError('Select a patient first.'); return; }
    if (lines.length === 0) { setError('Add at least one report, or apply a package.'); return; }

    setBusy(true);
    try {
      const saved = await api.post<LabOrder>('/api/lab/orders', {
        id: null,
        patientId: patient.id,
        packageId,
        visitId: null,
        referredBy: referredBy.trim() || null,
        specimenId: specimenId.trim() || null,
        discount: Number(discount) || 0,
        paymentMode,
        transactionNo: transactionNo.trim() || null,
        remarks: remarks.trim() || null,
        lines: lines.map((l) => ({ reportId: l.reportId, reportName: l.reportName, price: l.price })),
      });

      setStatus(`${saved.orderNo} saved — ${money(saved.finalAmount)}.`);
      newOrderForm();
      await loadOrders(patient.id);
      setSelectedOrderId(saved.id);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not save the order.');
    } finally {
      setBusy(false);
    }
  };

  // ── Order actions ──────────────────────────────────────────────────────

  const run = async (fn: () => Promise<string>) => {
    setError(null);
    try {
      setStatus(await fn());
      if (patient) await loadOrders(patient.id);
      if (selectedOrderId) await loadResultRows(selectedOrderId);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'That did not go through.');
    }
  };

  const markCollected = () => {
    if (!selectedOrder) return;
    void run(async () => {
      await api.post(`/api/lab/orders/${selectedOrder.id}/status`, { status: 'SampleCollected' });
      return 'Sample marked collected.';
    });
  };

  const saveResults = () => {
    if (!selectedOrder) return;
    const entered = resultRows.filter((r) => r.value.trim() !== '');
    if (entered.length === 0) {
      setError('Enter at least one result before saving.');
      return;
    }

    void run(async () => {
      const count = await api.post<number>(`/api/lab/orders/${selectedOrder.id}/results`, {
        results: entered.map((r) => ({
          orderReportId: r.orderReportId,
          analyteId: r.analyteId,
          analyteName: r.analyteName,
          units: r.units,
          resultValue: r.value,
          resultType: r.resultType,
        })),
      });
      return `${count} result(s) saved.`;
    });
  };

  const verify = () => {
    if (!selectedOrder) return;
    void run(async () => {
      await api.post(`/api/lab/orders/${selectedOrder.id}/verify`);
      return 'Order verified — the report can now be printed.';
    });
  };

  const printReport = async () => {
    if (!selectedOrder) return;
    setError(null);
    try {
      await openPdf(`/api/print/lab-report/${selectedOrder.id}`);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not open the report.');
    }
  };

  const canPrint = selectedOrder?.status === 'Verified' || selectedOrder?.status === 'Completed';

  // Rows grouped by report, so the panel reads as panels rather than one
  // undifferentiated column of analytes.
  const groupedRows = useMemo(() => {
    const groups = new Map<string, ResultRow[]>();
    for (const row of resultRows) {
      const list = groups.get(row.reportName) ?? [];
      list.push(row);
      groups.set(row.reportName, list);
    }
    return [...groups.entries()];
  }, [resultRows]);

  return (
    <div className="page">
      <div className="page-head">
        <div>
          <h1>Pathology Lab</h1>
          <p className="hint">{orders.length === 0 ? 'No orders for this patient' : `${orders.length} order(s)`}</p>
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
            onPick={selectPatient}
            onNewPatient={() => setAddingPatient(true)}
          />
        )}
      </section>

      {error && <p className="auth-error">{error}</p>}
      {status && <p className="hint status-line">{status}</p>}

      <section className="card">
        <h2>New order</h2>

        <div className="inline-form">
          <select value={reportToAdd} onChange={(e) => setReportToAdd(e.target.value)}>
            <option value="">Report…</option>
            {reports.map((r) => <option key={r.id} value={r.id}>{r.name} — {money(r.price)}</option>)}
          </select>
          <button type="button" onClick={addReport}>Add report</button>

          <select value={packageToApply} onChange={(e) => setPackageToApply(e.target.value)}>
            <option value="">Package…</option>
            {packages.map((p) => <option key={p.id} value={p.id}>{p.name} — {money(p.packagePrice)}</option>)}
          </select>
          <button type="button" onClick={() => void applyPackage()}>Apply package</button>
        </div>
        {packages.length === 0 && (
          <p className="hint">No lab packages set up yet — they arrive with the Masters module.</p>
        )}

        <table>
          <thead><tr><th>Report</th><th>Price</th><th></th></tr></thead>
          <tbody>
            {lines.map((l) => (
              <tr key={l.key}>
                <td>{l.reportName}</td>
                <td>{money(l.price)}</td>
                <td className="row-actions">
                  <button
                    type="button"
                    className="ghost"
                    onClick={() => setLines((c) => c.filter((x) => x.key !== l.key))}
                  >
                    Remove
                  </button>
                </td>
              </tr>
            ))}
            {lines.length === 0 && <tr><td colSpan={3}>Nothing on this order yet.</td></tr>}
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
              onChange={(e) => setDiscount(e.target.value)}
              style={{ width: '7rem' }}
            />
          </div>
          <div><strong>Payable</strong><strong>{money(finalAmount)}</strong></div>
        </div>

        <div className="settings-form">
          <div className="settings-row">
            <label>Referred by</label>
            <input value={referredBy} onChange={(e) => setReferredBy(e.target.value)} />
          </div>
          <div className="settings-row">
            <label>Specimen id</label>
            <input value={specimenId} onChange={(e) => setSpecimenId(e.target.value)} />
          </div>
          <div className="settings-row">
            <label>Mode</label>
            <select value={paymentMode} onChange={(e) => setPaymentMode(e.target.value as PaymentMode)}>
              {PAYMENT_MODES.map((m) => <option key={m} value={m}>{m}</option>)}
            </select>
          </div>
          {(paymentMode === 'Upi' || paymentMode === 'Card') && (
            <div className="settings-row">
              <label>Reference no.</label>
              <input value={transactionNo} onChange={(e) => setTransactionNo(e.target.value)} />
            </div>
          )}
          <div className="settings-row">
            <label>Remarks</label>
            <input value={remarks} onChange={(e) => setRemarks(e.target.value)} />
          </div>
        </div>

        <div className="settings-actions">
          <button type="button" disabled={busy} onClick={() => void saveOrder()}>Save order</button>
          <button type="button" className="ghost" onClick={newOrderForm}>Clear</button>
        </div>
      </section>

      <section className="card">
        <h2>Orders</h2>
        <table>
          <thead><tr><th>Order</th><th>Date</th><th>Reports</th><th>Amount</th><th>Status</th></tr></thead>
          <tbody>
            {orders.map((o) => (
              <tr
                key={o.id}
                onClick={() => setSelectedOrderId(o.id)}
                className={selectedOrderId === o.id ? 'selected-row' : undefined}
              >
                <td>{o.orderNo}</td>
                <td>{new Date(o.orderDate).toLocaleDateString()}</td>
                <td>{o.reports.length}</td>
                <td>{money(o.finalAmount)}</td>
                <td><span className="badge">{STATUS_LABELS[o.status]}</span></td>
              </tr>
            ))}
            {orders.length === 0 && <tr><td colSpan={5}>No orders for this patient.</td></tr>}
          </tbody>
        </table>
      </section>

      {selectedOrder && (
        <section className="card">
          <h2>
            Results — {selectedOrder.orderNo}
            <span className="hint"> · {STATUS_LABELS[selectedOrder.status]}</span>
          </h2>

          <div className="inline-form">
            <button type="button" onClick={markCollected}>Mark sample collected</button>
            <button type="button" onClick={saveResults}>Save results</button>
            <button type="button" onClick={verify}>Verify</button>
            {/* Refused server-side too; disabled here so the reason is
                visible before the click rather than after it. */}
            <button type="button" className="ghost" disabled={!canPrint} onClick={() => void printReport()}>
              Print report
            </button>
          </div>
          {!canPrint && (
            <p className="hint">The report prints once the order is verified.</p>
          )}

          {groupedRows.map(([reportName, rows]) => (
            <div key={reportName} className="sub-card">
              <h3 className="sub-heading">{reportName}</h3>
              <table>
                <thead>
                  <tr><th>Analyte</th><th>Result</th><th>Units</th><th>Reference range</th><th>Flag</th></tr>
                </thead>
                <tbody>
                  {rows.map((row) => (
                    <tr key={row.key}>
                      <td>{row.analyteName}</td>
                      <td>
                        <input
                          value={row.value}
                          type={row.resultType === 'Numeric' ? 'number' : 'text'}
                          step="any"
                          onChange={(e) =>
                            setResultRows((current) =>
                              current.map((r) => (r.key === row.key ? { ...r, value: e.target.value } : r)))
                          }
                          style={{ width: '7rem' }}
                        />
                      </td>
                      <td>{row.units}</td>
                      {/* Both of these come back from the server after
                          saving — the range is matched to this patient's
                          gender and age, and the flag computed from it. */}
                      <td>{row.referenceRange || '—'}</td>
                      <td>
                        {row.flag && row.flag !== 'Normal'
                          ? <span className="badge">{row.flag}</span>
                          : row.flag ? '—' : ''}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ))}

          {groupedRows.length === 0 && <p className="hint">This order has no analytes to enter.</p>}
        </section>
      )}

      <ShortcutHints keys={[['f2', 'new order'], ['f3', 'find patient'], ['f7', 'apply package'], ['f6', 'sample collected'], ['f4', 'save order'], ['f8', 'save results']]} />

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
