import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react';
import { api, ApiError } from '../api/client';
import type { Batch, Product } from '../api/types';
import { unitsFromPacking, unitWordFor } from '../pharmacy/packing';
import { ReceiveStockDialog } from '../pharmacy/ReceiveStockDialog';
import { CorrectStockDialog } from '../pharmacy/CorrectStockDialog';

interface StockAdjustment {
  id: string;
  productName: string;
  batchNo: string;
  quantityBefore: number;
  quantityAfter: number;
  reason: string;
  notes: string | null;
  adjustedOn: string;
  adjustedBy: string | null;
}

/**
 * Stock: what is on the shelf, and the two things that can be done to it.
 *
 * Split from the catalogue because they are different jobs done by different
 * people at different times — the catalogue is set up once, stock moves every
 * delivery.
 */
export function InventoryPage() {
  const [search, setSearch] = useState('');
  const [products, setProducts] = useState<Product[]>([]);
  const [selected, setSelected] = useState<Product | null>(null);
  const [batches, setBatches] = useState<Batch[]>([]);
  const [adjustments, setAdjustments] = useState<StockAdjustment[]>([]);

  const [receiving, setReceiving] = useState(false);
  const [correcting, setCorrecting] = useState(false);
  const [status, setStatus] = useState('');
  const [error, setError] = useState<string | null>(null);

  const find = useCallback(async (term: string, keepId?: string) => {
    try {
      const found = await api.get<Product[]>(`/api/pharmacy/products?term=${encodeURIComponent(term)}&take=200`);
      setProducts(found);
      if (keepId) setSelected(found.find((p) => p.id === keepId) ?? null);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not load stock.');
    }
  }, []);

  const loadBatches = useCallback(async (productId: string) => {
    setBatches(await api.get<Batch[]>(`/api/pharmacy/products/${productId}/batches`));
  }, []);

  const loadAdjustments = useCallback(async () => {
    try {
      setAdjustments(await api.get<StockAdjustment[]>('/api/pharmacy/adjustments?take=100'));
    } catch { /* the trail is informative, not load-bearing */ }
  }, []);

  useEffect(() => {
    void find('');
    void loadAdjustments();
  }, [find, loadAdjustments]);

  useEffect(() => {
    if (selected) void loadBatches(selected.id);
    else setBatches([]);
  }, [selected, loadBatches]);

  /**
   * Says so when a medicine's pack size and its units-per-pack disagree.
   * That combination sells whole strips to anyone asking for tablets and
   * reports no error, so it has to be visible where stock is handled.
   */
  const packWarning = useMemo(() => {
    if (!selected) return '';

    const stated = unitsFromPacking(selected.packSize);
    const perPack = Math.max(1, selected.unitsPerPack);

    if (stated !== null && stated !== perPack) {
      return `⚠ The pack size says ${stated} per pack but this medicine is set to ${perPack}. ` +
        `The counter will sell whole packs to anyone asking for ${unitWordFor(selected.dispensingUnit, 2)}. ` +
        `Fix it on the Medicines screen — set Units in one pack to ${stated} and save, and the stock ` +
        `already on the shelf is re-counted with it.`;
    }

    // The medicine may be right while stock received earlier is not.
    const stale = batches.filter((b) => b.unitsPerPack !== perPack);
    if (stale.length > 0) {
      return `⚠ ${stale.length} batch(es) here were received at a different pack size. ` +
        `Open this medicine on the Medicines screen and save it to re-count them.`;
    }

    return '';
  }, [selected, batches]);

  const onSearch = (e: FormEvent) => {
    e.preventDefault();
    void find(search, selected?.id);
  };

  return (
    <div className="page wide">
      <div className="page-head">
        <div>
          <h1>Inventory</h1>
          <p className="hint">
            {selected
              ? `${selected.name} · ${selected.stockOnHand} ${unitWordFor(selected.dispensingUnit, selected.stockOnHand)} on hand`
              : `${products.length} medicine(s) · pick one to receive or correct stock`}
          </p>
        </div>
        <form className="inline-form" onSubmit={onSearch}>
          <input placeholder="Find a medicine" value={search} onChange={(e) => setSearch(e.target.value)} />
          <button type="submit" className="ghost">Search</button>
          <button
            type="button"
            disabled={!selected}
            onClick={() => (selected ? setReceiving(true) : setStatus('Choose the medicine you are receiving.'))}
          >
            Receive stock
          </button>
          <button
            type="button"
            className="ghost"
            disabled={!selected}
            onClick={() => {
              if (!selected) return;
              // No count to put right, and an empty batch list only invites a
              // correction against whatever else was selected.
              if (batches.length === 0) {
                setStatus(`${selected.name} has no stock on the shelf to correct. Receive some first.`);
                return;
              }
              setCorrecting(true);
            }}
          >
            Correct count
          </button>
        </form>
      </div>

      {error && <p className="auth-error">{error}</p>}
      {packWarning && <p className="hint warn">{packWarning}</p>}
      {status && <p className="hint status-line">{status}</p>}

      <div className="queue-columns">
        <section className="card">
          <h2>Medicines</h2>
          <table>
            <thead>
              <tr><th>Name</th><th>Maker</th><th>Pack</th><th>Per pack</th><th>On hand</th><th>Rack</th></tr>
            </thead>
            <tbody>
              {products.map((p) => (
                <tr
                  key={p.id}
                  onClick={() => setSelected(p)}
                  className={selected?.id === p.id ? 'selected-row' : undefined}
                >
                  <td>{p.name}</td>
                  <td>{p.manufacturer ?? ''}</td>
                  <td>{p.packSize ?? ''}</td>
                  <td>{p.unitsPerPack}</td>
                  <td>{p.stockOnHand}</td>
                  <td>{p.rackLocation ?? ''}</td>
                </tr>
              ))}
              {products.length === 0 && <tr><td colSpan={6}>No medicines match.</td></tr>}
            </tbody>
          </table>
        </section>

        <section className="card">
          <h2>Batches {selected && <span className="hint">· {selected.name}</span>}</h2>
          {!selected ? (
            <p className="hint">Pick a medicine to see what is on the shelf.</p>
          ) : (
            <table>
              <thead><tr><th>Batch</th><th>Expiry</th><th>MRP</th><th>Per pack</th><th>On hand</th></tr></thead>
              <tbody>
                {batches.map((b) => (
                  <tr key={b.id}>
                    <td>
                      {b.batchNo}
                      {b.isProvisional && <div className="hint">provisional</div>}
                    </td>
                    <td>{new Date(b.expiryDate).toLocaleDateString(undefined, { month: '2-digit', year: 'numeric' })}</td>
                    <td>{b.mrp.toFixed(2)}</td>
                    <td>{b.unitsPerPack}</td>
                    <td>{b.qtyOnHand}</td>
                  </tr>
                ))}
                {batches.length === 0 && <tr><td colSpan={5}>Nothing on the shelf.</td></tr>}
              </tbody>
            </table>
          )}
        </section>
      </div>

      <section className="card">
        <h2>Correction trail</h2>
        <p className="hint">
          Stock otherwise only moves by receiving or selling, and both leave a document.
          A correction has none, so it writes one.
        </p>
        <table>
          <thead>
            <tr>
              <th>When</th><th>Medicine</th><th>Batch</th>
              <th>Was</th><th>Now</th><th>Change</th><th>Reason</th><th>Notes</th>
            </tr>
          </thead>
          <tbody>
            {adjustments.map((a) => (
              <tr key={a.id}>
                <td>{new Date(a.adjustedOn).toLocaleString()}</td>
                <td>{a.productName}</td>
                <td>{a.batchNo}</td>
                <td>{a.quantityBefore}</td>
                <td>{a.quantityAfter}</td>
                {/* The delta, signed — how big the correction was is the
                    thing worth seeing at a glance, not the two endpoints. */}
                <td>{a.quantityAfter - a.quantityBefore > 0 ? '+' : ''}{a.quantityAfter - a.quantityBefore}</td>
                <td>{a.reason}</td>
                <td>{a.notes ?? ''}</td>
              </tr>
            ))}
            {adjustments.length === 0 && <tr><td colSpan={8}>No corrections recorded.</td></tr>}
          </tbody>
        </table>
      </section>

      {receiving && selected && (
        <ReceiveStockDialog
          product={selected}
          onClose={() => setReceiving(false)}
          onReceived={async (message) => {
            setReceiving(false);
            // Everything clears, the selection included. Receiving twice
            // against a medicine still sitting selected is how one delivery
            // becomes two.
            setSelected(null);
            setSearch('');
            await find('');
            setStatus(`${message} The screen is clear for the next line.`);
          }}
        />
      )}

      {correcting && selected && (
        <CorrectStockDialog
          product={selected}
          batches={batches}
          onClose={() => setCorrecting(false)}
          onCorrected={async (message) => {
            setCorrecting(false);
            await loadBatches(selected.id);
            await loadAdjustments();
            await find(search, selected.id);
            setStatus(message);
          }}
        />
      )}
    </div>
  );
}
