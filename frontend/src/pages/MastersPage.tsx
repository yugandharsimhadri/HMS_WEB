import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { api, ApiError } from '../api/client';
import type {
  AnesthesiaTypeMaster, DentalPackageMaster, DentalReplacementMaster, GeneralSettings,
  LabAnalyte, LabPackageMaster, LabReport, Procedure, ProcedureDepartment, VaccineMaster,
} from '../api/types';
import { VaccineEditorDialog, describeAgeDays } from '../masters/VaccineEditorDialog';
import { ProcedureEditorDialog } from '../masters/ProcedureEditorDialog';
import { DentalPackageEditorDialog } from '../masters/DentalPackageEditorDialog';
import { DentalReplacementEditorDialog } from '../masters/DentalReplacementEditorDialog';
import { AnesthesiaTypeEditorDialog } from '../masters/AnesthesiaTypeEditorDialog';
import { LabAnalyteEditorDialog } from '../masters/LabAnalyteEditorDialog';
import { LabReportEditorDialog } from '../masters/LabReportEditorDialog';
import { LabPackageEditorDialog } from '../masters/LabPackageEditorDialog';
import { ShortcutHints } from '../shell/ShortcutHints';
import { useHotkey } from '../shell/hotkeys';

type TabId =
  | 'vaccines' | 'procedures'
  | 'dental-packages' | 'dental-replacements' | 'anesthesia'
  | 'lab-analytes' | 'lab-reports' | 'lab-packages';

const DEPARTMENTS: ProcedureDepartment[] = ['General', 'Pediatrics', 'Dentist'];

/**
 * The clinic's own catalogue — the definitions everything else is billed and
 * recorded against.
 *
 * One screen composing every master rather than an editor buried in each
 * module's page, which is how the desktop has it (GeneralMasterViewModel plus
 * PathologyLabMasterViewModel): setting a clinic up is one sitting, done once,
 * usually by one person, and hunting six screens for it is the wrong shape
 * for that job.
 *
 * Tabs follow the module switches under Settings — a clinic that does not run
 * a lab is not shown three tabs of lab vocabulary.
 */
export function MastersPage() {
  const [general, setGeneral] = useState<GeneralSettings | null>(null);
  const [tab, setTab] = useState<TabId>('procedures');
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState('');
  const [error, setError] = useState<string | null>(null);

  // Rows for whichever tab is showing. One state each rather than a union,
  // because each editor needs its own row type back.
  const [vaccines, setVaccines] = useState<VaccineMaster[]>([]);
  const [procedures, setProcedures] = useState<Procedure[]>([]);
  const [department, setDepartment] = useState<ProcedureDepartment>('General');
  const [dentalPackages, setDentalPackages] = useState<DentalPackageMaster[]>([]);
  const [replacements, setReplacements] = useState<DentalReplacementMaster[]>([]);
  const [anesthesia, setAnesthesia] = useState<AnesthesiaTypeMaster[]>([]);
  const [analytes, setAnalytes] = useState<LabAnalyte[]>([]);
  const [labReports, setLabReports] = useState<LabReport[]>([]);
  const [labPackages, setLabPackages] = useState<LabPackageMaster[]>([]);

  // `undefined` = closed, `null` = new, a row = editing that row.
  const [editing, setEditing] = useState<unknown | undefined>(undefined);

  const searchRef = useRef<HTMLInputElement>(null);

  // F2 adds whatever the open tab is a master of, so one key covers all
  // eight lists rather than each needing its own.
  const G = 'Masters';
  useHotkey('f2', 'Add to this master', G, () => setEditing(null));
  useHotkey('f3', 'Search this master', G, () => {
    searchRef.current?.focus();
    searchRef.current?.select();
  }, { whileTyping: true });

  useEffect(() => {
    void api.get<GeneralSettings>('/api/settings/general').then(setGeneral).catch(() => {});
  }, []);

  const tabs = useMemo(() => {
    const list: { id: TabId; label: string; group: string }[] = [
      { id: 'procedures', label: 'Procedures', group: 'Shared' },
    ];
    if (general?.pediatricsEnabled) list.unshift({ id: 'vaccines', label: 'Vaccines', group: 'Pediatrics' });
    if (general?.dentistEnabled) {
      list.push(
        { id: 'dental-packages', label: 'Dental packages', group: 'Dentist' },
        { id: 'dental-replacements', label: 'Replacements', group: 'Dentist' },
        { id: 'anesthesia', label: 'Anesthesia', group: 'Dentist' },
      );
    }
    if (general?.pathologyLabEnabled) {
      list.push(
        { id: 'lab-analytes', label: 'Analytes', group: 'Lab' },
        { id: 'lab-reports', label: 'Lab reports', group: 'Lab' },
        { id: 'lab-packages', label: 'Lab packages', group: 'Lab' },
      );
    }
    return list;
  }, [general]);

  // A tab can vanish when a module is switched off elsewhere; landing on a
  // dead tab would show an empty screen with no way back.
  useEffect(() => {
    if (tabs.length > 0 && !tabs.some((t) => t.id === tab)) setTab(tabs[0].id);
  }, [tabs, tab]);

  const load = useCallback(async () => {
    const term = encodeURIComponent(search.trim());
    setError(null);
    try {
      switch (tab) {
        case 'vaccines':
          setVaccines(await api.get<VaccineMaster[]>(`/api/pediatrics/vaccines?activeOnly=false&term=${term}`));
          break;
        case 'procedures':
          setProcedures(await api.get<Procedure[]>(
            `/api/pediatrics/procedures?department=${department}&activeOnly=false&term=${term}`));
          break;
        case 'dental-packages':
          setDentalPackages(await api.get<DentalPackageMaster[]>(`/api/dentist/packages?term=${term}`));
          break;
        case 'dental-replacements':
          setReplacements(await api.get<DentalReplacementMaster[]>(`/api/dentist/replacements?term=${term}`));
          break;
        case 'anesthesia':
          setAnesthesia(await api.get<AnesthesiaTypeMaster[]>('/api/dentist/anesthesia-types'));
          break;
        case 'lab-analytes':
          setAnalytes(await api.get<LabAnalyte[]>(`/api/lab/analytes?term=${term}`));
          break;
        case 'lab-reports':
          setLabReports(await api.get<LabReport[]>(`/api/lab/reports?term=${term}`));
          break;
        case 'lab-packages':
          setLabPackages(await api.get<LabPackageMaster[]>(`/api/lab/packages?term=${term}`));
          break;
      }
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not load this master.');
    }
  }, [tab, search, department]);

  useEffect(() => { void load(); }, [load]);

  const afterSave = async (message: string) => {
    setEditing(undefined);
    await load();
    setStatus(message);
  };

  const newLabel = {
    'vaccines': '+ New vaccine',
    'procedures': '+ New procedure',
    'dental-packages': '+ New package',
    'dental-replacements': '+ New replacement',
    'anesthesia': '+ New type',
    'lab-analytes': '+ New analyte',
    'lab-reports': '+ New report',
    'lab-packages': '+ New package',
  }[tab];

  /** Deactivating is offered inline on the vaccine list because it is the
   * supported alternative to a delete the system refuses, and making someone
   * open an editor to do it hides the only route they have. */
  const toggleVaccine = async (v: VaccineMaster) => {
    try {
      await api.post(`/api/pediatrics/vaccines/${v.id}/active`, { active: !v.active });
      await load();
      setStatus(`${v.name} ${v.active ? 'deactivated' : 'reactivated'}.`);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not change that.');
    }
  };

  return (
    <div className="page wide">
      <div className="page-head">
        <div>
          <h1>Masters</h1>
          <p className="hint">
            Your clinic's own catalogue — what everything else is billed and recorded against.
            Nothing here changes a bill already raised.
          </p>
        </div>
        <div className="inline-form">
          {tab !== 'anesthesia' && (
            <input ref={searchRef} placeholder="Search" value={search} onChange={(e) => setSearch(e.target.value)} />
          )}
          <button type="button" className="primary" onClick={() => setEditing(null)}>{newLabel}</button>
        </div>
      </div>

      <nav className="tabs">
        {tabs.map((t) => (
          <button
            key={t.id}
            type="button"
            className={t.id === tab ? 'tab active' : 'tab'}
            onClick={() => { setTab(t.id); setSearch(''); setStatus(''); }}
          >
            {t.label}
          </button>
        ))}
      </nav>

      {error && <p className="auth-error">{error}</p>}
      {status && <p className="hint status-line">{status}</p>}

      <section className="card">
        {tab === 'vaccines' && (
          <table>
            <thead>
              <tr><th>Vaccine</th><th>Dose</th><th>Due at</th><th>Category</th><th>Order</th><th></th></tr>
            </thead>
            <tbody>
              {vaccines.map((v) => (
                <tr key={v.id} className={v.active ? undefined : 'row-inactive'}>
                  <td>{v.name}{!v.active && <span className="hint"> · inactive</span>}</td>
                  <td>{v.doseNumber}</td>
                  <td>{describeAgeDays(v.recommendedAgeDays)}</td>
                  <td>{v.category}</td>
                  <td>{v.sequenceOrder}</td>
                  <td className="row-actions">
                    <button type="button" className="ghost" onClick={() => setEditing(v)}>Edit</button>
                    <button type="button" className="ghost" onClick={() => void toggleVaccine(v)}>
                      {v.active ? 'Deactivate' : 'Reactivate'}
                    </button>
                  </td>
                </tr>
              ))}
              {vaccines.length === 0 && <tr><td colSpan={6}>No vaccines match.</td></tr>}
            </tbody>
          </table>
        )}

        {tab === 'procedures' && (
          <>
            <div className="inline-form">
              <span className="hint">Department</span>
              {DEPARTMENTS.map((d) => (
                <button
                  key={d}
                  type="button"
                  className={d === department ? 'pill active' : 'pill'}
                  onClick={() => setDepartment(d)}
                >
                  {d}
                </button>
              ))}
            </div>
            <table>
              <thead><tr><th>Procedure</th><th>Category</th><th>Price</th><th></th></tr></thead>
              <tbody>
                {procedures.map((p) => (
                  <tr key={p.id} className={p.active ? undefined : 'row-inactive'}>
                    <td>{p.name}{!p.active && <span className="hint"> · inactive</span>}</td>
                    <td>{p.category}</td>
                    <td>{p.price.toFixed(2)}</td>
                    <td>
                      <button type="button" className="ghost" onClick={() => setEditing(p)}>Edit</button>
                    </td>
                  </tr>
                ))}
                {procedures.length === 0 && (
                  <tr><td colSpan={4}>No {department} procedures yet.</td></tr>
                )}
              </tbody>
            </table>
          </>
        )}

        {tab === 'dental-packages' && (
          <table>
            <thead><tr><th>Package</th><th>Description</th><th>Price</th><th></th></tr></thead>
            <tbody>
              {dentalPackages.map((p) => (
                <tr key={p.id} className={p.active ? undefined : 'row-inactive'}>
                  <td>{p.name}{!p.active && <span className="hint"> · inactive</span>}</td>
                  <td>{p.description ?? ''}</td>
                  <td>{p.packagePrice.toFixed(2)}</td>
                  <td><button type="button" className="ghost" onClick={() => setEditing(p)}>Edit</button></td>
                </tr>
              ))}
              {dentalPackages.length === 0 && <tr><td colSpan={4}>No packages yet.</td></tr>}
            </tbody>
          </table>
        )}

        {tab === 'dental-replacements' && (
          <table>
            <thead><tr><th>Replacement</th><th>Category</th><th>Per unit</th><th></th></tr></thead>
            <tbody>
              {replacements.map((r) => (
                <tr key={r.id} className={r.active ? undefined : 'row-inactive'}>
                  <td>{r.name}{!r.active && <span className="hint"> · inactive</span>}</td>
                  <td>{r.category}</td>
                  <td>{r.unitCost.toFixed(2)}</td>
                  <td><button type="button" className="ghost" onClick={() => setEditing(r)}>Edit</button></td>
                </tr>
              ))}
              {replacements.length === 0 && <tr><td colSpan={4}>No replacements yet.</td></tr>}
            </tbody>
          </table>
        )}

        {tab === 'anesthesia' && (
          <table>
            <thead><tr><th>Type</th><th>Default cost</th><th></th></tr></thead>
            <tbody>
              {anesthesia.map((a) => (
                <tr key={a.id} className={a.active ? undefined : 'row-inactive'}>
                  <td>{a.name}{!a.active && <span className="hint"> · inactive</span>}</td>
                  <td>{a.defaultCost.toFixed(2)}</td>
                  <td><button type="button" className="ghost" onClick={() => setEditing(a)}>Edit</button></td>
                </tr>
              ))}
              {anesthesia.length === 0 && <tr><td colSpan={3}>No anesthesia types yet.</td></tr>}
            </tbody>
          </table>
        )}

        {tab === 'lab-analytes' && (
          <table>
            <thead>
              <tr><th>Analyte</th><th>Category</th><th>Units</th><th>Decimals</th><th>Order</th><th></th></tr>
            </thead>
            <tbody>
              {analytes.map((a) => (
                <tr key={a.id} className={a.active ? undefined : 'row-inactive'}>
                  <td>{a.name}{!a.active && <span className="hint"> · inactive</span>}</td>
                  <td>{a.category}</td>
                  <td>{a.units || '—'}</td>
                  <td>{a.decimalPlaces}</td>
                  <td>{a.sequenceOrder}</td>
                  <td><button type="button" className="ghost" onClick={() => setEditing(a)}>Edit</button></td>
                </tr>
              ))}
              {analytes.length === 0 && <tr><td colSpan={6}>No analytes yet.</td></tr>}
            </tbody>
          </table>
        )}

        {tab === 'lab-reports' && (
          <table>
            <thead><tr><th>Report</th><th>Category</th><th>Price</th><th>Order</th><th></th></tr></thead>
            <tbody>
              {labReports.map((r) => (
                <tr key={r.id} className={r.active ? undefined : 'row-inactive'}>
                  <td>{r.name}{!r.active && <span className="hint"> · inactive</span>}</td>
                  <td>{r.category}</td>
                  <td>{r.price.toFixed(2)}</td>
                  <td>{r.sequenceOrder}</td>
                  <td><button type="button" className="ghost" onClick={() => setEditing(r)}>Edit</button></td>
                </tr>
              ))}
              {labReports.length === 0 && <tr><td colSpan={5}>No reports yet.</td></tr>}
            </tbody>
          </table>
        )}

        {tab === 'lab-packages' && (
          <table>
            <thead><tr><th>Package</th><th>Price</th><th></th></tr></thead>
            <tbody>
              {labPackages.map((p) => (
                <tr key={p.id} className={p.active ? undefined : 'row-inactive'}>
                  <td>{p.name}{!p.active && <span className="hint"> · inactive</span>}</td>
                  <td>{p.packagePrice.toFixed(2)}</td>
                  <td><button type="button" className="ghost" onClick={() => setEditing(p)}>Edit</button></td>
                </tr>
              ))}
              {labPackages.length === 0 && <tr><td colSpan={3}>No packages yet.</td></tr>}
            </tbody>
          </table>
        )}
      </section>

      {editing !== undefined && tab === 'vaccines' && (
        <VaccineEditorDialog
          existing={editing as VaccineMaster | null}
          onClose={() => setEditing(undefined)}
          onSaved={afterSave}
        />
      )}
      {editing !== undefined && tab === 'procedures' && (
        <ProcedureEditorDialog
          existing={editing as Procedure | null}
          department={department}
          onClose={() => setEditing(undefined)}
          onSaved={(message, saidDepartment) => {
            // Follow the row. Saving a dentist procedure while the list is
            // filtered to General would otherwise report success over a table
            // that did not change.
            setDepartment(saidDepartment);
            void afterSave(message);
          }}
        />
      )}
      {editing !== undefined && tab === 'dental-packages' && (
        <DentalPackageEditorDialog
          existing={editing as DentalPackageMaster | null}
          onClose={() => setEditing(undefined)}
          onSaved={afterSave}
        />
      )}
      {editing !== undefined && tab === 'dental-replacements' && (
        <DentalReplacementEditorDialog
          existing={editing as DentalReplacementMaster | null}
          onClose={() => setEditing(undefined)}
          onSaved={afterSave}
        />
      )}
      {editing !== undefined && tab === 'anesthesia' && (
        <AnesthesiaTypeEditorDialog
          existing={editing as AnesthesiaTypeMaster | null}
          onClose={() => setEditing(undefined)}
          onSaved={afterSave}
        />
      )}
      {editing !== undefined && tab === 'lab-analytes' && (
        <LabAnalyteEditorDialog
          existing={editing as LabAnalyte | null}
          onClose={() => setEditing(undefined)}
          onSaved={afterSave}
        />
      )}
      {editing !== undefined && tab === 'lab-reports' && (
        <LabReportEditorDialog
          existing={editing as LabReport | null}
          onClose={() => setEditing(undefined)}
          onSaved={afterSave}
        />
      )}
      {editing !== undefined && tab === 'lab-packages' && (
        <LabPackageEditorDialog
          existing={editing as LabPackageMaster | null}
          onClose={() => setEditing(undefined)}
          onSaved={afterSave}
        />
      )}

      <ShortcutHints keys={[['f2', 'add'], ['f3', 'search']]} />
    </div>
  );
}
