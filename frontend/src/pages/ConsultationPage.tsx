import { useGeneralSettings } from '../settings/SettingsContext';
import { useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { api, ApiError, openPdf } from '../api/client';
import type { CatalogueEntry, Visit } from '../api/types';
import { DOSE_OPTIONS, describePacks, unitsForCourse } from '../clinical/doseMath';
import { medicineDisplayName } from '../pharmacy/packing';
import { ShortcutHints } from '../shell/ShortcutHints';
import { useHotkey } from '../shell/hotkeys';

interface RxLine {
  /**
   * Identity for React's list reconciliation, not something the server sees.
   *
   * The rows were keyed by array index. Every cell is read-only today, so
   * removing one still rendered correctly — but the moment a cell becomes
   * editable, deleting a line hands its state to the line that moves up into
   * its place, and a dose lands against the wrong medicine. On a prescription
   * that is not a bug worth waiting for.
   */
  key: string;
  productId: string | null;
  medicine: string;
  dosage: string;
  frequency: string;
  days: number;
  quantity: number;
  instructions: string | null;
  unitsPerPack: number;
  packLabel: string | null;
}

interface TestLine {
  testId: string | null;
  testName: string;
}

interface DiagnosticTest {
  id: string;
  name: string;
  category: string;
}

export function ConsultationPage() {
  const { visitId = '' } = useParams();
  const navigate = useNavigate();

  const [visit, setVisit] = useState<Visit | null>(null);
  const [products, setProducts] = useState<CatalogueEntry[]>([]);
  const [tests, setTests] = useState<DiagnosticTest[]>([]);
  // From the shared settings rather than a fetch of its own. It decides only
  // whether the test box offers the catalogue or takes free text, and the
  // provider already knows.
  const diagnosticsEnabled = useGeneralSettings()?.diagnosticsEnabled ?? false;

  const [complaint, setComplaint] = useState('');
  const [diagnosis, setDiagnosis] = useState('');
  const [notes, setNotes] = useState('');
  const [weight, setWeight] = useState('');
  const [bloodPressure, setBloodPressure] = useState('');
  const [temperature, setTemperature] = useState('');
  const [height, setHeight] = useState('');
  const [heartRate, setHeartRate] = useState('');
  const [spo2, setSpo2] = useState('');
  const [fee, setFee] = useState('');
  const [followUpOn, setFollowUpOn] = useState('');

  const [lines, setLines] = useState<RxLine[]>([]);
  const [requestedTests, setRequestedTests] = useState<TestLine[]>([]);

  // ── The medicine entry row ────────────────────────────────────────────
  const [medicineSearch, setMedicineSearch] = useState('');
  const [pickedMedicine, setPickedMedicine] = useState<CatalogueEntry | null>(null);
  const [dosage, setDosage] = useState('');
  const [morning, setMorning] = useState('0');
  const [afternoon, setAfternoon] = useState('0');
  const [night, setNight] = useState('0');
  const [days, setDays] = useState(0);
  const [quantity, setQuantity] = useState(0);
  const [instructions, setInstructions] = useState('');

  const [testSearch, setTestSearch] = useState('');
  const [pickedTest, setPickedTest] = useState<DiagnosticTest | null>(null);

  const [status, setStatus] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [savedSnapshot, setSavedSnapshot] = useState('');

  const frequency = `${morning}-${afternoon}-${night}`;

  useEffect(() => {
    void (async () => {
      try {
        const v = await api.get<Visit>(`/api/visits/${visitId}`);
        setVisit(v);
        setComplaint(v.complaint ?? '');
        setDiagnosis(v.diagnosis ?? '');
        setNotes(v.notes ?? '');
        setWeight(v.weightKg?.toString() ?? '');
        setBloodPressure(v.bloodPressure ?? '');
        setTemperature(v.temperatureF?.toString() ?? '');
        setHeight(v.heightCm?.toString() ?? '');
        setHeartRate(v.heartRateBpm?.toString() ?? '');
        setSpo2(v.spo2Percent?.toString() ?? '');
        setFee(String(v.fee));
        setFollowUpOn(v.followUpOn ? v.followUpOn.slice(0, 10) : '');

        // The lean catalogue: names and prices, no batch history. This
        // screen only picks medicines by name, and the full product list
        // shipped every batch of every product — 1.7 MB to fill a dropdown.
        const catalogue = await api.get<CatalogueEntry[]>('/api/pharmacy/catalogue');
        setProducts(catalogue);

        setLines(
          v.prescription.map((p) => {
            const product = p.productId ? catalogue.find((c) => c.id === p.productId) : undefined;
            return {
              key: crypto.randomUUID(),
              productId: p.productId,
              medicine: p.medicineName,
              dosage: p.dosage ?? '',
              frequency: p.frequency ?? '',
              days: p.days,
              quantity: p.quantity,
              instructions: p.instructions,
              unitsPerPack: product?.unitsPerPack ?? 1,
              packLabel: product?.packSize ?? null,
            };
          }),
        );
        setRequestedTests(v.diagnosticRequests.map((r) => ({ testId: r.testId, testName: r.testName })));

      } catch (err) {
        setError(err instanceof ApiError ? err.message : 'Could not load the consultation.');
      }
    })();
  }, [visitId]);

  // The test catalogue, in its own effect keyed on the module flag.
  //
  // It used to sit inside the visit load, which read the flag from a fetch of
  // its own. Now the flag arrives from the shared settings provider — a tick
  // or two later than the first render — so loading the catalogue in the
  // visit effect would have run once, while the flag was still false, and
  // never again. Keyed on the flag, it runs when the answer is actually
  // known.
  useEffect(() => {
    if (!diagnosticsEnabled) return;
    void api
      .get<DiagnosticTest[]>('/api/diagnostics/tests?activeOnly=true')
      .then(setTests)
      .catch(() => {
        // The module is on but the catalogue would not load. Free text still
        // works, which is what the desktop falls back to as well, so this is
        // a degraded box rather than a broken screen.
      });
  }, [diagnosticsEnabled]);

  // The form as it was last read from or written to the server — cheaper and
  // harder to get wrong than a dirty flag on every field.
  const snapshot = useMemo(
    () =>
      JSON.stringify([
        complaint, diagnosis, notes, weight, bloodPressure, temperature,
        height, heartRate, spo2, fee, followUpOn,
        lines.map((l) => [l.medicine, l.dosage, l.frequency, l.days, l.quantity, l.instructions]),
        requestedTests.map((t) => t.testName),
      ]),
    [complaint, diagnosis, notes, weight, bloodPressure, temperature, height,
     heartRate, spo2, fee, followUpOn, lines, requestedTests],
  );

  useEffect(() => {
    if (visit && !savedSnapshot) setSavedSnapshot(snapshot);
    // Only seeds the baseline once, right after load.
  }, [visit, snapshot, savedSnapshot]);

  // Works out how many individual units the course needs — "1-0-1" for 3 days
  // is 6 tablets — and says what that is in strips, because the pharmacy
  // dispenses from strips.
  const courseHint = useMemo(() => {
    const units = unitsForCourse(frequency, days);
    if (units === null) {
      const nothingChosen = morning === '0' && afternoon === '0' && night === '0';
      return nothingChosen ? '' : `'${frequency}' for ${days} day(s) — enter the quantity yourself.`;
    }
    const perPack = pickedMedicine?.unitsPerPack ?? 1;
    return perPack > 1
      ? `${units} units · ${describePacks(units, perPack, pickedMedicine?.packSize)}`
      : `${units} units`;
  }, [frequency, days, morning, afternoon, night, pickedMedicine]);

  // Auto-fills the quantity from the course, exactly as the desktop does.
  useEffect(() => {
    const units = unitsForCourse(frequency, days);
    if (units !== null) setQuantity(units);
  }, [frequency, days]);

  const matches = useMemo(() => {
    const term = medicineSearch.trim().toLowerCase();
    if (term.length < 2 || pickedMedicine) return [];
    return products
      .filter(
        (p) =>
          p.name.toLowerCase().includes(term) ||
          // So typing "cetzine 5" narrows to the 5 mg rather than listing all
          // five strengths and leaving the choice to a glance.
          medicineDisplayName(p).toLowerCase().includes(term) ||
          (p.manufacturer ?? '').toLowerCase().includes(term),
      )
      .slice(0, 8);
  }, [medicineSearch, products, pickedMedicine]);

  const medicineHint = useMemo(() => {
    if (pickedMedicine) {
      return pickedMedicine.stockOnHand > 0
        ? `In our pharmacy · ${pickedMedicine.stockOnHand} in stock`
        : 'In our pharmacy · out of stock';
    }
    if (!medicineSearch.trim()) return '';
    return matches.length > 0
      ? 'Pick one from the list, or keep typing for a medicine we do not stock.'
      : 'Not in our pharmacy — it will be written on the prescription only.';
  }, [pickedMedicine, medicineSearch, matches.length]);

  const testMatches = useMemo(() => {
    const term = testSearch.trim().toLowerCase();
    if (term.length < 2 || pickedTest) return [];
    return tests.filter((t) => t.name.toLowerCase().includes(term)).slice(0, 8);
  }, [testSearch, tests, pickedTest]);

  const clearEntryRow = () => {
    // Emptied in full, dose and days included — a dose left behind from the
    // last medicine reads as chosen for this one, and a wrong dose nobody
    // typed is worse than retyping a right one.
    setPickedMedicine(null);
    setMedicineSearch('');
    setDosage('');
    setInstructions('');
    setMorning('0');
    setAfternoon('0');
    setNight('0');
    setDays(0);
    setQuantity(0);
  };

  const addLine = () => {
    // Either a catalogue medicine, or whatever was typed. A typed name is
    // written on the prescription and nowhere else — it never becomes a
    // medicine in our pharmacy.
    //
    // The strength is appended, because it is no longer part of the name:
    // five Cetirizine records all called "Cetzine" would otherwise every one
    // print as "Cetzine", and a prescription that does not say the dose is
    // not a prescription.
    const name = pickedMedicine
      ? medicineDisplayName(pickedMedicine)
      : medicineSearch;

    if (!name.trim()) {
      setStatus('Choose a medicine, or type its name.');
      return;
    }
    if (quantity <= 0) {
      setStatus('Enter how many units to dispense.');
      return;
    }

    setLines((prev) => [
      ...prev,
      {
        key: crypto.randomUUID(),
        productId: pickedMedicine?.id ?? null,
        medicine: name.trim(),
        dosage,
        frequency,
        days,
        quantity,
        instructions: instructions.trim() || null,
        unitsPerPack: pickedMedicine?.unitsPerPack ?? 1,
        packLabel: pickedMedicine?.packSize ?? null,
      },
    ]);

    setStatus(
      pickedMedicine
        ? `${name} added.`
        : `${name} added — not stocked here, so the patient buys it outside.`,
    );
    clearEntryRow();
  };

  const addTest = () => {
    const name = pickedTest?.name ?? testSearch;
    if (!name.trim()) {
      setStatus('Choose a test, or type its name.');
      return;
    }
    if (requestedTests.some((r) => r.testName.toLowerCase() === name.trim().toLowerCase())) {
      setStatus(`${name} is already on the list.`);
      return;
    }
    setRequestedTests((prev) => [...prev, { testId: pickedTest?.id ?? null, testName: name.trim() }]);
    setPickedTest(null);
    setTestSearch('');
    setStatus(`${name} added to the investigations.`);
  };

  const num = (v: string) => (v.trim() === '' ? null : Number(v));

  const persist = async (complete: boolean) => {
    setError(null);
    setSaving(true);
    try {
      await api.post(`/api/visits/${visitId}/consultation`, {
        complaint: complaint.trim() || null,
        diagnosis: diagnosis.trim() || null,
        notes: notes.trim() || null,
        weightKg: num(weight),
        bloodPressure: bloodPressure.trim() || null,
        temperatureF: num(temperature),
        heightCm: num(height),
        heartRateBpm: num(heartRate),
        spo2Percent: num(spo2),
        fee: Number(fee) || 0,
        followUpOn: followUpOn || null,
        prescription: lines.map((l) => ({
          productId: l.productId,
          medicineName: l.medicine,
          dosage: l.dosage || null,
          frequency: l.frequency || null,
          days: l.days,
          quantity: l.quantity,
          instructions: l.instructions,
        })),
        diagnosticRequests: requestedTests,
        complete,
      });
      setSavedSnapshot(snapshot);
      return true;
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not save the consultation.');
      return false;
    } finally {
      setSaving(false);
    }
  };

  // F3 is the medicine box here: during a consultation that is the field
  // being typed into over and over.
  const G = 'Consultation';
  const medicineRef = useRef<HTMLInputElement>(null);
  useHotkey('f3', 'Search medicines', G, () => {
    medicineRef.current?.focus();
    medicineRef.current?.select();
  }, { whileTyping: true });

  const save = async () => {
    if (await persist(false)) setStatus('Consultation saved.');
  };

  const complete = async () => {
    if (await persist(true)) navigate('/');
  };

  const print = async () => {
    if (!(await persist(false))) return;
    try {
      await openPdf(`/api/print/prescription/${visitId}`);
    } catch (err) {
      setStatus(err instanceof ApiError ? err.message : 'Could not open the prescription.');
    }
  };
  // Registered after the actions they call, so no binding refers to a
  // function declared further down the file.
  useHotkey('f4', 'Save the consultation', G, () => { if (!saving) void save(); });
  useHotkey('f8', 'Save and print', G, () => { if (!saving) void print(); });
  useHotkey('f9', 'Mark the visit completed', G, () => { if (!saving) void complete(); });

  const close = () => {
    // Anything typed but not saved is worth one question — a half-entered
    // prescription is not recoverable.
    if (snapshot !== savedSnapshot) {
      if (!window.confirm('This consultation has changes that have not been saved.\n\nLeave and lose them?')) return;
    }
    navigate('/');
  };

  if (!visit) {
    return (
      <div className="page">
        {error ? <p className="auth-error">{error}</p> : <p className="hint">Loading…</p>}
      </div>
    );
  }

  return (
    <div className="page wide">
      <div className="page-head">
        <div>
          <h1>Consultation</h1>
          <p className="hint">
            Token {visit.tokenNo} · {visit.patient.name} · {visit.patient.age}
            {visit.patient.gender.charAt(0)} · {visit.doctor.name}
          </p>
        </div>
        <div className="inline-form">
          <button type="button" className="primary" onClick={save} disabled={saving}>Save</button>
          <button type="button" onClick={complete} disabled={saving}>Complete</button>
          <button type="button" className="ghost" onClick={print}>Save &amp; print</button>
          <button type="button" className="ghost" onClick={close}>Close</button>
        </div>
      </div>

      {error && <p className="auth-error">{error}</p>}
      {status && <p className="hint status-line">{status}</p>}

      <section className="card">
        <h2>Clinical notes</h2>
        <div className="settings-row">
          <label>Complaint<input value={complaint} onChange={(e) => setComplaint(e.target.value)} /></label>
          <label>Diagnosis<input value={diagnosis} onChange={(e) => setDiagnosis(e.target.value)} /></label>
        </div>
        <label>Advice / notes<input value={notes} onChange={(e) => setNotes(e.target.value)} /></label>
      </section>

      <section className="card">
        <h2>Vitals</h2>
        <div className="settings-row">
          <label>Weight (kg)<input type="number" step="0.1" value={weight} onChange={(e) => setWeight(e.target.value)} /></label>
          <label>Height (cm)<input type="number" step="0.1" value={height} onChange={(e) => setHeight(e.target.value)} /></label>
          <label>BP<input placeholder="120/80" value={bloodPressure} onChange={(e) => setBloodPressure(e.target.value)} /></label>
          <label>Temp (°F)<input type="number" step="0.1" value={temperature} onChange={(e) => setTemperature(e.target.value)} /></label>
          <label>Heart rate<input type="number" value={heartRate} onChange={(e) => setHeartRate(e.target.value)} /></label>
          <label>SpO₂ (%)<input type="number" value={spo2} onChange={(e) => setSpo2(e.target.value)} /></label>
        </div>
      </section>

      <section className="card">
        <h2>Prescription</h2>

        <div className="rx-entry">
          <div className="patient-picker">
            <input
              ref={medicineRef}
              placeholder="Medicine — type to search, or write one we don't stock"
              value={medicineSearch}
              onChange={(e) => {
                setMedicineSearch(e.target.value);
                if (pickedMedicine && e.target.value !== pickedMedicine.name) setPickedMedicine(null);
              }}
            />
            {matches.length > 0 && (
              <ul className="picker-results">
                {matches.map((p) => (
                  <li key={p.id}>
                    <button
                      type="button"
                      onClick={() => {
                        setPickedMedicine(p);
                        setMedicineSearch(p.name);
                      }}
                    >
                      {/* Strength in bold, ahead of the pack. This is the
                          list a dose is chosen from, so telling 5 mg from
                          10 mg matters more here than anywhere. */}
                      {p.name}
                      {p.strength && <strong> {p.strength}</strong>}
                      {p.packSize ? ` (${p.packSize})` : ''} — stock {p.stockOnHand}
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </div>

          <div className="inline-form">
            <input placeholder="Dose (e.g. 1 tab)" value={dosage} onChange={(e) => setDosage(e.target.value)} />

            {/* Morning, afternoon and night, chosen rather than typed — a
                prescription is written this way, and picking from a list
                rules out "1-0-l" and "1_0_1". */}
            <span className="hint">Freq</span>
            {([[morning, setMorning], [afternoon, setAfternoon], [night, setNight]] as const).map(
              ([value, set], i) => (
                <select key={i} value={value} onChange={(e) => set(e.target.value)}>
                  {DOSE_OPTIONS.map((o) => <option key={o} value={o}>{o}</option>)}
                </select>
              ),
            )}

            <input
              placeholder="Days"
              type="number"
              min="0"
              value={days || ''}
              onChange={(e) => setDays(Number(e.target.value) || 0)}
            />
            <input
              placeholder="Qty"
              type="number"
              min="0"
              value={quantity || ''}
              onChange={(e) => setQuantity(Number(e.target.value) || 0)}
            />
            <button type="button" onClick={addLine}>Add</button>
            <button type="button" className="ghost" onClick={clearEntryRow}>Clear</button>
          </div>

          <input
            className="full"
            placeholder="Instructions (optional) — e.g. after food"
            value={instructions}
            onChange={(e) => setInstructions(e.target.value)}
          />

          {medicineHint && <p className="hint">{medicineHint}</p>}
          {courseHint && <p className="hint">{courseHint}</p>}
        </div>

        <table>
          <thead>
            <tr><th>Medicine</th><th>Dose</th><th>Frequency</th><th>Days</th><th>Qty</th><th></th></tr>
          </thead>
          <tbody>
            {lines.map((l, i) => (
              <tr key={l.key}>
                <td>
                  {l.medicine}
                  {l.instructions && <div className="hint">{l.instructions}</div>}
                </td>
                <td>{l.dosage}</td>
                <td>{l.frequency}</td>
                <td>{l.days || ''}</td>
                <td>
                  {l.quantity}
                  {l.unitsPerPack > 1 && (
                    <div className="hint">{describePacks(l.quantity, l.unitsPerPack, l.packLabel)}</div>
                  )}
                </td>
                <td>
                  <button
                    type="button"
                    className="danger"
                    onClick={() => setLines((prev) => prev.filter((_, j) => j !== i))}
                  >
                    Remove
                  </button>
                </td>
              </tr>
            ))}
            {lines.length === 0 && <tr><td colSpan={6}>Nothing prescribed yet.</td></tr>}
          </tbody>
        </table>
      </section>

      <section className="card">
        <h2>Investigations advised</h2>
        <div className="patient-picker">
          <input
            placeholder={diagnosticsEnabled ? 'Test — search the catalogue, or type any name' : 'Test name'}
            value={testSearch}
            onChange={(e) => {
              setTestSearch(e.target.value);
              if (pickedTest && e.target.value !== pickedTest.name) setPickedTest(null);
            }}
          />
          <button type="button" onClick={addTest}>Add</button>
          {testMatches.length > 0 && (
            <ul className="picker-results">
              {testMatches.map((t) => (
                <li key={t.id}>
                  <button type="button" onClick={() => { setPickedTest(t); setTestSearch(t.name); }}>
                    {t.name} <span className="hint">· {t.category}</span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>

        <ul className="chips">
          {requestedTests.map((t, i) => (
            <li key={i}>
              {t.testName}
              <button
                type="button"
                aria-label={`Remove ${t.testName}`}
                onClick={() => setRequestedTests((prev) => prev.filter((_, j) => j !== i))}
              >
                ×
              </button>
            </li>
          ))}
          {requestedTests.length === 0 && <li className="hint plain">None requested.</li>}
        </ul>
      </section>

      <section className="card">
        <h2>Follow-up</h2>
        <div className="settings-row">
          {/* Consultation fee is set once, at booking or fee collection —
              editing it again here duplicated that field without a reason
              to change it mid-consultation. `fee` still loads with the
              visit and round-trips on save unchanged; only the input is
              gone. */}
          <label>Review on<input type="date" value={followUpOn} onChange={(e) => setFollowUpOn(e.target.value)} /></label>
        </div>
      </section>

      <ShortcutHints keys={[['f3', 'search medicines'], ['f4', 'save'], ['f9', 'complete'], ['f8', 'save & print']]} />
    </div>
  );
}
