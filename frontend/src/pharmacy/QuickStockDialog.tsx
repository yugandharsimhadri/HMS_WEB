import { useMemo, useState, type FormEvent } from 'react';
import { api, ApiError } from '../api/client';
import type { Batch, Product } from '../api/types';
import { lastReceivedMrp } from '../api/types';
import { useModalBehaviour } from '../shell/useModalBehaviour';

interface Props {
  product: Product;
  onClose: () => void;
  onAdded: (message: string) => void;
}

/**
 * Putting stock on the shelf from the counter, for a medicine that is
 * physically there but not in the system.
 *
 * The supplier's file is not always usable and chasing a proper goods-inward
 * entry mid-queue is not realistic, so this asks for the least it can: how
 * many packs, and the MRP. The entry is marked provisional so purchases and
 * sales can be reconciled later rather than never.
 */
export function QuickStockDialog({ product, onClose, onAdded }: Props) {
  const [packs, setPacks] = useState('');
  // The last price this medicine was received at is nearly always right.
  const [mrp, setMrp] = useState(() => {
    const last = lastReceivedMrp(product);
    return last ? String(last) : '';
  });
  const [purchaseRate, setPurchaseRate] = useState('');
  const [batchNo, setBatchNo] = useState('');
  const [expiry, setExpiry] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const perPack = Math.max(1, product.unitsPerPack);

  // Spells out packs in, units out. "Qty" alone is the single most misread
  // field in a pharmacy: the shop counts strips, the counter sells tablets.
  const preview = useMemo(() => {
    const n = Number(packs);
    if (!n || n <= 0) return '';
    const units = n * perPack;
    return perPack > 1
      ? `${n} pack(s) × ${perPack} = ${units} onto the shelf`
      : `${units} onto the shelf`;
  }, [packs, perPack]);

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    setBusy(true);
    try {
      const batch = await api.post<Batch>(`/api/pharmacy/products/${product.id}/quick-add-stock`, {
        packs: Number(packs),
        mrp: Number(mrp),
        batchNo: batchNo.trim() || null,
        expiry: expiry || null,
        purchaseRate: Number(purchaseRate) || 0,
      });

      onAdded(
        `${batch.qtyOnHand} on the shelf as batch ${batch.batchNo}. ` +
        `It stays listed as provisional until the supplier bill is reconciled against it.`,
      );
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not add the stock.');
      setBusy(false);
    }
  };

  const cardRef = useModalBehaviour(onClose);

  return (
    <div className="overlay" role="dialog" aria-modal="true" aria-label="Quick stock">
      <div className="overlay-card" ref={cardRef}>
        <div className="overlay-head">
          <h2>Add stock — {product.name}</h2>
          <button type="button" className="ghost" onClick={onClose}>Close</button>
        </div>

        <form className="overlay-body" onSubmit={onSubmit}>
          <p className="hint">
            {product.stockOnHand} on hand ·{' '}
            {perPack > 1 ? `one pack is ${perPack}` : 'sold as a single unit'}
          </p>

          <div className="settings-row">
            <label>
              Packs
              <input type="number" min="1" value={packs} onChange={(e) => setPacks(e.target.value)} required autoFocus />
            </label>
            <label>
              MRP per pack
              <input type="number" min="0.01" step="0.01" value={mrp} onChange={(e) => setMrp(e.target.value)} required />
            </label>
            <label>
              Rate paid (optional)
              <input type="number" min="0" step="0.01" value={purchaseRate} onChange={(e) => setPurchaseRate(e.target.value)} />
            </label>
          </div>

          <div className="settings-row">
            <label>
              Batch no. (optional)
              <input value={batchNo} onChange={(e) => setBatchNo(e.target.value)} placeholder="auto if blank" />
            </label>
            <label>
              Expiry (optional)
              <input type="date" value={expiry} onChange={(e) => setExpiry(e.target.value)} />
            </label>
          </div>

          {preview && <p className="hint">{preview}</p>}
          <p className="hint">
            Anything left blank is filled in for you and the entry is marked provisional —
            reconcile it against the supplier bill when that arrives.
          </p>

          {error && <p className="auth-error">{error}</p>}

          <div className="overlay-actions">
            <button type="submit" disabled={busy}>{busy ? 'Adding…' : 'Add stock'}</button>
            <button type="button" className="ghost" onClick={onClose}>Cancel</button>
          </div>
        </form>
      </div>
    </div>
  );
}
