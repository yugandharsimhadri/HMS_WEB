import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { api } from '../api/client';
import type { Patient } from '../api/types';

/**
 * Find a patient by name, phone or number.
 *
 * This was written five times — Appointments, Dentist, Diagnostics, Pathology
 * Lab and Pediatrics each carried its own copy of the same debounce, the same
 * fetch and the same "if exactly one matched, take it" rule. The copies had
 * already drifted: Diagnostics cleared the page error on selecting a patient
 * and the others did not, which is the kind of difference nobody chooses, they
 * just end up with.
 *
 * `PatientPicker` had already been extracted for the markup. This is the other
 * half of the same job.
 *
 * Two things the copies did not do:
 *
 *   - **Cancel.** Nothing aborted the previous request, so typing "Ra" then
 *     "Ram" quickly could paint the results for "Ra" over the results for
 *     "Ram" if the first reply arrived second. Rare on a LAN; much less rare
 *     over a tunnel to a clinic's own server.
 *   - **Stop after unmount.** Navigating away mid-search set state on a gone
 *     component.
 */
interface Options {
  /** How many to ask for. The picker shows a short list. */
  take?: number;
  /** Milliseconds of quiet before asking. */
  delay?: number;
  /**
   * Called when exactly one patient matched.
   *
   * One match is the overwhelmingly common case at a front desk — somebody
   * types a phone number — and making them click the only row is a keystroke
   * that carries no information. Each screen does something slightly different
   * with the patient it gets, which is why this is a callback rather than
   * behaviour baked in here.
   */
  onSingleMatch?: (patient: Patient) => void;
  /** Set false to stop searching — once a patient is chosen, say. */
  enabled?: boolean;
}

interface Result {
  search: string;
  setSearch: (value: string) => void;
  matches: Patient[];
  /** Clears both the box and the list, for "start again" on a screen. */
  reset: () => void;
}

export function usePatientSearch({
  take = 20,
  delay = 250,
  onSingleMatch,
  enabled = true,
}: Options = {}): Result {
  const [search, setSearch] = useState('');
  const [matches, setMatches] = useState<Patient[]>([]);

  // In a ref so a caller passing an inline arrow — all of them do — does not
  // restart the debounce on every render.
  const onSingleMatchRef = useRef(onSingleMatch);
  useLayoutEffect(() => { onSingleMatchRef.current = onSingleMatch; });

  useEffect(() => {
    if (!enabled) return;

    const term = search.trim();
    if (!term) {
      setMatches([]);
      return;
    }

    const controller = new AbortController();
    const handle = setTimeout(() => {
      void (async () => {
        try {
          const found = await api.get<Patient[]>(
            `/api/patients?term=${encodeURIComponent(term)}&take=${take}`,
            controller.signal,
          );
          setMatches(found);
          if (found.length === 1) {
            onSingleMatchRef.current?.(found[0]);
            // Clearing the box is what all five screens did for themselves,
            // so it belongs here. Emptying the term also re-runs this effect
            // and empties the list, which is why matches are not cleared
            // separately.
            setSearch('');
          }
        } catch {
          // Covers the abort as well as a real failure. An abort is this hook
          // deciding the answer is no longer wanted, which is not something to
          // report; a genuine failure leaves the list empty, and the screen
          // says "no matches" rather than inventing an error for a search box.
          if (!controller.signal.aborted) setMatches([]);
        }
      })();
    }, delay);

    return () => {
      clearTimeout(handle);
      controller.abort();
    };
  }, [search, take, delay, enabled]);

  return {
    search,
    setSearch,
    matches,
    reset: () => {
      setSearch('');
      setMatches([]);
    },
  };
}
