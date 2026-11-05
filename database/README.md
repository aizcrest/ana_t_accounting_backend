# PostgreSQL database

The app uses PostgreSQL 18 with Npgsql 10 and .NET 10. SQL Server dependencies and setup have been replaced. The accounting database and all tables have been created and validated on PostgreSQL in this environment.

## Tables

| Table | Purpose |
|---|---|
| accounts | Eight-level hierarchy, parent FK, account category and posting leaves |
| cards | Customers, suppliers, employees and other counterparties |
| items | Goods/services, price, revenue and expense account FKs |
| signatory_assignments | Prepared, checked, verified, noted and approver names |
| module_access | Stored module policy configuration |
| atc_codes | Descriptions, source and verification status; no invented rates |
| documents | PR, PO, SI, collections, payables, disbursements and journals |
| journal_lines | Document/line key, account/card FKs, numeric(19,2) amounts |
| document_signatories | Names snapshotted onto documents |
| store_state | Optimistic concurrency revision for the shared workspace |
| schema_versions | Migration versions and UTC timestamps |

Schema version 1 is in `001_initial.sql`. It is embedded and applied transactionally. Documents use sourceId for PR→PO, Invoice→Collection and AP→Disbursement links. Constraints enforce unique document numbers, foreign keys, valid categories and nonnegative journal amounts; the save operation also verifies leaf postings, account ancestry and balanced posted documents. The API validates source type, card and available settlement balance.

## Authentication and school isolation

The original business tables now live in separate school databases, with banks/check ranges (migration 3) and immutable tenant/audit structures (migration 4). Identity, school registrations, memberships, sessions, devices and grants live in a separate control database. See [the current implementation guide](../docs/school-access-implementation.md).

```bash
dotnet restore --locked-mode
dotnet build --no-restore
python3 scripts/setup_secure_sample.py
python3 scripts/run_secure_api.py
```

The first setup prompts privately for administrator credentials. Existing `accounting` and ignored JSON books are left untouched; explicit owner-approved legacy import is a separate command. `setup_postgres.py` starts the local container but does not initialize a global anonymous accounting API. The old `run_postgres_api.py` name now launches secure mode.

Normal startup requires restricted control credentials. Every financial request resolves a verified membership/support context to its own restricted school connection and checks the immutable marker. No fallback database is used when auth/storage fails. PostgreSQL remains private; production runtime connections require SSL Mode=VerifyFull.

The small-workload repository still writes an atomic snapshot, but snapshots/revisions are now request-scoped and confined to a verified school database. Committed mutations append an audit event in the same transaction. There is no global cached ledger or cross-school connection selector controlled by the browser.

## Validation

Use `tests/isolation.py` and `tests/device_access.py` for real authentication/isolation and TLS-device checks. The report, banking and accounting regression scripts explicitly select loopback LegacyTest to check their calculations separately; passing those does not alone validate authentication. `--report-export-self-test` runs offline without serving financial routes. Never expose regression mode through public interfaces or tunnels.
