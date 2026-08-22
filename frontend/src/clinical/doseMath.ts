/**
 * Prescriptions are written in individual units — six tablets, not half a
 * strip. The pharmacy buys and prices in strips. This works out how many
 * units a course comes to so the two sides never have to be reconciled by
 * hand.
 *
 * A direct port of Pharma.Core.DoseMath. It lives on the client as well as
 * the server because the consultation screen has to show the course as the
 * doctor types it — a round trip per keystroke would be worse, and the
 * server still recomputes nothing it wasn't given.
 */

function tryDose(text: string): number | null {
  const t = text.trim();

  // Halves and quarters are written as fractions on a prescription.
  const slash = t.indexOf('/');
  if (slash > 0) {
    const top = Number(t.slice(0, slash));
    const bottom = Number(t.slice(slash + 1));
    if (Number.isFinite(top) && Number.isFinite(bottom) && bottom !== 0) return top / bottom;
    return null;
  }

  const value = Number(t);
  return Number.isFinite(value) ? value : null;
}

/**
 * Doses a day from the way it was written. Understands "1-0-1" style and the
 * usual Latin abbreviations. Returns null when it cannot tell, e.g. "SOS" —
 * an as-needed dose has no computable course length.
 */
export function dosesPerDay(frequency: string | null | undefined): number | null {
  if (!frequency?.trim()) return null;

  const text = frequency.trim().toUpperCase();

  switch (text) {
    case 'OD': case 'HS': case 'QD': return 1;
    case 'BD': case 'BID': return 2;
    case 'TDS': case 'TID': return 3;
    case 'QID': case 'QDS': return 4;
    case 'SOS': case 'PRN': case 'STAT': return null;
  }

  const parts = text.split('-').filter((p) => p.length > 0);
  if (parts.length < 2) return null;

  let total = 0;
  for (const part of parts) {
    const dose = tryDose(part);
    if (dose === null) return null;
    total += dose;
  }

  return total > 0 ? total : null;
}

/**
 * Units to dispense for a course. Rounded up, because half a tablet cannot be
 * handed over and running short mid-course is worse than one spare.
 */
export function unitsForCourse(frequency: string | null | undefined, days: number): number | null {
  if (days <= 0) return null;

  const perDay = dosesPerDay(frequency);
  if (perDay === null || perDay <= 0) return null;

  return Math.ceil(perDay * days);
}

/** Reads the unit off what is printed on the pack: "10 TAB" is tablets. */
function unitWordFrom(packLabel: string | null | undefined): string {
  const text = (packLabel ?? '').toUpperCase();
  if (text.includes('CAP')) return 'capsules';
  if (text.includes('TAB')) return 'tablets';
  if (text.includes('ML')) return 'bottles';
  return 'units';
}

/**
 * "3 strips + 4" — how stock and bill lines read to a human.
 * Port of PackMath.Describe.
 */
export function describePacks(
  quantityUnits: number,
  unitsPerPack: number,
  packLabel?: string | null,
  unitName?: string | null,
): string {
  if (quantityUnits < 0) return '0';
  if (unitsPerPack <= 1) return String(quantityUnits);

  const packs = Math.floor(quantityUnits / unitsPerPack);
  const loose = quantityUnits % unitsPerPack;
  const pack = packLabel?.trim() || 'pack';
  const each = unitName?.trim() || unitWordFrom(packLabel);

  if (packs === 0) return `${loose} ${each}`;
  if (loose === 0) return `${packs} × ${pack}`;
  return `${packs} × ${pack} + ${loose} ${each}`;
}

/** Half and quarter doses are normal on a paediatric prescription. */
export const DOSE_OPTIONS = ['0', '1/4', '1/2', '1', '2'];
