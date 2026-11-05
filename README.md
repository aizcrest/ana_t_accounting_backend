# Ledger PH — accounting API

ASP.NET Core 10 development API for a generic Philippine-peso accounting workspace. Pair with `../ana_t_accounting_frontend` (React + TypeScript). Uses a central Identity control database and a separate restricted PostgreSQL database per school. Approved-account login and MFA are required.

## Run

Install .NET SDK 10.0.403 (pinned by global.json), Docker Compose and Python 3. From this backend directory:

```bash
dotnet restore --locked-mode
dotnet build --no-restore
python3 scripts/setup_secure_sample.py
python3 scripts/run_secure_api.py
```

First setup prompts privately for the administrator email/password. Sign in, enroll MFA, then invite a school Operator or create a read-only support grant. For the full React build and startup use `python3 scripts/start_sample.py` (also requires Node.js 22+). In this cloud use `/workspace/.dotnet/dotnet`, `DOTNET_CLI_HOME=/workspace/.dotnet-home` and `NUGET_PACKAGES=/workspace/.nuget/packages`.

Existing legacy PostgreSQL/JSON books are not assigned or imported automatically. Real schools require explicit registration, offline provisioning and named-user invitations. The web process uses restricted control/school roles and never creates databases or applies migrations. There is no default anonymous/JSON fallback; the old storage harness is available only to explicit loopback regression tests.

See [the secure implementation and deployment guide](docs/school-access-implementation.md) for approved workstation certificates, verified database TLS, bootstrap/recovery, legacy import, audit and encrypted backups. No external Tailscale setup or public deployment has been configured automatically.

## Modules and accounting rules

- Eight levels of accounts: levels 1–7 are groups and level 8 accepts postings. Five categories: assets, liabilities, equity, revenue and expenses. Parent level and category are validated.
- Customer, supplier, employee and other cards; goods/services with revenue and expense GL defaults; five document signatory assignments.
- Purchase Request → Purchase Order (nonposting); Sales Invoice → Collection; Accounts Payable → Disbursement. Source type, card, source posting status, and available amount are validated. Draft children reserve source amounts.
- Draft documents can be created and reviewed; financial documents require explicit posting. Lines must use leaf accounts, contain a single positive debit or credit, balance exactly and match the document amount. PHP amounts support two decimal places. Posted documents cannot be posted twice.
- Trial balance: opening, period debit/credit movements, closing balances. Balance sheet: cumulative as of the end date, including current earnings in equity. General and subsidiary ledgers: posted entries, opening/running balances, date, account and card filters. Select a card for an individual subsidiary ledger.
- Sample postings: capital contribution, services sold on credit, partial collection, accrued utilities and partial supplier payment. They deliberately omit VAT and withholding; they are generic examples, not tax-complete Philippine transactions.

## API

`GET /api/health`, `GET /api/state`, `POST /api/accounts`, `/api/cards`, `/api/items`, `/api/documents`, `POST /api/documents/{id}/post`, `PUT /api/approvals`. Legacy module preferences are read-only in authenticated operation; the administrator manages roles.

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

Approved accounts, MFA, server-side role checks, school-specific databases and audit are implemented. Shared production deployment additionally requires the documented private HTTPS/client-certificate and PostgreSQL TLS configuration. Approval-state workflows, period locks, reversal workflows, document editing/cancellation, automatic taxes, inventory costing and statutory certification remain outside the prototype. Signatory names are metadata, not digital approvals. Legacy module preferences cannot grant privileges; the administrator controls Operator/Viewer access. Use synthetic data in the loopback sample.

## School canteen reports

Open **Canteen Reports** for the DM 536, s. 2026 library, school settings, supplementary records and submission tracking. All applicable reports export as portrait PDF, DOCX and XLSX. See [the report scope, data requirements and validation](docs/canteen-reporting.md). Migration 2 adds the reporting data alongside the existing PostgreSQL ledger. No existing data is reset. Run `dotnet run --no-build -- --report-export-self-test` and `DOTNET_EXECUTABLE=dotnet python3 tests/canteen_reports.py` to validate report formats and memo-specific workflows.

## Bank List and Income Statement

Open **Bank List** for Bank Information (GL account combobox), bank-specific Bank Reconciliation, and Check Setup with validated check-number ranges. **Income Statement** is a monthly management report with revenue, mapped COGS, gross profit, expenses and net income, in detailed/condensed form with PDF/DOCX/XLSX exports. See [banking setup and validation](docs/banking.md). Migration 3 preserves existing reconciliation records and adds bank/check-range tables. Run `DOTNET_EXECUTABLE=dotnet python3 tests/banking.py` to test these workflows.

## Authentication and school isolation

The app requires approved accounts and MFA and routes each request to a separately permissioned school database. Use [the implementation and secure setup guide](docs/school-access-implementation.md) for administrator bootstrap, invitations, workstation certificates, migration, audit, encrypted backups, tests and deployment. The [architecture design](docs/design/school-isolation.md) records the design rationale. Existing legacy books are not automatically assigned or imported.
