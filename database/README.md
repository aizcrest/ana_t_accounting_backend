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

## Local PostgreSQL setup

Requires Docker Compose, Python 3 and .NET SDK 10.0.403. From the backend directory:

```bash
dotnet restore --locked-mode
dotnet build --no-restore
python3 scripts/setup_postgres.py
python3 scripts/run_postgres_api.py
```

Cloud commands use `/workspace/.dotnet/dotnet`, `DOTNET_CLI_HOME=/workspace/.dotnet-home` and `NUGET_PACKAGES=/workspace/.nuget/packages`.

Setup generates a random local password in ignored `.local/postgres.env`, starts the official PostgreSQL 18 image on **127.0.0.1:5432**, waits for health and initializes `accounting`. Its local connection is stored in ignored `.local/postgres-connection.json`. PostgreSQL data persists in the Compose volume `postgres_data`; do not delete that volume to restart the sample. The API imports the existing local JSON ledger only when the database is empty, otherwise it retains database data. The original JSON file is preserved but stops receiving writes while PostgreSQL mode is active.

The container is a local-only development service. For remote databases set `ConnectionStrings__Accounting` securely and use a verified TLS connection such as SSL Mode=VerifyFull with a trusted root certificate; never commit credentials. PostgreSQL connections configured but unavailable cause startup to fail. Without an explicit connection, the API retains JSON fallback mode for offline development.

## Browser sample

From the backend directory, `python3 scripts/start_sample.py` builds React, copies generated static assets into ignored `wwwroot`, builds .NET, initializes PostgreSQL and starts the complete app on localhost port 5080. This requires Node.js 22+ as well as the tools above. Open that address on the same computer. No public internet preview or authentication is provided.

## Validation

```bash
python3 scripts/run_postgres_api.py --check-database
python3 scripts/run_postgres_api.py --database-self-test
ACCOUNTING_TEST_POSTGRES=1 DOTNET_EXECUTABLE=dotnet python3 tests/smoke.py
```

The database self-test creates a generated `accounting_test_<guid>` database, verifies migration, seed/reload, stale-write rejection and constraint rollback, then drops only that test database. The HTTP suite with ACCOUNTING_TEST_POSTGRES=1 also uses a generated temporary PostgreSQL database, runs all 10 accounting tests and removes it. Without that flag the suite uses temporary JSON data and clears inherited database bindings. Both modes have passed. SQL self-tests require a development identity with create/drop database privileges; ordinary shared deployment should provision schema separately with a restricted application identity.

## Persistence scope

This is a small single-workspace prototype. Parameterized writes replace the in-memory snapshot within one serializable transaction, guarded by a revision check. Use one API process; other processes may hold cached reads until restarted or a conflict reload. Incremental repositories, audit history, multi-company isolation and enforced authorization remain separate work. No real financial data should be placed in the downloadable sample.
