# Notarization ticket validation — fresh-user-account test

**Purpose:** determine whether Kestrel's notarization-ticket failure is **user-level** (fixable) or **machine-wide** (data-volume corruption; only an erase-install clears it).

Background and prior findings: [NOTARIZATION_TICKET_ISSUE.md](../implementation/NOTARIZATION_TICKET_ISSUE.md). In short — Kestrel cannot validate *any* vendor's stapled notarization ticket (`stapler validate` → exit 65, `syspolicyd: Unable to parse ticket`), while the same artifacts validate fine on Caracara and Egret. Restarting syspolicyd, resetting the Tickets DB (with SIP off), and ruling out a process lock have all failed to fix it. This is the last cheap diagnostic left.

**Time:** ~10 minutes. **Risk:** none — creates and deletes one throwaway standard account; touches nothing in your own account.

---

## What the outcome means

| Result in the throwaway account | Meaning | Next step |
|---|---|---|
| `stapler validate` **succeeds** | Corruption is **user-level** — in the `fsnow` account (keychain, per-user security state) | Worth pursuing: bisect the user account rather than erasing the machine |
| `stapler validate` **fails identically** (exit 65) | Corruption is **machine-wide**, on the data volume | Confirms the standing conclusion — stop investigating; keep Caracara-build / Egret-verify |

The second outcome is the more likely one (the Tickets DB is root-owned and machine-wide), but either way it is decisive and ends the open question.

---

## Step 0 — Record the baseline in your own account

Do this **first**, so you're comparing identical commands against the same binary.

Pick a reference app that is known to be **stapled**. Per the prior session, Brave / Chrome / VS Code / Slack all fail on Kestrel and are all stapled. (Do **not** use Zoom — it is *unstapled*, a different case that fails for an unrelated reason.)

```bash
REF="/Applications/Google Chrome.app"      # or Brave / VS Code / Slack

xcrun stapler validate -v "$REF"; echo "stapler exit: $?"
spctl --assess -vv "$REF" 2>&1
```

Expected on Kestrel today: `stapler` exits **65** ("Could not validate ticket"), `spctl` reports the app as not notarized / rejected.

Optionally also test a CST Reader DMG. If you do, **copy it to `/Users/Shared/`** so the throwaway account can reach it — a file in `~/Downloads` will not be readable by the other user:

```bash
cp ~/Downloads/CST-Reader-arm64.dmg /Users/Shared/
xcrun stapler validate -v /Users/Shared/CST-Reader-arm64.dmg; echo "stapler exit: $?"
```

Write the exit codes down.

---

## Step 1 — Create the throwaway account

A **standard** (non-admin) account is sufficient and keeps the test clean — `stapler validate` and `spctl --assess` are read-only and need no privileges.

**CLI (fastest):**
```bash
sudo sysadminctl -addUser gktest -fullName "GK Test" -password 'ChangeMe-123'
```

**GUI equivalent:** System Settings → **Users & Groups** → **Add User…** → unlock with your admin password → *New User: Standard*, name `gktest`.

Verify it exists:
```bash
dscl . -list /Users | grep gktest
```

---

## Step 2 — Log in as that user (a real login, not `su`)

**This matters.** Use **Fast User Switching** (Control Center → your user name → *GK Test*) or log out and log back in.

Do **not** test with `su - gktest` from Terminal. That gives you the user's shell but not a real login session — no per-user keychain unlock, no launchd session, no user security context — so a pass or fail there proves nothing about user-level state, which is the entire point of the test.

The first login will take a moment to create the home directory. Click through the setup prompts (Apple ID: **Skip**; analytics: decline; Siri: skip).

---

## Step 3 — Run the same checks in the throwaway account

Open Terminal **inside the `gktest` session** and run exactly what you ran in Step 0:

```bash
REF="/Applications/Google Chrome.app"

xcrun stapler validate -v "$REF"; echo "stapler exit: $?"
spctl --assess -vv "$REF" 2>&1
```

And the CST DMG, if you staged one:
```bash
xcrun stapler validate -v /Users/Shared/CST-Reader-arm64.dmg; echo "stapler exit: $?"
```

If it's useful to capture the daemon's view at the same time, run this in a second Terminal tab *before* the validate, then reproduce:
```bash
log stream --predicate 'process == "syspolicyd"' --info
```
Look for whether `Unable to parse ticket` still appears — its presence in the fresh account confirms the failure is not user-scoped.

---

## Step 4 — Interpret, then record

Compare against Step 0 and apply the table at the top.

Add the result to [NOTARIZATION_TICKET_ISSUE.md](../implementation/NOTARIZATION_TICKET_ISSUE.md) as a dated entry — including the exit codes and whether `Unable to parse ticket` appeared — so this doesn't get re-run later.

---

## Step 5 — Clean up

Switch back to your own account (Fast User Switching, or log out), then:

```bash
sudo sysadminctl -deleteUser gktest
```

Confirm the account and its home directory are gone:
```bash
dscl . -list /Users | grep gktest   # expect no output
ls /Users                            # expect no gktest
```

If you staged a DMG in `/Users/Shared/`, remove it too.

---

## Notes

- **SIP does not need to be disabled for this test.** It was disabled in the June 21 session only to delete the SIP-protected Tickets DB. SIP is currently **enabled** (verified 2026-07-28) and should stay that way.
- The test does not modify any system state — no daemon restarts, no database edits. It is purely observational.
- If the fresh account *does* validate successfully, the follow-on bisect within your own account would start with the login keychain and per-user trust settings (`security dump-trust-settings -u`), which the earlier session found empty.
