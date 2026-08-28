import { useMemo } from 'react';
import type { GrowthMeasurement, Patient } from '../api/types';
import {
  DAYS_PER_MONTH,
  MAX_AGE_MONTHS,
  expectedFor,
  unitFor,
  type GrowthMetric,
} from '../clinical/growthReference';

interface Props {
  patient: Patient;
  history: GrowthMeasurement[];
  metric: GrowthMetric;
}

const WIDTH = 480;
const HEIGHT = 190;
const DOT_RADIUS = 3;
const SAMPLES = 24;

/**
 * Expected (approximate reference) against this patient's own measurements,
 * on **shared axes** — two series each stretched to its own range would
 * look comparable while being nothing of the sort.
 *
 * The desktop builds WPF `PathGeometry` strings via `ChartGeometry`; those
 * mean nothing in a browser, so the geometry is recomputed here as SVG. The
 * rules it encodes — sampling, padding, the age cap — are the desktop's.
 */
export function GrowthChart({ patient, history, metric }: Props) {
  const chart = useMemo(() => {
    /** Age in months at a date — from date of birth when on file (precise,
     * which matters most under a year old), falling back to the whole-year
     * age otherwise (coarse, but usable for an older child). */
    const ageMonthsAt = (on: string): number => {
      if (patient.dateOfBirth) {
        const dob = new Date(patient.dateOfBirth);
        const days = (new Date(on).getTime() - dob.getTime()) / 86_400_000;
        return Math.max(0, days / DAYS_PER_MONTH);
      }
      return (patient.age ?? 0) * 12;
    };

    const valueOf = (g: GrowthMeasurement): number | null => {
      if (metric === 'Weight') return g.weightKg;
      if (metric === 'Height') return g.heightCm;
      return g.headCircumferenceCm;
    };

    // A measurement with no reading for the selected metric is skipped, not
    // plotted as zero — a child with no head-circumference reading has no
    // point, not a point on the floor.
    const measurements = [...history]
      .sort((a, b) => a.measuredOn.localeCompare(b.measuredOn))
      .map((g) => ({ ageMonths: ageMonthsAt(g.measuredOn), value: valueOf(g) }))
      .filter((m): m is { ageMonths: number; value: number } => m.value !== null);

    if (measurements.length === 0) return null;

    const minAge = 0;
    const maxAge = Math.min(Math.max(...measurements.map((m) => m.ageMonths), 1), MAX_AGE_MONTHS);

    const expected = Array.from({ length: SAMPLES + 1 }, (_, i) => {
      const age = minAge + ((maxAge - minAge) * i) / SAMPLES;
      return { age, value: expectedFor(metric, age) };
    });

    const all = [...measurements.map((m) => m.value), ...expected.map((e) => e.value)];
    let yMin = Math.min(...all);
    let yMax = Math.max(...all);
    // A series that barely moves would otherwise collapse to a flat line
    // pinned to the frame.
    if (yMax - yMin < 0.5) { yMin -= 1; yMax += 1; }
    const pad = (yMax - yMin) * 0.1;
    yMin -= pad;
    yMax += pad;

    const x = (age: number) =>
      maxAge - minAge < 0.0001 ? 0 : ((age - minAge) / (maxAge - minAge)) * WIDTH;
    const y = (value: number) =>
      yMax - yMin < 0.0001 ? HEIGHT : HEIGHT - ((value - yMin) / (yMax - yMin)) * HEIGHT;

    const line = (points: { age: number; value: number }[]) =>
      points.map((p) => `${x(p.age).toFixed(2)},${y(p.value).toFixed(2)}`).join(' ');

    return {
      expected: line(expected),
      patient: line(measurements.map((m) => ({ age: m.ageMonths, value: m.value }))),
      dots: measurements.map((m) => ({ cx: x(m.ageMonths), cy: y(m.value) })),
      yTop: `${yMax.toFixed(1)} ${unitFor(metric)}`,
      yBottom: `${yMin.toFixed(1)} ${unitFor(metric)}`,
      xRight: `${maxAge.toFixed(0)} mo`,
    };
  }, [patient, history, metric]);

  if (!chart) {
    return <p className="hint">No {metric === 'HeadCircumference' ? 'head circumference' : metric.toLowerCase()} readings yet.</p>;
  }

  return (
    <div className="growth-chart">
      <svg viewBox={`0 0 ${WIDTH} ${HEIGHT}`} width="100%" height={HEIGHT} role="img"
           aria-label={`Growth chart — ${metric}`}>
        <polyline
          points={chart.expected}
          fill="none"
          stroke="var(--c-chart-axis)"
          strokeWidth={1.5}
          strokeDasharray="4 3"
        />
        <polyline
          points={chart.patient}
          fill="none"
          stroke="var(--c-chart-1)"
          strokeWidth={2}
        />
        {chart.dots.map((d, i) => (
          <circle key={i} cx={d.cx} cy={d.cy} r={DOT_RADIUS} fill="var(--c-chart-1)" />
        ))}
      </svg>
      <div className="growth-chart-axes">
        <span>{chart.yTop}</span>
        <span>{chart.yBottom}</span>
        <span>0 mo — {chart.xRight}</span>
      </div>
      <p className="hint">
        Dashed line is an approximate reference for age, not a WHO percentile band.
      </p>
    </div>
  );
}
