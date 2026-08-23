import { useCallback, useEffect, useState } from 'react';
import { api, ApiError } from '../api/client';
import type { HealthFinding, RepairResult } from '../api/types';

/**
 * Medicines whose records cannot be right.
 *
 * Worth more than its size suggests: a medicine whose pack size says "15
 * TAB" while units-per-pack says 1 sells a whole strip to anyone asking for
 * one tablet, at fifteen times the price, and nothing anywhere reports an
 * error. Inventory already warns about that condition — this is the fix it
 * could not offer.
 *
 * Findings are selected rather than repaired wholesale, because a repair
 * that moves a stock count is a different decision from one that only sets a
 * missing label, and the two arrive mixed together.
 */
export function DataHealthTab() {
  const [findings, setFindings] = useState<HealthFinding[] | null>(null);
  const [chosen, setChosen] = useState<Set<string>>(new Set());
  const [status, setStatus] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const scan = useCallback(async () => {
    setBusy(true);
    setError(null);
    try {
      const found = await api.get<HealthFinding[]>('/api/data-health/scan');
      setFindings(found);
      // Pre-ticks only what is safe and unambiguous. A count-changing repair
      // is left for a person to choose deliberately.
      setChosen(new Set(found.filter((f) => f.canRepairAutomatically && !f.changesStock)
        .map((f) => f.productId)));
    } catch (err) {
      setError(err instanceof ApiError && err.status === 403
        ? 'Only an Admin can run a data health check.'
        : 'Could not scan.');
    } finally {
      setBusy(false);
    }
  }, []);

  useEffect(() => { void scan(); }, [scan]);

  const toggle = (id: string) => {
    const next = new Set(chosen);
    if (next.has(id)) next.delete(id); else next.add(id);
    setChosen(next);
  };

  const repair = async () => {
    const moving = (findings ?? []).filter((f) => chosen.has(f.productId) && f.changesStock);

    if (moving.length > 0 && !window.confirm(
      `${moving.length} of these re-count stock on the shelf.\n\n` +
      'Each one writes a stock adjustment you can see on Inventory, so nothing is silent — but the ' +
      'counts really do change. Go ahead?')) return;

    setBusy(true);
    setError(null);
    try {
      const result = await api.post<RepairResult>('/api/data-health/repair', {
        productIds: [...chosen],
      });
      await scan();
      setStatus(result.repaired === 0
        ? 'Nothing was repaired — those findings need a person to decide.'
        : `${result.repaired} repaired. ${result.remainingFindings} finding(s) left.`);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not repair.');
      setBusy(false);
    } finally {
      setBusy(false);
    }
  };

  const repairable = (findings ?? []).filter((f) => f.canRepairAutomatically);
  const manual = (findings ?? []).filter((f) => !f.canRepairAutomatically);

  return (
    <section className="settings-form">
      <p className="hint">
        Checks every medicine for records that cannot be right — a pack size that disagrees with
        units-per-pack, batches received at a different pack size, a missing dispensing unit.
      </p>

      {error && <p className="auth-error">{error}</p>}
      {status && <p className="hint status-line">{status}</p>}

      <div className="inline-form">
        <button type="button" className="ghost" disabled={busy} onClick={() => void scan()}>
          {busy ? 'Scanning…' : 'Scan again'}
        </button>
        <button
          type="button"
          className="primary"
          disabled={busy || chosen.size === 0}
          onClick={() => void repair()}
        >
          Repair {chosen.size > 0 ? `${chosen.size} selected` : 'selected'}
        </button>
      </div>

      {findings === null ? (
        <p className="hint">Scanning…</p>
      ) : findings.length === 0 ? (
        <p className="hint">Nothing wrong found. Every medicine's pack size, units and stock agree.</p>
      ) : (
        <>
          {repairable.length > 0 && (
            <>
              <h3 className="sub-heading">Can be repaired here</h3>
              <table>
                <thead>
                  <tr>
                    <th></th><th>Medicine</th><th>Problem</th>
                    <th>Now</th><th>Would become</th><th>Stock</th>
                  </tr>
                </thead>
                <tbody>
                  {repairable.map((f) => (
                    <tr key={f.productId}>
                      <td>
                        <label className="checkbox-label">
                          <input
                            type="checkbox"
                            checked={chosen.has(f.productId)}
                            onChange={() => toggle(f.productId)}
                          />
                        </label>
                      </td>
                      <td>
                        {f.productName}
                        <div className="hint">{f.explanation}</div>
                      </td>
                      <td>{f.problemLabel}</td>
                      <td>{f.current}</td>
                      <td>{f.proposed}</td>
                      <td>
                        {f.changesStock
                          ? <span className="badge unpaid">{f.quantityBefore} → {f.quantityAfter}</span>
                          : <span className="hint">unchanged</span>}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </>
          )}

          {manual.length > 0 && (
            <>
              <h3 className="sub-heading">Needs you to decide</h3>
              <p className="hint">
                Two records that look like the same medicine. Which one to keep is a judgement about your
                own catalogue, so nothing here is repaired automatically — merge them on Medicines.
              </p>
              <table>
                <thead><tr><th>Medicine</th><th>Problem</th><th>Detail</th></tr></thead>
                <tbody>
                  {manual.map((f) => (
                    <tr key={f.productId}>
                      <td>{f.productName}</td>
                      <td>{f.problemLabel}</td>
                      <td>{f.explanation}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </>
          )}
        </>
      )}
    </section>
  );
}
