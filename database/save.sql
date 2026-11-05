-- Only execute through PostgresLedgerStore inside its revision-guarded transaction.
DELETE FROM document_signatories; DELETE FROM journal_lines; DELETE FROM documents;
DELETE FROM items; DELETE FROM accounts; DELETE FROM cards; DELETE FROM signatory_assignments; DELETE FROM module_access; DELETE FROM atc_codes;
INSERT INTO accounts SELECT * FROM jsonb_populate_recordset(NULL::accounts,CAST(@data AS jsonb)->'accounts');
INSERT INTO cards SELECT * FROM jsonb_populate_recordset(NULL::cards,CAST(@data AS jsonb)->'cards');
INSERT INTO items SELECT * FROM jsonb_populate_recordset(NULL::items,CAST(@data AS jsonb)->'items');
INSERT INTO signatory_assignments SELECT * FROM jsonb_populate_recordset(NULL::signatory_assignments,CAST(@data AS jsonb)->'signatories');
INSERT INTO module_access SELECT * FROM jsonb_populate_recordset(NULL::module_access,CAST(@data AS jsonb)->'access');
INSERT INTO atc_codes SELECT * FROM jsonb_populate_recordset(NULL::atc_codes,CAST(@data AS jsonb)->'atcCodes');
INSERT INTO documents SELECT * FROM jsonb_populate_recordset(NULL::documents,CAST(@data AS jsonb)->'documents');
INSERT INTO journal_lines SELECT d->>'id',n::integer,l->>'account',(l->>'debit')::numeric,(l->>'credit')::numeric,l->>'cardId' FROM jsonb_array_elements(CAST(@data AS jsonb)->'documents') d CROSS JOIN LATERAL jsonb_array_elements(d->'lines') WITH ORDINALITY AS lines(l,n);
INSERT INTO document_signatories SELECT d->>'id',s->>'role',s->>'name' FROM jsonb_array_elements(CAST(@data AS jsonb)->'documents') d CROSS JOIN LATERAL jsonb_array_elements(d->'approvalNames') s;
DO $$ BEGIN
IF EXISTS(SELECT FROM accounts a JOIN accounts p ON a.parent=p.code WHERE a.level<>p.level+1 OR a.category<>p.category) THEN RAISE EXCEPTION 'Invalid account hierarchy' USING ERRCODE='23514'; END IF;
IF EXISTS(SELECT FROM journal_lines l JOIN accounts a ON l.account=a.code WHERE a.level<>8) THEN RAISE EXCEPTION 'Posting account required' USING ERRCODE='23514'; END IF;
IF EXISTS(SELECT d.id FROM documents d LEFT JOIN journal_lines l ON d.id=l.document_id WHERE d.status='Posted' GROUP BY d.id,d.amount HAVING count(l.line_number)<2 OR COALESCE(sum(l.debit),0)<>d.amount OR COALESCE(sum(l.credit),0)<>d.amount) THEN RAISE EXCEPTION 'Unbalanced posted document' USING ERRCODE='23514'; END IF;
IF EXISTS(SELECT FROM journal_lines l JOIN documents d ON d.id=l.document_id WHERE d.status='Posted' AND l.debit=0 AND l.credit=0) THEN RAISE EXCEPTION 'Posted lines must be nonzero' USING ERRCODE='23514'; END IF;
END $$;
