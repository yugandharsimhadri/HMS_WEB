import { useCallback, useEffect, useRef, useState } from 'react';
import { api, ApiError, downloadFile, openPdf } from '../api/client';
import { ShortcutHints } from '../shell/ShortcutHints';
import { useHotkey } from '../shell/hotkeys';
import type {
  DayBookSummary,
  DiagnosticsReport,
  ReportFormat,
  ReportKind,
  ReportTable,
  PaymentMode,
} from '../api/types';

/** The tab order. `None` holds a place for the two tabs with no export of
 * their own, exactly as the desktop's `TabOrder` array does, so inserting or
 * reordering a tab can never point an export at the wrong report. */
interface Tab {
  id: string;
  label: string;
  kind: ReportKind;
  /** Endpoint override for the two tabs that are not a `ReportKind`. */
  path?: string;
}

const TABS: Tab[] = [
  { id: 'daybook', label: 'Day Book', kind: 'DayBook' },
  { id: 'gst', label: 'GST Summary', kind: 'GstSummary' },
  { id: 'opd', label: 'OPD Register', kind: 'OpdRegister' },
  { id: 'expiring', label: 'Expiring Soon', kind: 'ExpiringSoon' },
  { id: 'partpacks', label: 'Part Packs', kind: 'None', path: '/api/reports/part-packs' },
  { id: 'reconcile', label: 'Stock to Reconcile', kind: 'None', path: '/api/reports/to-reconcile' },
  { id: 'lowstock', label: 'Low Stock', kind: 'LowStock' },
  { id: 'stock', label: 'Stock Register', kind: 'StockRegister' },
  { id: 'h1', label: 'Schedule H1', kind: 'ScheduleH1' },
  // Money in, from every module, grouped by how it was paid — the till and
  // the bank statement are checked against this one.
  { id: 'collections', label: 'Collections', kind: 'Collections' },
  // Last, and with no export of its own — the desktop's tenth tab.
  { id: 'diagnostics', label: 'Diagnostics', kind: 'None', path: '/api/reports/diagnostics' },
];

const EXPIRING_DAY_OPTIONS = [30, 60, 90, 180];

/**
 * Which document sits behind a Collections row, keyed by its Source column.
 * The report gathers money from six modules, and each keeps its receipt in a
 * different place — a consultation fee on the visit, a dental instalment on
 * the payment.
 */
const COLLECTION_ROUTES: Record<string, string> = {
  'Consultation': 'receipt',
  'Pharmacy': 'bill',
  'Diagnostics': 'diagnostic-bill',
  'Procedures': 'procedure-bill',
  'Pathology Lab': 'lab-report',
  'Dentist': 'dental-receipt',
};

/** Undefined is "all modes", which keeps the per-mode split in the totals. */
const PAYMENT_MODES: { value: PaymentMode | undefined; label: string }[] = [
  { value: undefined, label: 'All' },
  { value: 'Cash', label: 'Cash' },
  { value: 'Upi', label: 'UPI' },
  { value: 'Card', label: 'Card' },
];

const localDate = (d: Date) =>
  `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

const today = () => localDate(new Date());

/** Formats a cell the same way the PDF does, so the screen and the export
 * read alike. */
function formatCell(value: string | number | null, format: ReportFormat): string {
  if (value === null || value === undefined || value === '') return '—';

  switch (format) {
    case 'Money':
      return `₹${Number(value).toLocaleString('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
    case 'Number':
      return Number(value).toLocaleString('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    case 'Integer':
      return Number(value).toLocaleString('en-IN');
    case 'Date':
      return new Date(String(value)).toLocaleDateString();
    case 'Time':
      return new Date(String(value)).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
    case 'DateTime':
      return new Date(String(value)).toLocaleString();
    default:
      return String(value);
  }
}

export function ReportsPage() {
  const [tab, setTab] = useState<Tab>(TABS[0]);

  const [date, setDate] = useState(today);
  const [from, setFrom] = useState(today);
  const [to, setTo] = useState(today);
  const [expiringDays, setExpiringDays] = useState(90);
  const [includeZeroStock, setIncludeZeroStock] = useState(false);
  const [stockSearch, setStockSearch] = useState('');
  const [billSearch, setBillSearch] = useState('');

  const [table, setTable] = useState<ReportTable | null>(null);
  const [summary, setSummary] = useState<DayBookSummary | null>(null);
  const [selectedRow, setSelectedRow] = useState<number | null>(null);
  const [diagnostics, setDiagnostics] = useState<DiagnosticsReport | null>(null);

  const [paymentMode, setPaymentMode] = useState<PaymentMode | undefined>(undefined);
  const [status, setStatus] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const query = useCallback(() => {
    const p = new URLSearchParams({
      date, from, to,
      expiringDays: String(expiringDays),
      includeZeroStock: String(includeZeroStock),
    });
    if (stockSearch.trim()) p.set('search', stockSearch.trim());
    if (paymentMode) p.set('mode', paymentMode);
    return p.toString();
  }, [date, from, to, expiringDays, includeZeroStock, stockSearch, paymentMode]);

  // The generation a load belongs to. Switching tabs quickly — click Day
  // Book, then OPD Register before the first request has returned — starts
  // a second fetch while the first is still in flight, and nothing before
  // this ordered them: whichever response happened to arrive *last* won
  // setTable(), not whichever tab was actually selected last. The active
  // tab button's own highlight comes from tab.id alone, with no network
  // dependency, so it always showed the right tab — only the table under it
  // could be left showing the previous tab's rows and columns, silently.
  // Bumped once per call and captured in a closure, so a response is only
  // allowed to update state if it is still the most recent request made.
  const loadGeneration = useRef(0);

  const load = useCallback(async (t: Tab) => {
    const generation = ++loadGeneration.current;
    const isCurrent = () => generation === loadGeneration.current;

    setBusy(true);
    setError(null);
    try {
      setSelectedRow(null);

      if (t.id === 'diagnostics') {
        // Cleared eagerly, same as the branch below: switching away from
        // whichever report was on screen should read as "leaving that
        // report" immediately, not only once the new one has arrived.
        setTable(null);
        setSummary(null);
        const result = await api.get<DiagnosticsReport>(`/api/reports/diagnostics?${query()}`);
        if (!isCurrent()) return;
        setDiagnostics(result);
        return;
      }
      setDiagnostics(null);

      const path = t.path ?? `/api/reports/${t.kind}?${query()}`;
      const result = await api.get<ReportTable>(path);
      if (!isCurrent()) return;
      setTable(result);

      // The cards belong to the day book alone — they are not rows of any
      // other report.
      if (t.kind === 'DayBook') {
        const dayBookSummary = await api.get<DayBookSummary>(`/api/reports/day-book/summary?date=${date}`);
        if (!isCurrent()) return;
        setSummary(dayBookSummary);
      } else {
        setSummary(null);
      }
    } catch (err) {
      if (!isCurrent()) return;
      setError(err instanceof ApiError ? err.message : 'Could not load the report.');
      setTable(null);
    } finally {
      if (isCurrent()) setBusy(false);
    }
  }, [query, date]);

  useEffect(() => { void load(tab); }, [tab, load]);

  const findBill = async () => {
    setError(null);
    setBusy(true);
    try {
      const found = await api.get<ReportTable>(
        `/api/reports/find-bill?term=${encodeURIComponent(billSearch.trim())}`,
      );
      setTable(found);
      setStatus(found.rows.length === 0
        ? `No bill matches “${billSearch}”.`
        : `${found.rows.length} bill(s) matching “${billSearch}”, across all dates.`);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not search bills.');
    } finally {
      setBusy(false);
    }
  };

  // Nothing here is saved, so F4 has no meaning on this screen and is left
  // unbound rather than given a second job.
  const G = 'Reports';

  const exportAs = async (format: 'pdf' | 'excel') => {
    if (tab.kind === 'None') return;
    setError(null);
    try {
      if (format === 'pdf') {
        await openPdf(`/api/reports/${tab.kind}/pdf?${query()}`);
      } else {
        // A workbook is a download, not something to read in a tab.
        await downloadFile(`/api/reports/${tab.kind}/excel?${query()}`, `${tab.kind}.xlsx`);
      }
    } catch (err) {
      setError(err instanceof ApiError ? err.message : `Could not export the ${format}.`);
    }
  };

  /**
   * Reprints the selected row's document, marked as a duplicate. Which
   * document depends on the report: a pharmacy bill from the day book, a
   * fee receipt from the OPD register, a diagnostic bill from the
   * Diagnostics tab. All three are what the desktop offers here, and all
   * three re-read the record rather than printing the row on screen.
   */
  const reprintRow = async (id: string) => {
    setError(null);
    try {
      if (tab.kind === 'DayBook') await openPdf(`/api/print/bill/${id}?reprint=true`);
      else if (tab.kind === 'OpdRegister') await openPdf(`/api/print/receipt/${id}?reprint=true`);
      else if (tab.id === 'diagnostics') await openPdf(`/api/print/diagnostic-bill/${id}?reprint=true`);
      else if (tab.kind === 'Collections') {
        // Collections deliberately spans every module that takes money, so
        // the id alone cannot say whether it belongs to a visit, a sale or a
        // lab order. The row's own Source column is what decides.
        const source = String(table?.rows[selectedRow ?? -1]?.cells[2] ?? '');
        const route = COLLECTION_ROUTES[source];

        if (!route) {
          setError(`There is no printable document behind a ${source || 'that'} receipt.`);
          return;
        }
        await openPdf(`/api/print/${route}/${id}?reprint=true`);
      }
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not reprint.');
    }
  };

  /** The reports whose rows have a document behind them. Collections covers
   * the other five modules, which is what makes "reprint anything" possible
   * from one screen. */
  const reprintLabel = tab.kind === 'DayBook' ? 'Reprint selected bill'
    : tab.kind === 'OpdRegister' ? 'Reprint selected receipt'
    : tab.kind === 'Collections' ? 'Reprint selected'
    : null;

  const selectedId = selectedRow !== null ? (table?.rows[selectedRow]?.id ?? null) : null;

  /** Stock Register is Excel-only by design — a wide, analysis-oriented dump
   * rather than a printable statement. */
  const canExportPdf = tab.kind !== 'None' && tab.kind !== 'StockRegister' && (table?.rows.length ?? 0) > 0;
  const canExportExcel = tab.kind !== 'None' && (table?.rows.length ?? 0) > 0;

  // Diagnostics reads both: today's bills off the Date picker, and
  // revenue-by-day plus the most-ordered tests off the From/To range — the
  // same split the day book and the GST summary already use.
  const isRangeBased = tab.kind === 'GstSummary' || tab.kind === 'ScheduleH1'
    || tab.kind === 'Collections' || tab.id === 'diagnostics';
  const hasDatePicker = tab.id === 'diagnostics'
    || (tab.kind !== 'GstSummary' && tab.kind !== 'ScheduleH1'
        && tab.kind !== 'StockRegister' && tab.kind !== 'LowStock' && tab.kind !== 'None');

  // Registered after the actions they call, so no binding refers to a
  // function declared further down the file.
  useHotkey('f9', 'Refresh this report', G, () => { void load(tab); });
  useHotkey('f7', 'Export to Excel', G, () => { void exportAs('excel'); });
  useHotkey('f8', 'Export to PDF', G, () => { void exportAs('pdf'); });

  return (
    <div className="page">
      <div className="page-head">
        <div>
          <h1>Reports</h1>
          <p className="hint">{table ? `${table.title} · ${table.dateLabel}` : 'Loading…'}</p>
        </div>
        <div className="inline-form">
          {reprintLabel && (
            <button type="button" disabled={!selectedId} onClick={() => selectedId && void reprintRow(selectedId)}>
              {reprintLabel}
            </button>
          )}
          <button type="button" disabled={!canExportPdf} onClick={() => void exportAs('pdf')}>Export PDF</button>
          <button type="button" disabled={!canExportExcel} onClick={() => void exportAs('excel')}>Export Excel</button>
          <button type="button" className="ghost" disabled={busy} onClick={() => void load(tab)}>Refresh</button>
        </div>
      </div>

      <div className="tabs">
        {TABS.map((t) => (
          <button
            key={t.id}
            type="button"
            className={t.id === tab.id ? 'tab active' : 'tab'}
            onClick={() => setTab(t)}
          >
            {t.label}
          </button>
        ))}
      </div>

      {/* Only the filters this report actually reads — a From/To pair on the
          day book would imply a range it does not use. */}
      <div className="inline-form">
        {hasDatePicker && (
          <>
            <label>Date</label>
            <input type="date" value={date} onChange={(e) => setDate(e.target.value)} />
          </>
        )}

        {isRangeBased && (
          <>
            <label>From</label>
            <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
            <label>To</label>
            <input type="date" value={to} onChange={(e) => setTo(e.target.value)} />
          </>
        )}

        {/* Cash gives the till to count; UPI gives the list to check a bank
            statement against. "All" keeps the per-mode split in the totals. */}
        {tab.kind === 'Collections' && (
          <>
            <label>Paid by</label>
            {PAYMENT_MODES.map((m) => (
              <button
                key={m.value ?? 'all'}
                type="button"
                className={m.value === paymentMode ? 'tab active' : 'tab'}
                onClick={() => setPaymentMode(m.value)}
              >
                {m.label}
              </button>
            ))}
          </>
        )}

        {tab.kind === 'ExpiringSoon' && (
          <>
            <label>Within</label>
            {EXPIRING_DAY_OPTIONS.map((d) => (
              <button
                key={d}
                type="button"
                className={d === expiringDays ? 'tab active' : 'tab'}
                onClick={() => setExpiringDays(d)}
              >
                {d}d
              </button>
            ))}
          </>
        )}

        {tab.kind === 'StockRegister' && (
          <>
            <input
              placeholder="Search medicine or batch"
              value={stockSearch}
              onChange={(e) => setStockSearch(e.target.value)}
            />
            <label className="checkbox-label">
              <input
                type="checkbox"
                checked={includeZeroStock}
                onChange={() => setIncludeZeroStock(!includeZeroStock)}
              />
              <span>Include zero stock</span>
            </label>
          </>
        )}

        {tab.kind === 'DayBook' && (
          <>
            <input
              placeholder="Find a bill by no. or name"
              value={billSearch}
              onChange={(e) => setBillSearch(e.target.value)}
              // The desktop binds Enter to FindBillCommand; typing a bill
              // number and pressing Enter is the whole interaction.
              onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); void findBill(); } }}
            />
            <button type="button" className="ghost" onClick={() => void findBill()}>Find</button>
          </>
        )}
      </div>

      {error && <p className="auth-error">{error}</p>}
      {status && <p className="hint status-line">{status}</p>}

      {summary && (
        <div className="kpi-row">
          <div className="kpi">
            <span className="kpi-label">Collected (pharmacy)</span>
            <span className="kpi-value">₹{summary.totalCollected.toFixed(2)}</span>
            <span className="kpi-delta">
              cash ₹{summary.cashTotal.toFixed(2)} · UPI ₹{summary.upiTotal.toFixed(2)}
            </span>
          </div>
          <div className="kpi">
            <span className="kpi-label">GST in that</span>
            <span className="kpi-value">₹{(summary.cgstTotal + summary.sgstTotal).toFixed(2)}</span>
            <span className="kpi-delta">on ₹{summary.taxableTotal.toFixed(2)} taxable</span>
          </div>
          {/* Beside pharmacy revenue, never inside it — a consultation fee is
              not a taxable supply of goods. */}
          <div className="kpi">
            <span className="kpi-label">Consultation fees</span>
            <span className="kpi-value">₹{summary.consultationTotal.toFixed(2)}</span>
            <span className="kpi-delta">counted separately from pharmacy</span>
          </div>
          <div className="kpi">
            <span className="kpi-label">Patients seen</span>
            <span className="kpi-value">{summary.visitCount}</span>
            <span className="kpi-delta">excluding cancellations</span>
          </div>
        </div>
      )}

      {/* The Diagnostics tab is three grids rather than one table, which is
          why it is not a ReportKind and has no export — the desktop makes
          the same call. */}
      {diagnostics && (
        <>
          <div className="kpi-row">
            <div className="kpi">
              <span className="kpi-label">Diagnostics today</span>
              <span className="kpi-value">₹{diagnostics.todayTotal.toFixed(2)}</span>
              <span className="kpi-delta">{diagnostics.todaysBills.length} bill(s)</span>
            </div>
          </div>

          <section className="card">
            <h2>Today&rsquo;s diagnostic bills</h2>
            <table>
              <thead><tr><th>Time</th><th>Bill No</th><th>Patient</th><th>Amount</th><th>Status</th><th></th></tr></thead>
              <tbody>
                {diagnostics.todaysBills.map((b) => (
                  <tr key={b.id}>
                    <td>{new Date(b.billDate).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}</td>
                    <td>{b.billNo}</td>
                    <td>{b.patientName}<div className="hint">{b.patientNo}</div></td>
                    <td>₹{b.finalAmount.toFixed(2)}</td>
                    <td><span className="badge">{b.status}</span></td>
                    <td className="row-actions">
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
                {diagnostics.todaysBills.length === 0 && <tr><td colSpan={6}>Nothing billed today.</td></tr>}
              </tbody>
            </table>
          </section>

          <div className="queue-columns">
            <section className="card">
              <h2>Revenue by day</h2>
              <table>
                <thead><tr><th>Date</th><th>Bills</th><th>Revenue</th></tr></thead>
                <tbody>
                  {diagnostics.revenue.map((r) => (
                    <tr key={r.day}>
                      <td>{new Date(r.day).toLocaleDateString()}</td>
                      <td>{r.bills}</td>
                      <td>₹{r.amount.toFixed(2)}</td>
                    </tr>
                  ))}
                  {diagnostics.revenue.length === 0 && <tr><td colSpan={3}>Nothing in this range.</td></tr>}
                </tbody>
              </table>
            </section>

            <section className="card">
              <h2>Most ordered tests</h2>
              <table>
                <thead><tr><th>Test</th><th>Times ordered</th><th>Revenue</th></tr></thead>
                <tbody>
                  {diagnostics.topTests.map((t) => (
                    <tr key={t.test}>
                      <td>{t.test}</td>
                      <td>{t.times}</td>
                      <td>₹{t.amount.toFixed(2)}</td>
                    </tr>
                  ))}
                  {diagnostics.topTests.length === 0 && <tr><td colSpan={3}>Nothing in this range.</td></tr>}
                </tbody>
              </table>
            </section>
          </div>
        </>
      )}

      {!diagnostics && (
      <section className="card">
        {table ? (
          <>
            <table>
              <thead>
                <tr>
                  {table.columns.map((c) => (
                    <th key={c.header} style={{ textAlign: c.align === 'Right' ? 'right' : c.align === 'Center' ? 'center' : 'left' }}>
                      {c.header}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {table.rows.map((row, i) => (
                  <tr
                    key={i}
                    onClick={() => row.id && setSelectedRow(i)}
                    className={[
                      row.emphasise ? 'row-flagged' : '',
                      selectedRow === i ? 'selected-row' : '',
                    ].filter(Boolean).join(' ') || undefined}
                  >
                    {table.columns.map((c, j) => (
                      <td key={c.header} style={{ textAlign: c.align === 'Right' ? 'right' : c.align === 'Center' ? 'center' : 'left' }}>
                        {formatCell(row.cells[j] ?? null, c.format)}
                      </td>
                    ))}
                  </tr>
                ))}
                {table.rows.length === 0 && (
                  <tr><td colSpan={Math.max(1, table.columns.length)}>Nothing to report.</td></tr>
                )}
              </tbody>
            </table>

            {table.totals.length > 0 && (
              <div className="totals">
                {table.totals.map((t) => (
                  <div key={t.label}>
                    <span>{t.label}</span>
                    <span>{formatCell(t.value, t.format)}</span>
                  </div>
                ))}
              </div>
            )}
          </>
        ) : (
          !error && <p className="hint">Loading…</p>
        )}
      </section>
      )}

      <ShortcutHints keys={[['f9', 'refresh'], ['f7', 'export excel'], ['f8', 'export pdf']]} />
    </div>
  );
}
