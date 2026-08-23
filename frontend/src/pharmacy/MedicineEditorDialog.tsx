import { useEffect, useMemo, useState, type FormEvent } from 'react';
import { api, ApiError } from '../api/client';
import type { DispensingUnit, DrugSchedule, Product } from '../api/types';
import { DISPENSING_UNITS, unitsFromPacking, unitWordFor } from './packing';

interface Props {
  existing: Product | null;
  /**
   * A medicine to copy the shared details from, when adding another strength
   * or pack of something already in the catalogue.
   *
   * Everything that is true of every variant — the drug, the maker, the
   * composition, storage, GST, HSN, schedule, rack — carries over. What
   * differs between variants is left blank, because those are the fields
   * somebody is actually here to type. Saved as a new medicine: each strength
   * is its own record, with its own batches and stock.
   */
  basedOn?: Product | null;
  onClose: () => void;
  onSaved: (message: string) => void;
}

interface RepackPreview {
  unitsPerPack: number;
  batches: number;
  quantityBefore: number;
  quantityAfter: number;
  anythingToDo: boolean;
}

const SCHEDULES: DrugSchedule[] = ['None', 'H', 'H1', 'X'];

export function MedicineEditorDialog({ existing, basedOn, onClose, onSaved }: Props) {
  // What every variant of one drug shares, so adding a second strength does
  // not mean retyping the maker, composition, GST and schedule.
  const seed = existing ?? basedOn ?? null;

  const [name, setName] = useState(seed?.name ?? '');
  const [genericName, setGenericName] = useState(seed?.genericName ?? '');
  const [manufacturer, setManufacturer] = useState(seed?.manufacturer ?? '');
  const [composition, setComposition] = useState(seed?.composition ?? '');
  const [storage, setStorage] = useState(seed?.storage ?? '');

  // The two that differ between variants start blank when copying — they are
  // the fields somebody opened this to type.
  const [strength, setStrength] = useState(existing?.strength ?? '');
  const [packSize, setPackSize] = useState(existing?.packSize ?? '');

  const [hsnCode, setHsnCode] = useState(seed?.hsnCode ?? '3004');
  const [gstRate, setGstRate] = useState(String(seed?.gstRate ?? 0));
  const [schedule, setSchedule] = useState<DrugSchedule>(seed?.schedule ?? 'None');
  const [rackLocation, setRackLocation] = useState(seed?.rackLocation ?? '');
  const [reorderLevel, setReorderLevel] = useState(String(seed?.reorderLevel ?? 0));
  const [isActive, setIsActive] = useState(seed?.isActive ?? true);

  // Not copied: units-per-pack and the dispensing unit follow the pack, and a
  // syrup copied from a tablet must not inherit "15 per pack".
  const [unitsPerPack, setUnitsPerPack] = useState(existing?.unitsPerPack ?? 1);
  const [allowLooseSale, setAllowLooseSale] = useState(existing?.allowLooseSale ?? true);
  const [dispensingUnit, setDispensingUnit] = useState<DispensingUnit>(existing?.dispensingUnit ?? 'Tablet');

  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  // Stock already on the shelf is counted against an existing medicine's
  // units-per-pack, so never quietly change it from the pack size here — say
  // it looks wrong and let them decide. A brand-new medicine has no stock to
  // protect, so it may follow the pack size as it is typed.
  const [setByHand, setSetByHand] = useState(existing !== null);

  useEffect(() => {
    if (setByHand) return;
    const stated = unitsFromPacking(packSize);
    if (stated !== null) setUnitsPerPack(stated);
  }, [packSize, setByHand]);

  const packHint = useMemo(() => {
    const perPack = Math.max(1, unitsPerPack);
    const stated = unitsFromPacking(packSize);

    // The one combination that silently overcharges every customer.
    if (stated !== null && stated !== perPack) {
      return {
        warn: true,
        text:
          `⚠ Pack size says ${stated} but one pack is set to ${perPack}. At ${perPack} the counter ` +
          `would sell whole packs to anyone asking for ${unitWordFor(dispensingUnit, 2)}. ` +
          `Set it to ${stated} unless this is right.`,
      };
    }

    return {
      warn: false,
      text: perPack > 1
        ? `One pack holds ${perPack} ${unitWordFor(dispensingUnit, perPack)}. ` +
          `Stock and sales are counted in ${unitWordFor(dispensingUnit, 2)}.`
        : `Sold as a single ${unitWordFor(dispensingUnit, 1)} — a pack is one unit.`,
    };
  }, [unitsPerPack, packSize, dispensingUnit]);

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);

    if (!name.trim()) { setError('The brand name is required.'); return; }

    setBusy(true);
    try {
      const body = {
        id: existing?.id,
        name: name.trim(),
        genericName: genericName.trim() || null,
        manufacturer: manufacturer.trim() || null,
        composition: composition.trim() || null,
        storage: storage.trim() || null,
        strength: strength.trim() || null,
        packSize: packSize.trim() || null,
        hsnCode: hsnCode.trim() || '3004',
        gstRate: Number(gstRate) || 0,
        schedule,
        rackLocation: rackLocation.trim() || null,
        reorderLevel: Number(reorderLevel) || 0,
        isActive,
        unitsPerPack: Math.max(1, unitsPerPack),
        // Keep what was actually chosen. Forcing it false when a pack holds
        // one unit looks harmless but sticks, and then correcting a wrong
        // pack size later leaves the medicine refusing to sell loose with no
        // clue why.
        allowLooseSale,
        dispensingUnit,
      };

      await api.post('/api/pharmacy/products', body);

      // A batch keeps the units-per-pack it was received under, so changing
      // the medicine alone leaves stock on the shelf still sold by the pack.
      // Offer to re-count it — the packs don't move, only what the software
      // believes one of them holds.
      const productId = existing?.id ?? (await findJustSaved(body.name));
      let message = `${body.name} saved.`;

      if (productId) {
        const preview = await api.post<RepackPreview>(
          `/api/pharmacy/products/${productId}/repack-preview`,
          { unitsPerPack: body.unitsPerPack },
        );

        if (preview.anythingToDo) {
          const packs = Math.floor(preview.quantityAfter / Math.max(1, preview.unitsPerPack));
          const agreed = window.confirm(
            `${preview.batches} batch(es) of ${body.name} on the shelf were received under a ` +
            `different pack size, so the counter still sells them by the pack.\n\n` +
            `Re-count them as ${preview.unitsPerPack} per pack?\n\n` +
            `    ${preview.quantityBefore} → ${preview.quantityAfter}\n` +
            `    (${packs} pack(s) — nothing on the shelf changes)\n\n` +
            `Every batch is recorded in the correction trail.`,
          );

          if (agreed) {
            const count = await api.post<number>(
              `/api/pharmacy/products/${productId}/repack`,
              { unitsPerPack: body.unitsPerPack },
            );
            message =
              `${body.name} saved. ${count} batch(es) re-counted at ${preview.unitsPerPack} per pack — ` +
              `now ${preview.quantityAfter} on hand.`;
          }
        }
      }

      onSaved(message);
    } catch (err) {
      // A duplicate is not a failure — it is a wrong turn worth offering a
      // way out of. Nobody adds one on purpose; they could not find the first.
      if (err instanceof ApiError && err.status === 409) {
        setError(err.message);
      } else {
        setError(err instanceof ApiError ? err.message : 'Could not save the medicine.');
      }
      setBusy(false);
    }
  };

  const findJustSaved = async (saved: string): Promise<string | null> => {
    const found = await api.get<Product[]>(`/api/pharmacy/products?term=${encodeURIComponent(saved)}&take=5`);
    return found.find((p) => p.name === saved)?.id ?? null;
  };

  return (
    <div className="overlay" role="dialog" aria-modal="true" aria-label="Medicine">
      <div className="overlay-card wide">
        <div className="overlay-head">
          <h2>
            {existing ? `Medicine — ${existing.name}`
              : basedOn ? `Another strength or pack of ${basedOn.name}`
              : 'New medicine'}
          </h2>
          <button type="button" className="ghost" onClick={onClose}>Close</button>
        </div>

        <form className="overlay-body" onSubmit={onSubmit}>
          <div className="settings-row">
            <label>Brand name<input value={name} onChange={(e) => setName(e.target.value)} required autoFocus /></label>
            <label>Generic name<input value={genericName} onChange={(e) => setGenericName(e.target.value)} /></label>
            <label>Manufacturer<input value={manufacturer} onChange={(e) => setManufacturer(e.target.value)} /></label>
          </div>

          <div className="settings-row">
            <label>Composition<input value={composition} onChange={(e) => setComposition(e.target.value)} /></label>
            <label>Storage<input value={storage} onChange={(e) => setStorage(e.target.value)} /></label>
            <label>Rack<input value={rackLocation} onChange={(e) => setRackLocation(e.target.value)} /></label>
          </div>

          <div className="settings-row">
            {/* Its own field rather than part of the name, so five strengths
                of one drug can be told apart and sorted 5 before 10 — see
                Product.Strength. */}
            <label>
              Strength
              <input
                placeholder="e.g. 5 mg, 100 ml, 250mg/5ml"
                value={strength}
                onChange={(e) => setStrength(e.target.value)}
              />
            </label>
            <label>Pack size<input placeholder="e.g. 15 TAB" value={packSize} onChange={(e) => setPackSize(e.target.value)} /></label>
            <label>
              Units in one pack
              <input
                type="number"
                min="1"
                value={unitsPerPack}
                onChange={(e) => { setSetByHand(true); setUnitsPerPack(Number(e.target.value) || 1); }}
              />
            </label>
            <label>
              Dispensing unit
              <select value={dispensingUnit} onChange={(e) => setDispensingUnit(e.target.value as DispensingUnit)}>
                {DISPENSING_UNITS.map((u) => <option key={u} value={u}>{u}</option>)}
              </select>
            </label>
          </div>

          <p className={packHint.warn ? 'hint warn' : 'hint'}>{packHint.text}</p>

          <div className="settings-row">
            <label>HSN code<input value={hsnCode} onChange={(e) => setHsnCode(e.target.value)} /></label>
            <label>GST %<input type="number" min="0" step="0.01" value={gstRate} onChange={(e) => setGstRate(e.target.value)} /></label>
            <label>
              Schedule
              <select value={schedule} onChange={(e) => setSchedule(e.target.value as DrugSchedule)}>
                {SCHEDULES.map((s) => <option key={s} value={s}>{s === 'None' ? 'None' : `Schedule ${s}`}</option>)}
              </select>
            </label>
            <label>Reorder level<input type="number" min="0" value={reorderLevel} onChange={(e) => setReorderLevel(e.target.value)} /></label>
          </div>

          <div className="settings-row">
            <label className="checkbox-label">
              <input type="checkbox" checked={allowLooseSale} onChange={(e) => setAllowLooseSale(e.target.checked)} />
              Can be sold loose
            </label>
            <label className="checkbox-label">
              <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />
              Active
            </label>
          </div>

          {schedule === 'H1' && (
            <p className="hint warn">
              Schedule H1: every sale needs the prescriber's name and is recorded in a
              register kept for three years.
            </p>
          )}

          {error && <p className="auth-error">{error}</p>}

          <div className="overlay-actions">
            <button type="submit" disabled={busy}>{busy ? 'Saving…' : 'Save'}</button>
            <button type="button" className="ghost" onClick={onClose}>Cancel</button>
          </div>
        </form>
      </div>
    </div>
  );
}
