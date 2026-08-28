import { useEffect, useLayoutEffect, useRef, useState } from 'react';

/**
 * What every modal in this application was missing.
 *
 * Twenty-three dialogs each hand-wrote the same overlay skeleton, and all
 * twenty-three behaved the same way in three respects — badly:
 *
 *   - **Escape did nothing.** In an application built around F2/F3/F4/F6/F8, a
 *     command palette and a live shortcut sheet, somebody trained by all that
 *     presses Escape to back out of a fee dialog. The only Escape binding was
 *     the shell's, and it closed the palette and the shortcut sheet.
 *   - **Focus was not trapped.** Tab walked out of the modal and into the page
 *     behind it: disorienting with a mouse, unusable with a screen reader.
 *   - **Focus was not restored.** Closing dropped focus to the document, so
 *     the next Tab started from the top of the page rather than the button
 *     that opened the dialog.
 *
 * Delivered as a hook rather than a `<Dialog>` component on purpose. The
 * duplicated markup is five divs and a heading — cosmetic, and each dialog
 * styles its own body. The missing behaviour is the actual defect, and a hook
 * fixes it in every dialog by adding two lines to each, with no markup moved
 * and no stylesheet rule at risk. Rewriting twenty-three screens to gain a
 * shared wrapper would have been a much larger change for a much smaller
 * return, and a far better chance of breaking one of them.
 *
 * Usage: spread the returned ref onto the `overlay-card` element.
 */
export function useModalBehaviour(onClose: () => void) {
  const cardRef = useRef<HTMLDivElement>(null);

  // Captured in a state initialiser, which runs during the first render —
  // before React commits the DOM.
  //
  // An effect is too late. Several dialogs mark their first field `autoFocus`,
  // and React applies that during commit, so by the time any effect runs
  // `document.activeElement` is already the dialog's own input. Capturing
  // there recorded the field this hook was about to leave, then tried to
  // restore focus to an element that had just been unmounted — so focus fell
  // to the body and the next Tab started from the top of the page. Exactly the
  // bug this hook exists to fix, reintroduced one layer down.
  const [opener] = useState<HTMLElement | null>(
    () => document.activeElement as HTMLElement | null,
  );

  // Held in a ref so the effect does not re-run — and re-steal focus — every
  // time the parent re-renders with a fresh inline onClose.
  const onCloseRef = useRef(onClose);
  useLayoutEffect(() => { onCloseRef.current = onClose; });

  useEffect(() => {

    const focusables = () =>
      Array.from(
        cardRef.current?.querySelectorAll<HTMLElement>(
          'a[href], button:not([disabled]), input:not([disabled]):not([type="hidden"]),' +
            ' select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])',
        ) ?? [],
      ).filter((el) => el.offsetParent !== null);

    // Only claim focus if it is not already inside the card. Several dialogs
    // mark their own first field autoFocus, and overriding that would move the
    // cursor off the field the screen was designed to open on.
    if (!cardRef.current?.contains(document.activeElement)) {
      (focusables()[0] ?? cardRef.current)?.focus();
    }

    const onKeyDown = (e: KeyboardEvent) => {
      // Only act for the dialog the key actually happened in. Two dialogs are
      // stacked in a couple of places — the medicine editor opens over the
      // catalogue — and without this both would close on one Escape.
      if (!cardRef.current?.contains(e.target as Node)) return;

      if (e.key === 'Escape') {
        // Stopped as well as prevented: the shell's Escape binding listens in
        // the capture phase, so without this both would fire — closing this
        // dialog and the palette behind it.
        e.preventDefault();
        e.stopPropagation();
        onCloseRef.current();
        return;
      }

      if (e.key !== 'Tab') return;

      // Recomputed per press rather than cached: a dialog's fields come and go
      // — a payment mode that reveals a reference-number box, a list that
      // grows a row.
      const items = focusables();
      if (items.length === 0) return;

      const first = items[0];
      const last = items[items.length - 1];

      if (e.shiftKey && document.activeElement === first) {
        e.preventDefault();
        last.focus();
      } else if (!e.shiftKey && document.activeElement === last) {
        e.preventDefault();
        first.focus();
      }
    };

    // Capture, for the same reason the shell listens in capture: to run before
    // anything inside the dialog can swallow the key.
    document.addEventListener('keydown', onKeyDown, true);

    return () => {
      document.removeEventListener('keydown', onKeyDown, true);
      // Guarded: the opener may itself have been unmounted by the action just
      // taken — deleting the row whose Edit button opened this.
      if (opener?.isConnected) opener.focus();
    };
  }, [opener]);

  return cardRef;
}
