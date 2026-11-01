# Phone numbers, welcome messages and password reset

A clinic admin who forgets their password has, until now, had one route
back: ask platform support. This adds the ordinary one — a code to the phone
on the account — and the rule that makes it safe: **the code goes to the
number already on file, never to a number somebody types in.**

> The end-to-end picture — HMS_WEB and SivaCom together, sequence diagrams,
> every state and status, and every configuration key on both sides — is in
> SivaCom's `docs/GUIDE.md`. This page is HMS_WEB's half in depth.

---

## The shape

There are two flows. **`SivaCom:Enabled` is off by default, and off means
the original flow, unchanged** — the same page, the same endpoints, the same
responses, the same tables written. The three-step flow exists only when the
flag is on. ([The switch](#the-switch-sivacomenabled) below.)

**Original flow — `SivaCom:Enabled = false` (default):**

| | |
|---|---|
| `POST /api/tenants/register` | takes `phone`, stores it on the admin user, sends a welcome message |
| `POST /api/auth/forgot-password` | `{ username }` → `{ message }`; sends a 6-digit code to that account's phone |
| `POST /api/auth/reset-password` | `{ username, code, newPassword }` → sets the password |

The page asks for the username, then for the code and the new password on
one screen (`ClassicForgotPasswordPage`). `verify-reset-code` and
`resend-reset-code` answer **404** — in this flow they do not exist.

**SivaCom flow — `SivaCom:Enabled = true`:**

| | |
|---|---|
| `POST /api/auth/forgot-password` | `{ username }` → `{ message, resetToken }`; SivaCom sends the code |
| `POST /api/auth/verify-reset-code` | `{ resetToken, code }` → `VERIFIED`, or what went wrong |
| `POST /api/auth/reset-password` | `{ resetToken, newPassword }` → sets the password, **only** after `VERIFIED` |
| `POST /api/auth/resend-reset-code` | `{ resetToken }` → a fresh code for the same reset |

`GET /api/auth/options` → `{ passwordResetEnabled, sivaComEnabled }` tells
the page which flow to show. Until it answers (or if it cannot), the page
shows the original flow, so with the flag off nothing on screen waits on it.
All reset endpoints are anonymous, like `login`.

### Three steps, not two (SivaCom flow)

The original flow takes the code and the new password in one submit. The
SivaCom flow takes them separately, and the screen shows the password
fields only after the API has said `VERIFIED` — the browser does not decide
that.

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

A request in the other flow's shape — a browser that loaded the page before
the flag was switched — gets `400 "This page is out of date. Reload it and
request a new code."`

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

## The switch: `SivaCom:Enabled`

```json
"SivaCom": { "Enabled": true, "BaseUrl": "…", "ClientId": "hms-web", "KeyId": "…", "Secret": "…" }
```

One flag, read from the config files at startup:

| `SivaCom:Enabled` | What happens |
|---|---|
| `false` (default) | **The normal flow.** This application generates and checks the code in its own `PasswordResetCodes` table and sends it (and the welcome message) itself — through WhatsApp if the `WhatsApp` section is filled in, otherwise to the log. Nothing in this deployment talks to SivaCom. |
| `true` | **OTP authentication through SivaCom.** SivaCom generates, sends and verifies the code and sends the welcome message. This application still owns the user, the stored phone, the reset transaction and the password. `BaseUrl`, `ClientId`, `KeyId` and `Secret` are required — the API refuses to start without them rather than failing at the first reset somebody needs. |

The user, the stored phone and the password are this application's either
way, and the password never leaves it.

| | **Enabled = false (default)** | **Enabled = true** |
|---|---|---|
| Reset page | original: code + new password on one screen | three steps: code, then new password |
| Code generated, stored, checked | here, `PasswordResetCodes` | SivaCom (keyed HMAC, not stored) |
| Sent through | `IMessageSender` → WhatsApp or the log | SivaCom's worker → WhatsApp |
| Welcome message | `IMessageSender` here | SivaCom (`SivaComMessageSender`) |
| `PasswordResetTransactions` rows | none | one per reset |
| Delivery receipts, dashboard | no | yes — SivaCom's admin dashboard |
| Configured in | the `WhatsApp` section | the `SivaCom` section |

**Off is the code that ran before SivaCom, not a re-implementation of it:**
`forgot-password` runs the original method verbatim
(`AuthController.ClassicForgotPasswordAsync`), `reset-password` the original
one-step branch, and the page is the original component
(`ClassicForgotPasswordPage`, identical to `main` but for its name).
`ClassicPasswordResetTests` pins that behaviour, and it was checked end to end
in a browser with no `SivaCom` section in the config at all.

What a deployment that leaves the flag off still gets from this branch:

- **One new, empty table**, `PasswordResetTransactions`, from the migration —
  run migrations before publishing as for every release. Nothing writes to it
  while the flag is off.
- `GET /api/auth/options`, read by the sign-in page. The "Forgot password?"
  link does not wait for it.
- `PasswordReset:Enabled` (default `true` = as before).

### A second, optional switch: `PasswordReset:Enabled`

```json
"PasswordReset": { "Enabled": true }
```

A kill switch for self-service reset as a whole, independent of who sends the
code. `true` (the default) is the above. `false` hides the "Forgot password?"
link ("ask your clinic admin") and makes every reset endpoint answer **403**
(`DISABLED`), including a reset already in progress — the route that existed
before OTP reset: the clinic admin resets staff (Settings → Staff logins), and
platform support resets a clinic admin. The browser learns it from
`GET /api/auth/options` → `{ "passwordResetEnabled": true|false }`, anonymous
and the same for every caller, and `forgot-password` refuses before looking at
the username, so it is not an enumeration channel. Leave it alone unless you
want to withdraw self-service reset entirely.

### What `SivaCom:Enabled = true` does on each step

```
forgot-password  ─► PasswordResetFlow creates PasswordResetTransactions row (hms-<id>)
                 ─► SivaCom POST /api/v1/otp/challenges { resetReference: hms-<id>, userReference: u-<userId>, phone }
                    ◄─ 202 QUEUED + challengeId        (stored on the row; browser gets only resetToken)
verify-reset-code ─► SivaCom POST /otp/challenges/{id}/verify { resetReference, otp }
                    ◄─ 200 VERIFIED + verificationGrant (stored on the row ENCRYPTED; never sent to the browser)
                    ◄─ 422 INVALID_OTP / EXPIRED / …    (relayed as the same status)
reset-password   ─► SivaCom POST /grants/consume { grant, resetReference }   Idempotency-Key: hms-<id>
                    ◄─ 200 CONSUMED                     then, in ONE SaveChanges here: new password hash,
                                                        MustChangePassword = false, stage PasswordChanged
                 ─► SivaCom POST /otp/challenges/{id}/completion   (reporting only, best effort)
```

`userReference` is the user's internal id, never the username or phone.
Requests are HMAC-signed with the key SivaCom issued (`SivaComClient`);
SivaCom's `docs/API.md` has the contract.

### When something fails in the middle

- **SivaCom down, slow or answering 5xx / 401 / garbage** → `TEMPORARY_ERROR`
  (503). `SivaComClient` turns every non-answer into that, and the flow
  never treats it as verified. The same code can be tried again.
- **Grant consumed, then our database write fails** → the reset stays at
  `Verified`. Submitting again re-consumes with the same `Idempotency-Key`,
  SivaCom answers `CONSUMED` again (replay of the same reset), and the
  password change is retried.
- **Password changed, response to the browser lost** → resubmitting within 5
  minutes answers `PASSWORD_CHANGED` without changing anything again; later,
  `ALREADY_USED`.
- **Two submits at once** → the reset row's `RowVersion` lets one
  `SaveChanges` through; the other sees the finished row and answers
  `PASSWORD_CHANGED`.

### Turning it on

1. Register HMS in SivaCom's dashboard (code `hms-web`), and set its two
   templates (an Authentication template for the code, `hms_welcome` with 2
   parameters). Copy the key id and secret it shows once.
2. In `appsettings.Production.json`:

   ```json
   "SivaCom": { "Enabled": true, "BaseUrl": "https://comm.example", "ClientId": "hms-web", "KeyId": "k…", "Secret": "…" }
   ```

3. Migrate (adds `PasswordResetTransactions`) and restart. With `Enabled`
   true and the section incomplete the API refuses to start, rather than
   failing at the first reset somebody needs.

Switching back is setting `Enabled` to `false`. A reset in flight at the
moment of switching is closed (each row records which flow issued it) and the
person starts again.

---

## Sending the messages (`SivaCom:Enabled = false`)

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

## Logs: why did somebody not get a code?

Every server writes `logs\messages-<date>.log` beside the application (or
where `Logging:Messages:PathFormat` says), whatever the other log settings.
It holds only messaging: a startup block saying how codes are sent, then one
line per *Forgot password* request and per call to SivaCom or WhatsApp. No
code, password, grant or secret is written; phone numbers show their last
four digits only. The browser's answer is the same whatever the log says.

On/off: `Logging:Messages:Enabled` (and `Logging:File:Enabled` for the
optional main log), both `true` by default. Set `false` and recycle the
application pool to stop writing that file.

Under IIS the application pool must be allowed to write to that folder, once:

```powershell
New-Item -ItemType Directory -Force <site folder>\logs | Out-Null
icacls <site folder>\logs /grant "IIS AppPool\<pool name>:(OI)(CI)M"
```

| Line | Meaning |
|---|---|
| `HMS messaging at startup … Reset codes : through SivaCom at … / straight to WhatsApp / NOT SENT …` | how this server sends codes, and which config files it read (with when each was saved) |
| `Password reset requested (…): NO code sent - no clinic matches / no such username / inactive / has no phone number on file` | nothing was sent, and why |
| `Password reset requested (…): user … code to ******4929` | a code was asked for |
| `SivaCom POST …/otp/challenges -> 202 QUEUED … challenge … [correlation …]` | SivaCom accepted it; SivaCom's own messages log has the same correlation id and follows it to WhatsApp |
| `SivaCom … -> 401 …` | SivaCom refused the signature; its log names the reason |
| `Could not reach SivaCom: … No connection could be made …` | SivaCom is down or `SivaCom:BaseUrl` is wrong |
| `WhatsApp refused a … message (400) …` / `WhatsApp accepted …` | classic mode (`SivaCom:Enabled` false), straight to Meta |

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
  small. SivaCom sweeps its own.
- **Existing sessions are not revoked.** Sign-in is a stateless 8-hour JWT
  with no server-side session list, so a reset does not sign out a browser
  that is already signed in — the same is true of change-password today.
  Revoking needs a per-user token version checked on each request; it is a
  change to authentication, not to reset, and is left as its own piece of work.
- **A bare 10-digit number with no `DefaultCountryCode`** is accepted by
  `WhatsAppCloudMessageSender.TryNormalise` (length check `>= 10`) and would
  be sent without a country code, despite its comment saying it is refused.
  SivaCom's copy of the rule requires 11+ digits after prefixing; the Local
  sender here is unchanged and should get the same fix.
