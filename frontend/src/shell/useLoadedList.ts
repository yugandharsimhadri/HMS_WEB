import { useCallback, useEffect, useState } from 'react';
import { api } from '../api/client';

/**
 * A list from the server, plus whether it actually arrived.
 *
 * Master lists were loaded with `.catch(() => {})`, which conflates two very
 * different situations into the same empty array. The screens then report the
 * empty array confidently: *"No packages set up yet — they arrive with the
 * Masters module."* When the request had in fact failed, that sentence is a
 * false statement about the clinic's data, and it sends somebody to Masters to
 * create records that are already there.
 *
 * Silence is defensible for a genuinely optional enrichment — the category
 * suggestions in an editor dialog, where an empty list costs a person nothing
 * but a bit of typing. It is not defensible for the list a screen is about.
 *
 * `failed` is what the difference is for: a screen can say "could not be
 * loaded" where it would otherwise have said "there are none".
 */
export function useLoadedList<T>(path: string, enabled = true) {
  const [items, setItems] = useState<T[]>([]);
  const [failed, setFailed] = useState(false);
  const [loading, setLoading] = useState(enabled);

  const reload = useCallback(async () => {
    if (!enabled) return;
    setLoading(true);
    try {
      setItems(await api.get<T[]>(path));
      setFailed(false);
    } catch {
      // Keeps whatever was already loaded. A failed refresh should not make
      // rows vanish from under somebody mid-shift; it should say so instead.
      setFailed(true);
    } finally {
      setLoading(false);
    }
  }, [path, enabled]);

  useEffect(() => { void reload(); }, [reload]);

  return { items, failed, loading, reload, setItems };
}
