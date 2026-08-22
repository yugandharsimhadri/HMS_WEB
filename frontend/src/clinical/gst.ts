/**
 * Indian retail pharmacy GST, ported from Pharma.Core's GstCalculator and
 * PackMath so the counter can show a running total as lines are added.
 *
 * Medicines are sold at the MRP printed on the pack, and that MRP is
 * inclusive of GST — so tax is back-calculated out of the line value, never
 * added on top of it.
 *
 * The server recomputes all of this when the bill is saved and its answer is
 * the one that gets printed; this exists so the operator is not staring at a
 * total that only appears after saving. The two must agree, which is why
 * this is a faithful port rather than an approximation — including the
 * whole-pack pricing rule, which is what stopped nine tablets being billed
 * as nine strips.
 */

/**
 * Round to paise, half away from zero — .NET's MidpointRounding.AwayFromZero.
 *
 * The epsilon matters: in binary floating point 1.005 * 100 is 100.4999…,
 * so a naive Math.round gives 1.00 where .NET's decimal gives 1.01. Nudging
 * by one ulp-ish before rounding restores the decimal answer for the money
 * magnitudes a pharmacy bill deals in.
 */
function round2(value: number): number {
  const scaled = value * 100;
  const nudged = scaled >= 0 ? scaled + 1e-9 : scaled - 1e-9;
  return Math.round(nudged) / 100;
}

export function unitPrice(packMrp: number, unitsPerPack: number): number {
  return unitsPerPack <= 1 ? packMrp : round2(packMrp / unitsPerPack);
}

/**
 * What a quantity of base units costs before discount.
 *
 * Whole packs price from the pack MRP rather than by multiplying a rounded
 * unit price, so a full strip always costs exactly what is printed on it.
 * Only the remainder is priced per unit — and that remainder is capped at
 * the pack price, because nobody may be charged more for part of something
 * than for all of it.
 */
export function gross(packMrp: number, unitsPerPack: number, quantityUnits: number): number {
  if (quantityUnits <= 0 || packMrp <= 0) return 0;
  if (unitsPerPack <= 1) return round2(packMrp * quantityUnits);

  const packs = Math.floor(quantityUnits / unitsPerPack);
  const loose = quantityUnits % unitsPerPack;

  let loosePrice = round2(loose * unitPrice(packMrp, unitsPerPack));
  if (loosePrice > packMrp) loosePrice = packMrp;

  return round2(packs * packMrp + loosePrice);
}

export interface LineAmounts {
  gross: number;
  discount: number;
  net: number;
  taxable: number;
  gst: number;
  cgst: number;
  sgst: number;
}

export function lineAmounts(
  mrp: number, unitsPerPack: number, quantity: number, discountPercent: number, gstRate: number,
): LineAmounts {
  const g = gross(mrp, unitsPerPack, quantity);
  const discount = round2((g * discountPercent) / 100);
  const net = g - discount;

  const taxable = round2((net * 100) / (100 + gstRate));
  const gstAmount = net - taxable;
  const half = round2(gstAmount / 2);

  // Any half-paise remainder goes to CGST so cgst + sgst === gst exactly.
  return { gross: g, discount, net, taxable, gst: gstAmount, cgst: gstAmount - half, sgst: half };
}

export interface BillAmounts {
  gross: number;
  discount: number;
  taxable: number;
  cgst: number;
  sgst: number;
  roundOff: number;
  net: number;
}

export function billAmounts(lines: LineAmounts[]): BillAmounts {
  let g = 0, discount = 0, taxable = 0, cgst = 0, sgst = 0, net = 0;

  for (const l of lines) {
    g += l.gross;
    discount += l.discount;
    taxable += l.taxable;
    cgst += l.cgst;
    sgst += l.sgst;
    net += l.net;
  }

  // The printed bill is rounded to the rupee, and the difference is shown as
  // its own line so the arithmetic on the paper still adds up.
  const rounded = Math.round(round2(net));
  return {
    gross: round2(g), discount: round2(discount), taxable: round2(taxable),
    cgst: round2(cgst), sgst: round2(sgst),
    roundOff: round2(rounded - net), net: rounded,
  };
}
