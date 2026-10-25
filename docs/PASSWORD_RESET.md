# Phone numbers, welcome messages and password reset

A clinic admin who forgets their password has, until now, had one route
back: ask platform support. This adds the ordinary one — a code to the phone
on the account — and the rule that makes it safe: **the code goes to the
number already on file, never to a number somebody types in.**

---

## The shape

| | |
|---|---|
| `POST /api/tenants/register` | now takes `phone`, stores it on the admin user, sends a welcome message |
| `POST /api/auth/forgot-password` | `{ username }` → `{ message, resetToken }`; sends a 6-digit code to that account's phone |
| `POST /api/auth/verify-reset-code` | `{ resetToken, code }` → `VERIFIED`, or what went wrong |
| `POST /api/auth/reset-password` | `{ resetToken, newPassword }` → sets the password, **only** after `VERIFIED` |
| `POST /api/auth/resend-reset-code` | `{ resetToken }` → a fresh code for the same reset |

All four reset endpoints are anonymous, like `login`.

### Three steps, not two

The first version took the code and the new password in one submit. It now
takes them separately, and the screen shows the password fields only after
the API has said `VERIFIED` — the browser does not decide that.

That ordering is enforced on the server, not just on the screen. Each reset
is a row in `PasswordResetTransactions` (stage `CodeSent` → `Verified` →
`PasswordChanged`, or `Closed`), found by a hash of the browser's opaque
`resetToken`. `reset-password` refuses anything not at `Verified`, so posting
straight to it with a guessed or skipped code gets `NOT_VERIFIED`.

Every step answers in one shape, `{ status, message, remainingAttempts? }`:

| status | HTTP | Screen does |
|---|---|---|
| `VERIFIED` | 200 | shows the new-password fields |
| `PASSWORD_CHANGED` | 200 | goes to sign-in |
| `INVALID_OTP` | 400 | says so, with tries left; stays on the code |
| `EXPIRED`, `ATTEMPTS_EXCEEDED`, `ALREADY_USED`, `CANCELLED` | 400 | the reset is over — offers *Start again* |
| `NOT_VERIFIED`, `INVALID_PASSWORD` | 400 | says so |
| `RATE_LIMITED` | 429 | wait and retry |
| `TEMPORARY_ERROR` | 503 | "try again in a moment" — **never** treated as success |

The old one-step shape (`{ username, code, newPassword }`) is still accepted
in Local mode, so a browser holding the previous build keeps working through
a deploy. It is refused in SivaComm mode.

---

## Why the welcome message has no password in it

It was asked for and deliberately left out. The person chose that password
seconds earlier, so repeating it tells them nothing they do not know — and it
then lives in a chat history, a phone backup, a lock-screen preview and the
provider's logs, none of which the clinic controls. A forgotten password is
what the reset flow is for, and a one-time code is safe to send because it
expires.

The message carries the clinic name and the username, which is the part
people genuinely do forget: `drsri@sunrise`, not `drsri`.

---

## Why "forgot password" answers the same way every time

`forgot-password` returns one sentence and HTTP 200 for **every** input:
unknown clinic, unknown user, deactivated user, no phone on file, or the send
cap already hit. Byte for byte the same.

The reason is that a login page that says "no such user" is a free directory
of every account on the platform. An attacker with a username list learns who
exists; the genuine owner learns nothing from the difference, because either
the message arrives or it does not.

This bit is easy to half-do. The first version of this endpoint returned a
masked hint — `••••••••3210` — so the person knew which phone to check. It
was identical in message and status code, and it still leaked: the hint is
present for a real account and absent for an invented one, so the field alone
answers the question the sentence refuses to. It was removed. The screen now
says "check the mobile number on your account".

`verify-reset-code` **does** say what went wrong — wrong code, expired, too
many attempts — because the difference between those three is the difference
between retyping six digits and giving up. It still never distinguishes an
unknown account from a wrong code, and that took more than one sentence:

- `forgot-password` returns a `resetToken` for **every** request. For an
  unknown, inactive or phoneless account the token is backed by a *decoy*
  transaction.
- A decoy fails `verify-reset-code` exactly as a real wrong code does —
  `INVALID_OTP` with 4, 3, 2, 1 tries left, then `ATTEMPTS_EXCEEDED`. Without
  that, "tries left" would be the hint field all over again: present for a
  real account, absent for an invented one.
- `resend-reset-code` always answers the same sentence. Telling the caller
  "wait 60 seconds" would be true only for real accounts, so the cooldown is
  enforced silently on the server and shown by the page's own timer.

---

## What stops a stranger resetting somebody's password

Each rule closes one route, and none of them is optional:

| Rule | Without it |
|---|---|
| Code goes to the stored phone, never a submitted one | anyone could have a code sent to their own phone |
| Only a PBKDF2 hash of the code is stored, salted per row | a database dump would be a list of live reset codes |
| 6 digits, 10-minute life | a code seen on a lock screen last week still works |
| Dead after 5 wrong guesses | a million possibilities is minutes of scripted guessing |
| A new code kills the previous one | codes accumulate, multiplying an attacker's chances |
| Max 3 sends per user per 15 minutes | the button becomes a way to bombard someone's phone |
| Success clears `MustChangePassword` | a support-issued temporary password stays live |
| New password accepted only at stage `Verified` | the browser could skip the code |
| A new reset closes the user's earlier ones | an old verified reset left open could be finished later |
| The whole reset expires 30 minutes after it starts | a verified tab left open overnight still sets a password |

---

## Turning self-service reset on or off

```json
"PasswordReset": { "Enabled": true }
```

| `PasswordReset:Enabled` | What users get |
|---|---|
| `true` (default) | "Forgot password?" on the sign-in page; a code to the phone on file, from whichever service `Messaging:Mode` names below |
| `false` | No link — the sign-in page says "ask your clinic admin". Every reset endpoint answers **403** (`DISABLED`), including a reset already in progress when the flag was turned off. Passwords are reset by the clinic admin (Settings → Staff logins), and a clinic admin by platform support — the route that existed before OTP reset |

The browser learns the setting from `GET /api/auth/options` →
`{ "passwordResetEnabled": true|false }`, anonymous and the same for every
caller, so it describes the deployment and nothing about any account.
`forgot-password` refuses before looking at the username, so turning it off
cannot become an enumeration channel either.

The flag only affects password reset. Welcome messages still go out through
`Messaging:Mode` either way.

Default `true` keeps the self-service reset existing deployments already
have; set `false` in `appsettings.Production.json` (or
`PasswordReset__Enabled=false`) and restart to withdraw it.

## Two modes: Local and SivaComm

When reset is enabled, `Messaging:Mode` chooses who handles the code. The rest of the flow — the
user, the stored phone, the reset transaction, the password — is this
application's in both modes, and the password never leaves it.

| | **Local** (default) | **SivaComm** |
|---|---|---|
| Code generated, stored, checked | here, `PasswordResetCodes` | SivaComm (keyed HMAC, not stored) |
| Sent through | `IMessageSender` → WhatsApp or the log | SivaComm's worker → WhatsApp |
| Welcome message | `IMessageSender` here | SivaComm (`SivaCommMessageSender`) |
| Delivery receipts, dashboard | no | yes — SivaComm's admin dashboard |
| Configured in | the `WhatsApp` section | the `SivaComm` section |

Local is exactly the behaviour this document described before, re-housed
behind `IOtpGateway` (`LocalOtpGateway`) so the three-step screen works in
both modes. Nothing changes for a deployment that does not opt in.

### What SivaComm mode does on each step

```
forgot-password  ─► PasswordResetFlow creates PasswordResetTransactions row (hms-<id>)
                 ─► SivaComm POST /api/v1/otp/challenges { resetReference: hms-<id>, userReference: u-<userId>, phone }
                    ◄─ 202 QUEUED + challengeId        (stored on the row; browser gets only resetToken)
verify-reset-code ─► SivaComm POST /otp/challenges/{id}/verify { resetReference, otp }
                    ◄─ 200 VERIFIED + verificationGrant (stored on the row ENCRYPTED; never sent to the browser)
                    ◄─ 422 INVALID_OTP / EXPIRED / …    (relayed as the same status)
reset-password   ─► SivaComm POST /grants/consume { grant, resetReference }   Idempotency-Key: hms-<id>
                    ◄─ 200 CONSUMED                     then, in ONE SaveChanges here: new password hash,
                                                        MustChangePassword = false, stage PasswordChanged
                 ─► SivaComm POST /otp/challenges/{id}/completion   (reporting only, best effort)
```

`userReference` is the user's internal id, never the username or phone.
Requests are HMAC-signed with the key SivaComm issued (`SivaCommClient`);
SivaComm's `docs/API.md` has the contract.

### When something fails in the middle

- **SivaComm down, slow or answering 5xx / 401 / garbage** → `TEMPORARY_ERROR`
  (503). `SivaCommClient` turns every non-answer into that, and the flow
  never treats it as verified. The same code can be tried again.
- **Grant consumed, then our database write fails** → the reset stays at
  `Verified`. Submitting again re-consumes with the same `Idempotency-Key`,
  SivaComm answers `CONSUMED` again (replay of the same reset), and the
  password change is retried.
- **Password changed, response to the browser lost** → resubmitting within 5
  minutes answers `PASSWORD_CHANGED` without changing anything again; later,
  `ALREADY_USED`.
- **Two submits at once** → the reset row's `RowVersion` lets one
  `SaveChanges` through; the other sees the finished row and answers
  `PASSWORD_CHANGED`.

### Turning it on

1. Register HMS in SivaComm's dashboard (code `hms-web`), and set its two
   templates (an Authentication template for the code, `hms_welcome` with 2
   parameters). Copy the key id and secret it shows once.
2. In `appsettings.Production.json`:

   ```json
   "Messaging": { "Mode": "SivaComm" },
   "SivaComm": { "BaseUrl": "https://comm.example", "ClientId": "hms-web", "KeyId": "k…", "Secret": "…" }
   ```

3. Migrate (adds `PasswordResetTransactions`) and restart. With Mode set and
   the section incomplete the API refuses to start, rather than failing at
   the first reset somebody needs.

Switching back is setting Mode to `Local`. A reset in flight at the moment
of switching is closed (each row records which mode issued it) and the
person starts again.

---

## Sending the messages (Local mode)

Nothing is sent yet. `IMessageSender` has one implementation,
`LoggingMessageSender`, which writes the message to the API log — which is
what makes the whole flow testable on a laptop with no provider account:

```
warn: SivayaanHMS.Data.Messaging.LoggingMessageSender[0]
      No SMS/WhatsApp provider is configured, so this message was NOT sent.
  To      : +91 91234 56780
  Purpose : PasswordResetCode
  Message : Sunrise Family Clinic: your Sivayaan HMS password reset code is 754857...
```

Outside Development the API logs a warning at startup saying exactly this,
because the failure is otherwise silent: codes are generated, nobody receives
them, and every one sits in the log.

**WhatsApp is implemented** — `WhatsAppCloudMessageSender`, against Meta's
Cloud API. It takes over automatically as soon as the `WhatsApp` section
carries both an access token and a phone number id; until then the logging
sender stands in. Setting it up is **docs/WHATSAPP_SETUP.md**, and the slow
part is Meta's business verification and template approval, not the code.

Adding SMS later is another `IMessageSender` and one line in `Program.cs`.
Note that Indian SMS needs DLT registration of the sender ID and each
template with the telecom regulator — which is why `MessagePurpose` and
`OutboundMessage.Parameters` exist: every transport here is template-based.

---

## Known gaps

- **Staff accounts without a number still ask an Admin.** Settings → Staff
  logins now collects a mobile number, but it is optional there, unlike at
  registration: a clinic adding six people at once should not be blocked
  because two are away from their desk. Anyone without one cannot use Forgot
  password — which is exactly where they were before — and the field says so
  while it is empty. Existing staff have no number until somebody edits them.
- **`EnterpriseAdmin` is excluded on purpose.** It belongs to no clinic and
  has no phone; it is recovered by whoever holds the server's configuration.
- **Codes are not swept.** Spent and expired rows stay in `PasswordResetCodes`
  and `PasswordResetTransactions` (decoys included). They are harmless —
  hashed, single-use, expired — but a periodic delete would keep the tables
  small. SivaComm sweeps its own.
- **Existing sessions are not revoked.** Sign-in is a stateless 8-hour JWT
  with no server-side session list, so a reset does not sign out a browser
  that is already signed in — the same is true of change-password today.
  Revoking needs a per-user token version checked on each request; it is a
  change to authentication, not to reset, and is left as its own piece of work.
- **A bare 10-digit number with no `DefaultCountryCode`** is accepted by
  `WhatsAppCloudMessageSender.TryNormalise` (length check `>= 10`) and would
  be sent without a country code, despite its comment saying it is refused.
  SivaComm's copy of the rule requires 11+ digits after prefixing; the Local
  sender here is unchanged and should get the same fix.
