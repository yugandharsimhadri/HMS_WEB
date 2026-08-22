import { useEffect, useState, type FormEvent } from 'react';
import { api, ApiError } from '../api/client';
import type { Batch, Product } from '../api/types';

interface Props {
  product: Product;
  batches: Batch[];
  onClose: () => void;
  onCorrected: (message: string) => void;
}

interface StockAdjustment {
  productName: string;
  batchNo: string;
  quantityBefore: number;
  quantityAfter: number;
  reason: string;
}

/** Exactly the values of Core's AdjustmentReason — the names are the API
 * contract, not labels, so they are spelled the way the enum spells them
 * and given readable text separately. */
const REASONS: { value: string; label: string }[] = [
  { value: 'Recount', label: 'Recount — physically counted, system was wrong' },
  { value: 'Breakage', label: 'Breakage' },
  { value: 'Expired', label: 'Expired' },
  { value: 'Lost', label: 'Lost' },
  { value: 'EntryError', label: 'Entry error — keyed in wrongly when received' },
  { value: 'Other', label: 'Other' },
];

/**
 * Putting a shelf count right.
 *
 * Stock otherwise only moves by receiving or selling, and both leave a
 * document. A correction has none, so it writes one — otherwise a shortfall
 * is indistinguishable from theft and nobody can answer what happened. That
 * is why the reason is asked for rather than assumed.
 */
export function CorrectStockDialog({ product, batches, onClose, onCorrected }: Props) {
  // One batch is the common case, and making them pick it from a list of one
  // is a click that teaches nothing.
  const [batchId, setBatchId] = useState(batches.length === 1 ? batches[0].id : '');
  const [corrected, setCorrected] = useState('');
  const [reason, setReason] = useState('Recount');
  const [notes, setNotes] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  // Starts at what the system currently believes, so the operator changes a
  // number rather than typing one from nothing — and a correction to zero is
  // then a deliberate act, not an empty box submitted by accident.
  useEffect(() => {
    const batch = batches.find((b) => b.id === batchId);
    setCorrected(batch ? String(batch.qtyOnHand) : '');
  }, [batchId, batches]);

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);

    if (!batchId) { setError('Choose the batch whose count is wrong.'); return; }

    setBusy(true);
    try {
      const adjustment = await api.post<StockAdjustment>('/api/pharmacy/adjust-stock', {
        batchId,
        correctedQuantity: Number(corrected) || 0,
        reason,
        notes: notes.trim() || null,
      });

      onCorrected(
        `${adjustment.productName} batch ${adjustment.batchNo}: ` +
        `${adjustment.quantityBefore} → ${adjustment.quantityAfter} (${adjustment.reason}).`,
      );
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not correct the count.');
      setBusy(false);
    }
  };

  return (
    <div className="overlay" role="dialog" aria-modal="true" aria-label="Correct the count">
      <div className="overlay-card">
        <div className="overlay-head">
          <h2>Correct the count — {product.name}</h2>
          <button type="button" className="ghost" onClick={onClose}>Close</button>
        </div>

        <form className="overlay-body" onSubmit={onSubmit}>
          <label>
            Batch
            <select value={batchId} onChange={(e) => setBatchId(e.target.value)} required>
              <option value="">Choose…</option>
              {batches.map((b) => (
                <option key={b.id} value={b.id}>
                  {b.batchNo} · exp {new Date(b.expiryDate).toLocaleDateString(undefined, { month: '2-digit', year: 'numeric' })} · {b.qtyOnHand} on hand
                </option>
              ))}
            </select>
          </label>

          <label>
            Corrected quantity
            <input type="number" min="0" value={corrected} onChange={(e) => setCorrected(e.target.value)} required />
          </label>

          <label>
            Reason
            <select value={reason} onChange={(e) => setReason(e.target.value)}>
              {REASONS.map((r) => <option key={r.value} value={r.value}>{r.label}</option>)}
            </select>
          </label>

          <label>
            Notes
            <input value={notes} onChange={(e) => setNotes(e.target.value)} placeholder="What happened" />
          </label>

          <p className="hint">
            This is recorded in the correction trail with the reason — a shortfall with no
            document is indistinguishable from theft.
          </p>

          {error && <p className="auth-error">{error}</p>}

          <div className="overlay-actions">
            <button type="submit" disabled={busy}>{busy ? 'Correcting…' : 'Correct'}</button>
            <button type="button" className="ghost" onClick={onClose}>Cancel</button>
          </div>
        </form>
      </div>
    </div>
  );
}
