import { useState, type FormEvent } from 'react';
import { api, ApiError, openPdf } from '../api/client';
import type { PaymentMode, Visit } from '../api/types';

interface Props {
  visit: Visit;
  onClose: () => void;
  onCollected: (message: string) => void;
}

/**
 * Taking the consultation fee.
 *
 * Pressing "Fee" on a tile used to take the money there and then, at whatever
 * payment mode happened to be left selected, and go straight to a print
 * preview. A receipt is numbered and dated the moment it is written, so a fee
 * taken wrongly is a fee reversed on paper. So this asks: the amount and the
 * mode are both on screen and both editable, and the last press names the
 * figure and the patient.
 */
export function CollectFeeDialog({ visit, onClose, onCollected }: Props) {
  const booked = visit.fee;
  const [fee, setFee] = useState(booked.toFixed(2));
  const [mode, setMode] = useState<PaymentMode>('Cash');
  const [transactionNo, setTransactionNo] = useState('');
  const [printReceipt, setPrintReceipt] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const amount = Number(fee);

  // A reference number only means anything once money moved electronically —
  // cash has nothing to reconcile against.
  const showTransactionNo = mode === 'Upi' || mode === 'Card';

  // A concession is a decision; a mistyped digit is not, and they look the
  // same until somebody says which this is.
  const feeChanged = Number.isFinite(amount) && amount !== booked;

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);

    if (!Number.isFinite(amount) || amount < 0) {
      setError('A fee cannot be less than nothing.');
      return;
    }

    // The last gate before a receipt number is burnt. It names the figure,
    // the mode and the patient, because those are the three things that get
    // mixed up when two people are at the desk at once.
    const confirmed = window.confirm(
      `Take ₹${amount.toFixed(2)} from ${visit.patient.name} by ${mode}?`,
    );
    if (!confirmed) return;

    setBusy(true);
    try {
      const paid = await api.post<Visit>(`/api/visits/${visit.id}/collect-fee`, {
        mode,
        amount,
        transactionNo: transactionNo.trim() || null,
      });

      onCollected(
        `Receipt ${paid.feeReceiptNo} — ₹${paid.fee.toFixed(2)} received from ${paid.patient.name} by ${mode}.`,
      );

      if (printReceipt) {
        try {
          await openPdf(`/api/print/receipt/${visit.id}`);
        } catch {
          // The money is taken and the receipt is numbered; a blocked pop-up
          // must not read as a failed collection. It reprints from the queue.
        }
      }
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not take the fee.');
      setBusy(false);
    }
  };

  const scheduled = new Date(visit.scheduledOn);

  return (
    <div className="overlay" role="dialog" aria-modal="true" aria-label="Consultation fee">
      <div className="overlay-card">
        <div className="overlay-head">
          <h2>Token {visit.tokenNo} — {visit.patient.name}</h2>
          <button type="button" className="ghost" onClick={onClose}>Close</button>
        </div>

        <form className="overlay-body" onSubmit={onSubmit}>
          {/* Who is being seen and by whom: the check before money changes hands. */}
          <p className="hint">
            {visit.patient.age}
            {visit.patient.gender.charAt(0)} · booked {scheduled.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
            {' '}· {visit.doctor.name}
          </p>

          <label>
            Amount
            <input
              type="number"
              min="0"
              step="0.01"
              value={fee}
              onChange={(e) => setFee(e.target.value)}
              autoFocus
            />
          </label>

          {feeChanged && (
            <p className="hint warn">
              Booked at ₹{booked.toFixed(2)}. This receipt will say ₹{amount.toFixed(2)}.
            </p>
          )}

          <label>
            Payment mode
            <select value={mode} onChange={(e) => setMode(e.target.value as PaymentMode)}>
              <option value="Cash">Cash</option>
              <option value="Upi">UPI</option>
              <option value="Card">Card</option>
            </select>
          </label>

          {showTransactionNo && (
            <label>
              Transaction / reference no.
              <input value={transactionNo} onChange={(e) => setTransactionNo(e.target.value)} />
            </label>
          )}

          <label className="checkbox-label">
            <input
              type="checkbox"
              checked={printReceipt}
              onChange={(e) => setPrintReceipt(e.target.checked)}
            />
            Print the receipt
          </label>

          {error && <p className="auth-error">{error}</p>}

          <div className="overlay-actions">
            <button type="submit" disabled={busy}>{busy ? 'Taking…' : 'Take fee'}</button>
            <button type="button" className="ghost" onClick={onClose}>Cancel</button>
          </div>
        </form>
      </div>
    </div>
  );
}
