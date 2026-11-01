import type { ActiveAdmission } from '../api/types';

/**
 * "Charge to IP bill" in place of taking payment. Only drawn for an admitted patient;
 * once their final bill is made it explains why the option is gone rather than
 * silently disappearing.
 */
export function ChargeToIp({ admission, checked, onChange }: {
  admission: ActiveAdmission | null;
  checked: boolean;
  onChange: (checked: boolean) => void;
}) {
  if (!admission) return null;

  if (admission.hasFinalBill) {
    return (
      <p className="hint">
        Admitted ({admission.ipNo}), but the final IP bill is made - take payment here.
      </p>
    );
  }

  return (
    <label className="ip-charge checkbox-label">
      <input type="checkbox" checked={checked} onChange={(e) => onChange(e.target.checked)} />
      <span>
        <strong>Charge to IP bill</strong>
        <span className="hint">
          {' · '}{admission.ipNo} · bed {admission.bedNo} ({admission.ward}).
          {checked ? ' No payment taken here.' : ' Tick to add this to their IP bill instead of taking payment.'}
        </span>
      </span>
    </label>
  );
}
