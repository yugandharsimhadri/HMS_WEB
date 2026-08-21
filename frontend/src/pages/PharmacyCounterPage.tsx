import { useState, type FormEvent } from 'react';
import { api, ApiError } from '../api/client';
import type { Batch, CartLine, Product, Sale } from '../api/types';

interface Allocation {
  batch: Batch;
  units: number;
}

export function PharmacyCounterPage() {
  const [term, setTerm] = useState('');
  const [results, setResults] = useState<Product[]>([]);
  const [selected, setSelected] = useState<Product | null>(null);
  const [batches, setBatches] = useState<Batch[]>([]);
  const [units, setUnits] = useState('');

  const [stockPacks, setStockPacks] = useState('');
  const [stockMrp, setStockMrp] = useState('');

  const [cart, setCart] = useState<CartLine[]>([]);
  const [customerName, setCustomerName] = useState('Guest');
  const [savedSale, setSavedSale] = useState<Sale | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const search = async (e: FormEvent) => {
    e.preventDefault();
    setResults(await api.get<Product[]>(`/api/pharmacy/products?term=${encodeURIComponent(term)}`));
  };

  const pick = async (product: Product) => {
    setSelected(product);
    setResults([]);
    setSavedSale(null);
    setBatches(await api.get<Batch[]>(`/api/pharmacy/products/${product.id}/batches`));
  };

  const addStock = async (e: FormEvent) => {
    e.preventDefault();
    if (!selected) return;
    setError(null);
    try {
      await api.post(`/api/pharmacy/products/${selected.id}/quick-add-stock`, {
        packs: Number(stockPacks),
        mrp: Number(stockMrp),
        batchNo: null,
        expiry: null,
        purchaseRate: 0,
      });
      setStockPacks('');
      setStockMrp('');
      setBatches(await api.get<Batch[]>(`/api/pharmacy/products/${selected.id}/batches`));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not add stock.');
    }
  };

  const addToCart = async (e: FormEvent) => {
    e.preventDefault();
    if (!selected) return;
    const wanted = Number(units);
    if (!wanted || wanted <= 0) return;

    setError(null);
    try {
      const result = await api.post<{ allocations: Allocation[]; shortfall: number }>('/api/pharmacy/allocate', {
        productId: selected.id,
        units: wanted,
      });

      if (result.shortfall > 0) {
        setError(`Only enough stock for ${wanted - result.shortfall} of ${wanted} unit(s).`);
      }

      const lines: CartLine[] = result.allocations.map((a) => ({
        productId: selected.id,
        batchId: a.batch.id,
        productName: selected.name,
        batchNo: a.batch.batchNo,
        expiryDate: a.batch.expiryDate,
        quantity: a.units,
        unitsPerPack: a.batch.unitsPerPack,
        packLabel: selected.packSize,
        mrp: a.batch.mrp,
        discountPercent: 0,
        gstRate: selected.gstRate,
        schedule: selected.schedule,
      }));

      setCart((prev) => [...prev, ...lines]);
      setUnits('');
      setSelected(null);
      setBatches([]);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not allocate stock.');
    }
  };

  const removeLine = (index: number) => setCart((prev) => prev.filter((_, i) => i !== index));

  const estimatedTotal = cart.reduce((sum, l) => sum + l.quantity * l.mrp, 0);

  const saveSale = async () => {
    if (cart.length === 0) return;
    setSaving(true);
    setError(null);
    try {
      const sale = await api.post<Sale>('/api/pharmacy/sales', {
        sale: { customerName, paymentMode: 'Cash', isTaxInvoice: true },
        lines: cart,
      });
      setSavedSale(sale);
      setCart([]);
      setCustomerName('Guest');
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not save the bill.');
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="page">
      <h1>Pharmacy Counter</h1>

      {savedSale && (
        <section className="card success">
          <h2>Bill {savedSale.billNo} saved</h2>
          <p>
            Net amount: <strong>₹{savedSale.netAmount.toFixed(2)}</strong> ({savedSale.paymentMode})
          </p>
        </section>
      )}

      <section className="card">
        <h2>Find a medicine</h2>
        <form className="inline-form" onSubmit={search}>
          <input placeholder="Medicine name" value={term} onChange={(e) => setTerm(e.target.value)} />
          <button type="submit">Search</button>
        </form>

        {results.length > 0 && (
          <ul className="picker-results static">
            {results.map((p) => (
              <li key={p.id}>
                <button type="button" onClick={() => pick(p)}>
                  {p.name} {p.packSize ? `(${p.packSize})` : ''} — stock {p.stockOnHand}
                </button>
              </li>
            ))}
          </ul>
        )}

        {selected && (
          <div className="counter-selected">
            <h3>{selected.name}</h3>

            {batches.length === 0 ? (
              <>
                <p className="hint">No stock on the shelf. Add some to sell it.</p>
                <form className="inline-form" onSubmit={addStock}>
                  <input
                    placeholder="Packs"
                    type="number"
                    min="1"
                    value={stockPacks}
                    onChange={(e) => setStockPacks(e.target.value)}
                    required
                  />
                  <input
                    placeholder="MRP per pack"
                    type="number"
                    min="0.01"
                    step="0.01"
                    value={stockMrp}
                    onChange={(e) => setStockMrp(e.target.value)}
                    required
                  />
                  <button type="submit">Add stock</button>
                </form>
              </>
            ) : (
              <form className="inline-form" onSubmit={addToCart}>
                <span className="hint">{batches.reduce((s, b) => s + b.qtyOnHand, 0)} unit(s) on hand</span>
                <input
                  placeholder="Units"
                  type="number"
                  min="1"
                  value={units}
                  onChange={(e) => setUnits(e.target.value)}
                  required
                />
                <button type="submit">Add to bill</button>
              </form>
            )}
          </div>
        )}

        {error && <p className="auth-error">{error}</p>}
      </section>

      <section className="card">
        <h2>Bill</h2>
        <table>
          <thead>
            <tr>
              <th>Medicine</th>
              <th>Batch</th>
              <th>Expiry</th>
              <th>Qty</th>
              <th>MRP</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {cart.map((line, i) => (
              <tr key={`${line.batchId}-${i}`}>
                <td>{line.productName}</td>
                <td>{line.batchNo}</td>
                <td>{new Date(line.expiryDate).toLocaleDateString()}</td>
                <td>{line.quantity}</td>
                <td>{line.mrp.toFixed(2)}</td>
                <td>
                  <button type="button" className="danger" onClick={() => removeLine(i)}>
                    Remove
                  </button>
                </td>
              </tr>
            ))}
            {cart.length === 0 && (
              <tr>
                <td colSpan={6}>Nothing on the bill yet.</td>
              </tr>
            )}
          </tbody>
        </table>

        {cart.length > 0 && (
          <div className="bill-footer">
            <p className="hint">Estimate before GST/rounding: ₹{estimatedTotal.toFixed(2)}</p>
            <div className="inline-form">
              <input
                placeholder="Customer name"
                value={customerName}
                onChange={(e) => setCustomerName(e.target.value)}
              />
              <button type="button" onClick={saveSale} disabled={saving}>
                {saving ? 'Saving…' : 'Save bill'}
              </button>
            </div>
          </div>
        )}
      </section>
    </div>
  );
}
