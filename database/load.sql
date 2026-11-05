SELECT jsonb_build_object(
'banks',COALESCE((SELECT jsonb_agg(to_jsonb(b) ORDER BY name,id) FROM banks b),'[]'::jsonb),
'checkRanges',COALESCE((SELECT jsonb_agg(to_jsonb(r) ORDER BY "bankId","startNumber") FROM check_ranges r),'[]'::jsonb),
'canteenProfile',(SELECT data FROM canteen_settings WHERE id=1),
'canteenInputs',COALESCE((SELECT jsonb_agg(to_jsonb(i) ORDER BY date,id) FROM canteen_inputs i),'[]'::jsonb),
'canteenSubmissions',COALESCE((SELECT jsonb_agg(to_jsonb(s) ORDER BY "periodEnd",id) FROM canteen_submissions s),'[]'::jsonb),
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
