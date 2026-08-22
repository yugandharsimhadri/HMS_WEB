import type { DispensingUnit } from '../api/types';

/**
 * Reads a countable pack size out of a vendor's free-text packing, or null
 * when it does not describe a count. Port of PackMath.UnitsFromPacking.
 *
 * "10 TAB" and "30s" are counts. "60ML" and "1GR" are a volume and a weight —
 * a syrup bottle is not sixty sellable units, so those deliberately return
 * null rather than a guess. Guessing here would wreck both stock and price.
 */
export function unitsFromPacking(packing: string | null | undefined): number | null {
  if (!packing?.trim()) return null;

  const text = packing.trim().toUpperCase();

  // A measured pack is one sellable thing however large the number is.
  if (/\d\s*(ML|L|GM|GR|G|MG|KG|MCG)$/.test(text)) return null;

  // "1X10", "2 X 15" — the second number is what is in the pack.
  const multiplied = /^(\d+)\s*X\s*(\d+)/.exec(text);
  if (multiplied) return sane(Number(multiplied[2]));

  // "30S", "10'S", "10 TAB", "15 CAPSULES", "6 PCS"
  const counted = /^(\d+)\s*'?\s*(S|TAB|TABS|TABLET|TABLETS|CAP|CAPS|CAPSULE|CAPSULES|PC|PCS|NO|NOS)\.?$/.exec(text);
  if (counted) return sane(Number(counted[1]));

  return null;
}

function sane(units: number): number | null {
  return units > 1 && units <= 1000 ? units : null;
}

/**
 * Singular or plural name for a dispensing unit — port of
 * DispensingUnits.Name.
 *
 * "Others" is already plural and is not the name of a thing you can hand
 * over, so it reads as a plain unit rather than "3 otherss".
 */
export function unitWordFor(unit: DispensingUnit, count = 2): string {
  if (unit === 'Others') return count === 1 ? 'unit' : 'units';
  const base = unit.toLowerCase();
  return count === 1 ? base : `${base}s`;
}

/** Every dispensing unit, in the order the enum declares them — what the
 * medicine editor offers. */
export const DISPENSING_UNITS: DispensingUnit[] = [
  'Tablet', 'Capsule', 'Bottle', 'Sachet', 'Tube', 'Vial',
  'Piece', 'Syrup', 'Moisturizer', 'Soap', 'Others',
];
