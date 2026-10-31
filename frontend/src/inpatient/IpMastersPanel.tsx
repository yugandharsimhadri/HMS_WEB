import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react';
import { api, ApiError } from '../api/client';
import type { Bed, IpChargeCategory, IpService, RoomType } from '../api/types';
import { CATEGORY_LABEL, MANUAL_CATEGORIES } from './ip';

/**
 * In-patient masters: room types, beds and the IP service list. Set up once,
 * so every list has an "add" row at the top that keeps focus for the next
 * entry, and beds can be added several at a time ("GW-01, GW-02, GW-03").
 */
export function IpMastersPanel() {
  const [roomTypes, setRoomTypes] = useState<RoomType[]>([]);
  const [beds, setBeds] = useState<Bed[]>([]);
  const [services, setServices] = useState<IpService[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [status, setStatus] = useState('');

  const load = useCallback(async () => {
    const [r, b, s] = await Promise.all([
      api.get<RoomType[]>('/api/inpatient/room-types'),
      api.get<Bed[]>('/api/inpatient/beds'),
      api.get<IpService[]>('/api/inpatient/services'),
    ]);
    setRoomTypes(r); setBeds(b); setServices(s);
  }, []);

  useEffect(() => { void load(); }, [load]);

  const run = async (fn: () => Promise<unknown>, done: string) => {
    setError(null); setStatus('');
    try {
      await fn();
      await load();
      setStatus(done);
      return true;
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not save.');
      return false;
    }
  };

  return (
    <div className="ip-masters">
      {error && <p className="auth-error">{error}</p>}
      {status && <p className="hint status-line">{status}</p>}
      <RoomTypes list={roomTypes} run={run} />
      <Beds list={beds} roomTypes={roomTypes.filter((r) => r.active)} run={run} />
      <Services list={services} run={run} />
    </div>
  );
}

type Run = (fn: () => Promise<unknown>, done: string) => Promise<boolean>;

// ── Room types ────────────────────────────────────────────────────────────

function RoomTypes({ list, run }: { list: RoomType[]; run: Run }) {
  const blank = { id: '', name: '', dailyRent: '', nursingPerDay: '', gstPercent: '0', active: true };
  const [form, setForm] = useState(blank);

  const save = async (e: FormEvent) => {
    e.preventDefault();
    const ok = await run(() => api.post('/api/inpatient/room-types', {
      id: form.id || null, name: form.name, dailyRent: Number(form.dailyRent) || 0,
      nursingPerDay: Number(form.nursingPerDay) || 0, gstPercent: Number(form.gstPercent) || 0, active: form.active,
    }), `${form.name} saved.`);
    if (ok) setForm(blank);
  };

  return (
    <section className="card">
      <h2>Room types</h2>
      <p className="hint">What a day in each kind of bed costs. A change applies to patients admitted after it.</p>
      <form className="ip-add" onSubmit={save}>
        <label className="ip-add-name">Name<input value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} placeholder="General ward" /></label>
        <label className="ip-add-num">Rent / day ₹<input type="number" min="0" value={form.dailyRent} onChange={(e) => setForm({ ...form, dailyRent: e.target.value })} /></label>
        <label className="ip-add-num">Nursing / day ₹<input type="number" min="0" value={form.nursingPerDay} onChange={(e) => setForm({ ...form, nursingPerDay: e.target.value })} /></label>
        <label className="ip-add-num">GST %<input type="number" min="0" max="28" value={form.gstPercent} onChange={(e) => setForm({ ...form, gstPercent: e.target.value })} /></label>
        {form.id && <label className="checkbox-label"><input type="checkbox" checked={form.active} onChange={() => setForm({ ...form, active: !form.active })} /><span>In use</span></label>}
        <button type="submit" disabled={!form.name.trim()}>{form.id ? 'Save' : 'Add'}</button>
        {form.id && <button type="button" className="ghost" onClick={() => setForm(blank)}>Cancel</button>}
      </form>
      <table>
        <thead><tr><th>Room type</th><th className="num">Rent / day</th><th className="num">Nursing / day</th><th className="num">GST</th><th /></tr></thead>
        <tbody>
          {list.map((r) => (
            <tr key={r.id} className={r.active ? undefined : 'row-inactive'}>
              <td>{r.name}{!r.active && <span className="hint"> · not in use</span>}</td>
              <td className="num">{r.dailyRent.toLocaleString('en-IN')}</td>
              <td className="num">{r.nursingPerDay.toLocaleString('en-IN')}</td>
              <td className="num">{r.gstPercent}%</td>
              <td><button type="button" className="ghost" onClick={() => setForm({
                id: r.id, name: r.name, dailyRent: String(r.dailyRent), nursingPerDay: String(r.nursingPerDay),
                gstPercent: String(r.gstPercent), active: r.active,
              })}>Edit</button></td>
            </tr>
          ))}
          {list.length === 0 && <tr><td colSpan={5} className="hint">Add your first room type, e.g. General ward ₹800 / day.</td></tr>}
        </tbody>
      </table>
      <p className="hint">GST: rooms above ₹5,000 a day (other than ICU) carry 5%. Everything else is 0.</p>
    </section>
  );
}

// ── Beds ──────────────────────────────────────────────────────────────────

function Beds({ list, roomTypes, run }: { list: Bed[]; roomTypes: RoomType[]; run: Run }) {
  const [ward, setWard] = useState('');
  const [roomTypeId, setRoomTypeId] = useState('');
  const [numbers, setNumbers] = useState('');
  const [editing, setEditing] = useState<Bed | null>(null);

  const wards = useMemo(() => [...new Set(list.map((b) => b.ward))], [list]);

  useEffect(() => {
    if (!roomTypeId && roomTypes.length > 0) setRoomTypeId(roomTypes[0].id);
  }, [roomTypes, roomTypeId]);

  /** "GW-01, GW-02" or one per line - several beds in one go. */
  const add = async (e: FormEvent) => {
    e.preventDefault();
    const bedNos = numbers.split(/[,\n]/).map((n) => n.trim()).filter(Boolean);
    const start = list.length;
    const ok = await run(async () => {
      for (const [i, bedNo] of bedNos.entries()) {
        await api.post('/api/inpatient/beds', { id: null, bedNo, ward, roomTypeId, sortOrder: start + i, active: true });
      }
    }, `${bedNos.length} bed${bedNos.length === 1 ? '' : 's'} added to ${ward}.`);
    if (ok) setNumbers('');
  };

  const saveEdit = async (e: FormEvent) => {
    e.preventDefault();
    if (!editing) return;
    const ok = await run(() => api.post('/api/inpatient/beds', {
      id: editing.id, bedNo: editing.bedNo, ward: editing.ward, roomTypeId: editing.roomTypeId,
      sortOrder: editing.sortOrder, active: editing.active,
    }), `Bed ${editing.bedNo} saved.`);
    if (ok) setEditing(null);
  };

  return (
    <section className="card">
      <h2>Beds</h2>
      {roomTypes.length === 0 ? <p className="hint">Add a room type first.</p> : (
        <form className="ip-add" onSubmit={add}>
          <label>Ward
            <input list="ip-wards" value={ward} onChange={(e) => setWard(e.target.value)} placeholder="General ward" />
            <datalist id="ip-wards">{wards.map((w) => <option key={w} value={w} />)}</datalist>
          </label>
          <label>Room type
            <select value={roomTypeId} onChange={(e) => setRoomTypeId(e.target.value)}>
              {roomTypes.map((r) => <option key={r.id} value={r.id}>{r.name} · ₹{r.dailyRent}</option>)}
            </select>
          </label>
          <label className="ip-add-name">Bed numbers<input value={numbers} onChange={(e) => setNumbers(e.target.value)} placeholder="GW-01, GW-02, GW-03" /></label>
          <button type="submit" disabled={!ward.trim() || !numbers.trim()}>Add beds</button>
        </form>
      )}

      {editing && (
        <form className="ip-add ip-editing" onSubmit={saveEdit}>
          <label>Bed no.<input value={editing.bedNo} onChange={(e) => setEditing({ ...editing, bedNo: e.target.value })} /></label>
          <label>Ward<input list="ip-wards" value={editing.ward} onChange={(e) => setEditing({ ...editing, ward: e.target.value })} /></label>
          <label>Room type
            <select value={editing.roomTypeId} onChange={(e) => setEditing({ ...editing, roomTypeId: e.target.value })}>
              {roomTypes.map((r) => <option key={r.id} value={r.id}>{r.name}</option>)}
            </select>
          </label>
          <label className="checkbox-label"><input type="checkbox" checked={editing.active} onChange={() => setEditing({ ...editing, active: !editing.active })} /><span>In use</span></label>
          <button type="submit">Save</button>
          <button type="button" className="ghost" onClick={() => setEditing(null)}>Cancel</button>
        </form>
      )}

      <table>
        <thead><tr><th>Bed</th><th>Ward</th><th>Room type</th><th>Status</th><th /></tr></thead>
        <tbody>
          {list.map((b) => (
            <tr key={b.id} className={b.active ? undefined : 'row-inactive'}>
              <td className="mono">{b.bedNo}</td>
              <td>{b.ward}</td>
              <td>{b.roomType?.name}</td>
              <td>{b.active ? b.status : 'Not in use'}</td>
              <td><button type="button" className="ghost" onClick={() => setEditing(b)}>Edit</button></td>
            </tr>
          ))}
          {list.length === 0 && <tr><td colSpan={5} className="hint">No beds yet.</td></tr>}
        </tbody>
      </table>
    </section>
  );
}

// ── IP services ───────────────────────────────────────────────────────────

function Services({ list, run }: { list: IpService[]; run: Run }) {
  const blank = { id: '', name: '', category: 'Procedure' as IpChargeCategory, rate: '', active: true };
  const [form, setForm] = useState(blank);

  const save = async (e: FormEvent) => {
    e.preventDefault();
    const ok = await run(() => api.post('/api/inpatient/services', {
      id: form.id || null, name: form.name, category: form.category, rate: Number(form.rate) || 0, active: form.active,
    }), `${form.name} saved.`);
    if (ok) setForm({ ...blank, category: form.category });
  };

  return (
    <section className="card">
      <h2>IP services</h2>
      <p className="hint">What can be added to an IP bill, with its usual rate. The rate can still be changed on the bill.</p>
      <form className="ip-add" onSubmit={save}>
        <label className="ip-add-name">Service<input value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} placeholder="Nebulisation" /></label>
        <label>Type
          <select value={form.category} onChange={(e) => setForm({ ...form, category: e.target.value as IpChargeCategory })}>
            {MANUAL_CATEGORIES.map((c) => <option key={c} value={c}>{CATEGORY_LABEL[c]}</option>)}
          </select>
        </label>
        <label className="ip-add-num">Rate ₹<input type="number" min="0" step="0.01" value={form.rate} onChange={(e) => setForm({ ...form, rate: e.target.value })} /></label>
        {form.id && <label className="checkbox-label"><input type="checkbox" checked={form.active} onChange={() => setForm({ ...form, active: !form.active })} /><span>In use</span></label>}
        <button type="submit" disabled={!form.name.trim()}>{form.id ? 'Save' : 'Add'}</button>
        {form.id && <button type="button" className="ghost" onClick={() => setForm(blank)}>Cancel</button>}
      </form>
      <table>
        <thead><tr><th>Service</th><th>Type</th><th className="num">Rate</th><th /></tr></thead>
        <tbody>
          {list.map((s) => (
            <tr key={s.id} className={s.active ? undefined : 'row-inactive'}>
              <td>{s.name}{!s.active && <span className="hint"> · not in use</span>}</td>
              <td>{CATEGORY_LABEL[s.category]}</td>
              <td className="num">{s.rate.toLocaleString('en-IN')}</td>
              <td><button type="button" className="ghost" onClick={() => setForm({
                id: s.id, name: s.name, category: s.category, rate: String(s.rate), active: s.active,
              })}>Edit</button></td>
            </tr>
          ))}
          {list.length === 0 && <tr><td colSpan={4} className="hint">Add the services you bill most: doctor visit, nebulisation, dressing, oxygen per hour, ECG.</td></tr>}
        </tbody>
      </table>
    </section>
  );
}
