# Sending on WhatsApp

Welcome messages and password-reset codes go out through **Meta's WhatsApp
Cloud API**. Until the `WhatsApp` section is filled in, the application falls
back to writing every message to its log, and says so at startup.

The code is finished. What remains is an account, a number, and two templates
Meta has to approve — which is days of waiting, not hours of work, so start it
before you need it.

---

## The one thing to understand first

A business cannot send free-form WhatsApp text to somebody who has not
messaged it in the last 24 hours. Both of our messages are business-initiated
— a welcome after signup, a code somebody asked for — so **both must be
pre-approved templates**, with the changing parts sent as numbered
parameters.

That is why the code passes `Parameters` alongside the sentence, and why
adding a new kind of message later means a new approved template rather than
new text.

---

## 1 · Accounts

1. A **Meta Business account** at business.facebook.com.
2. In **Meta for Developers**, create an app of type *Business* and add the
   **WhatsApp** product.
3. Business verification — documents proving the business is real. This is
   the slow part; begin it first.

You get a free test number immediately, which can only message up to five
numbers you list by hand. Fine for testing, useless for clinics: for real use,
add your own number in **WhatsApp Manager**. A number already registered on
the normal WhatsApp app must be deleted from it first, and that is not
reversible for that number.

## 2 · Two values the app needs

From **WhatsApp → API Setup**:

- **Phone number ID** — a long number beside your sender, *not* the phone
  number itself.
- **Access token** — the token shown there first lasts **24 hours**. Create a
  **System User** in Business Settings, give it access to the app, and
  generate a **permanent** token. Every "it worked yesterday" report about
  this API is this token.

## 3 · Two templates to submit

**Messaging → Message templates**, category **Utility** (not Marketing —
Utility is cheaper and is what these are).

Name `hms_welcome`, body:

```
Welcome to Sivayaan HMS, {{1}}. Your admin username is {{2}} — sign in with the password you chose. Password reset codes will come to this number.
```

Name `hms_password_reset`, body:

```
{{1}}: your Sivayaan HMS password reset code is {{2}}. It expires in {{3}} minutes. If you did not ask for it, ignore this message and tell your clinic admin.
```

The parameter order matters and is what the code sends:

| Template | {{1}} | {{2}} | {{3}} |
|---|---|---|---|
| `hms_welcome` | clinic name | username | — |
| `hms_password_reset` | clinic name | the code | minutes |

Approval usually takes minutes to a day. Rejections are almost always about
category, or about a template that looks promotional.

> Meta has a dedicated **Authentication** category for codes, with its own
> rules and a one-tap copy button. It is worth moving to later; this
> configuration deliberately uses a plain Utility template so there is one
> shape to get working, not two.

## 4 · Configuration

`appsettings.Local.json` on a developer machine, `appsettings.Production.json`
on the server — both git-ignored, and this section holds a token that can send
messages as your business:

```json
{
  "WhatsApp": {
    "AccessToken": "EAAG...the permanent system-user token...",
    "PhoneNumberId": "123456789012345",
    "WelcomeTemplate": "hms_welcome",
    "ResetCodeTemplate": "hms_password_reset",
    "LanguageCode": "en",
    "DefaultCountryCode": "91"
  }
}
```

`DefaultCountryCode` is prepended when somebody typed a bare national number
— "98765 43210" becomes "919876543210". Leave it empty and such numbers are
refused rather than guessed at, because guessing sends a clinic's reset codes
to whoever in another country holds those digits.

`LanguageCode` must match what the template was approved in exactly: `en` and
`en_US` are different templates to Meta, and a mismatch fails at send time.

Restart the API. The startup warning about messages going to the log
disappears when the section is read.

---

## Checking it works

Register a clinic with your own number, or press **Forgot password**. Then
read the API log:

- `WhatsApp accepted a Welcome message` — Meta took it. The id in that line is
  what a delivery webhook would report against later.
- `WhatsApp refused a ... message (400)` — the response body is logged
  verbatim. The three that actually happen:

| Meta says | It means |
|---|---|
| `(#132001) Template name does not exist` | name or `LanguageCode` does not match an approved template |
| `(#190) Access token has expired` | still using the 24-hour token; make a permanent one |
| `(#131030) Recipient not in allowed list` | still on the test number, which only messages numbers you listed |

- `is not a number WhatsApp can use` — the stored number has no country code
  and none is configured.

---

## What this does not do yet

- **No delivery receipts.** The app knows Meta accepted a message, not that a
  phone received it. That needs a webhook endpoint Meta can call.
- **No SMS fallback.** A number that has never used WhatsApp simply fails, and
  that person asks an admin. Adding SMS is another `IMessageSender`.
- **Utility templates, not Authentication.** See the note in §3.
