# Bank List and Income Statement

Bank List has three tabs:

1. **Bank Information**: create/edit bank name, branch, account name, masked account label, status and GL link. The combobox lists level-eight asset accounts. Each bank requires a different GL account so balances do not mix. Add another cash-in-bank posting account in Chart of Accounts when needed. A bank's GL link cannot change after reconciliation records or check ranges exist; create a new bank account instead. Inactive banks retain their history.
2. **Bank Reconciliation**: select a bank and reporting month, enter the statement balance and reconciling items, and compare adjusted bank/book balances. Entries belong to the selected bank and the posted book balance comes from its GL account. Month-end statement cut-offs and duplicates are validated. The preview exports PDF, DOCX and XLSX. Unresolved differences and missing profile/source data stay marked Draft. Reconciling items do not post adjusting journals automatically.
3. **Check Setup**: configure bank-specific prefixes, inclusive start/end numbers, digit width, description and active status. Leading zeros are preserved in display. Bounds are positive, ascending and at most 12 digits. Overlapping ranges for the same bank/prefix are rejected even when inactive. This tab configures checkbooks only; it does not issue checks, consume/reserve numbers, clear payments or change the ledger.

## Existing reconciliation data

Migration 3 adds `banks`, `check_ranges` and an optional `bankId` on supplementary inputs. Existing untagged reconciliation records remain associated with the school's original default bank GL account. A registered bank mapped to that GL can display those legacy records; other banks cannot. Duplicate month-end statement validation includes that legacy scope. The existing canteen bank-reconciliation report resolves the registered bank linked to its default bank GL. An explicit bankId selects another bank for the same report/export API. Historical data is not deleted during migration.

## Income Statement

Income Statement is available directly in the Reports navigation and in the canteen library's **Income statement** category. It is a monthly management report, separate from the memo's quarterly Statement of Financial Performance, and adds no new statutory submission deadline or transmittal requirement.

Calculations use posted documents only:

- Revenue = credits minus debits in revenue accounts.
- Cost of sales = debits minus credits in the school's mapped COGS accounts.
- Gross profit = revenue minus cost of sales.
- Operating/other expenses = debit balances of expense movements excluding mapped COGS.
- Net income/(loss) = gross profit minus other expenses.

Detailed and condensed layouts export to portrait PDF, DOCX and XLSX. Select the reporting month; use School settings to review the COGS mapping. The January sample has revenue PHP 10,000, COGS PHP 0, operating expenses PHP 2,500 and net income PHP 7,500. Draft transactions do not affect it.

## API and validation

`GET/POST /api/banks`, `PUT /api/banks/{id}`, `GET /api/banks/{id}/reconciliation?month=YYYY-MM`, `POST /api/banks/check-ranges`, `PUT /api/banks/check-ranges/{id}`, `PUT /api/banks/check-ranges/{id}/status`.

Reconciliation entries use `/api/canteen/inputs` with an optional bankId (only for bank-reconciliation input types). Preview/export uses `/api/canteen/reports/bank-reconciliation` with `month` and optional `bankId`. Income Statement uses `/api/canteen/reports/income-statement` and its `/export/{pdf|docx|xlsx}` routes.

Run `DOTNET_EXECUTABLE=dotnet python3 tests/banking.py` for isolated PostgreSQL tests covering bank/GL validation, multiple-bank isolation, legacy records, check ranges, income/COGS arithmetic, formats and restart persistence. The report self-test now validates 34 report types and 102 export combinations; canteen and accounting suites remain separate. Browser checks use a generated disposable database so demonstration bank details never enter the working school ledger.
