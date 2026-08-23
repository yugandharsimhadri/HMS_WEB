# Platform support (EnterpriseAdmin)

One identity exists outside every clinic. It does two things and nothing
else: put a locked-out clinic owner back in, and move a licence date.

It cannot open a clinic, list patients, read a bill, or see a prescription —
not by policy alone but by construction, and both mechanisms are described
below because either one on its own would be a single point of failure.

---

## Setting the password

**The password is not in the repository, and must not be put there.** This
account can reset any clinic admin's password on the whole platform; a secret
pushed to git history stays there whatever gets deleted afterwards.

It is read from configuration, last file wins:

| Source | Use |
|---|---|
| `backend/src/SivayaanHMS.Api/appsettings.Local.json` | Development. Git-ignored. |
| `PlatformAdmin__Password` environment variable | Deployment. |

Create the local file yourself — it is deliberately not in the repo:

```json
{
  "PlatformAdmin": {
    "Username": "EnterpriseAdmin",
    "Password": "<the password>"
  }
}
```

If it is unset the account simply cannot sign in, which is the correct
failure for a missing credential — `PlatformAdminService.Verify` refuses an
empty password outright rather than hashing `""` and letting anyone in by
leaving the box blank. Outside Development the API refuses to *start* without
one, so a forgotten override is loud rather than a quiet hole.

## Signing in

Through the ordinary login page, as `EnterpriseAdmin` — no `@clinic` suffix,
because it belongs to no clinic. It lands on `/platform`, never the clinic
shell.

---

## What it can reach

### Two independent barriers

**1. The token carries no tenant.** `IssuePlatformToken` deliberately omits
the `tenant_id` claim that every clinic token has. `HttpCurrentTenantContext`
therefore resolves `Guid.Empty`, and `AppDbContext`'s global query filter
returns zero rows for every tenant-scoped table. A route that forgot its
policy would still show nothing.

**2. The default authorization policy requires that claim.** `ClinicPolicy`
is registered as `options.DefaultPolicy` in `Program.cs`, so a bare
`[Authorize]` anywhere in this API means *a signed-in user of some clinic* —
which a support token is not. This is why clinic controllers need no new
attribute: they were already protected the moment the default changed.

`PlatformController` names `PlatformAdminPolicy` explicitly, which is what
lets it opt out of the clinic default.

Verified end to end:

| Token | Endpoint | Result |
|---|---|---|
| Clinic | `/api/platform/clinics` | 403 |
| Platform | `/api/patients` | 403 |
| Platform | `/api/visits` | 403 |
| Platform | `/api/settings/general` | 403 |
| Platform | `/api/platform/clinics` | 200 |
| Clinic | `/api/patients` | 200 |

### Resetting a clinic admin's password

The console generates the temporary password rather than letting the support
agent type one — a human under time pressure picks a password they can say
quickly, and that is exactly the password worth guessing. It avoids
characters that are misheard down a phone (`0`/`O`, `1`/`l`/`I`, `5`/`S`) and
is grouped: `DUgW-Dj42-mMmP`.

It is shown **once**. It is hashed the instant it is issued and is not
retrievable afterwards; if it is lost, issue another.

`MustChangePassword` is always set, and the clinic-side half of that is real:
`ProtectedRoute` wraps the entire clinic shell, so the owner cannot reach any
screen — or get round it by typing a URL — until they have chosen their own
password. Until they do, the support agent knows their password, which is the
whole reason the gate exists.

Only accounts with the `Admin` role can be reset here. A clinic's
receptionists are its own business; this console restores the owner, who then
fixes everyone else from Settings.

### Licence expiry

A date, or empty for a clinic on no fixed term.

Nothing is deleted, hidden, or degraded when it passes. The clinic's records
stay entirely theirs; they simply cannot sign in until the date moves.

The rule lives in `SivayaanHMS.Core/LicenseRules.cs` because two callers must
never disagree about it — sign-in decides whether to open the door, the
console reports how long that stays true, and drift between them would show
up as a clinic the console calls "Active" being turned away at login.

- **A licence runs to the end of its last day.** Expiry is *strictly earlier*
  than today, not "not later than". A clinic whose licence expires today can
  still work today. Pinned by `LicenseRulesTests`.
- **Expiring** starts 30 days out, so a renewal conversation can happen
  before a waiting room does.
- **The check happens after the password, never before.** The message names
  the clinic and its expiry date, which is only safe to show someone who has
  just proved they work there. Shown to an anonymous caller it would confirm
  which clinics exist and when each one's subscription lapsed — a wrong
  password at an expired clinic still returns the ordinary 401.

Expired sign-in returns **402 Payment Required** with a message that says the
records are safe, because that is the first thing the person on the phone
will want to know.

---

## Deliberately absent

- **No route that returns clinical data.** Not "none exposed yet" — none, and
  a console that *could* show them would eventually be asked to.
- **No delete-a-clinic.** Nothing here destroys a customer's records.
- **No password stored in readable form**, anywhere, at any point.
- **No second platform account.** If support needs to be per-person and
  audited, that is a real user table and a real decision, not another
  constant.

## Known gap

Actions are logged (`LogWarning`, with the username and tenant) but there is
no queryable audit trail — you would be reading server logs. Worth revisiting
before more than one person holds this password.
