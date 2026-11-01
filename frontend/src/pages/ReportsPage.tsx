import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { api, ApiError, downloadFile, openPdf } from '../api/client';
import { useGeneralSettings } from '../settings/SettingsContext';
import { DateRange } from '../shell/DateRange';
import { todayInput, type Range } from '../shell/dates';
import { ShortcutHints } from '../shell/ShortcutHints';
import { useHotkey } from '../shell/hotkeys';
import type {
  DayBookSummary,
  DiagnosticsReport,
  ReportFormat,
  ReportKind,
  ReportTable,
  ReportTone,
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
  { id: 'daybook', label: 'Day book', kind: 'DayBook' },
  { id: 'gst', label: 'GST summary', kind: 'GstSummary' },
  { id: 'opd', label: 'OPD register', kind: 'OpdRegister' },
  // Grouped by doctor over a range — a day rarely says much about one
  // doctor's own load, so this sits with Collections and the GST summary
  // rather than the single-day OPD Register above it.
  { id: 'opd-doctor', label: 'OPD by doctor', kind: 'OpdByDoctor' },
  { id: 'expiring', label: 'Expiring soon', kind: 'ExpiringSoon' },
  { id: 'partpacks', label: 'Part packs', kind: 'None', path: '/api/reports/part-packs' },
  { id: 'reconcile', label: 'Stock to reconcile', kind: 'None', path: '/api/reports/to-reconcile' },
  { id: 'lowstock', label: 'Low stock', kind: 'LowStock' },
  { id: 'stock', label: 'Stock Register', kind: 'StockRegister' },
  { id: 'h1', label: 'Schedule H1 register', kind: 'ScheduleH1' },
  // Money in, from every module, grouped by how it was paid — the till and
  // the bank statement are checked against this one.
  { id: 'collections', label: 'Collections', kind: 'Collections' },
  // Last, and with no export of its own — the desktop's tenth tab.
  { id: 'diagnostics', label: 'Diagnostics', kind: 'None', path: '/api/reports/diagnostics' },
];

/** Shown only when the In-patients module is on. The census and dues are about now; the
 * other three are read over the From/To range. */
const IP_TABS: Tab[] = [
  { id: 'ip-census', label: 'IP census', kind: 'IpCensus' },
  { id: 'ip-register', label: 'Admissions & discharges', kind: 'IpRegister' },
  { id: 'ip-bills', label: 'IP bills', kind: 'IpBills' },
  { id: 'ip-dues', label: 'IP dues', kind: 'IpDues' },
  { id: 'ip-occupancy', label: 'Bed occupancy', kind: 'BedOccupancy' },
];

const IP_KINDS: ReportKind[] = ['IpCensus', 'IpRegister', 'IpBills', 'IpDues', 'BedOccupancy'];

/** Every report about what happened is read over From/To; the stock views, the census and
 * the dues are about now and take no dates. */
const RANGE_KINDS: ReportKind[] = [
  'DayBook', 'GstSummary', 'OpdRegister', 'ScheduleH1', 'Collections', 'OpdByDoctor',
  'IpRegister', 'IpBills', 'BedOccupancy',
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
  'In-patient': 'ip-receipt',
};

/** Undefined is "all modes", which keeps the per-mode split in the totals. */
const PAYMENT_MODES: { value: PaymentMode | undefined; label: string }[] = [
  { value: undefined, label: 'All' },
  { value: 'Cash', label: 'Cash' },
  { value: 'Upi', label: 'UPI' },
  { value: 'Card', label: 'Card' },
];

/** Formats a cell the same way the PDF does, so the screen and the export
 * read alike. */
function formatCell(value: string | number | boolean | null, format: ReportFormat): string {
  if (value === null || value === undefined || value === '') return '—';

  switch (format) {
    case 'Money':
      return `₹${Number(value).toLocaleString('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
    case 'Number':
      return Number(value).toLocaleString('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    case 'Integer':
      return Number(value).toLocaleString('en-IN');
    case 'Percent':
      return `${Number(value).toFixed(1)}%`;
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

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const two = (n: number) => String(n).padStart(2, '0');

/** HH:mm, the desktop's TIME column. */
const timeOf = (iso: string) => { const d = new Date(iso); return `${two(d.getHours())}:${two(d.getMinutes())}`; };

/** dd MMM yy, the desktop's DATE column on the diagnostics tab. */
const dateOf = (iso: string) => {
  const d = new Date(iso);
  return `${two(d.getDate())} ${MONTHS[d.getMonth()]} ${String(d.getFullYear()).slice(2)}`;
};

/** The desktop's cell styles: the name carries the weight, detail is quiet, an amount is semibold. */
const TONE_CLASS: Record<ReportTone, string | undefined> = {
  Text: undefined,
  Name: 'cell-name',
  Muted: 'cell-muted',
  Number: 'cell-number',
  Amount: 'cell-amount',
};


export function ReportsPage() {
  const general = useGeneralSettings();
  const navigate = useNavigate();
  const [params] = useSearchParams();

  const tabs = useMemo(() => (general?.inpatientEnabled ? [...TABS, ...IP_TABS] : TABS), [general?.inpatientEnabled]);
  // ?tab=ip-register opens straight onto a report - the dashboard's tiles link here.
  const [tab, setTab] = useState<Tab>(() => [...TABS, ...IP_TABS].find((t) => t.id === params.get('tab')) ?? TABS[0]);

  const [range, setRange] = useState<Range>(() => ({ from: todayInput(), to: todayInput() }));
  const { from, to } = range;
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
    // `date` rides along for the API's older single-date reading of a report.
    const p = new URLSearchParams({
      date: to, from, to,
      expiringDays: String(expiringDays),
      includeZeroStock: String(includeZeroStock),
    });
    if (stockSearch.trim()) p.set('search', stockSearch.trim());
    if (paymentMode) p.set('mode', paymentMode);
    return p.toString();
  }, [from, to, expiringDays, includeZeroStock, stockSearch, paymentMode]);

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

    } catch (err) {
      if (!isCurrent()) return;
      setError(err instanceof ApiError ? err.message : 'Could not load the report.');
      setTable(null);
    } finally {
      if (isCurrent()) setBusy(false);
    }
  }, [query]);

  // The cards above the tabs belong to the From/To range, not to whichever report is open - the
  // desktop shows them on every tab.
  const loadSummary = useCallback(async () => {
    try {
      setSummary(await api.get<DayBookSummary>(`/api/reports/day-book/summary?from=${from}&to=${to}`));
    } catch {
      setSummary(null);
    }
  }, [from, to]);

  useEffect(() => { void load(tab); }, [tab, load]);
  useEffect(() => { void loadSummary(); }, [loadSummary]);

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
      if (tab.kind === 'IpBills') await openPdf(`/api/print/ip/${id}/bill?reprint=true`);
      else if (IP_KINDS.includes(tab.kind)) navigate(`/ip/${id}`);
      else if (tab.kind === 'DayBook') await openPdf(`/api/print/bill/${id}?reprint=true`);
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
    : tab.kind === 'IpBills' ? 'Reprint selected bill'
    : tab.kind === 'IpCensus' || tab.kind === 'IpRegister' || tab.kind === 'IpDues' ? 'Open IP file'
    : null;

  const selectedId = selectedRow !== null ? (table?.rows[selectedRow]?.id ?? null) : null;

  /** Stock Register is Excel-only by design — a wide, analysis-oriented dump
   * rather than a printable statement. */
  const canExportPdf = tab.kind !== 'None' && tab.kind !== 'StockRegister' && (table?.rows.length ?? 0) > 0;
  const canExportExcel = tab.kind !== 'None' && (table?.rows.length ?? 0) > 0;

  const isRangeBased = RANGE_KINDS.includes(tab.kind) || tab.id === 'diagnostics';

  // Registered after the actions they call, so no binding refers to a
  // function declared further down the file.
  useHotkey('f9', 'Refresh this report', G, () => { void load(tab); void loadSummary(); });
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
          <button type="button" className="ghost" disabled={busy} onClick={() => { void load(tab); void loadSummary(); }}>Refresh</button>
        </div>
      </div>

      <div className="tabs">
        {tabs.map((t) => (
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
        {isRangeBased && <DateRange value={range} onChange={setRange} />}
        {(tab.kind === 'IpCensus' || tab.kind === 'IpDues') && <span className="hint">As of now</span>}

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
            <span className="kpi-label">Pharmacy sales</span>
            <span className="kpi-value">{formatCell(summary.totalCollected, 'Money')}</span>
          </div>
          <div className="kpi">
            <span className="kpi-label">Cash</span>
            <span className="kpi-value">{formatCell(summary.cashTotal, 'Money')}</span>
          </div>
          <div className="kpi">
            <span className="kpi-label">UPI</span>
            <span className="kpi-value">{formatCell(summary.upiTotal, 'Money')}</span>
          </div>
          {/* Beside pharmacy sales, never inside them - a consultation fee is not a taxable
              supply of goods. */}
          <div className="kpi">
            <span className="kpi-label">Consultation fees</span>
            <span className="kpi-value">{formatCell(summary.consultationTotal, 'Money')}</span>
          </div>
          <div className="kpi">
            <span className="kpi-label">OPD visits</span>
            <span className="kpi-value">{summary.visitCount}</span>
          </div>
          {/* Medicines that went on IP bills: sold, but paid for with the IP bill, not here. */}
          {summary.onIpBills > 0 && (
            <div className="kpi">
              <span className="kpi-label">Pharmacy on IP bills</span>
              <span className="kpi-value">{formatCell(summary.onIpBills, 'Money')}</span>
            </div>
          )}
        </div>
      )}

      {/* The Diagnostics tab is three grids rather than one table, which is
          why it is not a ReportKind and has no export — the desktop makes
          the same call. */}
      {diagnostics && (
        <>
          <div className="kpi-row">
            <div className="kpi">
              <span className="kpi-label">Total</span>
              <span className="kpi-value">{formatCell(diagnostics.todayTotal, 'Number')}</span>
            </div>
          </div>

          <section className="card">
            <h2>Diagnostic bills</h2>
            <table>
              <thead><tr><th>TIME</th><th>BILL NO</th><th>PATIENT</th><th style={{ textAlign: 'right' }}>TESTS</th><th style={{ textAlign: 'right' }}>AMOUNT</th><th>STATUS</th><th></th></tr></thead>
              <tbody>
                {diagnostics.todaysBills.map((b) => (
                  <tr key={b.id}>
                    <td className="cell-muted">{timeOf(b.billDate)}</td>
                    <td className="cell-name">{b.billNo}</td>
                    <td>{b.patientName}</td>
                    <td style={{ textAlign: 'right' }}>{b.tests}</td>
                    <td className="cell-amount" style={{ textAlign: 'right' }}>{formatCell(b.finalAmount, 'Number')}</td>
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
                {diagnostics.todaysBills.length === 0 && <tr><td colSpan={7}>No records found for the selected dates.</td></tr>}
              </tbody>
            </table>
          </section>

          <div className="queue-columns">
            <section className="card">
              <h2>Revenue by day</h2>
              <table>
                <thead><tr><th>DATE</th><th style={{ textAlign: 'right' }}>BILLS</th><th style={{ textAlign: 'right' }}>REVENUE</th></tr></thead>
                <tbody>
                  {diagnostics.revenue.map((r) => (
                    <tr key={r.day}>
                      <td className="cell-muted">{dateOf(r.day)}</td>
                      <td style={{ textAlign: 'right' }}>{r.bills}</td>
                      <td className="cell-amount" style={{ textAlign: 'right' }}>{formatCell(r.amount, 'Number')}</td>
                    </tr>
                  ))}
                  {diagnostics.revenue.length === 0 && <tr><td colSpan={3}>Nothing in this range.</td></tr>}
                </tbody>
              </table>
            </section>

            <section className="card">
              <h2>Most ordered tests</h2>
              <table>
                <thead><tr><th>TEST</th><th style={{ textAlign: 'right' }}>TIMES ORDERED</th><th style={{ textAlign: 'right' }}>REVENUE</th></tr></thead>
                <tbody>
                  {diagnostics.topTests.map((t) => (
                    <tr key={t.test}>
                      <td className="cell-name">{t.test}</td>
                      <td style={{ textAlign: 'right' }}>{t.times}</td>
                      <td className="cell-amount" style={{ textAlign: 'right' }}>{formatCell(t.amount, 'Number')}</td>
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
                      <td
                        key={c.header}
                        className={TONE_CLASS[c.tone]}
                        style={{ textAlign: c.align === 'Right' ? 'right' : c.align === 'Center' ? 'center' : 'left' }}
                      >
                        {c.format === 'Check'
                          ? <input type="checkbox" checked={row.cells[j] === true} disabled aria-label={c.header} />
                          : formatCell(row.cells[j] ?? null, c.format)}
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
              <div className="report-totals">
                {table.totals.map((t) => (
                  <span key={t.label}>
                    <span className="report-totals-label">{t.label}: </span>
                    <strong>{formatCell(t.value, t.format)}</strong>
                  </span>
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
