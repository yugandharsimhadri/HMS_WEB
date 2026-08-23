import { useCallback, useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import type { DashboardResponse } from '../api/types';
import { useHotkey } from '../shell/hotkeys';
import { ShortcutHints } from '../shell/ShortcutHints';

const money = (n: number) => `₹${n.toFixed(2)}`;

/** "▲ 8%" or "▼ 5%" — the direction reads from the glyph, not from colour
 * alone, which is decorative reinforcement here rather than the signal. */
const delta = (percent: number) => `${percent >= 0 ? '▲' : '▼'} ${Math.abs(Math.round(percent))}%`;

const CATEGORY_COLORS = {
  opd: 'var(--accent, #0f766e)',
  pharmacy: 'var(--accent-2, #b45309)',
  diagnostics: 'var(--accent-3, #6d28d9)',
} as const;

/**
 * One donut segment as an SVG path. The desktop builds this with
 * `ChartGeometry.DonutSegment` into a WPF `PathGeometry`; the maths is the
 * same, the output is an SVG `d` string.
 */
function donutSegment(
  cx: number, cy: number, rOuter: number, rInner: number,
  startDeg: number, endDeg: number,
): string {
  const rad = (deg: number) => (deg * Math.PI) / 180;
  const large = endDeg - startDeg > 180 ? 1 : 0;

  const x1 = cx + rOuter * Math.cos(rad(startDeg));
  const y1 = cy + rOuter * Math.sin(rad(startDeg));
  const x2 = cx + rOuter * Math.cos(rad(endDeg));
  const y2 = cy + rOuter * Math.sin(rad(endDeg));
  const x3 = cx + rInner * Math.cos(rad(endDeg));
  const y3 = cy + rInner * Math.sin(rad(endDeg));
  const x4 = cx + rInner * Math.cos(rad(startDeg));
  const y4 = cy + rInner * Math.sin(rad(startDeg));

  return [
    `M ${x1.toFixed(2)} ${y1.toFixed(2)}`,
    `A ${rOuter} ${rOuter} 0 ${large} 1 ${x2.toFixed(2)} ${y2.toFixed(2)}`,
    `L ${x3.toFixed(2)} ${y3.toFixed(2)}`,
    `A ${rInner} ${rInner} 0 ${large} 0 ${x4.toFixed(2)} ${y4.toFixed(2)}`,
    'Z',
  ].join(' ');
}

export function DashboardPage() {
  const navigate = useNavigate();
  const [data, setData] = useState<DashboardResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useHotkey('f9', 'Refresh the figures', 'Dashboard', () => { if (!busy) void load(); });

  const load = useCallback(async () => {
    setBusy(true);
    try {
      setData(await api.get<DashboardResponse>('/api/dashboard'));
      setError(null);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not load the dashboard.');
    } finally {
      setBusy(false);
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  /** Segments, gaps and percentages — computed once so the donut and its
   * legend can never disagree. */
  const donut = useMemo(() => {
    if (!data) return null;

    const parts: { key: keyof typeof CATEGORY_COLORS; label: string; value: number }[] = [
      { key: 'opd', label: 'OPD', value: data.opdRevenueToday },
      { key: 'pharmacy', label: 'Pharmacy', value: data.pharmacyRevenueToday },
    ];
    if (data.diagnosticsEnabled) {
      parts.push({ key: 'diagnostics', label: 'Diagnostics', value: data.diagnosticsRevenueToday });
    }

    const total = parts.reduce((s, p) => s + p.value, 0);
    if (total <= 0) return { total: 0, segments: [] as { key: string; label: string; d: string; pct: number; value: number }[] };

    const cx = 62, cy = 62, rOuter = 58, rInner = 36, gapDeg = 2.2;
    let angle = -90;

    const segments = parts
      .filter((p) => p.value > 0)
      .map((p) => {
        const sweep = (p.value / total) * 360;
        const start = angle;
        const end = angle + sweep - gapDeg;
        angle += sweep;
        return {
          key: p.key,
          label: p.label,
          value: p.value,
          d: donutSegment(cx, cy, rOuter, rInner, start, Math.max(end, start + 0.5)),
          pct: Math.round((p.value / total) * 100),
        };
      });

    return { total, segments };
  }, [data]);

  /**
   * All three lines on **one shared scale**. Each stretched to its own range
   * would make a tiny category look exactly as busy as the whole clinic —
   * precisely what a "which department is moving" chart must not do.
   */
  const trend = useMemo(() => {
    if (!data || data.trend.length === 0) return null;

    const w = 100, h = 100;
    const series = {
      opd: data.trend.map((d) => d.opd),
      pharmacy: data.trend.map((d) => d.pharmacy),
      diagnostics: data.trend.map((d) => d.diagnostics),
    };

    const sharedMax = Math.max(
      ...series.opd, ...series.pharmacy, ...series.diagnostics, 0,
    );

    const line = (values: number[]) => {
      if (values.length < 2) return '';
      return values
        .map((v, i) => {
          const x = (i / (values.length - 1)) * w;
          const y = sharedMax <= 0 ? h : h - (v / sharedMax) * h;
          return `${x.toFixed(2)},${y.toFixed(2)}`;
        })
        .join(' ');
    };

    const endPoint = (values: number[]) => {
      const v = values[values.length - 1] ?? 0;
      return { x: w, y: sharedMax <= 0 ? h : h - (v / sharedMax) * h };
    };

    return {
      sharedMax,
      opd: { line: line(series.opd), end: endPoint(series.opd) },
      pharmacy: { line: line(series.pharmacy), end: endPoint(series.pharmacy) },
      diagnostics: { line: line(series.diagnostics), end: endPoint(series.diagnostics) },
    };
  }, [data]);

  if (error) return <div className="page"><p className="auth-error">{error}</p></div>;
  if (!data) return <div className="page"><p className="hint">Loading…</p></div>;

  const today = new Date().toLocaleDateString(undefined, {
    weekday: 'long', day: 'numeric', month: 'long', year: 'numeric',
  });

  return (
    <div className="page">
      <div className="page-head">
        <div>
          <h1>Dashboard</h1>
          <p className="hint">{today}</p>
        </div>
        <div className="inline-form">
          <button type="button" disabled={busy} onClick={() => void load()}>Refresh <span className="kbd">F9</span></button>
        </div>
      </div>

      {/* Nothing here is a destination in its own right — every tile has a
          real screen behind it for the detail. */}
      <div className="kpi-row">
        <button type="button" className="kpi" onClick={() => navigate('/')}>
          <span className="kpi-label">Patients today</span>
          <span className="kpi-value">{data.patientsToday}</span>
          <span className={data.patientsDeltaPercent >= 0 ? 'kpi-delta up' : 'kpi-delta down'}>
            {delta(data.patientsDeltaPercent)} vs yesterday
          </span>
        </button>

        <button type="button" className="kpi" onClick={() => navigate('/')}>
          <span className="kpi-label">In queue now</span>
          <span className="kpi-value">{data.inQueueNow}</span>
          <span className="kpi-delta">waiting or in consultation</span>
        </button>

        <div className="kpi">
          <span className="kpi-label">Revenue today</span>
          <span className="kpi-value">{money(data.revenueToday)}</span>
          <span className={data.revenueDeltaPercent >= 0 ? 'kpi-delta up' : 'kpi-delta down'}>
            {delta(data.revenueDeltaPercent)} vs yesterday
          </span>
        </div>

        <button type="button" className="kpi" onClick={() => navigate('/inventory')}>
          <span className="kpi-label">Needs restocking</span>
          <span className="kpi-value">{data.lowStockCount}</span>
          <span className="kpi-delta">at or below reorder level</span>
        </button>
      </div>

      <div className="queue-columns">
        <section className="card">
          <h2>Today&rsquo;s revenue</h2>
          {donut && donut.total > 0 ? (
            <div className="donut-row">
              <svg viewBox="0 0 124 124" width="150" height="150" role="img" aria-label="Revenue split by department">
                {donut.segments.map((s) => (
                  <path key={s.key} d={s.d} fill={CATEGORY_COLORS[s.key as keyof typeof CATEGORY_COLORS]} />
                ))}
              </svg>
              <ul className="donut-legend">
                {donut.segments.map((s) => (
                  <li key={s.key}>
                    <span className="swatch" style={{ background: CATEGORY_COLORS[s.key as keyof typeof CATEGORY_COLORS] }} />
                    {s.label} — {money(s.value)} <span className="hint">({s.pct}%)</span>
                  </li>
                ))}
              </ul>
            </div>
          ) : (
            <p className="hint">Nothing taken yet today.</p>
          )}
        </section>

        <section className="card">
          <h2>
            Last 14 days
            <span className="hint"> · {money(data.revenueTrendTotal)} total</span>
          </h2>
          {trend && trend.sharedMax > 0 ? (
            <>
              <svg viewBox="0 0 104 104" width="100%" height="160" preserveAspectRatio="none"
                   role="img" aria-label="Revenue trend by department over 14 days">
                <polyline points={trend.opd.line} fill="none" stroke={CATEGORY_COLORS.opd} strokeWidth={2} vectorEffect="non-scaling-stroke" />
                <circle cx={trend.opd.end.x} cy={trend.opd.end.y} r={2} fill={CATEGORY_COLORS.opd} />
                <polyline points={trend.pharmacy.line} fill="none" stroke={CATEGORY_COLORS.pharmacy} strokeWidth={2} vectorEffect="non-scaling-stroke" />
                <circle cx={trend.pharmacy.end.x} cy={trend.pharmacy.end.y} r={2} fill={CATEGORY_COLORS.pharmacy} />
                {data.diagnosticsEnabled && (
                  <>
                    <polyline points={trend.diagnostics.line} fill="none" stroke={CATEGORY_COLORS.diagnostics} strokeWidth={2} vectorEffect="non-scaling-stroke" />
                    <circle cx={trend.diagnostics.end.x} cy={trend.diagnostics.end.y} r={2} fill={CATEGORY_COLORS.diagnostics} />
                  </>
                )}
              </svg>
              <p className="hint">
                One shared scale across all three, so they are honestly comparable. Peak day {money(trend.sharedMax)}.
              </p>
            </>
          ) : (
            <p className="hint">No revenue in the last 14 days.</p>
          )}
        </section>
      </div>

      <div className="queue-columns">
        <section className="card">
          <h2>Recent activity</h2>
          <table>
            <thead><tr><th>Time</th><th>No.</th><th>Who</th><th>Where</th><th>Amount</th></tr></thead>
            <tbody>
              {data.recentActivity.map((r, i) => (
                <tr key={`${r.billNo}-${i}`}>
                  <td>{new Date(r.when).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}</td>
                  <td>{r.billNo || '—'}</td>
                  <td>{r.patientName}</td>
                  <td><span className="badge">{r.department}</span></td>
                  <td>{money(r.amount)}</td>
                </tr>
              ))}
              {data.recentActivity.length === 0 && <tr><td colSpan={5}>Nothing yet today.</td></tr>}
            </tbody>
          </table>
        </section>

        <section className="card">
          <h2>
            Needs restocking
            {data.lowStockCount > data.lowStock.length && (
              <span className="hint"> · showing {data.lowStock.length} of {data.lowStockCount}</span>
            )}
          </h2>
          <table>
            <thead><tr><th>Medicine</th><th>On hand</th><th>Reorder at</th></tr></thead>
            <tbody>
              {data.lowStock.map((p) => (
                <tr key={p.productId}>
                  <td>{p.name}</td>
                  <td>{p.stockOnHand}</td>
                  <td>{p.reorderLevel}</td>
                </tr>
              ))}
              {data.lowStock.length === 0 && <tr><td colSpan={3}>Nothing below its reorder level.</td></tr>}
            </tbody>
          </table>
        </section>
      </div>

      <ShortcutHints keys={[['f9', 'refresh'], ['mod+k', 'search anything']]} />
    </div>
  );
}
