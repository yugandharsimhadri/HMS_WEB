import { useCallback, useEffect, useMemo, useState } from 'react';
import { api, ApiError } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import type { PlatformAdminAccount, PlatformClinic, ResetPasswordResponse } from '../api/types';

/** Empty string is what a cleared date input gives back, and it means "no
 * expiry" — distinct from a date that simply has not loaded yet. */
type DateDraft = Record<string, string>;

function isoDate(value: string | null): string {
  return value ? value.slice(0, 10) : '';
}

/**
 * The support console. Deliberately its own screen, outside the clinic
 * shell, with its own layout and no navigation into the application — there
 * is nothing here to navigate to.
 *
 * Two jobs: put a locked-out clinic owner back in, and move a licence date.
 * No patient, visit or bill appears anywhere on this page, and no endpoint
 * behind it can return one.
 */
export function PlatformConsolePage() {
  const { session, logout } = useAuth();
  const [clinics, setClinics] = useState<PlatformClinic[]>([]);
  const [search, setSearch] = useState('');
  const [drafts, setDrafts] = useState<DateDraft>({});
  const [issued, setIssued] = useState<ResetPasswordResponse | null>(null);
  const [status, setStatus] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    try {
      const found = await api.get<PlatformClinic[]>('/api/platform/clinics');
      setClinics(found);
      setDrafts(Object.fromEntries(found.map((c) => [c.tenantId, isoDate(c.licenseExpiresOn)])));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not load the clinic list.');
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  const visible = useMemo(() => {
    const term = search.trim().toLowerCase();
    if (!term) return clinics;
    return clinics.filter((c) =>
      c.clinicName.toLowerCase().includes(term) ||
      c.slug.includes(term) ||
      c.admins.some((a) => a.username.includes(term)));
  }, [clinics, search]);

  const resetPassword = async (clinic: PlatformClinic, admin: PlatformAdminAccount) => {
    // The one destructive thing on this page: it locks the real owner out of
    // their own clinic until they are told the new password. Worth a pause,
    // especially since the list shows several clinics at once and the rows
    // look alike.
    const ok = window.confirm(
      `Reset the password for ${admin.username} at ${clinic.clinicName}?\n\n` +
      'Their current password stops working immediately. You will be shown a ' +
      'temporary one to read back to them, once.');
    if (!ok) return;

    setError(null);
    setStatus('');
    setBusy(true);
    try {
      const result = await api.post<ResetPasswordResponse>(
        `/api/platform/clinics/${clinic.tenantId}/admins/${admin.userId}/reset-password`, {});
      setIssued(result);
      await load();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not reset that password.');
    } finally {
      setBusy(false);
    }
  };

  const saveLicense = async (clinic: PlatformClinic) => {
    setError(null);
    setIssued(null);
    setBusy(true);
    try {
      const draft = drafts[clinic.tenantId] ?? '';
      await api.put(`/api/platform/clinics/${clinic.tenantId}/license`, {
        expiresOn: draft === '' ? null : draft,
      });
      await load();
      setStatus(draft === ''
        ? `${clinic.clinicName} now has no licence expiry.`
        : `${clinic.clinicName}'s licence now runs to ${new Date(draft).toLocaleDateString()}.`);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not update that licence.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="platform-page">
      <header className="platform-head">
        <div>
          <h1>Sivayaan HMS · Platform support</h1>
          <p className="hint">
            {clinics.length} registered clinic(s). Signed in as {session?.username}. This console can reset a
            clinic admin's password and move a licence date — it cannot open a clinic or read its records.
          </p>
        </div>
        <div className="inline-form">
          <input
            placeholder="Find a clinic or admin"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
          <button type="button" className="ghost" onClick={() => void load()}>Refresh</button>
          <button type="button" className="ghost" onClick={logout}>Sign out</button>
        </div>
      </header>

      {error && <p className="auth-error">{error}</p>}
      {status && <p className="hint status-line">{status}</p>}

      {/* Shown once, and deliberately hard to miss — it cannot be recovered
          from anywhere afterwards, because it is hashed the moment it is
          issued. */}
      {issued && (
        <section className="card issued-password">
          <h2>Temporary password for {issued.username}</h2>
          <p className="issued-value">{issued.temporaryPassword}</p>
          <p className="hint">
            Read this back to them now — it is not stored anywhere and cannot be shown again. They will be
            asked to choose their own password as soon as they sign in. If it is lost, issue another.
          </p>
          <button type="button" className="ghost" onClick={() => setIssued(null)}>Done</button>
        </section>
      )}

      <section className="card">
        <table>
          <thead>
            <tr>
              <th>Clinic</th><th>Address</th><th>Registered</th>
              <th>Licence</th><th>Expires</th><th>Admins</th>
            </tr>
          </thead>
          <tbody>
            {visible.map((c) => (
              <tr key={c.tenantId}>
                <td>{c.clinicName}</td>
                <td><code>{c.slug}</code></td>
                <td>{new Date(c.createdAt).toLocaleDateString()}</td>
                <td>
                  <span className={`badge licence-${c.licenseStatus.replace(/\s+/g, '-').toLowerCase()}`}>
                    {c.licenseStatus}
                  </span>
                  {c.licenseStatus === 'Expiring' && (
                    <div className="hint">{c.daysRemaining} day(s) left</div>
                  )}
                  {c.licenseStatus === 'Expired' && (
                    <div className="hint">cannot sign in</div>
                  )}
                </td>
                <td>
                  <div className="inline-form">
                    <input
                      type="date"
                      value={drafts[c.tenantId] ?? ''}
                      onChange={(e) => setDrafts({ ...drafts, [c.tenantId]: e.target.value })}
                    />
                    <button
                      type="button"
                      className="ghost"
                      disabled={busy || (drafts[c.tenantId] ?? '') === isoDate(c.licenseExpiresOn)}
                      onClick={() => void saveLicense(c)}
                    >
                      Save
                    </button>
                  </div>
                  <p className="hint">Clear the date for no expiry.</p>
                </td>
                <td>
                  {c.admins.map((a) => (
                    <div key={a.userId} className="admin-row">
                      <div>
                        <strong>{a.username}</strong>
                        {!a.isActive && <span className="hint"> · disabled</span>}
                        <div className="hint">
                          {a.lastLoginOn
                            ? `Last signed in ${new Date(a.lastLoginOn).toLocaleString()}`
                            : 'Never signed in'}
                        </div>
                      </div>
                      <button
                        type="button"
                        className="ghost"
                        disabled={busy}
                        onClick={() => void resetPassword(c, a)}
                      >
                        Reset password
                      </button>
                    </div>
                  ))}
                  {c.admins.length === 0 && <span className="hint">No admin account.</span>}
                </td>
              </tr>
            ))}
            {visible.length === 0 && (
              <tr><td colSpan={6}>{clinics.length === 0 ? 'No clinics registered yet.' : 'No clinic matches.'}</td></tr>
            )}
          </tbody>
        </table>
      </section>
    </div>
  );
}
