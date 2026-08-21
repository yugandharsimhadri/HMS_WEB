# Splitting Settings, and configurable document branding

Written 31 July 2026, from the request to break the single **Settings** screen
into **General**, **Clinic**, **Pharmacy**, **Doctors** and **Reports**
(document branding — logo and footer), and to do it without touching data
already live at the customer.

**Implemented 31 July 2026**, as a tab strip inside the Settings screen rather
than five sidebar entries — the recommendation in §2 below, taken up as
written. Two things came out different from how they were planned here;
both are called out where they happen: §3's compiled letterhead fallback
(dropped entirely rather than kept, see §3a), and §9's identity header, which
took three attempts before it held — see "The logo sits in the corner, not in
a row with the name" under §9, which also has the actual bug the second
attempt shipped with and how it was found. Everything else — the key names,
the seed, the identity split, the layout redesign — shipped as described.

---

## 1. Why one screen became five

Today's Settings screen carries two identities that happen to share a card —
the clinic's own details and the pharmacy's — plus the doctor list, the backup
tools, the theme switch and the licence, all in one scrolling column. It grew
one field at a time and each addition was reasonable on its own; together they
no longer are.

Splitting it also fixes something the single-identity model was quietly
getting wrong. `ShopProfile` is one bag of fields used for **every** printed
document — the OPD prescription, the consultation fee receipt, and the
pharmacy tax invoice all read the same `Gstin`, `DrugLicenceNo` and
`PharmacistName`. A drug licence number and a pharmacist's name belong to the
pharmacy, not to a doctor's prescription, and today's prescription prints them
anyway because nothing tells `AddClinicHeader` not to. See §5.

## 2. The new structure

**Recommendation: Settings becomes its own section with an internal tab strip**
— General / Clinic / Pharmacy / Doctors / Reports — rather than five new
buttons added to the main sidebar. The sidebar is at 7 entries today; growing
it to 11 works against everything already documented in this guide about small
clinic screens. A tab strip inside Settings is the same pattern already used on
the Reports screen and the Patients history panel, so it costs nothing new to
learn.

> **Needs your call.** The alternative — five flat items in the main sidebar,
> maybe under a "SETTINGS" label — is a smaller code change (no new sub-nav
> concept) and puts every destination one click away instead of two. I have
> planned the split so either works; say which and the rest does not change.

### What moves where

**General** — nothing here is clinic identity; it is how the software behaves.
| Field | From |
|---|---|
| Appearance (Light/Dark) | Settings, unchanged |
| OPD queue layout (Tiles/Rows) | Settings, unchanged |
| Data health check | Settings, unchanged |
| Backup — path, last backup, Back up now, Open backup folder | Settings, unchanged |
| Database file path, Activity log path, Open log folder | Settings, unchanged |
| Licence summary, About | Settings, unchanged |

**Clinic** — the identity that prints on the prescription and the fee receipt.
| Field | Notes |
|---|---|
| Clinic name, Address, Phone | From the old `ShopProfile` |
| Registered for GST / GSTIN | Off by default. Most consultations are not a taxable supply — see §5 |
| Consulting hours (Morning / Evening) | Already exists; moves here, since it is a clinic fact, not a pharmacy one |

**Pharmacy** — the identity that prints on the medicine bill.
| Field | Notes |
|---|---|
| Pharmacy name, Address, Phone | Can differ from the clinic's if the pharmacy trades under its own name |
| Registered for GST / GSTIN | Drives TAX INVOICE vs INVOICE, exactly as today |
| Drug licence no | Moved here — it is a pharmacy credential, not a clinic one |
| Pharmacist name | Moved here, same reason |

**Doctors** — unchanged fields, its own destination instead of the right-hand
half of a shared screen: Name, Speciality, Registration no, **Phone
(optional)** — already exists on `Doctor`, nothing to add — Default
consultation fee.

**Reports** (document branding — the name matches the existing Reports screen
because this configures what Reports' documents look like, not because it
lives inside Reports).
| Field | Notes |
|---|---|
| Logo | Upload PNG/JPEG. Replaces the compiled-in Twinkle letterhead everywhere a letterhead prints. See §4 |
| Footer message | Free text at the foot of every bill — the returns policy, "get well soon", whatever the clinic wants |

> This is the default and, for now, the only theme — matching *"the below is
> default theme, we can configure more later."* One logo, one footer, used on
> every document. Per-document themes (a different footer on the bill than on
> the prescription) are a real thing to want later; nothing in this plan makes
> it harder to add, because the theme is already its own settings group rather
> than folded into Clinic or Pharmacy.

---

## 3. Data model — no schema change at all

`Settings` is already a key/value table (`Setting.Key`, `Setting.Value`, both
TEXT). That is the whole reason this can ship without an EF Core migration:
every field above becomes new **rows**, not new columns.

```
clinic.name  clinic.address  clinic.phone  clinic.gstregistered  clinic.gstin
clinic.morningfrom  clinic.morningto  clinic.eveningfrom  clinic.eveningto

pharmacy.name  pharmacy.address  pharmacy.phone
pharmacy.gstregistered  pharmacy.gstin
pharmacy.druglicence  pharmacy.pharmacist

docs.footer
docs.logo.base64  docs.logo.filename  docs.logo.contenttype
```

The logo is the one field that looks like it needs a new column, and
deliberately does not get one: stored as **base64 text in a Settings row**
rather than a `byte[]` column or a file on disk.

> **Why not a file on disk.** `C:\HMS\DB\twinkle.db` is what the daily backup
> protects. A logo saved beside it as `C:\HMS\Assets\logo.png` is a second file
> nobody remembers to back up, and the exact failure
> [`DocumentHeaderImage.cs`](../src/Pharma.App/Printing/DocumentHeaderImage.cs)
> was written to avoid — *"a single-file install cannot lose it and a clinic
> cannot half-replace it."* Inside the database, the logo travels with every
> backup and every copy handed to us for support, automatically.
>
> SQLite's TEXT column has no practical size limit, so a capped upload — see
> §4 — comfortably fits as a row.

**Existing `shop.*` keys are never touched.** A clinic on the current build has
`shop.name`, `shop.gstin`, `shop.licence` and so on already saved. Deleting or
renaming them the day this ships is the one way this reorg could lose
something the customer typed in. Instead:

## 4. The one-time seed — how existing data survives

The first time the new settings are read after the update, and only once, a
small migrator (not an EF migration — plain code, guarded by a flag) copies
the old single identity into both new ones:

```
clinic.name     ← shop.name          pharmacy.name     ← shop.name
clinic.address  ← shop.address       pharmacy.address  ← shop.address
clinic.phone    ← shop.phone         pharmacy.phone    ← shop.phone
                                      pharmacy.gstin        ← shop.gstin
                                      pharmacy.gstregistered ← shop.gstregistered
                                      pharmacy.druglicence  ← shop.licence
                                      pharmacy.pharmacist   ← shop.pharmacist
docs.footer     ← shop.footer
```

Clinic's own GST fields start **off** rather than inherited — see §5 for why.
Consulting hours already live under their own keys
(`opd.morning.from` etc. — added for the session-picker work) and are read as
they are today; nothing to seed.

`clinic.address2` / `pharmacy.address2` (§9) have no `shop.*` counterpart to
seed from — there never was a second address line before this — so both
simply start empty on every install, seeded or brand new, until someone
types a second line in under Clinic or Pharmacy.

A guard key, `migrations.settingssplit.done = true`, makes this idempotent —
run it twice, or open the application twice, and the second run is a no-op.
Same doctrine as the EF migrations: see `Upgrading_twice_is_the_same_as
_upgrading_once` in
[`UpgradeTests.cs`](../tests/Pharma.Tests/UpgradeTests.cs).

The old `shop.*` rows are **left in place**, unread but not deleted. If this
release ever had to be rolled back, the previous build finds its data exactly
where it left it.

### Logo upload — what the form does before Save

- Accepts PNG or JPEG only.
- Capped at 1 MB — a safety net against a phone photo, checked first since
  it is the cheapest check to fail on.
- Then decoded to check its **pixel** dimensions, which is the check that
  actually matters: the print slot is a landscape column roughly 190x50 DIU
  (see `DocumentBuilder.LogoElement`), so width and height are checked
  separately — **700-1300px wide, 200-350px tall**. Below the floor it
  upscales to the column and prints blurred; above the ceiling the Settings
  row only grows for no benefit. A wide **wordmark**, roughly 4 times wider
  than tall, is the sweet spot — a logo with its name already built into it,
  not a standalone icon. A taller or more square image is still accepted —
  it just leaves empty space left and right inside the column — and the
  upload says so rather than silently under-using it.
- Shown immediately as a preview, so what will print is seen before it is
  saved.
- **Remove logo** clears the row. The header prints as text only until
  another logo is uploaded — see §3a for why this is not what shipped.

### 3a. What actually shipped: no compiled fallback at all

This section originally planned for **Remove logo** to fall back to the
compiled-in Twinkle banner — the full-width letterhead every document printed
before this change. That fallback does not exist in what shipped, and the
banner has been retired outright: `DocumentHeaderImage.cs` and its embedded
`Header.png` reader are deleted, along with the tests that pinned the old
banner's behaviour (`LetterheadTests.cs`).

The reason is the half-page redesign in §9, decided the day after this
document was first written. That banner was designed to be read at full page
width; the logo column the new boxed header has room for is roughly
190x50 DIU. Shrinking a banner built for one purpose into a space sized for a
different one does not produce a small logo, it produces an illegible smear
of colour. Rather than ship that, every document now prints **with the
clinic or pharmacy name in text only** until someone uploads a proper
landscape-format logo through this screen.

> **This is a visible change from what was printing before today**, at the
> one customer already live on this build. The colourful full-width Twinkle
> banner every prescription, receipt and bill used to open with is gone from
> the next print, replaced by plain centred text, until a logo sized for a
> corner mark is uploaded here. Worth deciding deliberately rather than
> discovering on the next printed bill — flagged prominently for exactly that
> reason when this shipped.

---

## 5. What changes on the printed page

`DocumentBuilder.AddClinicHeader` currently takes one `ShopProfile` and prints
it on all three document types. It becomes aware of **which** identity a
document is speaking as:

| Document | Identity | GSTIN shown? | Drug licence / pharmacist shown? |
|---|---|---|---|
| OPD prescription | Clinic | Only if the clinic itself is GST-registered | No — never was a pharmacy fact |
| Consultation fee receipt | Clinic | Only if the clinic itself is GST-registered | No |
| Pharmacy bill | Pharmacy | If the pharmacy is GST-registered, as today | Yes, as today |

That "only if the clinic itself is GST-registered" is a quiet correctness fix
riding along with the split. Today, `AddClinicHeader` defaults `showGstin` to
`true` and neither `PrescriptionPrinter` nor `FeeReceiptDocument` overrides it
— so a GST-registered pharmacy's GSTIN currently prints on the prescription and
the fee receipt too, alongside the drug licence number, even though a
consultation is a professional service, not a sale of goods. The mockups in
§6 show current vs proposed side by side.

The logo and the footer are shared: whichever is configured under **Reports**
prints on all three, replacing the compiled-in Twinkle banner and appearing
after the totals, exactly where `BillFooter` already appears on the pharmacy
bill today. This is the "one theme for now" decision from §2 — the plumbing
(`AddClinicHeader` takes a small `DocumentTheme` alongside whichever identity
is speaking) does not prevent per-document themes later; it just is not built
yet, because nobody asked for it yet.

---

## 6. Mockups

Sent separately as a visual: the OPD prescription, the consultation fee
receipt, and the pharmacy tax invoice, each shown as they print **today**
against how they would print **after** this change — same sample patient and
bill used in the rest of the documentation (Baby Anika, Dr. A. Kumar, Twinkle
Children's Hospital), so they can be compared directly against
`docs/images/print-preview.png`.

> **Superseded by §9.** After a real print from another clinic was shared for
> comparison, the "proposed" layout changed to match it — boxed header, dense
> multi-row identity block, half-page. §6 still describes the identity-split
> content; §9 describes the layout it is now set in.

What to look for:
- The prescription and receipt **stop carrying the drug licence number and
  (usually) the GSTIN** — those move to the bill, where they belong.
- Wherever a logo is uploaded, it replaces the compiled Twinkle banner on all
  three documents, not just the bill.
- The footer text becomes whatever was typed under Reports, in the same
  position the pharmacy bill's footer already occupies.

Layout, table structure, fonts and spacing are kept as close as possible to
today's `DocumentBuilder`/`BillPrinter`/`PrescriptionPrinter`/
`FeeReceiptDocument` output — this is a data change, not a redesign, per
*"prints not required to mimic exactly … whatever we have currently should be
printed as good as possible."*

---

## 7. Cost of the rename — what else has to move

Splitting the screen means splitting its automation IDs, which several
existing UI tests reference directly: `ShopName`, `ShopGstin`,
`ShopGstRegistered`, `ShopSave`, `ShopClear`. `ShellCreditUiTests`,
`PharmacyUiTests`, `ScreenshotCapture` and others set these up via
`SetUpShop()` helpers. Every call site needs updating to whichever new screen
now owns the field — mechanical, but not small, and worth budgeting as its own
pass rather than folding into the split itself.

`docs/USER_GUIDE.md` §2 (Settings) needs a full rewrite and five new
screenshots in place of one. `docs/DATABASE_DESIGN.md`'s "Settings — shop
identity" table needs the new key names alongside the old ones, with a note
that `shop.*` is retained but no longer written to by the application.

## 8. Suggested build order

1. `ClinicProfile`, `PharmacyProfile`, `DocumentTheme` read/write in
   `SettingsService`, the one-time seed, and its idempotency test — no UI yet.
   This is the part with a data-safety consequence, so it is worth having its
   own tests before anything is visible on screen.
2. The five Settings screens and their navigation, wired to the new services.
3. The print changes in §5 and §9 — the identity split and the layout redesign.
4. Rename the automation IDs and update the affected UI tests (§7).
5. `USER_GUIDE.md` and `DATABASE_DESIGN.md`.

---

## 9. The layout redesign — from a real print

Two prints from another clinic's billing software were shared for comparison
(a consultation cash receipt and a pharmacy OP sales bill) with the
instruction to keep our own **as similar as possible**, on **A4, using only
about the top half of the sheet**. Both photographs show exactly that: a
compact block of content across the top third to half of an A4 page, the rest
left blank.

Nothing here changes what data is collected — it changes how
`DocumentBuilder` lays out what we already have, and adds one field the
reference print carries that ours does not.

### What "half page" means for us

Not a smaller sheet — still A4, `PageWidth = 794` at 96 dpi as today. What
makes the reference print land in the top half is that it is **dense**:
tight row heights, a boxed header instead of stacked centered lines, and a
three-row identity grid instead of our current two. Ours runs long by
comparison because of generous spacing, not because it holds more
information. Tightening the same content is most of the fix; nothing needs to
be cut to make an A4 sheet with 5–8 bill lines finish well inside the top half.

### The boxed header

The reference receipt puts the whole identity block — logo, name, address,
contact, document title — inside a single ruled box, rather than the stacked
centered lines `AddClinicHeader` prints today with a plain rule underneath.
The pharmacy bill does not box its header the same way, but is otherwise just
as dense. Proposed: **box the header on patient-facing OPD documents
(prescription, fee receipt); leave the pharmacy bill unboxed**, matching each
reference exactly rather than forcing one convention onto both.

### The logo's column, and how it got there

The identity block went through four shapes before landing on the one that
shipped. Corrected after the first pass of mockups: the logo is not a
sibling of the clinic name in a centred flex row — the first two attempts
below, ruled out by something the mockup could not have shown, tried to fix
that:

1. **A `Grid` of `TextBlock`s**, matching the mockup's absolutely-positioned
   overlap most closely. Ruled out because a `FlowDocument`'s `TextRange` —
   what the on-screen preview lets you select and copy, and what every print
   test in `PrintDocumentTests.cs` reads to check a bill — only sees real
   document content. A `BlockUIContainer` is opaque to it, so the whole
   header silently stopped being selectable, copyable, or testable.
2. **A two-column `Table`** — a narrow cell for the logo, a wide cell of
   ordinary `Paragraph` text beside it. Fixed the `TextRange` problem, but
   printed a blank box roughly a third of a page tall above the header on
   any document with no logo — worse than what it replaced. The cause:
   `TableCell.BorderThickness` set to different values on the touching edges
   of two adjacent cells, which is what "two cells that read as one box"
   needs, trips a row-height measurement bug in WPF's `Table`.
3. **A `Section`** — a `Block`, like `Paragraph` or `Table`, and one that has
   carried its own `BorderBrush`/`BorderThickness`/`Padding` since .NET 4.5.
   The logo, when there was one, was the first block inside it, stacked
   *above* the name rather than beside it, with the name, contact line and
   licences as ordinary centred `Paragraph`s underneath. No table anywhere
   near it, so the row-height bug had nothing to trip on. This is what
   originally shipped, and it printed cleanly — but a small logo stacked
   above centred text is not the same shape as "a column beside the text,"
   and a follow-up screenshot with the target region circled asked for
   exactly that: the logo occupying roughly the left quarter to third of the
   header's width, top to bottom, with the name and the rest of the text in
   the remainder.
4. **What shipped**: the same bordered `Section` as attempt 3 — still no
   table forms the box itself — but with a borderless `Table` *nested inside
   it* when a logo is configured, two columns at a 28/72 star-width split.
   Unlike attempt 2, neither `TableCell` carries any border at all
   (`BorderThickness = new Thickness(0)` on both), so the row-height bug
   that attempt 2 tripped has nothing to catch on; the box the reader sees is
   still drawn entirely by the outer `Section`. With no logo configured, the
   table is skipped altogether and the name/contact/title print centred
   across the full width, exactly as before a logo ever existed — the
   text-only fallback is unchanged by any of this.

### Centred, plainly — and the address split into two lines

A same-day refinement to attempt 4, in two parts, both from a real printed
receipt held up against the mockup.

First: a short-lived attempt at "centre only when there's room, otherwise
start flush against the logo" — measuring the widest identity line with
`FormattedText` and switching `TextAlignment` per document. It worked as
built, but was not what was actually wanted: on an address long enough to
trigger it, the header read as *left-aligned text next to a logo*, not as a
letterhead — visually unlike every reference print this whole layout has
been built from. Reverted in favour of what those references actually do:
plain, unconditional centre alignment inside the text column, the same as
centring a paragraph in a word processor — no measurement, no per-document
branching, `TextAlignment.Center` on every identity paragraph regardless of
what shares the header with it.

Second, addressing the actual problem the flush-left attempt was reaching
for: a single long `AddressLine` reads as one dense, hard-to-parse run once
centred. Real letterheads split an address across two lines instead of
wrapping one. `ClinicProfile` and `PharmacyProfile` each gained an
`AddressLine2`, captured on their own Settings tabs right under address line
1, stored as its own Settings row (`clinic.address2` / `pharmacy.address2` —
new rows again, no migration). `DocumentBuilder.ContactLines` folds address
line 1, address line 2 and the phone number down to at most two printed
lines: line 1 is address line 1 alone; line 2 is address line 2 and the
phone together, so a clinic with only one address line and a phone still
gets a tidy two-line block rather than a lone "Ph …" line of its own.
Verified by rendering a clinic with both address lines, a phone and a logo
to PNG: name, both address lines and the phone all centred as one block,
exactly as a hand-typed letterhead would read.

### Every paragraph carries its own colour, not an inherited one

A separate bug, found after a live screenshot rather than in a test: the
clinic name and document title printed in the app's own teal/green accent
colour, and the contact line printed blue and underlined, as if it were a
hyperlink. Not reproducible in a headless test, since `PrintDocumentTests`
never loads `Theme.xaml` or a live `Application` — the suspected mechanism is
`Theme.xaml`'s `Hyperlink` style, which sets `Foreground` from a
`DynamicResource`, bleeding into any inherited-colour text nearby when the
app's real resource dictionaries are in scope.

Proven or not, the fix is unconditionally correct: four `Paragraph`s inside
`DocumentBuilder` were relying on inherited `Foreground` instead of setting
their own, breaking the file's own rule that every printable
`Paragraph`/`Section` states its ink explicitly (`PrintForeground` or
`PrintSecondaryForeground`) rather than trusting whatever it happens to
inherit. WPF gives a local value on an element priority over any style
setter regardless of what that style targets, so an explicit `Foreground` on
every paragraph cannot be overridden by an ambient theme resource no matter
the mechanism. `PrintDocumentTests` now asserts this file-wide: every
`Paragraph` a built document contains — including ones nested inside a
`Table` cell, which the new logo column adds — must carry a
`SolidColorBrush` that is exactly `PrintForeground` or
`PrintSecondaryForeground`, nothing else.

### How big the logo actually prints, and why

`DocumentBuilder.LogoColumnMaxWidth = 190` and `LogoColumnMaxHeight = 50` —
device-independent units, the same units as everything else on the page.
Neither is arbitrary. The page is A4 at 96 dpi (`PageWidth = 794`) with
30 DIU of padding each side, leaving 734 DIU of usable width; 28% of that
(the logo column's star-width share) is 205 DIU, minus the logo cell's own
4-DIU padding on each side, leaving 197 — rounded down to 190 for a little
slack. The height ceiling is matched to the text column beside it rather
than picked independently: the header's own text (name, contact line,
licence line, document title) runs roughly 50-55 DIU tall at its fullest, so
50 keeps the image from being the thing that stretches the row, in every
case but the rare four-line one, where it comes within a few DIU anyway.
Both are enforced as `MaxWidth`/`MaxHeight` on the `Image`, not a fixed
`Width`/`Height` — a backstop against a very large or oddly-shaped source
image inflating the row, the same kind of unbounded growth that produced
attempt 2's blank box above, verified by rendering a very wide, a very tall,
a square and a correctly-proportioned test logo to PNG and inspecting each
one directly.

Working back from that print size to what the upload should accept: 190 DIU
is 1.98 inch, 50 DIU is 0.52 inch. A clinic laser printer commonly runs
300-600 DPI, which asks for 594-1188 physical pixels across the width and
156-313 across the height to print crisp rather than upscaled and soft.
`SettingsViewModel` enforces **700-1300px wide, 200-350px tall** on the
source image as a result — each floor sits at roughly the same relative
position inside its own DPI range that the previous square slot's 150px
floor sat inside its 112-225px range, and each ceiling gives a little
headroom past the 600 DPI mark rather than cutting off right at it. The
Reports screen's own hint text states the practical sweet spot as a wide
wordmark, roughly 4 times wider than tall.

Confirmed by rendering the header with logos at both ends of that range and
at two off-square aspect ratios (a wide banner, a tall strip) to PNG and
looking at them directly, rather than trusting the reasoning alone — both
non-square cases letterbox inside the 36x36 slot exactly as expected, without
breaking the layout around them, which is what the upload flow's aspect-ratio
nudge (§4) is for.

In the mockup, where the logo overlaps a header centred on the full width,
this reads slightly closer to the reference prints than what shipped, where
the text centres in the column beside the logo instead. The difference is a
few millimetres of centring, not a different design — the logo still prints
first, top-left, before the clinic or pharmacy name, which was the actual
point of the request this section answers.

### Half a page is a hard constraint, not a loose visual echo

The first pass of mockups let content trail off wherever it naturally
finished, well short of the midline. Corrected: the redesign works to the
midline as a **budget** — everything above it, nothing below — not a
suggestion. The mockup now draws it as a solid line labelled *"cut here"*
rather than a faint dashed hint, and the sample content is sized to use most
of that budget rather than a third of it.

**The design target is five medicines.** Sized so a prescription (or a bill)
with up to five lines — header, vitals, complaint, diagnosis, the Rx table,
signature, all of it — finishes above the line with room to spare. Five was
chosen, not just accepted as "usually enough": it is comfortably above what
this clinic's own visits run to on ordinary days, so it is not a target
tuned to the one sample in the mockup.

**Past five, it continues below the line — same sheet, not a new page.**
A sixth medicine, a seventh, do not get truncated to keep the top half
looking tidy, and they do not push onto a second physical page either. They
print into the bottom half of the same A4 sheet, which stops being blank
exactly when there is more to say. The line marks where a typical visit
*should* end, not a hard page break the content is clipped against.

The mockup now shows this directly: a five-medicine prescription finishing
inside the line, beside a seven-medicine one where the last two rows and the
signature sit below it, on the one sheet.

Font sizes throughout are also reduced from the first two passes — every
component in `.a4` is smaller, not just the ones that were reading loosest —
since fitting five medicines above the line needed the whole document
tightened, not only the header that started this section.

### The header grid — different shapes for different documents

Today's header is the same two-row, two-column table
(`Bill No / Date`, then `Patient / Doctor`) on all three document types. The
two references do not share one grid between them, and copying that is part
of why they read as denser without looking cramped:

- **The consultation receipt** uses **three rows of three fields** —
  `Bill No / Date / Patient`, `Sv No / PHUID / Age·Sex`, `Ref By / Time /
  Doctor` — which is what lets it carry patient number, age, sex, a referral
  source and the token-equivalent (`Sv No`) in three lines instead of four.
- **The pharmacy bill** uses a **two-column stacked list** instead — more
  rows than columns, because it carries more fields per side (`Bill No / MRNO
  / Patient Name / DR Name` on the left; `Date / Time / DL No / GST No /
  Paymode` on the right).

Proposed: the prescription and fee receipt adopt the receipt's 3×3 grid (a
prescription's third row carries token and visit number where the reference's
carries "Ref By", since we have no referral field); the pharmacy bill adopts
its own reference's two-column stacked list rather than being forced into the
same shape.

### A column we do not have: manufacturer

The pharmacy bill's item table carries `Mfg Nm` — manufacturer — as its own
column, between `Sch.Drug` and `Batch`. We do not print it today. This is the
same gap already written up independently in
[`MANUFACTURER.md`](MANUFACTURER.md) §1, from the manufacturer question
answered a day earlier — a real bill from a second, unrelated clinic now
confirms it as a standard line on a chemist's bill in this market, not a
one-off. Worth doing together with this redesign rather than as two separate
changes to the same table.

> **Needs a decision.** The reference also prints `CGST`/`SGST` as a
> **rate-and-amount pair on every line**, rather than a `GST%` column per
> line plus one summary table below, which is what we do today. The reference
> is denser and arguably harder to scan; ours is easier to read but takes more
> vertical room — the opposite of what "half page" wants. I'd keep the summary
> table (it is the more readable convention, and per-line CGST/SGST is nine
> extra characters of width per row on an already twelve-column table) unless
> you specifically want line-by-line tax to match. Say which.

### Two footer conventions, not one

The reference receipt signs off `"Authorised signatory"` with a printed name.
The pharmacy bill signs off `"User Name"` with the person who billed it — a
**cashier**, not the pharmacist. We have `ShopProfile.PharmacistName` (now
`pharmacy.pharmacist`) but nothing recording who was on the till for a
particular sale. Matching this exactly would mean capturing an operator name
against each `Sale`, which is new data, not a layout change — flagged here so
it is a deliberate decision to skip for this pass, not an oversight.

Proposed for now: keep the pharmacist's name on the bill as today, drop the
"Authorised signatory" line from the OPD documents (nobody signs a printed
consultation receipt in the reference either — it is the pharmacy bill that
has a signatory line, not the receipt), and leave the cashier field for a
later pass if it turns out to matter at the counter.

### Everything this does not touch

- The identity split in §1–§5 — this section is purely about layout.
- Font family, table borders, right-aligned money columns, the print-safe
  black-on-white palette — all stay exactly as `DocumentBuilder.cs` already
  has them.
- Page size and the print pipeline (`DocumentBuilder.Send`, the print
  dialog) — unchanged.

Updated mockups reflecting all of the above are in the same artifact as §6,
republished rather than duplicated.
