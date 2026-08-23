import { useEffect } from 'react';
import { useSyncExternalStore } from 'react';

/**
 * The keyboard layer.
 *
 * Every shortcut in the app registers itself here rather than attaching its
 * own listener, for two reasons. The shortcut sheet behind `?` is generated
 * from this registry, so it lists what is genuinely bound right now instead
 * of a hand-kept list that drifts out of date. And a single listener can
 * enforce one rule consistently: a shortcut must never fire while somebody
 * is typing.
 */

/** The group for shortcuts that work on every screen. Named once so the
 *  sheet can order it last without matching on a loose string. */
export const ANYWHERE = 'Anywhere';

export interface Shortcut {
  /** Normalised combo, e.g. "mod+k", "f2", "shift+/" */
  combo: string;
  /** What it does, in the words the user would use. */
  label: string;
  /** Grouping for the sheet. */
  group: string;
  handler: (e: KeyboardEvent) => void;
  /** Fire even while a field has focus. Only Escape and the palette do. */
  whileTyping?: boolean;
  /**
   * Checked before the key is claimed. A shortcut that only applies in one
   * place — arrowing a search box's results, say — must not swallow the key
   * everywhere else, and deciding that *after* preventDefault would already
   * have broken the number spinner next door.
   */
  when?: () => boolean;
}

const registry = new Map<string, Shortcut>();
const listeners = new Set<() => void>();

/** Registration order is the sheet's order, so it reads front-desk-first. */
let seq = 0;
const order = new Map<string, number>();

function announce() {
  for (const l of listeners) l();
}

function subscribe(l: () => void) {
  listeners.add(l);
  return () => {
    listeners.delete(l);
  };
}

let snapshot: Shortcut[] = [];
let dirty = true;

function getSnapshot(): Shortcut[] {
  if (dirty) {
    snapshot = [...registry.values()].sort(
      (a, b) => (order.get(a.combo) ?? 0) - (order.get(b.combo) ?? 0),
    );
    dirty = false;
  }
  return snapshot;
}

/** Everything currently bound, for the shortcut sheet. */
export function useShortcutList(): Shortcut[] {
  return useSyncExternalStore(subscribe, getSnapshot);
}

/** "Ctrl" on Windows and Linux, "Cmd" on a Mac, without asking twice. */
export const isMac =
  typeof navigator !== 'undefined' && /Mac|iPhone|iPad/.test(navigator.platform || '');

export const modLabel = isMac ? 'Cmd' : 'Ctrl';

/** Render a combo the way a person would read it: "mod+k" -> "Ctrl K". */
export function describeCombo(combo: string): string {
  return combo
    .split('+')
    .map((part) => {
      if (part === 'mod') return modLabel;
      if (part === 'shift') return 'Shift';
      if (part === 'alt') return isMac ? 'Option' : 'Alt';
      if (part === 'escape') return 'Esc';
      if (part === 'arrowup') return 'Up';
      if (part === 'arrowdown') return 'Down';
      if (part === 'enter') return 'Enter';
      if (/^f\d+$/.test(part)) return part.toUpperCase();
      if (part === '/') return '/';
      if (part === '?') return '?';
      return part.toUpperCase();
    })
    .join(' ');
}

function comboOf(e: KeyboardEvent): string {
  const parts: string[] = [];
  if (e.ctrlKey || e.metaKey) parts.push('mod');
  if (e.altKey) parts.push('alt');

  const key = e.key;

  // Shifted punctuation is identified by the character it produces, not by
  // Shift plus the key underneath — "?" is Shift+/ on one layout and its own
  // key on another, and only the character is stable across both.
  if (key.length === 1 && !/[a-z0-9]/i.test(key)) return [...parts, key].join('+');

  // For everything else Shift is a real modifier and has to be part of the
  // combo. Reading it off the character alone fails here: Shift+D arrives as
  // "D", one character long, and dropping the modifier silently turns
  // Ctrl+Shift+D into Ctrl+D.
  if (e.shiftKey) parts.push('shift');

  return [...parts, key.toLowerCase()].join('+');
}

function isTyping(target: EventTarget | null): boolean {
  const el = target as HTMLElement | null;
  if (!el) return false;
  const tag = el.tagName;
  return (
    tag === 'INPUT' ||
    tag === 'TEXTAREA' ||
    tag === 'SELECT' ||
    el.isContentEditable === true
  );
}

let attached = false;

function attach() {
  if (attached) return;
  attached = true;

  window.addEventListener(
    'keydown',
    (e) => {
      const combo = comboOf(e);
      const hit = registry.get(combo);
      if (!hit) return;

      if (isTyping(e.target) && !hit.whileTyping) return;

      // Asked before the key is claimed, never after: returning here leaves
      // the keystroke to whatever would normally have handled it.
      if (hit.when && !hit.when()) return;

      // A browser shortcut we are deliberately taking over (Ctrl K is
      // "search" in some browsers) has to be stopped, or both fire.
      e.preventDefault();
      hit.handler(e);
    },
    // Capture, so a dialog that stops propagation on its own container
    // cannot silently disable Escape.
    true,
  );
}

/**
 * Bind one shortcut for as long as the component is mounted.
 *
 * `deps` works like useEffect's: pass anything the handler closes over, or
 * the binding keeps calling the first render's copy.
 */
export function useHotkey(
  combo: string,
  label: string,
  group: string,
  handler: (e: KeyboardEvent) => void,
  opts: { whileTyping?: boolean; enabled?: boolean; when?: () => boolean } = {},
) {
  const { whileTyping = false, enabled = true, when } = opts;

  useEffect(() => {
    if (!enabled) return;
    attach();

    const key = combo.toLowerCase();

    // Re-stamped on every registration, not just the first. Keeping the
    // first number a combo ever got meant the sheet ordered by whichever
    // screen happened to use F2 first, so a later screen's own keys were
    // listed after keys it shares with an earlier one. Effects run
    // child-before-parent, so this gives page shortcuts first, then the
    // ones that work anywhere.
    order.set(key, seq++);

    registry.set(key, { combo: key, label, group, handler, whileTyping, when });
    dirty = true;
    announce();

    return () => {
      // Only clear it if we are still the owner. Two screens binding F2 in
      // sequence during a route change must not leave the key dead.
      if (registry.get(key)?.handler === handler) {
        registry.delete(key);
        dirty = true;
        announce();
      }
    };
  }, [combo, label, group, handler, whileTyping, enabled, when]);
}
