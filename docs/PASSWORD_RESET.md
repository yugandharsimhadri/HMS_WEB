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
| `POST /api/auth/forgot-password` | `{ username }` → sends a 6-digit code to that account's phone |
| `POST /api/auth/reset-password` | `{ username, code, newPassword }` → sets the password |

Both reset endpoints are anonymous, like `login`.

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

`reset-password` **does** say what went wrong — wrong code, expired, too many
attempts — because by then the caller has demonstrated they hold a code, and
the difference between those three is the difference between retyping six
digits and giving up. It still never distinguishes an unknown account from a
wrong code.

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

---

## Sending the messages

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
- **Codes are not swept.** Spent and expired rows stay in `PasswordResetCodes`.
  They are harmless — hashed, single-use, expired — but a periodic delete
  would keep the table small.
