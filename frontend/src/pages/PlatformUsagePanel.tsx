import { useCallback, useEffect, useMemo, useState } from 'react';
import { api, ApiError } from '../api/client';
import type { PlatformClinicUsage, PlatformUsage, UsageCount, UsageStatus } from '../api/types';

const WINDOWS = [7, 30, 90];

const STATUS_LABEL: Record<UsageStatus, string> = {
  Active: 'Active',
  SigningInOnly: 'Signing in only',
  Dormant: 'Dormant',
  NotStarted: 'Not started',
};

/** The order the list is read in when looking for clinics to call: the ones that never
 * started, then the ones that stopped, then the ones that only look around. */
const STATUS_RANK: Record<UsageStatus, number> = { NotStarted: 0, Dormant: 1, SigningInOnly: 2, Active: 3 };

type Sort = 'attention' | 'newest' | 'busiest';

/** The kinds of record counted, in the order a clinic usually starts using them. */
const KINDS: { key: keyof Pick<PlatformClinicUsage,
  'patients' | 'visits' | 'pharmacyBills' | 'diagnosticBills' | 'labOrders' | 'procedureBills' |
  'dentalCases' | 'vaccinations' | 'appointments'>; label: string }[] = [
  { key: 'patients', label: 'Patients' },
  { key: 'visits', label: 'OPD visits' },
  { key: 'pharmacyBills', label: 'Pharmacy bills' },
  { key: 'diagnosticBills', label: 'Diagnostics' },
  { key: 'labOrders', label: 'Lab orders' },
  { key: 'procedureBills', label: 'Procedures' },
  { key: 'dentalCases', label: 'Dental cases' },
  { key: 'vaccinations', label: 'Vaccinations' },
  { key: 'appointments', label: 'Appointments' },
];

const DAY = 24 * 60 * 60 * 1000;

function ago(iso: string | null): string {
  if (!iso) return 'Never';
  const days = Math.floor((Date.now() - new Date(iso).getTime()) / DAY);
  if (days <= 0) return 'Today';
  if (days === 1) return 'Yesterday';
  if (days < 60) return `${days} days ago`;
  return new Date(iso).toLocaleDateString();
}

function Count({ count }: { count: UsageCount }) {
  if (count.total === 0) return <span className="hint">—</span>;
  return (
    <>
      <strong>{count.total.toLocaleString('en-IN')}</strong>
      {count.recent > 0 && <div className="usage-recent">+{count.recent.toLocaleString('en-IN')}</div>}
    </>
  );
}

/**
 * Which clinics are using what they registered for.
 *
 * Counts and dates only — how many patients, visits and bills each clinic has entered,
 * how many of those inside the window, and when anyone last signed in. The endpoint
 * behind it groups in the database and returns no record, so nothing here can show
 * who a patient is.
 */
export function PlatformUsagePanel() {
  const [days, setDays] = useState(30);
  const [usage, setUsage] = useState<PlatformUsage | null>(null);
  const [filter, setFilter] = useState<UsageStatus | null>(null);
  const [sort, setSort] = useState<Sort>('attention');
  const [search, setSearch] = useState('');
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setError(null);
    try {
      setUsage(await api.get<PlatformUsage>(`/api/platform/usage?days=${days}`));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not load usage.');
    }
  }, [days]);

  useEffect(() => { void load(); }, [load]);

  const clinics = usage?.clinics ?? [];

  const counts = useMemo(() => {
    const by: Record<UsageStatus, number> = { Active: 0, SigningInOnly: 0, Dormant: 0, NotStarted: 0 };
    for (const c of clinics) by[c.status]++;
    return by;
  }, [clinics]);

  const totals = useMemo(() => ({
    patients: clinics.reduce((n, c) => n + c.patients.total, 0),
    newPatients: clinics.reduce((n, c) => n + c.patients.recent, 0),
    recordsRecent: clinics.reduce((n, c) => n + c.recordsRecent, 0),
    newClinics: clinics.filter((c) => usage && new Date(c.createdAt) >= new Date(usage.since)).length,
  }), [clinics, usage]);

  const visible = useMemo(() => {
    const term = search.trim().toLowerCase();
    const rows = clinics.filter((c) =>
      (!filter || c.status === filter) &&
      (!term || c.clinicName.toLowerCase().includes(term) || c.slug.toLowerCase().includes(term)));

    const time = (iso: string | null) => (iso ? new Date(iso).getTime() : 0);
    return [...rows].sort((a, b) => {
      if (sort === 'newest') return time(b.createdAt) - time(a.createdAt);
      if (sort === 'busiest') return b.recordsRecent - a.recordsRecent || b.recordsTotal - a.recordsTotal;
      return STATUS_RANK[a.status] - STATUS_RANK[b.status]
        || time(a.lastActivity) - time(b.lastActivity)
        || time(a.createdAt) - time(b.createdAt);
    });
  }, [clinics, filter, sort, search]);

  const card = (status: UsageStatus | null, label: string, value: number, note: string) => (
    <button
      type="button"
      className={`kpi${filter === status ? ' kpi-selected' : ''}`}
      onClick={() => setFilter(filter === status ? null : status)}
    >
      <span className="kpi-label">{label}</span>
      <span className="kpi-value">{value}</span>
      <span className="kpi-delta">{note}</span>
    </button>
  );

  return (
    <>
      <div className="inline-form">
        <label>Window</label>
        {WINDOWS.map((d) => (
          <button key={d} type="button" className={d === days ? 'tab active' : 'tab'} onClick={() => setDays(d)}>
            Last {d} days
          </button>
        ))}
        <input placeholder="Find a clinic" value={search} onChange={(e) => setSearch(e.target.value)} />
        <select value={sort} onChange={(e) => setSort(e.target.value as Sort)} aria-label="Sort">
          <option value="attention">Needs attention first</option>
          <option value="newest">Newest registration first</option>
          <option value="busiest">Busiest first</option>
        </select>
        <button type="button" className="ghost" onClick={() => void load()}>Refresh</button>
      </div>

      {error && <p className="auth-error">{error}</p>}

      <div className="kpi-row">
        {card(null, 'Registered clinics', clinics.length, `${totals.newClinics} new in the window`)}
        {card('Active', 'Active', counts.Active, 'entered records in the window')}
        {card('SigningInOnly', 'Signing in only', counts.SigningInOnly, 'signed in, entered nothing')}
        {card('Dormant', 'Dormant', counts.Dormant, 'used before, quiet now')}
        {card('NotStarted', 'Not started', counts.NotStarted, 'never entered a record')}
      </div>

      <div className="kpi-row">
        <div className="kpi">
          <span className="kpi-label">Patients registered</span>
          <span className="kpi-value">{totals.patients.toLocaleString('en-IN')}</span>
          <span className="kpi-delta">+{totals.newPatients.toLocaleString('en-IN')} in the window</span>
        </div>
        <div className="kpi">
          <span className="kpi-label">Records entered</span>
          <span className="kpi-value">{totals.recordsRecent.toLocaleString('en-IN')}</span>
          <span className="kpi-delta">in the last {days} days, all clinics</span>
        </div>
      </div>

      <section className="card">
        <table>
          <thead>
            <tr>
              <th>Clinic</th>
              <th>Status</th>
              <th>Registered</th>
              <th>Last record</th>
              <th>Last sign-in</th>
              <th style={{ textAlign: 'right' }}>Users</th>
              {KINDS.map((k) => <th key={k.key} style={{ textAlign: 'right' }}>{k.label}</th>)}
              <th>Licence</th>
            </tr>
          </thead>
          <tbody>
            {visible.map((c) => (
              <tr key={c.tenantId}>
                <td className="usage-clinic">
                  <strong>{c.clinicName}</strong>
                  <div className="hint"><code>{c.slug}</code>{!c.isActive && ' · disabled'}</div>
                </td>
                <td><span className={`badge usage-${c.status.toLowerCase()}`}>{STATUS_LABEL[c.status]}</span></td>
                <td>
                  {new Date(c.createdAt).toLocaleDateString()}
                  <div className="hint">{ago(c.createdAt)}</div>
                </td>
                <td>{ago(c.lastActivity)}</td>
                <td>{ago(c.lastSignIn)}</td>
                <td style={{ textAlign: 'right' }}>{c.users}</td>
                {KINDS.map((k) => (
                  <td key={k.key} style={{ textAlign: 'right' }}><Count count={c[k.key]} /></td>
                ))}
                <td>
                  <span className={`licence-${c.licenseStatus.replace(/\s+/g, '-').toLowerCase()}`}>{c.licenseStatus}</span>
                  {c.licenseExpiresOn && <div className="hint">{new Date(c.licenseExpiresOn).toLocaleDateString()}</div>}
                </td>
              </tr>
            ))}
            {visible.length === 0 && (
              <tr><td colSpan={7 + KINDS.length}>{usage ? 'No clinic matches.' : 'Loading…'}</td></tr>
            )}
          </tbody>
        </table>
        <p className="hint usage-note">
          Each figure is the total entered so far; <span className="usage-recent">+n</span> is how many of those
          arrived in the last {days} days. Counts only — no record is read or shown.
        </p>
      </section>
    </>
  );
}
