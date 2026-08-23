import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react';
import { api, ApiError, openPdf } from '../api/client';
import type { Batch, PaymentMode, PharmacyProfile, Product, Sale, Visit } from '../api/types';
import { nextBatchMrp } from '../api/types';
import { billAmounts, lineAmounts, unitPrice } from '../clinical/gst';
import { describePacks } from '../clinical/doseMath';
import { unitWordFor } from '../pharmacy/packing';
import { QuickStockDialog } from '../pharmacy/QuickStockDialog';
import { EditQuantityDialog } from '../pharmacy/EditQuantityDialog';
import { describeCombo, useHotkey } from '../shell/hotkeys';
import { ShortcutHints } from '../shell/ShortcutHints';

interface Allocation {
  batch: Batch;
  units: number;
}

/** One line on the bill being built at the counter. */
export interface SaleRow {
  productId: string;
  batchId: string;
  productName: string;
  batchNo: string;
  expiryDate: string;
  hsnCode: string;
  gstRate: number;
  schedule: string;
  available: number;
  unitsPerPack: number;
  packLabel: string | null;
  unitName: string;
  quantity: number;
  mrp: number;
  discountPercent: number;
}

/** What one of a medicine is called, so "9 loose" reads "9 tablets". */
const unitWord = (product: Product, count: number) => unitWordFor(product.dispensingUnit, count);

/** "strips of 15" for tablets, "boxes" for sachets — the desktop's PackWord. */
function packWord(product: Product): string {
  switch (product.dispensingUnit) {
    case 'Tablet': case 'Capsule': return 'strips';
    case 'Sachet': return 'boxes';
    default: return 'packs';
  }
}

const daysToExpiry = (iso: string) =>
  Math.floor((new Date(iso).getTime() - Date.now()) / 86_400_000);

export function PharmacyCounterPage() {
  const [search, setSearch] = useState('');
  const [matches, setMatches] = useState<Product[]>([]);
  const [selected, setSelected] = useState<Product | null>(null);

  const [quantity, setQuantity] = useState(1);
  const [quantityUnit, setQuantityUnit] = useState('');
  // What was last chosen for each medicine, so the second sale of the day
  // does not have to be told again.
  const rememberedUnit = useRef<Record<string, string>>({});

  const [lines, setLines] = useState<SaleRow[]>([]);
  const [customerName, setCustomerName] = useState('Guest');
  const [doctorName, setDoctorName] = useState('');
  const [paymentMode, setPaymentMode] = useState<PaymentMode>('Cash');
  const [transactionNo, setTransactionNo] = useState('');

  const [prescribedVisits, setPrescribedVisits] = useState<Visit[]>([]);
  const [selectedVisitId, setSelectedVisitId] = useState('');

  const [gstRegistered, setGstRegistered] = useState(false);
  const [status, setStatus] = useState('');
  const [warning, setWarning] = useState('');
  const [loadWarning, setLoadWarning] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [savedSale, setSavedSale] = useState<Sale | null>(null);

  const [quickStockFor, setQuickStockFor] = useState<Product | null>(null);
  const [editingLine, setEditingLine] = useState<number | null>(null);

  // The counter's keyboard. The search box is where the hands live, so it
  // gets a ref: every shortcut that ends an action returns focus here rather
  // than leaving the operator to reach for the mouse to start the next line.
  const searchRef = useRef<HTMLInputElement>(null);

  // Which match the arrow keys are sitting on. -1 is "none yet", which is
  // what a fresh search should be: pressing Enter then adds nothing by
  // accident.
  const [highlight, setHighlight] = useState(-1);

  useEffect(() => {
    void api.get<PharmacyProfile>('/api/settings/pharmacy')
      .then((p) => setGstRegistered(p.gstRegistered))
      .catch(() => {});
    void api.get<Visit[]>('/api/visits')
      .then((vs) => setPrescribedVisits(vs.filter((v) => v.status === 'Completed' || v.status === 'InConsultation')))
      .catch(() => {});
  }, []);

  // Filters as the operator types — no button to press.
  useEffect(() => {
    let cancelled = false;
    void (async () => {
      const found = await api.get<Product[]>(`/api/pharmacy/products?term=${encodeURIComponent(search)}&take=40`);
      if (cancelled) return;
      setMatches(found);
      // A single hit is almost always the one wanted — select it so the
      // operator can go straight to the quantity box.
      if (found.length === 1) pick(found[0]);
    })();
    return () => { cancelled = true; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [search]);

  const pick = (product: Product) => {
    setSelected(product);

    // Offer the units this medicine can actually be sold in: a syrup is
    // bottles and nothing else; a strip of ten is tablets, or strips of 10.
    const perPack = Math.max(1, product.unitsPerPack);
    const units = [unitWord(product, 2)];
    if (perPack > 1) units.push(`${packWord(product)} of ${perPack}`);

    const remembered = rememberedUnit.current[product.id];
    setQuantityUnit(remembered && units.includes(remembered) ? remembered : units[0]);
  };

  const quantityUnits = useMemo(() => {
    if (!selected) return [];
    const perPack = Math.max(1, selected.unitsPerPack);
    const units = [unitWord(selected, 2)];
    if (perPack > 1) units.push(`${packWord(selected)} of ${perPack}`);
    return units;
  }, [selected]);

  /** The quantity box in base units, whatever unit sits beside it. */
  const inBaseUnits = (product: Product, qty: number) => {
    const perPack = Math.max(1, product.unitsPerPack);
    const choseWholePacks = perPack > 1 && quantityUnit === `${packWord(product)} of ${perPack}`;
    return choseWholePacks ? qty * perPack : qty;
  };

  const selectedSummary = useMemo(() => {
    if (!selected) return '';
    const stock = selected.stockOnHand;
    if (stock <= 0) return `${selected.name} · out of stock`;

    // What one unit costs from the batch that would actually be dispensed.
    const packMrp = nextBatchMrp(selected);
    const price = selected.unitsPerPack > 1 ? unitPrice(packMrp, selected.unitsPerPack) : packMrp;

    return `${selected.name} · ${stock} ${unitWord(selected, stock)} in stock` +
      (price > 0 ? ` · ₹${price.toFixed(2)} each` : '');
  }, [selected]);

  /** Replaces every line for one medicine from a fresh allocation, so a
   * second Add of the same medicine does not double-count against the
   * same batch. */
  const relay = (product: Product, allocations: Allocation[]) => {
    setLines((prev) => [
      ...prev.filter((l) => l.productId !== product.id),
      ...allocations.map((a) => ({
        productId: product.id,
        batchId: a.batch.id,
        productName: product.name,
        batchNo: a.batch.batchNo,
        expiryDate: a.batch.expiryDate,
        hsnCode: product.hsnCode,
        // Zero when the pharmacy is not registered, so no tax is extracted
        // from the MRP and the bill carries none.
        gstRate: gstRegistered ? product.gstRate : 0,
        schedule: product.schedule,
        available: a.batch.qtyOnHand,
        unitsPerPack: a.batch.unitsPerPack,
        packLabel: product.packSize,
        unitName: unitWord(product, 2),
        quantity: a.units,
        mrp: a.batch.mrp,
        discountPercent: 0,
      })),
    ]);
  };

  const addLine = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    setWarning('');

    if (!selected) { setWarning('Choose a medicine from the list first.'); return; }
    if (quantity <= 0) { setWarning('Quantity must be at least 1.'); return; }

    const product = selected;
    const adding = inBaseUnits(product, quantity);

    // Some things cannot be broken open — a sealed bottle, a sachet strip
    // the manufacturer seals as one. The medicine says so and this is where
    // it has to mean something.
    if (!product.allowLooseSale && product.unitsPerPack > 1 && adding % product.unitsPerPack !== 0) {
      const packs = Math.floor(adding / product.unitsPerPack) + 1;
      setWarning(
        `${product.name} is not sold loose — it goes out in whole packs of ${product.unitsPerPack}. ` +
        `Enter ${packs * product.unitsPerPack} for ${packs} pack(s).`,
      );
      return;
    }

    // Whatever is already on this bill is committed as far as stock goes.
    const alreadyOnBill = lines.filter((l) => l.productId === product.id).reduce((s, l) => s + l.quantity, 0);

    try {
      const result = await api.post<{ allocations: Allocation[]; shortfall: number }>('/api/pharmacy/allocate', {
        productId: product.id,
        units: alreadyOnBill + adding,
      });

      if (result.shortfall > 0) {
        const have = result.allocations.reduce((s, a) => s + a.units, 0) - alreadyOnBill;
        setWarning(
          have <= 0
            ? `${product.name} has none left that can be sold.`
            : `Only ${have} ${unitWord(product, Math.max(have, 2))} of ${product.name} left to sell.`,
        );
        return;
      }

      relay(product, result.allocations);

      // The load report named what was out of stock at the time. Once
      // something has actually gone onto the bill it is stale, and a stale
      // "nothing could be billed" sitting above a bill with lines on it is
      // worse than no message.
      setLoadWarning('');

      // Nearest expiry goes first, which is right — but handing over
      // something with a fortnight left without saying so is how a customer
      // comes back.
      const soonest = Math.min(...result.allocations.map((a) => daysToExpiry(a.batch.expiryDate)));
      if (soonest <= 30) {
        const b = result.allocations.find((a) => daysToExpiry(a.batch.expiryDate) === soonest)!.batch;
        setWarning(
          `${product.name} batch ${b.batchNo} expires ${new Date(b.expiryDate).toLocaleDateString(undefined, { month: 'short', year: 'numeric' })} — ` +
          `${soonest} day(s) away. It is still on the bill; tell the customer.`,
        );
      }

      // Two prices for one medicine on a bill looks like a mistake unless
      // the operator knows before the customer asks.
      const prices = new Set(result.allocations.map((a) => a.batch.mrp)).size;
      setStatus(
        product.schedule === 'H1'
          ? `${product.name} is Schedule H1 — record the prescriber's name on this bill.`
          : result.allocations.length > 1
            ? `${product.name}: ${adding} from ${result.allocations.length} batches — ` +
              result.allocations.map((a) => `${a.units} from ${a.batch.batchNo}`).join(', ') +
              (prices > 1 ? ' at different prices.' : '.')
            : `${product.name} added.`,
      );

      // The line is on the bill, so the search that found it goes: left
      // there, pressing Add again re-lays the line they already have
      // instead of adding the next medicine.
      setQuantity(1);
      setSelected(null);
      setSearch('');
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not allocate stock.');
    }
  };

  /**
   * Changing a quantity has to re-take stock: raising it past what its batch
   * holds needs another batch, and lowering it should give the rest back.
   * Without this the line stays pinned to one batch and only fails on save.
   */
  const changeQuantity = async (index: number, newQuantity: number) => {
    const row = lines[index];
    const wanted = lines
      .filter((l) => l.productId === row.productId)
      .reduce((s, l, i) => s + (i === lines.filter((x) => x.productId === row.productId).indexOf(row) ? newQuantity : l.quantity), 0);

    // One product by id. This used to fetch the whole catalogue and search
    // it here, which at three hundred medicines meant downloading 1.7 MB to
    // answer a question about a single row — on every quantity edit.
    const product = matches.find((p) => p.id === row.productId)
      ?? await api.get<Product>(`/api/pharmacy/products/${row.productId}`).catch(() => null);
    if (!product) return;

    try {
      const result = await api.post<{ allocations: Allocation[]; shortfall: number }>('/api/pharmacy/allocate', {
        productId: row.productId,
        units: wanted,
      });

      if (result.shortfall > 0) {
        const have = result.allocations.reduce((s, a) => s + a.units, 0);
        setWarning(
          `Only ${have} ${unitWord(product, Math.max(have, 2))} of ${product.name} can be sold. ` +
          `The line has been set back to ${have}.`,
        );
      }
      relay(product, result.allocations);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not re-take stock.');
    }
  };

  const loadPrescription = async () => {
    setError(null);
    setLoadWarning('');
    if (!selectedVisitId) { setWarning("Choose a patient from today's OPD list first."); return; }

    try {
      const visit = await api.get<Visit>(`/api/visits/${selectedVisitId}`);

      // A prescription is a handful of lines, so it asks about a handful of
      // medicines. Fetching the entire catalogue with every batch to match
      // them cost 1.7 MB; a few small searches cost a few kilobytes, and the
      // name fallback below still works because the search matches on name.
      const wanted = new Map<string, Product>();
      for (const item of visit.prescription) {
        if (item.productId && !wanted.has(item.productId)) {
          const p = await api.get<Product>(`/api/pharmacy/products/${item.productId}`).catch(() => null);
          if (p) wanted.set(p.id, p);
        }
        if (!item.productId) {
          const found = await api
            .get<Product[]>(`/api/pharmacy/products?term=${encodeURIComponent(item.medicineName)}&take=5`)
            .catch(() => [] as Product[]);
          for (const p of found) if (!wanted.has(p.id)) wanted.set(p.id, p);
        }
      }
      const catalogue = [...wanted.values()];

      const missing: string[] = [];
      const partial: string[] = [];
      let next = lines;

      for (const item of visit.prescription) {
        let product = item.productId ? catalogue.find((p) => p.id === item.productId) : undefined;
        product ??= catalogue.find((p) => p.name.toLowerCase().includes(item.medicineName.toLowerCase()));

        if (!product) { missing.push(item.medicineName); continue; }

        const wanted = Math.max(1, item.quantity);
        const result = await api.post<{ allocations: Allocation[]; shortfall: number }>('/api/pharmacy/allocate', {
          productId: product.id, units: wanted,
        });

        if (result.allocations.length === 0) { missing.push(`${item.medicineName} (no stock)`); continue; }
        // Say so rather than quietly billing less than the doctor wrote.
        if (result.shortfall > 0) partial.push(`${product.name} (${wanted - result.shortfall} of ${wanted})`);

        const p = product;
        next = [
          ...next.filter((l) => l.productId !== p.id),
          ...result.allocations.map((a) => ({
            productId: p.id, batchId: a.batch.id, productName: p.name, batchNo: a.batch.batchNo,
            expiryDate: a.batch.expiryDate, hsnCode: p.hsnCode,
            gstRate: gstRegistered ? p.gstRate : 0, schedule: p.schedule,
            available: a.batch.qtyOnHand, unitsPerPack: a.batch.unitsPerPack,
            packLabel: p.packSize, unitName: unitWord(p, 2),
            quantity: a.units, mrp: a.batch.mrp, discountPercent: 0,
          })),
        ];
      }

      setLines(next);
      setCustomerName(visit.patient.name);
      setDoctorName(visit.doctor.name);

      const notes: string[] = [];
      if (missing.length) notes.push(`Not added: ${missing.join(', ')}`);
      if (partial.length) notes.push(`Short: ${partial.join(', ')}`);

      setStatus(
        notes.length === 0
          ? `Loaded ${visit.prescription.length} item(s) from token ${visit.tokenNo}.`
          : `Loaded. ${notes.join('. ')}.`,
      );

      // Spelled out separately from status: an operator who sees an empty
      // bill needs to know it is the stock that is missing, not the
      // prescription.
      setLoadWarning(
        notes.length === 0 ? ''
          : next.length === 0 ? `Nothing could be billed. ${notes.join('. ')}.`
          : `${notes.join('. ')}.`,
      );
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not load the prescription.');
    }
  };

  const totals = useMemo(
    () => billAmounts(lines.map((l) => lineAmounts(l.mrp, l.unitsPerPack, l.quantity, l.discountPercent, l.gstRate))),
    [lines],
  );

  const newBill = () => {
    setLines([]);
    setSearch('');
    setQuantity(1);
    setCustomerName('Guest');
    setDoctorName('');
    setTransactionNo('');
    setSelected(null);
    setSelectedVisitId('');
    setLoadWarning('');
    setWarning('');
  };

  const completeSale = async (print: boolean) => {
    setError(null);
    setWarning('');

    if (lines.length === 0) { setWarning('Add at least one medicine to the bill.'); return; }

    // The H1 register is a statutory record kept for three years, and the
    // prescriber is the whole point of it. Recording the sale without one
    // leaves a hole in a book an inspector can ask to see.
    const h1 = [...new Set(lines.filter((l) => l.schedule === 'H1').map((l) => l.productName))];
    if (h1.length > 0 && !doctorName.trim()) {
      setWarning(
        `${h1.join(', ')} is Schedule H1. Enter the prescribing doctor's name before saving — ` +
        `it goes in the H1 register, which has to be kept for three years.`,
      );
      return;
    }

    setSaving(true);
    try {
      const visit = prescribedVisits.find((v) => v.id === selectedVisitId);
      const sale = await api.post<Sale>('/api/pharmacy/sales', {
        sale: {
          billDate: new Date().toISOString().slice(0, 19),
          patientId: visit?.patientId ?? null,
          visitId: visit?.id ?? null,
          customerName: customerName.trim() || 'Guest',
          doctorName: doctorName.trim() || null,
          paymentMode,
          transactionNo: transactionNo.trim() || null,
          isTaxInvoice: gstRegistered,
        },
        lines: lines.map((l) => ({
          productId: l.productId, batchId: l.batchId, productName: l.productName,
          batchNo: l.batchNo, expiryDate: l.expiryDate, hsnCode: l.hsnCode,
          quantity: l.quantity, unitsPerPack: l.unitsPerPack, packLabel: l.packLabel,
          mrp: l.mrp, discountPercent: l.discountPercent, gstRate: l.gstRate, schedule: l.schedule,
        })),
      });

      setSavedSale(sale);
      setStatus(`Bill ${sale.billNo} saved · ₹${sale.netAmount.toFixed(2)}`);
      newBill();

      if (print) {
        try {
          await openPdf(`/api/print/bill/${sale.id}`);
        } catch {
          // The bill is saved and numbered; a blocked pop-up must not read
          // as a failed sale. It reprints from the patient's record.
        }
      }
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not save the bill.');
    } finally {
      setSaving(false);
    }
  };

  const showTransactionNo = paymentMode === 'Upi' || paymentMode === 'Card';

  // One list, used by both the arrow keys and the markup. Deriving it twice
  // is how the highlighted row and the row Enter picks drift apart.
  const visibleMatches = useMemo(
    () => (!selected && search.trim() ? matches.slice(0, 12) : []),
    [selected, search, matches],
  );

  // A new search starts with nothing chosen.
  useEffect(() => { setHighlight(-1); }, [search, selected]);

  const focusSearch = () => {
    searchRef.current?.focus();
    searchRef.current?.select();
  };

  // Same keys as the OPD queue wherever the meaning is the same: F2 starts a
  // new one, F4 takes the money, F8 finishes. A pharmacist who has learnt
  // the queue already knows three of these.
  const G = 'Pharmacy counter';
  useHotkey('f2', 'Start a new bill', G, () => { newBill(); focusSearch(); });
  useHotkey('f3', 'Find a medicine', G, focusSearch, { whileTyping: true });
  useHotkey('f6', 'Load an OPD prescription', G, () => {
    if (selectedVisitId) void loadPrescription();
  });
  useHotkey('f7', 'Quick stock for this medicine', G, () => {
    if (selected) setQuickStockFor(selected);
  });
  useHotkey('f4', 'Save the bill', G, () => {
    if (!saving && lines.length > 0) void completeSale(false);
  });
  useHotkey('f8', 'Save and print', G, () => {
    if (!saving && lines.length > 0) void completeSale(true);
  });

  // Arrowing the result list. Live only while the search box has focus and
  // there is a list to walk, so the quantity box beside it keeps its own
  // up and down.
  const searchHasFocus = () => document.activeElement === searchRef.current;
  const walking = () => searchHasFocus() && visibleMatches.length > 0;

  useHotkey('arrowdown', 'Next match', G, () => {
    setHighlight((i) => (i + 1) % visibleMatches.length);
  }, { whileTyping: true, when: walking });

  useHotkey('arrowup', 'Previous match', G, () => {
    setHighlight((i) => (i <= 0 ? visibleMatches.length - 1 : i - 1));
  }, { whileTyping: true, when: walking });

  // Only claims Enter once a match is actually highlighted; otherwise the
  // form submits as it always did and the line is added.
  useHotkey('enter', 'Choose the highlighted match', G, () => {
    const product = visibleMatches[highlight];
    if (product) { pick(product); focusSearch(); }
  }, { whileTyping: true, when: () => walking() && highlight >= 0 });

  return (
    <div className="page wide">
      <div className="page-head">
        <div>
          <h1>Pharmacy Counter</h1>
          <p className="hint">
            {lines.length === 0 ? 'No items on this bill' : `${lines.length} item(s) · ₹${totals.net.toFixed(2)}`}
            {!gstRegistered && ' · not GST registered, so this prints as a plain invoice'}
          </p>
        </div>
        <div className="inline-form">
          <select value={selectedVisitId} onChange={(e) => setSelectedVisitId(e.target.value)}>
            <option value="">Today's OPD…</option>
            {prescribedVisits.map((v) => (
              <option key={v.id} value={v.id}>Token {v.tokenNo} · {v.patient.name}</option>
            ))}
          </select>
          <button type="button" className="ghost" onClick={loadPrescription}>Load prescription</button>
          <button type="button" className="ghost" onClick={newBill}>New bill</button>
        </div>
      </div>

      {savedSale && (
        <section className="card success">
          <h2>Bill {savedSale.billNo} saved</h2>
          <p>
            Net ₹{savedSale.netAmount.toFixed(2)} ({savedSale.paymentMode}){' '}
            <button type="button" className="ghost" onClick={() => openPdf(`/api/print/bill/${savedSale.id}?reprint=true`)}>
              Reprint
            </button>
          </p>
        </section>
      )}

      {error && <p className="auth-error">{error}</p>}
      {warning && <p className="hint warn">{warning}</p>}
      {loadWarning && <p className="hint warn">{loadWarning}</p>}
      {status && <p className="hint status-line">{status}</p>}

      <section className="card">
        <h2>Add a medicine</h2>
        <form className="inline-form" onSubmit={addLine}>
          <input
            ref={searchRef}
            placeholder="Medicine name — type to search"
            value={search}
            onChange={(e) => { setSearch(e.target.value); setSelected(null); }}
          />
          <input
            type="number"
            min="1"
            value={quantity}
            onChange={(e) => setQuantity(Number(e.target.value) || 0)}
            style={{ width: 90 }}
          />
          {/* The unit sits beside the number because "9" on its own is what
              turned nine tablets into nine strips. */}
          <select
            value={quantityUnit}
            onChange={(e) => {
              setQuantityUnit(e.target.value);
              if (selected) rememberedUnit.current[selected.id] = e.target.value;
            }}
            disabled={!selected}
          >
            {quantityUnits.map((u) => <option key={u} value={u}>{u}</option>)}
          </select>
          <button type="submit" disabled={!selected}>Add to bill</button>
          <button
            type="button"
            className="ghost"
            disabled={!selected}
            onClick={() => selected && setQuickStockFor(selected)}
          >
            Quick stock
          </button>
        </form>

        {selectedSummary && <p className="hint">{selectedSummary}</p>}

        {visibleMatches.length > 0 && (
          <ul className="picker-results static" role="listbox">
            {visibleMatches.map((p, i) => (
              <li key={p.id}>
                <button
                  type="button"
                  role="option"
                  aria-selected={i === highlight}
                  className={i === highlight ? 'active-match' : undefined}
                  onMouseEnter={() => setHighlight(i)}
                  onClick={() => pick(p)}
                >
                  {/* Strength in bold and ahead of the pack: it is the one
                      thing that distinguishes five Cetirizine rows from each
                      other, and picking the wrong one is a wrong dose rather
                      than a wrong price. */}
                  {p.name}
                  {p.strength && <strong> {p.strength}</strong>}
                  {p.packSize ? ` (${p.packSize})` : ''} — stock {p.stockOnHand}
                  {p.schedule !== 'None' && <span className="hint"> · Schedule {p.schedule}</span>}
                </button>
              </li>
            ))}
          </ul>
        )}
      </section>

      <section className="card">
        <h2>Bill</h2>
        <table>
          <thead>
            <tr>
              <th>Medicine</th><th>Batch</th><th>Expiry</th><th>Qty</th><th>MRP</th><th>Amount</th><th></th>
            </tr>
          </thead>
          <tbody>
            {lines.map((l, i) => {
              const amount = lineAmounts(l.mrp, l.unitsPerPack, l.quantity, l.discountPercent, l.gstRate).net;
              return (
                <tr key={`${l.batchId}-${i}`}>
                  <td>
                    {l.productName}
                    {l.schedule === 'H1' && <span className="hint"> · Schedule H1</span>}
                  </td>
                  <td>{l.batchNo}</td>
                  <td>{new Date(l.expiryDate).toLocaleDateString(undefined, { month: '2-digit', year: '2-digit' })}</td>
                  <td>
                    {l.quantity}
                    {l.unitsPerPack > 1 && (
                      <div className="hint">{describePacks(l.quantity, l.unitsPerPack, l.packLabel, l.unitName)}</div>
                    )}
                  </td>
                  <td>{l.mrp.toFixed(2)}</td>
                  <td>{amount.toFixed(2)}</td>
                  <td className="row-actions">
                    <button type="button" className="ghost" onClick={() => setEditingLine(i)}>Qty</button>
                    <button
                      type="button"
                      className="danger"
                      onClick={() => setLines((prev) => prev.filter((_, j) => j !== i))}
                    >
                      Remove
                    </button>
                  </td>
                </tr>
              );
            })}
            {lines.length === 0 && <tr><td colSpan={7}>Nothing on the bill yet.</td></tr>}
          </tbody>
        </table>

        {lines.length > 0 && (
          <div className="totals">
            <div><span>Gross</span><span>{totals.gross.toFixed(2)}</span></div>
            {totals.discount > 0 && <div><span>Discount</span><span>-{totals.discount.toFixed(2)}</span></div>}
            {gstRegistered && (
              <>
                <div><span>Taxable</span><span>{totals.taxable.toFixed(2)}</span></div>
                <div><span>CGST</span><span>{totals.cgst.toFixed(2)}</span></div>
                <div><span>SGST</span><span>{totals.sgst.toFixed(2)}</span></div>
              </>
            )}
            {totals.roundOff !== 0 && (
              <div><span>Round off</span><span>{totals.roundOff > 0 ? '+' : ''}{totals.roundOff.toFixed(2)}</span></div>
            )}
            <div className="net"><span>Net payable</span><span>₹{totals.net.toFixed(2)}</span></div>
          </div>
        )}
      </section>

      <section className="card">
        <h2>Payment</h2>
        <div className="inline-form">
          <input placeholder="Customer name" value={customerName} onChange={(e) => setCustomerName(e.target.value)} />
          <input
            placeholder="Prescribing doctor"
            value={doctorName}
            onChange={(e) => setDoctorName(e.target.value)}
          />
          <select value={paymentMode} onChange={(e) => setPaymentMode(e.target.value as PaymentMode)}>
            <option value="Cash">Cash</option>
            <option value="Upi">UPI</option>
            <option value="Card">Card</option>
          </select>
          {showTransactionNo && (
            <input
              placeholder="Transaction / ref no."
              value={transactionNo}
              onChange={(e) => setTransactionNo(e.target.value)}
            />
          )}
          <button type="button" className="primary" onClick={() => completeSale(false)} disabled={saving || lines.length === 0}>
            {saving ? 'Saving…' : <>Save bill <span className="kbd">{describeCombo('f4')}</span></>}
          </button>
          <button type="button" onClick={() => completeSale(true)} disabled={saving || lines.length === 0}>
            Save &amp; print <span className="kbd">{describeCombo('f8')}</span>
          </button>
        </div>
      </section>

      <ShortcutHints keys={[['f2', 'new bill'], ['f3', 'find medicine'], ['arrowup arrowdown enter', 'choose'], ['f6', 'load prescription'], ['f7', 'quick stock'], ['f4', 'save'], ['f8', 'save & print']]} />

      {quickStockFor && (
        <QuickStockDialog
          product={quickStockFor}
          onClose={() => setQuickStockFor(null)}
          onAdded={async (message) => {
            setQuickStockFor(null);
            const refreshed = await api.get<Product[]>(
              `/api/pharmacy/products?term=${encodeURIComponent(search)}&take=40`,
            );
            setMatches(refreshed);
            const again = refreshed.find((p) => p.id === quickStockFor.id);
            if (again) pick(again);
            setStatus(message);
          }}
        />
      )}

      {editingLine !== null && lines[editingLine] && (
        <EditQuantityDialog
          row={lines[editingLine]}
          onClose={() => setEditingLine(null)}
          onConfirm={async (qty) => {
            const index = editingLine;
            setEditingLine(null);
            await changeQuantity(index, qty);
          }}
        />
      )}
    </div>
  );
}
