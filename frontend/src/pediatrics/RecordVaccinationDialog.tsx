import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { api } from '../api/client';
import { nextBatchMrp, type Product, type VaccineMaster } from '../api/types';

/** What the form hands back: enough to both record the dose and charge for
 * it. The vaccine is the clinical "type" from Vaccine Master; everything
 * about which actual unit was given — brand, batch, price — comes from
 * Pharmacy stock, never retyped here. */
export interface VaccinationDraft {
  vaccine: VaccineMaster;
  givenOn: string;
  site: string | null;
  administeredBy: string | null;
  productId: string;
  productName: string;
  batchId: string;
  batchNo: string;
  price: number;
}

interface Props {
  vaccines: VaccineMaster[];
  /** Pre-selected when reached from a row on the immunization card, so
   * recording a due dose does not ask the desk to find it again. */
  preselectVaccineId?: string | null;
  onClose: () => void;
  onAdd: (draft: VaccinationDraft) => void;
}

const today = () => {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
};

/** The batch a dose would actually be drawn from — nearest expiry with
 * stock on hand, the same one the pharmacy counter would dispense. */
const nextBatch = (product: Product) =>
  (product.batches ?? [])
    .filter((b) => !b.isDeleted && b.qtyOnHand > 0)
    .sort((a, b) => a.expiryDate.localeCompare(b.expiryDate))[0];

export function RecordVaccinationDialog({ vaccines, preselectVaccineId, onClose, onAdd }: Props) {
  const [vaccineId, setVaccineId] = useState(preselectVaccineId ?? '');
  const [givenOn, setGivenOn] = useState(today);
  const [site, setSite] = useState('');
  const [administeredBy, setAdministeredBy] = useState('');

  const [search, setSearch] = useState('');
  const [matches, setMatches] = useState<Product[]>([]);
  const [product, setProduct] = useState<Product | null>(null);

  const [vaccineMissing, setVaccineMissing] = useState(false);
  const [productMissing, setProductMissing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const find = useCallback(async (term: string) => {
    if (!term.trim()) {
      setMatches([]);
      return;
    }
    try {
      const found = await api.get<Product[]>(`/api/pharmacy/products?term=${encodeURIComponent(term.trim())}&take=20`);
      // Only what is actually in stock: there is nothing to draw the dose
      // from otherwise.
      setMatches(found.filter((p) => nextBatch(p) !== undefined));
    } catch {
      setMatches([]);
    }
  }, []);

  useEffect(() => {
    if (product) return;
    const handle = setTimeout(() => void find(search), 250);
    return () => clearTimeout(handle);
  }, [search, find, product]);

  const batch = product ? nextBatch(product) : undefined;

  const submit = (e: FormEvent) => {
    e.preventDefault();
    setError(null);

    const vaccine = vaccines.find((v) => v.id === vaccineId);
    if (!vaccine) {
      setVaccineMissing(true);
      setError('Pick a vaccine.');
      return;
    }
    if (!product || !batch) {
      setProductMissing(true);
      setError('Pick the brand given, from Pharmacy stock.');
      return;
    }

    onAdd({
      vaccine,
      givenOn,
      site: site.trim() || null,
      administeredBy: administeredBy.trim() || null,
      productId: product.id,
      productName: product.name,
      batchId: batch.id,
      batchNo: batch.batchNo,
      // The nearest-expiry batch's real unit price — the same figure
      // Pharmacy itself would quote for this product right now.
      price: nextBatchMrp(product),
    });
  };

  return (
    <div className="overlay" role="dialog" aria-modal="true" aria-label="Record vaccination">
      <div className="overlay-card wide">
        <div className="overlay-head">
          <h2>Record vaccination</h2>
          <button type="button" onClick={onClose}>Close</button>
        </div>

        <form className="overlay-body settings-form" onSubmit={submit}>
          {error && <p className="auth-error">{error}</p>}

          <div className="settings-row">
            <label>Vaccine</label>
            <select
              value={vaccineId}
              className={vaccineMissing ? 'field-missing' : undefined}
              onChange={(e) => { setVaccineId(e.target.value); if (e.target.value) setVaccineMissing(false); }}
            >
              <option value="">Vaccine…</option>
              {vaccines.map((v) => (
                <option key={v.id} value={v.id}>{v.name} — dose {v.doseNumber}</option>
              ))}
            </select>
          </div>

          <div className="settings-row">
            <label>Brand given</label>
            {product ? (
              <div className="counter-selected">
                <strong>{product.name}</strong>
                {product.manufacturer && <span className="hint"> · {product.manufacturer}</span>}
                {batch && (
                  <div className="hint">
                    Batch {batch.batchNo} · exp{' '}
                    {new Date(batch.expiryDate).toLocaleDateString(undefined, { month: '2-digit', year: '2-digit' })}
                    {' · '}₹{nextBatchMrp(product).toFixed(2)} per unit
                  </div>
                )}
                <button
                  type="button"
                  className="ghost"
                  onClick={() => { setProduct(null); setSearch(''); setMatches([]); }}
                >
                  Change brand
                </button>
              </div>
            ) : (
              <div className="patient-picker">
                <input
                  placeholder="Search pharmacy stock"
                  value={search}
                  className={productMissing ? 'field-missing' : undefined}
                  onChange={(e) => { setSearch(e.target.value); setProductMissing(false); }}
                />
                {matches.length > 0 && (
                  <ul className="pick-list">
                    {matches.map((p) => (
                      <li key={p.id}>
                        <button type="button" onClick={() => { setProduct(p); setMatches([]); setProductMissing(false); }}>
                          {p.name}
                          {p.manufacturer && ` · ${p.manufacturer}`}
                          {' · ₹'}{nextBatchMrp(p).toFixed(2)}
                        </button>
                      </li>
                    ))}
                  </ul>
                )}
                <p className="hint">
                  The brand, batch and price come from Pharmacy stock — one place stock and pricing live.
                </p>
              </div>
            )}
          </div>

          <div className="settings-row">
            <label>Given on</label>
            <input type="date" value={givenOn} onChange={(e) => setGivenOn(e.target.value)} />
          </div>
          <div className="settings-row">
            <label>Site of injection</label>
            <input value={site} onChange={(e) => setSite(e.target.value)} />
          </div>
          <div className="settings-row">
            <label>Administered by</label>
            <input value={administeredBy} onChange={(e) => setAdministeredBy(e.target.value)} />
          </div>

          <div className="overlay-actions">
            <button type="submit">Add to bill</button>
            <button type="button" className="ghost" onClick={onClose}>Cancel</button>
          </div>

          {/* Said out loud, because it is the whole design: there is no
              "charge or not" choice, and nothing is on record until the
              bill saves. */}
          <p className="hint">
            Every dose goes on the bill. It is recorded only once the bill is saved.
          </p>
        </form>
      </div>
    </div>
  );
}
