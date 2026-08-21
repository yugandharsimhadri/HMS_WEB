# Giving someone the installer

How to build the setup file, how to get it to a clinic PC, and what the person
there has to do.

---

## 1. Build it

From the repository root, on a machine with the .NET SDK:

```bash
powershell -ExecutionPolicy Bypass -File scripts\make-installer.ps1
```

Takes two or three minutes, most of it compressing. You get one file:

```
C:\HMS\Setup\ShivayaanHMSSetup-1.0.0.5.exe        about 67 MB
```

That is the whole thing. There is no folder to copy alongside it.

To put it somewhere else:

```bash
powershell -ExecutionPolicy Bypass -File scripts\make-installer.ps1 -OutputDir D:\Handover
```

> **The version comes from the project, not the script.** `<Version>` in
> `src/Pharma.App/Pharma.App.csproj` is what names the setup file, what the
> sidebar shows and what Add or remove programs lists. The fourth part is the
> publish number — **raise it by one before building any installer you intend
> to hand over**, or two different releases will both call themselves the same
> thing and a support call will have no way to tell them apart.

---

## 2. Get it to the clinic

67 MB is too big for most mail systems, so in practice:

- **A pen drive.** Simplest, and it works with no internet at the clinic.
- **OneDrive, Google Drive or WeTransfer.** Share a link, let them download it.

Either way, tell them the file will be flagged — see the next section — because
otherwise the first thing they meet is a warning that looks like a virus alert.

> **Nothing else needs to go with it.** Not the .NET runtime, not Visual C++,
> not the database. The runtime is inside the file and the database is created
> on first launch.

---

## 3. What the person at the clinic does

1. **Copy the setup file to the desktop.** It is named for the version it
   installs — `ShivayaanHMSSetup-1.0.0.5.exe`. Running it straight off a pen
   drive works, but from the desktop it is easier to find again.

2. **If it came from the internet, unblock it first.** Right-click the file →
   **Properties** → tick **Unblock** at the bottom → **OK**. Windows marks
   anything downloaded, and without this the install can fail with no useful
   message. A file that arrived on a pen drive is usually not marked.

3. **Double-click it.**

4. **Windows will say the publisher is unknown.** Click **More info**, then
   **Run anyway**. This is expected: the file is not code-signed. See
   [Signing](#signing) below.

5. **Answer Yes** to "Install Sivaayaan HMS on this PC?"

6. **Answer Yes to the Windows administrator prompt.** The installer needs it
   once, to write to `C:\` and to register the entry under Add or remove
   programs. If the person using the PC is not an administrator, someone who is
   has to do this step.

7. Wait for **"Sivaayaan HMS is installed. There is a shortcut on the desktop."**

8. **Open it from the desktop shortcut.** The first launch creates the database
   and can take a few seconds longer than usual.

---

## 4. What it puts on the machine

| Path | What it is |
|---|---|
| `C:\HMS\App\ShivayaanHMS.exe` | The application. The only program file |
| `C:\HMS\DB\` | The database. **Never touched by an install** |
| `C:\HMS\DBBackup\` | A copy taken each day the application is opened |
| `C:\HMS\Logs\` | Activity logs, rolled daily, capped at 10 MB |
| Desktop and Start menu | Shortcuts, for all users of the PC |
| Add or remove programs | An entry with an uninstaller |

---

## 5. Setting the clinic up on first run

Once it opens, in this order:

1. **Settings → Clinic / Pharmacy details.** The clinic name, address, phone, GSTIN, drug
   licence number and pharmacist. Everything here prints on every bill, receipt
   and prescription, so it is worth getting right before the first sale.
2. **Settings → Doctors.** At least one, or no visit can be booked.
3. **Medicines**, then **Inventory** — or **Inventory → Import supplier bill**
   if there is a supplier file to load.

The [user guide](USER_GUIDE.pdf) covers all of this properly.

---

## 6. Installing a newer version

Exactly the same steps. Build a new setup file, carry it over, run it.

- The program is **replaced**.
- `C:\HMS\DB`, `C:\HMS\DBBackup` and `C:\HMS\Logs` are **left alone**. This is
  the whole reason the database does not live beside the executable.
- **Sivaayaan HMS must be closed.** The installer stops with *"Sivaayaan HMS is open. Close
  it and run this again"* rather than failing partway through on a locked file.

Take a backup before updating anyway — **Settings → Back up now** — and copy
`C:\HMS\DBBackup` to a pen drive. It costs a minute.

---

## 7. Removing it

**Settings → Apps → Installed apps → Sivaayaan HMS → Uninstall.**

That removes the program, the shortcuts and the Add or remove programs entry.

**It deliberately leaves `C:\HMS\DB` behind.** Uninstalling the software should
never be what destroys a clinic's records. Delete that folder by hand if the
data really is meant to go — and take a copy first.

---

## 8. When it goes wrong

| What you see | What it is |
|---|---|
| "Windows protected your PC" | Not signed. **More info → Run anyway** |
| Nothing happens on double-click | The file is blocked. Properties → **Unblock** |
| "Sivaayaan HMS is open. Close it and run this again" | Exactly that. Close it, including from the system tray |
| Administrator prompt never appears, install fails | The account is not an administrator. Someone who is has to run it |
| Antivirus quarantines the file | An unsigned self-extracting exe is a common false positive. Allow it, or sign the file |
| It installs but will not open | `C:\HMS\Logs` has the reason. The last few lines are usually enough |

---

## Signing

The setup file is not code-signed, which is why Windows calls the publisher
unknown and why antivirus is occasionally suspicious of it.

For one or two clinics, telling people to click **Run anyway** is workable. For
wider distribution it is worth buying a code-signing certificate and signing
both `ShivayaanHMS.exe` and `ShivayaanHMSSetup.exe` — the warnings go away and
SmartScreen stops interrupting.

---

## The other two ways to publish

Both still work and are still in the repository. Neither replaces the setup file
for handing to someone else.

**`scripts/publish.ps1`** — publishes straight into `C:\HMS\App` on *this*
machine and makes a desktop shortcut. For the development PC, not for handover.

**ClickOnce** (`src/Pharma.App/Properties/PublishProfiles/ClickOnceProfile.pubxml`)
— publish from Visual Studio, or:

```bash
msbuild src\Pharma.App\Pharma.App.csproj -t:Publish -p:PublishProfile=ClickOnceProfile -p:Configuration=Release
```

It produces `setup.exe`, `ShivayaanHMS.application` and an `Application Files`
folder in `src\Pharma.App\bin\publish`. **All three have to travel together** —
the setup.exe is only a bootstrapper and fails on its own, which is why it is
not what we hand over. Its real strength is automatic updates from a shared
folder, which is worth revisiting if the clinic ever has more than one PC.
