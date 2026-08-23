import { useCallback, useEffect, useState } from 'react';
import { api, ApiError, downloadFile, openPdf } from '../api/client';
import type {
  DayBookSummary,
  ReportFormat,
  ReportKind,
  ReportTable,
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
];

const EXPIRING_DAY_OPTIONS = [30, 60, 90, 180];

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
    return p.toString();
  }, [date, from, to, expiringDays, includeZeroStock, stockSearch]);

  const load = useCallback(async (t: Tab) => {
    setBusy(true);
    setError(null);
    try {
      const path = t.path ?? `/api/reports/${t.kind}?${query()}`;
      setTable(await api.get<ReportTable>(path));

      // The cards belong to the day book alone — they are not rows of any
      // other report.
      if (t.kind === 'DayBook') {
        setSummary(await api.get<DayBookSummary>(`/api/reports/day-book/summary?date=${date}`));
      } else {
        setSummary(null);
      }
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not load the report.');
      setTable(null);
    } finally {
      setBusy(false);
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

  /** Stock Register is Excel-only by design — a wide, analysis-oriented dump
   * rather than a printable statement. */
  const canExportPdf = tab.kind !== 'None' && tab.kind !== 'StockRegister' && (table?.rows.length ?? 0) > 0;
  const canExportExcel = tab.kind !== 'None' && (table?.rows.length ?? 0) > 0;

  const isRangeBased = tab.kind === 'GstSummary' || tab.kind === 'ScheduleH1';

  return (
    <div className="page">
      <div className="page-head">
        <div>
          <h1>Reports</h1>
          <p className="hint">{table ? `${table.title} · ${table.dateLabel}` : 'Loading…'}</p>
        </div>
        <div className="inline-form">
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
        {!isRangeBased && tab.kind !== 'StockRegister' && tab.kind !== 'LowStock' && tab.kind !== 'None' && (
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
                  <tr key={i} className={row.emphasise ? 'row-flagged' : undefined}>
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
    </div>
  );
}
