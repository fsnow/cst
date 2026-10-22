# macOS Notarization Ticket Failure on Kestrel

**Status:** Resolved 2026-10-22 (#1081). The cause was an admin-domain trust-settings override on
**Apple Root CA - G3**. Removing it fixed ticket validation on Kestrel. Stapling a freshly notarized build there
is the one step not yet re-run. Until it is, release DMGs are notarized on Caracara as before.

## Symptoms

First observed 2025-09-24, on Kestrel only. The same artifacts validated on Caracara and Egret.

- `xcrun stapler staple` and `xcrun stapler validate` exit **65**: "Could not validate ticket".
- syspolicyd logs `Unable to parse ticket.`, then `error registering ticket: -1`.
- `spctl` rejects the DMG as "Unnotarized Developer ID", and a downloaded build needs Open Anyway.
- The failure was not specific to CST Reader. Every vendor's stapled ticket failed the same way: Chrome, VS
  Code, Slack and Brave.
- Apple-side notarization was never at fault. `notarytool` reports every submission **Accepted**.

## Cause

[observed, 2026-10-22] Kestrel's admin trust domain held two certificate overrides, each with an empty trust
settings list:

```
$ security dump-trust-settings -d
Number of trusted certs = 2
Cert 0: Apple Root CA - G3
   Number of trust settings : 0
Cert 1: Developer ID Certification Authority
   Number of trust settings : 0
```

Caracara, where stapling works, holds only the Developer ID Certification Authority override, so that one is
harmless. **Apple Root CA - G3** was the difference. It is the root of the chain that signs notarization
tickets. #1081 reported that a G3 override alone reproduced every symptom on a test machine.

Why the earlier investigation missed it: the June 2026 debug session listed trust-setting overrides as
"ruled out". It took these two entries for the normal default and never compared them against a working
machine. With that hypothesis closed, the failure was put down to corrupt data-volume state. A fresh user
account, the Tickets database reset and the upgrade to macOS 27 all failed to clear it. That fitted the
theory, but the real reason was simpler: the admin trust domain is machine-wide and survives all three.

## Fix

Remove the G3 override from the admin domain:

```bash
security find-certificate -c "Apple Root CA - G3" -p \
  /System/Library/Keychains/SystemRootCertificates.keychain > apple-root-g3.pem
sudo security remove-trusted-cert -d apple-root-g3.pem
```

[observed] Setting the certificate to "Use System Defaults" in Keychain Access does **not** persist. It reverts
to "Always Trust". The `security` command is what removes the entry. The certificate stays in the system root
store with default trust.

## Verification

[observed, 2026-10-22, Kestrel, macOS 27.0.1]

- `security dump-trust-settings -d` lists only the Developer ID Certification Authority.
- `xcrun stapler validate` on the stapled Beta 8 arm64 DMG passes, and `spctl` reports
  "accepted, source=Notarized Developer ID".
- `xcrun stapler validate` passes for Chrome, VS Code, Slack and Brave.
- `/var/db/SystemPolicyConfiguration/Tickets-wal` is being written again. It had been frozen since 2026-06-23,
  which fits ticket registration failing at validation, before it could commit.

Still to confirm: run `notarize-macos.sh` on Kestrel against a fresh build and check that `stapler staple`
succeeds.

## If it recurs

Compare `security dump-trust-settings -d` (admin domain) and `security dump-trust-settings` (user domain)
with the output on a machine where stapling works. Any override on an Apple root or intermediate certificate
is a suspect. How the G3 override got onto Kestrel is unknown.
