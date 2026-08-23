import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import type { Patient } from '../api/types';
import { useHotkey } from './hotkeys';

/**
 * The patient picker, once.
 *
 * Appointments, Diagnostics, Pediatrics, Dentist and Pathology Lab each had
 * their own copy of this markup, identical apart from the line of text on
 * each row. Keeping five copies meant giving the keyboard to five files and
 * getting it subtly different in at least one of them.
 *
 * F3 puts the cursor here, the arrows walk the results and Enter chooses.
 * Those three are live only while this box has focus, so a page can hold
 * other fields — a quantity, a tooth number — without losing their own keys.
 */

const defaultLabel = (p: Patient): ReactNode => (
  <>
    {p.name} · {p.patientNo} · {p.age}
    {p.gender.charAt(0)}
    {p.phone && ` · ${p.phone}`}
  </>
);

export function PatientPicker({
  search,
  onSearchChange,
  matches,
  onPick,
  onNewPatient,
  group,
  label = defaultLabel,
  placeholder = 'Name or phone number',
  newPatientLabel = '+ New patient',
}: {
  search: string;
  onSearchChange: (value: string) => void;
  matches: Patient[];
  onPick: (patient: Patient) => void;
  onNewPatient?: () => void;
  /** The shortcut sheet's heading for this screen. */
  group: string;
  label?: (patient: Patient) => ReactNode;
  placeholder?: string;
  newPatientLabel?: string;
}) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [highlight, setHighlight] = useState(-1);

  // A new search starts on nothing, so a stray Enter chooses nobody.
  useEffect(() => { setHighlight(-1); }, [search]);

  const hasFocus = () => document.activeElement === inputRef.current;
  const walking = useMemo(
    () => () => hasFocus() && matches.length > 0,
    [matches.length],
  );

  useHotkey('f3', 'Find a patient', group, () => {
    inputRef.current?.focus();
    inputRef.current?.select();
  }, { whileTyping: true });

  useHotkey('arrowdown', 'Next match', group, () => {
    setHighlight((i) => (i + 1) % matches.length);
  }, { whileTyping: true, when: walking });

  useHotkey('arrowup', 'Previous match', group, () => {
    setHighlight((i) => (i <= 0 ? matches.length - 1 : i - 1));
  }, { whileTyping: true, when: walking });

  // Only claims Enter once a row is actually highlighted; otherwise the key
  // belongs to whatever form the picker is sitting in.
  useHotkey('enter', 'Choose the highlighted patient', group, () => {
    const patient = matches[highlight];
    if (patient) onPick(patient);
  }, { whileTyping: true, when: () => walking() && highlight >= 0 });

  return (
    <div className="patient-picker">
      <div className="inline-form">
        <input
          ref={inputRef}
          placeholder={placeholder}
          value={search}
          onChange={(e) => onSearchChange(e.target.value)}
        />
        {onNewPatient && (
          <button type="button" onClick={onNewPatient}>{newPatientLabel}</button>
        )}
      </div>

      {matches.length > 0 && (
        <ul className="pick-list" role="listbox">
          {matches.map((p, i) => (
            <li key={p.id}>
              <button
                type="button"
                role="option"
                aria-selected={i === highlight}
                className={i === highlight ? 'active-match' : undefined}
                onMouseEnter={() => setHighlight(i)}
                onClick={() => onPick(p)}
              >
                {label(p)}
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
