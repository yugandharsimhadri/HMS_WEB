import { useState, type FormEvent } from 'react';
import type { SaleRow } from '../pages/PharmacyCounterPage';
import { describePacks } from '../clinical/doseMath';
import { lineAmounts } from '../clinical/gst';

interface Props {
  row: SaleRow;
  onClose: () => void;
  onConfirm: (quantity: number) => void;
}

/**
 * Changing the quantity on a bill line — a small popup rather than typing
 * straight into the grid cell, so the operator sees the new amount before it
 * takes effect rather than after.
 *
 * What happens to stock once confirmed (re-taking it, possibly spanning a
 * second batch, or giving some back) is the counter's job, unchanged — this
 * only decides the number.
 */
export function EditQuantityDialog({ row, onClose, onConfirm }: Props) {
  const [quantity, setQuantity] = useState(row.quantity);

  const packs = quantity > 0 ? describePacks(quantity, row.unitsPerPack, row.packLabel, row.unitName) : '';
  // The same arithmetic the bill line uses, recomputed live — so the price
  // shown here is exactly what the bill will carry, not an estimate.
  const amount = quantity > 0
    ? lineAmounts(row.mrp, row.unitsPerPack, quantity, row.discountPercent, row.gstRate).net
    : 0;

  const onSubmit = (e: FormEvent) => {
    e.preventDefault();
    if (quantity > 0) onConfirm(quantity);
  };

  return (
    <div className="overlay" role="dialog" aria-modal="true" aria-label="Change quantity">
      <div className="overlay-card">
        <div className="overlay-head">
          <h2>{row.productName}</h2>
          <button type="button" className="ghost" onClick={onClose}>Close</button>
        </div>

        <form className="overlay-body" onSubmit={onSubmit}>
          <p className="hint">Batch {row.batchNo} · {row.available} available in this batch</p>

          <label>
            Quantity
            <input
              type="number"
              min="1"
              value={quantity}
              onChange={(e) => setQuantity(Number(e.target.value) || 0)}
              autoFocus
            />
          </label>

          {quantity > 0 && (
            <p className="hint">{packs} · ₹{amount.toFixed(2)}</p>
          )}

          <p className="hint">
            Raising this past what the batch holds takes the rest from the next batch,
            nearest expiry first.
          </p>

          <div className="overlay-actions">
            <button type="submit" disabled={quantity <= 0}>Confirm</button>
            <button type="button" className="ghost" onClick={onClose}>Cancel</button>
          </div>
        </form>
      </div>
    </div>
  );
}
