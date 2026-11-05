# Ledger PH — accounting API

ASP.NET Core 10 development API for a generic Philippine-peso accounting workspace. Pair with `../ana_t_accounting_frontend` (React + TypeScript). Supports PostgreSQL 18 via Npgsql, with an explicit JSON fallback for local development.

## Run

Install .NET SDK 10.0.403 (pinned by global.json), then run `dotnet run --urls http://127.0.0.1:5080` from this directory. In this cloud environment use:

```bash
export DOTNET_CLI_HOME=/workspace/.dotnet-home
export NUGET_PACKAGES=/workspace/.nuget/packages
/workspace/.dotnet/dotnet build
ASPNETCORE_URLS=http://127.0.0.1:5080 /workspace/.dotnet/dotnet run --no-build
```

Without a SQL connection, data is seeded on first launch and saved atomically to ignored `data/ledger.json`. `ACCOUNTING_DATA_PATH` overrides this location. Back up the file before replacing or deleting it. Single-process local development storage; not a multi-server production database.

## PostgreSQL

See [database/README.md](database/README.md) for the schema, local PostgreSQL 18 setup, connection configuration and integration checks. Set `ConnectionStrings__Accounting` to enable PostgreSQL persistence. If PostgreSQL is configured but unavailable, startup fails rather than silently falling back. An empty PostgreSQL database imports the existing local JSON ledger, or uses the seed data if no local file exists. Existing PostgreSQL data is retained on startup.

The backend now targets **net10.0**, SDK **10.0.403**, the latest stable SDK reported by Microsoft release metadata on 2026-11-05. The running local API has been restarted on .NET 10.

## Modules and accounting rules

- Eight levels of accounts: levels 1–7 are groups and level 8 accepts postings. Five categories: assets, liabilities, equity, revenue and expenses. Parent level and category are validated.
- Customer, supplier, employee and other cards; goods/services with revenue and expense GL defaults; five document signatory assignments.
- Purchase Request → Purchase Order (nonposting); Sales Invoice → Collection; Accounts Payable → Disbursement. Source type, card, source posting status, and available amount are validated. Draft children reserve source amounts.
- Draft documents can be created and reviewed; financial documents require explicit posting. Lines must use leaf accounts, contain a single positive debit or credit, balance exactly and match the document amount. PHP amounts support two decimal places. Posted documents cannot be posted twice.
- Trial balance: opening, period debit/credit movements, closing balances. Balance sheet: cumulative as of the end date, including current earnings in equity. General and subsidiary ledgers: posted entries, opening/running balances, date, account and card filters. Select a card for an individual subsidiary ledger.
- Sample postings: capital contribution, services sold on credit, partial collection, accrued utilities and partial supplier payment. They deliberately omit VAT and withholding; they are generic examples, not tax-complete Philippine transactions.

## API

`GET /api/health`, `GET /api/state`, `POST /api/accounts`, `/api/cards`, `/api/items`, `/api/documents`, `POST /api/documents/{id}/post`, `PUT /api/approvals`, `PUT /api/access`.

`GET /api/reports/{trial-balance|balance-sheet|general-ledger|subsidiary-ledger}` accepts optional `from`, `to` (ISO dates), `account` and `card`. Balance sheet UI deliberately uses an as-of date rather than period movements.

## Validate

```bash
dotnet build
DOTNET_EXECUTABLE=dotnet python3 tests/smoke.py
```

The ten integration tests launch an isolated API on port 5081 with temporary storage; no demo ledger is altered. They test report arithmetic, eight-level ancestry, date/card filters, unbalanced posting, duplicate posting, source links, overpayment and precision.

## ATC provenance and current limitation

Official retrieval attempted on 2026-11-05 from https://www.bir.gov.ph and https://bir-cdn.bir.gov.ph. Both were blocked by environment HTTP 403. Four explicitly unverified reference examples (WI010, WC010, WI100, WC100) are supplied without tax rates. **These are not a downloaded or verified current BIR list.** Network domain additions have been saved in the environment configuration draft; apply them before retrying official retrieval. Do not use this reference table for statutory filings or infer the latest effective rates from it. No automated withholding computation is implemented.

## Scope and limitations

This is a functional local development prototype, not production accounting software. No authentication, tenant isolation, audit trail, approval-state workflow, period locks, reversal workflow, document editing/cancellation, automatic taxes, inventory costing, or statutory certification. Module access policies are persisted configuration only and do not enforce security. Signatories are names snapshotted on document creation, not digital approvals. All data is visible to anyone who can access the local API. Bind the API to loopback and use synthetic data. Build authentication and enforced authorization before any shared deployment.

## School canteen reports

Open **Canteen Reports** for the DM 536, s. 2026 library, school settings, supplementary records and submission tracking. All applicable reports export as portrait PDF, DOCX and XLSX. See [the report scope, data requirements and validation](docs/canteen-reporting.md). Migration 2 adds the reporting data alongside the existing PostgreSQL ledger. No existing data is reset. Run `dotnet run --no-build -- --report-export-self-test` and `DOTNET_EXECUTABLE=dotnet python3 tests/canteen_reports.py` to validate report formats and memo-specific workflows.

## Bank List and Income Statement

Open **Bank List** for Bank Information (GL account combobox), bank-specific Bank Reconciliation, and Check Setup with validated check-number ranges. **Income Statement** is a monthly management report with revenue, mapped COGS, gross profit, expenses and net income, in detailed/condensed form with PDF/DOCX/XLSX exports. See [banking setup and validation](docs/banking.md). Migration 3 preserves existing reconciliation records and adds bank/check-range tables. Run `DOTNET_EXECUTABLE=dotnet python3 tests/banking.py` to test these workflows.

## Proposed school-level access design

[School-level isolation design](docs/design/school-isolation.md) specifies separate accounting databases per school, a central identity/access control database, request-scoped school resolution, approved-device integration, report/file isolation and a migration/test plan. It is a design only: the current prototype still has no authentication or enforced school isolation.
