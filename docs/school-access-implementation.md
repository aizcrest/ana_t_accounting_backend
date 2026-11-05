# Implemented school isolation and authentication

The application now requires authenticated, MFA-verified access to separately permissioned PostgreSQL school databases. This replaces the old unauthenticated, process-wide ledger. The design rationale remains in design/school-isolation.md; this document describes the implemented boundaries and deployment prerequisites.

## What is implemented

- ASP.NET Core Identity password hashing, lockout, invitation tokens, authenticator MFA and single-use recovery codes. No public signup or default password.
- Protected HttpOnly cookie references to server-side sessions. Thirty-minute idle timeout (ten minutes while MFA is pending), eight-hour absolute expiry, immediate per-request user/session/school/grant checks. Reading session metadata alone does not extend idle expiry.
- Antiforgery tokens for every state-changing API request, same-origin checks, strict same-site cookies and endpoint/IP sign-in throttling. React stores no access token in localStorage. MFA QR codes are rendered locally.
- Central control database for users, schools, one active primary Operator per school, Viewer memberships, sessions, approved certificates, expiring read-only support grants and platform audit.
- Separate accounting database and restricted login role per school. Database resolution comes only from verified server-side membership/support context, never a browser school ID, header or connection string. Each request gets a fresh school snapshot; no mutable ledger is shared between schools or requests.
- Immutable tenant marker checked whenever a school connection is opened. Runtime roles cannot change that marker, audit history or schema, create databases, or connect to another school's/control database. Existing natural account codes/document numbers remain school-local.
- Registered name/code come from the school registry and cannot be changed by an Operator. All setup, documents, banks, reconciliations, canteen records, reports and PDF/DOCX/XLSX exports use the same scoped repository. Original legacy bank-record scoping is preserved inside each school.
- Operator can prepare/post/manage their school's books; Viewer and support access are read-only. `/api/access` is no longer a way for school users to grant themselves privileges; membership roles are managed by the administrator.
- Accounting writes and local audit events commit in one transaction. Read attempts are audited before financial handlers. A separate offline `--flush-audit` command forwards the durable school outboxes idempotently into the control audit view. Denied authenticated requests are recorded centrally.
- Administrator UI for school registration/suspension, user invitations/disabling/roles, device approval/revocation, support grants and audit. Administrators cannot read books by default; an explicit reasoned support grant requires recent authenticator MFA, lasts at most 30 minutes, and permits reads/exports only.
- Encrypted per-school offline backups and tenant-checked quarantine restore verification. Restoring never automatically changes a live database or another school's routing.

## Approved workstations: actual enforcement

Production requires a real **mutual TLS client certificate**, verified against your private client CA and its SHA-256 fingerprint approved in the control database. An Operator/Viewer certificate must be scoped to their school; an administrator uses an administrator workstation certificate. Sessions are tied to that certificate. Device revocation and current school scope are checked on every request; changing a device scope or revoking it through administration also revokes its application sessions.

Caller-supplied MAC addresses, device IDs, Tailscale headers or forwarded client-certificate strings are not trusted. The application accepts certificates from the real Kestrel TLS connection. Tailscale can remain the private network entrance but is not treated as school authorization or fabricated node identity. Use direct HTTPS over the private network or a TLS passthrough gateway. A proxy that terminates client TLS and forwards ordinary HTTP (including an unconfigured Tailscale Serve setup) does not satisfy this certificate boundary and is rejected rather than trusted through headers.

Install a personally approved certificate and private key on each workstation. Use hardware-backed/non-exportable keys where possible; a copied software private key represents the same certificate identity and is not hardware attestation. Keep the client CA signing key offline. The web service needs only the public client CA file, not the power to issue workstation certificates. Certificate chain, expiry and client-auth usage are verified; application approval supplies immediate revocation for the private CA deployment.

**No production network or user workstation certificates have been deployed automatically.** Local testing uses an explicit loopback-only Development mode; actual TLS device enforcement was exercised with generated temporary certificates, including rejection cases. This distinction is visible in `/api/health`.

## Prepare a local authenticated sample

Requirements: .NET SDK pinned by global.json, Docker Compose, Python 3, Node.js 22+.

```bash
# From the frontend checkout:
npm ci
npm run build
# From the backend checkout:
dotnet restore --locked-mode
dotnet build --no-restore
python3 scripts/setup_secure_sample.py
python3 scripts/run_secure_api.py
```

The setup command starts the existing PostgreSQL container, prompts privately for an administrator email/password on first initialization and provisions an explicit Demo School A with synthetic data in a separate database. It does not import, reset or assign the existing `accounting` database or ignored JSON file. Set up MFA at the administrator's first sign-in. To get an editable school workspace, invite a named Operator from the Users tab and accept that invitation in a signed-out browser. The invitation recipient chooses a password and enrolls MFA. An administrator can instead create an audited read-only support grant to inspect the demo.

`python3 scripts/start_sample.py` builds/copies React assets and runs the same secured sample workflow. `scripts/run_postgres_api.py` is now an alias for the secured launcher, not an anonymous fallback. To see the UI from a source checkout without that helper, copy frontend/dist into backend/wwwroot as generated assets or use Vite's same-origin development proxy.

Ignored local files contain credentials and Data Protection keys. Do not commit, print, attach to chat, or include `.local/` in the source sample archive. `security_cli.py` loads privileged PostgreSQL credentials only in the offline command; `run_secure_api.py` loads restricted control credentials and removes provisioning/bootstrap variables from the web process.

## Register a real school and migrate existing books

1. In the administrator UI register the verified school name/code. It starts Pending; no user can access it yet.
2. On the server, run `python3 scripts/security_cli.py --provision-pending` with the provisioning identity. It creates each school's database, protected marker, business schema, restricted role and templates. Fresh real schools have no copied transactions, cards, banks or fabricated signatories.
3. Back up the old workspace and identify its actual owner explicitly. To copy legacy data once, set `Ledger__TargetSchoolId` to the new school's internal UUID and `ConnectionStrings__LegacyImport` securely to the source, then run `--import-legacy`. The destination must be empty. The source is read without initializing or changing it. Verify counts, balances, bank inputs and report totals before inviting the Operator.
4. Invite the named Operator after status is Ready. Disabling a former Operator ends their sessions and frees the active assignment; historical audit actor IDs remain. Do not reuse a former employee's credentials for their replacement.
5. Stop the old anonymous web service. It must not remain reachable alongside the protected system.

Provisioning is deliberately outside HTTP: the web process refuses to start if privileged provisioning credentials are present. The `Pending/Failed/Ready` school status is the idempotent provisioning queue; the offline command may be scheduled by the owner. Control schema is version 1, initialized by Identity/EF `EnsureCreated` in the bootstrap command only. Future control-model changes require a reviewed EF schema migration; web startup never creates/migrates tables. School business migrations 1–4 run only in provisioning/regression commands.

## Production configuration

Supply these through secure deployment configuration, not tracked appsettings or chat:

| Setting | Purpose |
|---|---|
| ConnectionStrings__Provisioning | Offline owner/migrator only; never the web process |
| ConnectionStrings__Control | Restricted control runtime login generated during bootstrap |
| Ledger__Mode=Production | Default secure deployment mode; no anonymous/JSON fallback |
| ASPNETCORE_ENVIRONMENT=Production | Production framework behavior |
| Ledger__KeyPath | Persistent private Data Protection keyring; preserve across restarts/instances |
| Ledger__KeyEncryptionCertificate | Private PFX used to encrypt Data Protection keys at rest |
| Ledger__KeyEncryptionCertificatePassword | PFX password supplied securely |
| Ledger__ClientCaFile | Public PEM trust anchor for approved workstation certificates |
| Kestrel__Certificates__Default__Path / Password | Server HTTPS certificate, supplied securely |
| ASPNETCORE_URLS | Private HTTPS listener, not a public HTTP endpoint |

Control and per-school runtime PostgreSQL connections must use `SSL Mode=VerifyFull` in Production with a valid trusted/root certificate. Provision the database TLS trust before creating protected school connection entries. Hostname/certificate verification is not disabled. Bootstrap/provisioning uses the same persistent Data Protection keyring and production key-encryption certificate as the web process.

Block public API/database ports; optionally restrict HTTPS further with Tailscale device approval. Do not enable subnet routing that lets arbitrary school LAN devices bypass workstation admission. Approve the first administrator certificate offline with `--approve-device`, using `Ledger__DeviceFingerprint`, `Ledger__DeviceName` and optional `Ledger__DeviceSchoolId` (omit the school ID for an administrator workstation). Subsequent certificate approvals are available in the authenticated Devices UI.

Production startup fails when required trust/key configuration is absent; direct non-HTTPS requests are denied. `Development` and `LegacyTest` require ASPNETCORE_ENVIRONMENT=Development and loopback-only listening. They cannot be selected by a request, cookie or header. `LegacyTest` exists only to run the preexisting financial regression suites, not as a deployable account/security mode. Do not expose it through a public tunnel.

Current pool bounds: control runtime maximum 20 connections; each school pool maximum 5, minimum 0 with idle cleanup. Monitor the PostgreSQL cluster's total connection budget before expanding the school count. The snapshot repository remains suitable for the initial small workload; serializable revision checks reject stale writes with 409. An incremental repository is a future performance improvement, not a reason to share tenant snapshots.

## Administration and recovery

- User, school, device and support-grant status is checked on every protected request. Already delivered data/downloads cannot be recalled. A transaction that has already passed admission may finish; suspend/drain before maintenance.
- All administrator mutations require authenticator MFA within the last 10 minutes. Refresh it in the Support access tab before making privileged changes; recovery-code login alone does not meet that requirement.
- No public password-reset or registration endpoint is exposed. For owner-verified recovery, run offline `--reset-password` (hidden prompt) and/or `--reset-mfa`. These revoke existing sessions; MFA reset invalidates old authenticator/recovery material and requires enrollment again. Recovery codes are single-use and shown only once during initial enrollment.
- Invitation links are returned only to the administrator, expire after a day, and are not emailed automatically. Share them privately. Password reset consumes the framework token/security stamp so it cannot be reused.
- The certificate trust CA does not replace individual account/MFA controls. Revoke both a lost workstation's approval and its sessions. Do not grant platform administration merely to let someone review another school's reports.
- The application enforces Operator/Viewer roles. Legacy module preference checkboxes are read-only and do not grant privileges. Signatory text remains document metadata, not electronic multi-person approval.

## Backups and restore validation

For `--backup-school`, explicitly set `Ledger__TargetSchoolId` and optional private `Ledger__BackupPath`, suspend that school and drain its in-flight writes first. The command writes an encrypted, integrity-protected backup of its snapshot and audit history outside wwwroot. Preserve the Data Protection keyring/certificate separately; losing them prevents decrypting connection entries and backups.

Run `--verify-restore` with the same target school and backup path. It checks the embedded school UUID/code, snapshot hash and schema version before creating a private quarantine database. It restores business/audit records and verifies an exact reloaded snapshot. A School A backup cannot be verified for School B. No directory mapping or live school database is overwritten. The verified quarantine name is printed for owner review.

Live activation is an explicit maintenance procedure, not an automatic API: keep the school suspended, stop/drain web processes, retain a rollback copy, verify marker/totals, rotate its runtime credential and protected directory entry when switching databases, then restart/revalidate before enabling it. The runtime resolver currently requires the canonical `ledger_school_<uuid>` database name, so a quarantine database cannot accidentally be activated by changing a browser parameter. Remove unneeded quarantines through owner-controlled database maintenance after review.

## Tests and evidence

```bash
dotnet build
dotnet run --no-build -- --report-export-self-test
DOTNET_EXECUTABLE=dotnet python3 tests/isolation.py
DOTNET_EXECUTABLE=dotnet python3 tests/device_access.py
ACCOUNTING_TEST_POSTGRES=1 DOTNET_EXECUTABLE=dotnet python3 tests/smoke.py
DOTNET_EXECUTABLE=dotnet python3 tests/canteen_reports.py
DOTNET_EXECUTABLE=dotnet python3 tests/banking.py
```

The authenticated suites use real Identity passwords, cookie sessions, MFA/recovery, CSRF and generated databases/roles, not a trusted school/user test header. They cover separate balances and repeated record IDs, cross-school bank/exports and branding tampering, immutable database markers, denied cross-database grants, Viewer/admin support scope, revocation, restart, audit forwarding, encrypted backup and wrong-school restore rejection. The device suite exercises the actual Kestrel TLS connection with trusted/unknown/unapproved/wrong-school certificates, cookie replay on another workstation, revocation and production configuration rejection.

The financial suites opt explicitly into loopback-only LegacyTest solely to exercise existing calculations and database behavior; their success is not evidence of authenticated isolation. Browser checks separately exercise the actual login/MFA/invitation/role/support/revocation flows. No production school, external Tailscale configuration, real certificate enrollment, or public deployment is claimed by these local tests.

The browser fixture is also saved for repeatable checks: install Playwright/Chromium in your test environment, then run `DOTNET_EXECUTABLE=dotnet python3 tests/browser_auth.py`. Use PLAYWRIGHT_MODULE_PATH if Playwright is installed outside the normal Node module resolution path, and CHROMIUM_EXECUTABLE for a system browser. It uses generated credentials and disposable databases; never pass real production credentials. Screenshots deliberately exclude enrollment QR secrets, recovery-code displays, invitation tokens and typed passwords.
