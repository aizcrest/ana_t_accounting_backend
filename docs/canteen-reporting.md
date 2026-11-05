# School canteen reporting — DM 536, s. 2026

Source: the memorandum and annex text supplied by the user in chat, dated 30 October 2026, Schools Division of Batangas. This transcription has not been independently checked against the original PDF. Layouts are adapted working reports, not a claim of exact annex/GAM/COA form certification.

## Default small-school scope

The sample assumes a **school-managed, non-IU canteen with a bank account**. This is configurable under Canteen Reports → School settings; it is not a finding about the user's actual school. Choose IU/Non-IU, bank status, operation and congressional district to obtain the appropriate report catalogue and focal person. Cooperative and laboratory reports are excluded until applicable.

| Group | Default reports | Due date |
|---|---|---|
| A-1 | Financial Position, Financial Performance, Cash Flows, Notes to Financial Statements, Summary of Cost of Sales | Q1: May 15; Q2: August 15; Q3: November 15; Q4: February 15 of the next year |
| A-2 | Bank Reconciliation, Cost of Sales, Receipts and Disbursements, Capitalization | 10th of the following month |
| Other | Trial balance, journals, daily collections/deposits, receipt record, bank/check register, GL/SL, JEV extracts, payment-voucher register, supporting references, materials issued, stock/supplies cards, proof-of-posting register | 10th of the following month |
| Transmittals | Separate A-1 and A-2 portrait checklists | Matching quarterly/monthly deadline |

Quarterly performance/cash-flow reports show the selected quarter, financial position shows its closing date. Monthly reports show the selected month. Calendar deadlines follow the transcription literally; no holiday/weekend extensions are inferred. The module starts at the latest posted sample month, not necessarily the current calendar month.

For non-IU schools **without** a bank account, use the Cash Disbursement Register; the bank reconciliation, bank register and bank-check journal are excluded. For IU schools, use the Check/ADA Disbursement Record instead of the non-IU bank/cash register. Cooperative operation adds school-share utilization, cooperative gross-income utilization and cooperative program-support reports. Laboratory operation adds laboratory gross-income utilization. The default profile has 28 applicable outputs (5 A-1, 4 A-2, 16 supporting records, 2 transmittals and 1 additional monthly Income Statement); 34 distinct types are implemented including conditional types.

## Data and review rules

- Only posted entries feed financial statements. Cash/inventory/cost/investing/financing account mappings are editable; never infer approved government account codes from the sample's generic account labels.
- Financial position includes accumulated unclosed income/expenses in equity so the statement reconciles. Detailed and condensed variants of position, performance and cash flows are available. They are generated separately; the A-1 transmittal lists both.
- Cash flows net mapped cash accounts per document, excluding transfers. Investing/financing mappings classify counterpart accounts; other single-category flows default to operating and must be reviewed. Compound mixed flows remain unclassified unless a source-document override is entered.
- Bank reconciliation requires a month-end statement balance and explicit reconciling items. Book-side items are not posted automatically. An unresolved difference keeps the report a draft. Outstanding items must be carried forward into each monthly snapshot explicitly.
- Cost of sales reconciles opening GL inventory plus mapped inventory debits less independently entered closing stock against posted COGS. Returns, transfers and write-offs need review. Missing stock verification and differences keep it a draft; quarterly summaries also require each month's verification.
- Daily collections exclude capital/journal cash movements. Deposits require separately entered, source-referenced deposit evidence. Daily differences are movements, not opening-adjusted undeposited balances. Receipt-only/payment-only registers show actual running GL balances at each listed event, including intervening cash movements.
- Stock records capture quantity and entered unit cost by item. They do not create inventory or expense journals automatically and do not implement an approved stock-costing policy by themselves. Negative stock warns for review.
- Notes/disclosures, original receipts, signed vouchers, check/ADA references and proof-of-posting evidence cannot be derived from GL alone. Supplementary registers capture references, document series and counts. Registers/extracts do not replace physical originals, signed vouchers or posting photographs.
- School ID and signatory fields must be configured before review. Missing source information appears as Draft in the preview and every export. “For review” is not an approval/compliance certification.
- Exports are real PDF, DOCX and XLSX files generated from one report model. A4 portrait is set in all formats. Excel amounts are numeric and text never becomes a formula. Draft warnings are retained. Embedded DejaVu fonts are included with their license; PDFsharp is pinned to its MIT-licensed 6.2.3 release.

## Filing and monitoring

Submit **ONE COPY**, in **PORTRAIT** orientation, securely fastened in a folder, with the Records Section for routing to the SDO Accounting Unit. The module tracks this school's actual Records receipt, routing to Accounting and transmission to the Auditor independently from generation/export. A receiving/routing reference is required when activity dates are entered. Dates must follow that order. Exporting a report never marks it submitted.

Overall in-charge: Eduarda U. Alon, Accountant III. Focal persons from the supplied memo:

- IU: Eureka Shane Alpha P. Bool, Administrative Assistant III.
- Non-IU District I: Chubasco R. Ramirez, Administrative Assistant III.
- Non-IU District II: Ma. Ellaine Sairel R. Magpantay, Administrative Assistant II.
- Non-IU District III: Abegail Ivy C. Perez, Administrative Assistant II.
- Non-IU District IV: Jhece Mae C. Agelion, Administrative Assistant II.

Transmittals use the configured superintendent (default Marites A. Ibañez, CESO V), school details, computed focal person and submitted period. Original example receipt/check numbers and monetary amounts from the transcription are not inserted as actual school evidence.

## Implementation and validation

PostgreSQL migration 2 adds `canteen_settings`, `canteen_inputs` and `canteen_submissions` without replacing the existing ledger. JSON fallback also preserves these fields. Use a single API process as documented in database/README.md.

```bash
dotnet build
dotnet run --no-build -- --report-export-self-test
DOTNET_EXECUTABLE=dotnet python3 tests/canteen_reports.py
ACCOUNTING_TEST_POSTGRES=1 DOTNET_EXECUTABLE=dotnet python3 tests/smoke.py
```

The export self-test checks all 34 types / 102 format combinations, PDF portrait pages and official Word/Excel XML schemas. The canteen integration suite checks all 28 default previews / 84 exports, quarter/year deadline boundaries, ledger totals, bank differences, stock balance, spreadsheet formula safety, conditional profiles, submission sequence and persistence across API restart in a generated PostgreSQL database. Existing accounting regression tests remain separate. Test databases are removed; the working school ledger is not changed by these suites.

The app remains a local prototype without authentication or enforced access policies. No current BIR ATC verification or automatic tax computation has been added by this report work.
