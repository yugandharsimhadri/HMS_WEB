import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { api, ApiError } from '../api/client';
import type { Product } from '../api/types';
import { MedicineEditorDialog } from '../pharmacy/MedicineEditorDialog';

/**
 * The medicine catalogue — what a medicine is, not how much of it there is.
 *
 * Stock lives on Inventory. Setting a medicine up happens once; receiving and
 * correcting stock happens every delivery, often by someone else.
 */
export function MedicinesPage() {
  const [search, setSearch] = useState('');
  const [products, setProducts] = useState<Product[]>([]);
  const [editing, setEditing] = useState<Product | null | undefined>(undefined); // undefined = closed
  const [status, setStatus] = useState('');
  const [error, setError] = useState<string | null>(null);

  const find = useCallback(async (term: string) => {
    try {
      setProducts(await api.get<Product[]>(`/api/pharmacy/products?term=${encodeURIComponent(term)}&take=200`));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not load the catalogue.');
    }
  }, []);

  useEffect(() => { void find(''); }, [find]);

  const onSearch = (e: FormEvent) => {
    e.preventDefault();
    void find(search);
  };

  return (
    <div className="page wide">
      <div className="page-head">
        <div>
          <h1>Medicines</h1>
          <p className="hint">{products.length} medicine(s) in the catalogue</p>
        </div>
        <div className="inline-form">
          <form className="inline-form" onSubmit={onSearch}>
            <input placeholder="Name, maker or rack" value={search} onChange={(e) => setSearch(e.target.value)} />
            <button type="submit" className="ghost">Search</button>
          </form>
          <button type="button" onClick={() => setEditing(null)}>+ New medicine</button>
        </div>
      </div>

      {error && <p className="auth-error">{error}</p>}
      {status && <p className="hint status-line">{status}</p>}

      <section className="card">
        <table>
          <thead>
            <tr>
              <th>Name</th><th>Maker</th><th>Pack</th><th>Rack</th><th>Per pack</th>
              <th>GST</th><th>Schedule</th><th>Stock</th><th></th>
            </tr>
          </thead>
          <tbody>
            {products.map((p) => (
              <tr key={p.id}>
                <td>
                  {p.name}
                  {!p.isActive && <span className="hint"> · inactive</span>}
                  {p.genericName && <div className="hint">{p.genericName}</div>}
                </td>
                <td>{p.manufacturer ?? ''}</td>
                <td>{p.packSize ?? ''}</td>
                {/* Where it physically is. The desktop carries it here
                    because finding the box is half of dispensing. */}
                <td>{p.rackLocation ?? ''}</td>
                <td>
                  {p.unitsPerPack}
                  {!p.allowLooseSale && p.unitsPerPack > 1 && <div className="hint">whole packs only</div>}
                </td>
                <td>{p.gstRate}%</td>
                <td>{p.schedule === 'None' ? '' : p.schedule}</td>
                <td>{p.stockOnHand}</td>
                <td>
                  <button type="button" className="ghost" onClick={() => setEditing(p)}>Edit</button>
                </td>
              </tr>
            ))}
            {products.length === 0 && <tr><td colSpan={8}>No medicines match.</td></tr>}
          </tbody>
        </table>
      </section>

      {editing !== undefined && (
        <MedicineEditorDialog
          existing={editing}
          onClose={() => setEditing(undefined)}
          onSaved={async (message) => {
            setEditing(undefined);
            await find(search);
            setStatus(message);
          }}
        />
      )}
    </div>
  );
}
