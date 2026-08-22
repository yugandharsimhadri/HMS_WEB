import { useMemo, useState, type FormEvent } from 'react';
import { api, ApiError } from '../api/client';
import type { Product } from '../api/types';
import { unitWordFor } from './packing';

interface Props {
  product: Product;
  onClose: () => void;
  onReceived: (message: string) => void;
}

/**
 * One delivery line going onto the shelf.
 *
 * The medicine is chosen on the page behind, so this asks only what a
 * delivery note actually says: which batch, when it expires, how many packs,
 * what was paid and what is printed on them.
 */
export function ReceiveStockDialog({ product, onClose, onReceived }: Props) {
  const [batchNo, setBatchNo] = useState('');
  const [expiryDate, setExpiryDate] = useState(() => {
    const d = new Date();
    d.setFullYear(d.getFullYear() + 2);
    return d.toISOString().slice(0, 10);
  });
  const [packs, setPacks] = useState('');
  const [freePacks, setFreePacks] = useState('');
  const [purchaseRate, setPurchaseRate] = useState('');
  const [mrp, setMrp] = useState('');
  const [supplierName, setSupplierName] = useState('');
  const [supplierInvoiceNo, setSupplierInvoiceNo] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const perPack = Math.max(1, product.unitsPerPack);

  // Spells out packs in, units out. "Qty" alone is the single most misread
  // field in a pharmacy: the shop counts strips, the counter sells tablets.
  const intake = useMemo(() => {
    const total = (Number(packs) || 0) + (Number(freePacks) || 0);
    if (total <= 0) return '';
    const units = total * perPack;
    const word = unitWordFor(product.dispensingUnit, units);
    return perPack > 1
      ? `${total} pack(s) × ${perPack} = ${units} ${word} onto the shelf`
      : `${units} ${word} onto the shelf`;
  }, [packs, freePacks, perPack, product.dispensingUnit]);

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    setBusy(true);
    try {
      await api.post('/api/pharmacy/receive-stock', {
        productId: product.id,
        batchNo: batchNo.trim(),
        expiryDate,
        packs: Number(packs) || 0,
        freePacks: Number(freePacks) || 0,
        purchaseRate: Number(purchaseRate) || 0,
        mrp: Number(mrp) || 0,
        supplierName: supplierName.trim() || null,
        supplierInvoiceNo: supplierInvoiceNo.trim() || null,
      });

      const units = ((Number(packs) || 0) + (Number(freePacks) || 0)) * perPack;
      onReceived(
        `${units} ${unitWordFor(product.dispensingUnit, units)} of ${product.name} added to batch ${batchNo.trim()}.`,
      );
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not receive the stock.');
      setBusy(false);
    }
  };

  return (
    <div className="overlay" role="dialog" aria-modal="true" aria-label="Receive stock">
      <div className="overlay-card wide">
        <div className="overlay-head">
          <h2>Receive stock — {product.name}</h2>
          <button type="button" className="ghost" onClick={onClose}>Close</button>
        </div>

        <form className="overlay-body" onSubmit={onSubmit}>
          {/* What is there now, so the number about to be added has context. */}
          <p className="hint">
            {product.stockOnHand} {unitWordFor(product.dispensingUnit, product.stockOnHand)} on hand ·{' '}
            {perPack > 1
              ? `one pack is ${perPack} ${unitWordFor(product.dispensingUnit, 2)}`
              : `sold as a single ${unitWordFor(product.dispensingUnit, 1)}`}
          </p>

          <div className="settings-row">
            <label>
              Batch no.
              <input value={batchNo} onChange={(e) => setBatchNo(e.target.value)} required autoFocus />
            </label>
            <label>
              Expiry
              <input type="date" value={expiryDate} onChange={(e) => setExpiryDate(e.target.value)} required />
            </label>
          </div>

          <div className="settings-row">
            <label>Packs received<input type="number" min="0" value={packs} onChange={(e) => setPacks(e.target.value)} /></label>
            <label>Free packs<input type="number" min="0" value={freePacks} onChange={(e) => setFreePacks(e.target.value)} /></label>
            <label>Rate paid per pack<input type="number" min="0" step="0.01" value={purchaseRate} onChange={(e) => setPurchaseRate(e.target.value)} /></label>
            <label>MRP per pack<input type="number" min="0" step="0.01" value={mrp} onChange={(e) => setMrp(e.target.value)} required /></label>
          </div>

          <div className="settings-row">
            <label>Supplier<input value={supplierName} onChange={(e) => setSupplierName(e.target.value)} /></label>
            <label>Supplier invoice no.<input value={supplierInvoiceNo} onChange={(e) => setSupplierInvoiceNo(e.target.value)} /></label>
          </div>

          {intake && <p className="hint">{intake}</p>}
          <p className="hint">
            Receiving the same batch again adds to what is on the shelf — it never replaces it.
          </p>

          {error && <p className="auth-error">{error}</p>}

          <div className="overlay-actions">
            <button type="submit" disabled={busy}>{busy ? 'Receiving…' : 'Receive'}</button>
            <button type="button" className="ghost" onClick={onClose}>Cancel</button>
          </div>
        </form>
      </div>
    </div>
  );
}
