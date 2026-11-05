SELECT jsonb_build_object(
'accounts',COALESCE((SELECT jsonb_agg(to_jsonb(a) ORDER BY level,code) FROM accounts a),'[]'::jsonb),
'cards',COALESCE((SELECT jsonb_agg(to_jsonb(c) ORDER BY name) FROM cards c),'[]'::jsonb),
'items',COALESCE((SELECT jsonb_agg(to_jsonb(i) ORDER BY name) FROM items i),'[]'::jsonb),
'signatories',COALESCE((SELECT jsonb_agg(to_jsonb(s) ORDER BY role) FROM signatory_assignments s),'[]'::jsonb),
'access',COALESCE((SELECT jsonb_agg(to_jsonb(m) ORDER BY module) FROM module_access m),'[]'::jsonb),
'atcCodes',COALESCE((SELECT jsonb_agg(to_jsonb(a) ORDER BY code) FROM atc_codes a),'[]'::jsonb),
'documents',COALESCE((SELECT jsonb_agg(to_jsonb(d)||jsonb_build_object(
'lines',COALESCE((SELECT jsonb_agg(jsonb_build_object('account',l.account,'debit',l.debit,'credit',l.credit,'cardId',l."cardId") ORDER BY l.line_number) FROM journal_lines l WHERE l.document_id=d.id),'[]'::jsonb),
'approvalNames',COALESCE((SELECT jsonb_agg(jsonb_build_object('role',s.role,'name',s.name) ORDER BY role) FROM document_signatories s WHERE s.document_id=d.id),'[]'::jsonb)) ORDER BY d.number) FROM documents d),'[]'::jsonb)
)::text;
