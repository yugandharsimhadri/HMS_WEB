import { useEffect, useRef, useState } from 'react';
import { API_URL, ApiError, api, getToken } from '../api/client';
import type { ImportPreview, ImportProfile, ImportResult } from '../api/types';

interface Props {
  onClose: () => void;
  onImported: (message: string) => void;
}

/**
 * Loading a supplier's bill straight into stock.
 *
 * Two steps and no way to skip the first: nothing is written until the
 * preview has been read and confirmed. The file is uploaded twice — once to
 * preview, once to commit — because the server re-parses rather than trust a
 * preview posted back to it. A bill is kilobytes; the honesty is worth more
 * than the round trip.
 */
export function ImportBillDialog({ onClose, onImported }: Props) {
  const [profiles, setProfiles] = useState<ImportProfile[]>([]);
  const [profileId, setProfileId] = useState('');
  const [file, setFile] = useState<File | null>(null);
  const [preview, setPreview] = useState<ImportPreview | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const fileRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    void api.get<ImportProfile[]>('/api/import/profiles')
      .then((p) => { setProfiles(p); if (p.length === 1) setProfileId(p[0].id); })
      .catch(() => setError('Could not load the supplier formats.'));
  }, []);

  /** Multipart, so `api` (which sends JSON) is not the right tool — but the
   *  bearer token still has to go, same as everywhere else. */
  const send = async <T,>(path: string): Promise<T> => {
    const body = new FormData();
    body.append('profileId', profileId);
    body.append('file', file!);

    const headers = new Headers();
    const token = getToken();
    if (token) headers.set('Authorization', `Bearer ${token}`);

    const response = await fetch(`${API_URL}${path}`, { method: 'POST', headers, body });
    const text = await response.text();

    if (!response.ok) throw new ApiError(text || response.statusText, response.status);
    return JSON.parse(text) as T;
  };

  const runPreview = async () => {
    if (!file || !profileId) {
      setError('Choose the supplier and their file.');
      return;
    }
    setError(null);
    setBusy(true);
    try {
      setPreview(await send<ImportPreview>('/api/import/preview'));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not read that file.');
    } finally {
      setBusy(false);
    }
  };

  const commit = async () => {
    setError(null);
    setBusy(true);
    try {
      const result = await send<ImportResult>('/api/import/commit');
      onImported(
        `${result.entryNo}: ${result.lines} line(s), ${result.unitsAdded} unit(s) added` +
        (result.productsCreated > 0 ? `, ${result.productsCreated} new medicine(s) created.` : '.'));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not import that bill.');
      setBusy(false);
    }
  };

  return (
    <div className="overlay" role="dialog" aria-modal="true" aria-label="Import a purchase bill">
      <div className="overlay-card wide">
        <div className="overlay-head">
          <h2>Import a purchase bill</h2>
          <button type="button" onClick={onClose}>Close</button>
        </div>

        <div className="overlay-body settings-form">
          {error && <p className="auth-error">{error}</p>}

          <div className="settings-row">
            <label>Supplier format</label>
            <select
              value={profileId}
              onChange={(e) => { setProfileId(e.target.value); setPreview(null); }}
            >
              <option value="">Choose…</option>
              {profiles.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
            </select>
            <p className="hint">How this supplier lays their file out. The wrong one reads as gibberish.</p>
          </div>

          <div className="settings-row">
            <label>Their file</label>
            <input
              ref={fileRef}
              type="file"
              accept=".csv,text/csv"
              onChange={(e) => { setFile(e.target.files?.[0] ?? null); setPreview(null); }}
            />
          </div>

          {!preview && (
            <div className="overlay-actions">
              <button type="button" className="primary" disabled={busy} onClick={() => void runPreview()}>
                {busy ? 'Reading…' : 'Check the file'}
              </button>
              <button type="button" className="ghost" onClick={onClose}>Cancel</button>
            </div>
          )}

          {preview && (
            <>
              <h3 className="sub-heading">
                Bill {preview.billNo} · {new Date(preview.billDate).toLocaleDateString()}
                {preview.supplierName ? ` · ${preview.supplierName}` : ''}
              </h3>

              <p className="hint">
                {preview.lines.length} line(s) · {preview.totalUnits} unit(s) ·
                net {preview.netAmount.toFixed(2)}
                {preview.newMedicines > 0 && ` · ${preview.newMedicines} new medicine(s) would be created`}
                {preview.needsChecking > 0 && ` · ${preview.needsChecking} need checking`}
              </p>

              {preview.alreadyImported && (
                <p className="hint warn">
                  This bill has been imported before. Importing it again would add its stock twice.
                </p>
              )}

              {preview.blockedReason && <p className="auth-error">{preview.blockedReason}</p>}

              {preview.issues.length > 0 && (
                <table>
                  <thead><tr><th>Severity</th><th>Line</th><th>Field</th><th>Issue</th></tr></thead>
                  <tbody>
                    {preview.issues.map((i, n) => (
                      <tr key={n}>
                        <td>
                          <span className={i.severity === 'Error' ? 'badge unpaid' : 'badge'}>{i.severity}</span>
                        </td>
                        <td>{i.line > 0 ? i.line : '—'}</td>
                        <td>{i.field}</td>
                        <td>{i.message}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}

              <h3 className="sub-heading">What would be received</h3>
              <table>
                <thead>
                  <tr>
                    <th>Medicine</th><th>Pack</th><th>Batch</th><th>Expiry</th>
                    <th>Qty</th><th>Free</th><th>Units in</th><th>MRP</th><th>Status</th>
                  </tr>
                </thead>
                <tbody>
                  {preview.lines.map((l) => (
                    <tr key={l.sourceLine}>
                      <td>{l.productName}</td>
                      <td>{l.packSize ?? '—'}</td>
                      <td>{l.batchNo}</td>
                      <td>{new Date(l.expiry).toLocaleDateString(undefined, { month: '2-digit', year: 'numeric' })}</td>
                      <td>{l.quantity}</td>
                      <td>{l.freeQuantity || ''}</td>
                      <td>
                        {l.unitsReceived}
                        {/* Nobody told us the pack size, so this count is a
                            guess — and a wrong guess prices singles as packs. */}
                        {l.unitsAssumed && <div className="hint warn">pack size assumed</div>}
                      </td>
                      <td>{l.mrp.toFixed(2)}</td>
                      <td>{l.status}</td>
                    </tr>
                  ))}
                </tbody>
              </table>

              <div className="overlay-actions">
                <button
                  type="button"
                  className="primary"
                  disabled={busy || !preview.canImport}
                  onClick={() => void commit()}
                >
                  {busy ? 'Importing…' : 'Import into stock'}
                </button>
                <button type="button" className="ghost" onClick={() => setPreview(null)}>
                  Choose another file
                </button>
                <button type="button" className="ghost" onClick={onClose}>Cancel</button>
              </div>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
