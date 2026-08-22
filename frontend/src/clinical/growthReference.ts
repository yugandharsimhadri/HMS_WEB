/**
 * Approximate median growth-by-age reference curves — a faithful port of
 * `Pharma.App.Charts.GrowthReference`, control points copied verbatim.
 *
 * These are commonly used quick-estimation milestones from general
 * pediatric teaching (e.g. weight(kg) ≈ (age in months + 9) / 2 for 3–12
 * months), **not** the full WHO percentile (LMS) tables, which this app
 * does not embed. They exist to give the growth chart something sensible to
 * compare a patient's own measurements against at a glance — a rough
 * middle-of-the-road line, not a diagnostic percentile band. A doctor
 * reading a real growth concern should still go to a proper WHO/IAP
 * percentile chart, not this one.
 *
 * Lives here rather than on the server for the same reason `gst.ts` and
 * `doseMath.ts` do: the chart is drawn client-side, so the arithmetic has
 * to be where the chart is. If the C# side ever changes, change both.
 */

type Point = readonly [months: number, value: number];

const WEIGHT_KG: readonly Point[] = [
  [0, 3.25], [3, 6], [6, 7.5], [9, 9], [12, 10.5], [24, 12], [36, 14],
  [48, 16], [60, 18], [72, 20], [84, 22], [96, 25.5], [108, 29],
  [120, 32.5], [132, 36], [144, 39.5],
];

const HEIGHT_CM: readonly Point[] = [
  [0, 50], [12, 75], [24, 87], [36, 95], [48, 101], [60, 107], [72, 113],
  [84, 119], [96, 125], [108, 131], [120, 137], [132, 143], [144, 149],
];

const HEAD_CIRCUMFERENCE_CM: readonly Point[] = [
  [0, 35], [3, 40], [6, 43], [9, 45], [12, 46], [18, 47], [24, 48],
  [36, 49], [48, 50], [60, 50.5], [72, 51], [96, 51.4], [120, 51.8], [144, 52],
];

/** The oldest age these curves cover — 12 years, past which growth
 * charting belongs to a different (adult-adjacent) reference entirely and
 * Pediatrics itself stops being the relevant module. */
export const MAX_AGE_MONTHS = 144;

export type GrowthMetric = 'Weight' | 'Height' | 'HeadCircumference';

/** Piecewise-linear between the named control points. */
function interpolate(points: readonly Point[], ageMonths: number): number {
  const x = Math.min(Math.max(ageMonths, points[0][0]), points[points.length - 1][0]);

  for (let i = 0; i < points.length - 1; i++) {
    const [x0, y0] = points[i];
    const [x1, y1] = points[i + 1];
    if (x < x0 || x > x1) continue;

    const t = x1 - x0 < 0.0001 ? 0 : (x - x0) / (x1 - x0);
    return y0 + t * (y1 - y0);
  }

  return points[points.length - 1][1];
}

export function expectedFor(metric: GrowthMetric, ageMonths: number): number {
  if (metric === 'Weight') return interpolate(WEIGHT_KG, ageMonths);
  if (metric === 'Height') return interpolate(HEIGHT_CM, ageMonths);
  return interpolate(HEAD_CIRCUMFERENCE_CM, ageMonths);
}

export const unitFor = (metric: GrowthMetric): string => (metric === 'Weight' ? 'kg' : 'cm');

/** The desktop's average-days-per-month divisor, kept so a chart drawn here
 * lands on the same x as the one drawn there. */
export const DAYS_PER_MONTH = 30.4368;
