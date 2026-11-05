"""Integration checks against an isolated temporary ledger. Build before running."""
import json, os, pathlib, subprocess, tempfile, time, unittest, urllib.request, urllib.error, uuid
ROOT=pathlib.Path(__file__).resolve().parents[1]
class AccountingTests(unittest.TestCase):
 @classmethod
 def setUpClass(cls):
  cls.temp=tempfile.TemporaryDirectory(); env=os.environ.copy();env.update(Ledger__Mode="LegacyTest",ASPNETCORE_ENVIRONMENT="Development");env.update(ACCOUNTING_DATA_PATH=cls.temp.name+'/ledger.json',ASPNETCORE_URLS='http://127.0.0.1:5081')
  env.pop('ConnectionStrings__Accounting',None)
  cls.pg_database=None
  if os.environ.get('ACCOUNTING_TEST_POSTGRES')=='1':
   cls.pg_database='accounting_test_'+uuid.uuid4().hex
   binding=json.loads((ROOT/'.local/postgres-connection.json').read_text())['ConnectionStrings__Accounting']
   env['ConnectionStrings__Accounting']=binding.replace('Database=accounting;',f'Database={cls.pg_database};')
  cls.process=subprocess.Popen([os.environ.get('DOTNET_EXECUTABLE','dotnet'),str(ROOT/'bin/Debug/net10.0/Accounting.Api.dll')],env=env,cwd=ROOT,stdout=subprocess.DEVNULL)
  for _ in range(100):
   try:
    health=cls.req('health')
    if cls.pg_database and health['storage']!='PostgreSQL':raise RuntimeError('Expected PostgreSQL')
    break
   except Exception:time.sleep(.1)
  else:raise RuntimeError('API failed to start')
 @classmethod
 def tearDownClass(cls):
  cls.process.terminate();cls.process.wait(timeout=10);cls.temp.cleanup()
  if cls.pg_database:
   subprocess.run(['docker','--host=unix:///var/run/docker.sock','exec','ledger-ph-postgres','psql','-U','ledger','-d','postgres','-v','ON_ERROR_STOP=1','-c',f'DROP DATABASE {cls.pg_database} WITH (FORCE)'],check=True,stdout=subprocess.DEVNULL)
 @staticmethod
 def req(path,method='GET',body=None):
  req=urllib.request.Request('http://127.0.0.1:5081/api/'+path,data=None if body is None else json.dumps(body).encode(),method=method,headers={'Content-Type':'application/json'})
  with urllib.request.urlopen(req) as r:
   content=r.read();return json.loads(content) if content else None
 def reject(self,path,body,method='POST'):
  with self.assertRaises(urllib.error.HTTPError) as cm:self.req(path,method,body)
  self.assertEqual(cm.exception.code,400)
 def doc(self,**changes):
  d=dict(id='',number='',type='Adjustment/Journal',date='2027-02-01',cardId=None,sourceId=None,memo='Integration test',amount=10,status='Draft',lines=[],approvalNames=[]);d.update(changes);return d
 def test_seed_has_eight_levels_and_valid_parents(self):
  accounts=self.req('state')['accounts'];self.assertEqual(set(a['level'] for a in accounts),set(range(1,9)))
  for a in accounts:
   if a['level']>1:self.assertTrue(any(p['code']==a['parent'] and p['level']==a['level']-1 for p in accounts))
 def test_trial_balance(self):
  r=self.req('reports/trial-balance?to=2026-01-31');self.assertEqual(r['totals']['debit'],117500);self.assertEqual(r['totals']['credit'],117500)
 def test_balance_sheet_includes_current_earnings(self):
  t=self.req('reports/balance-sheet')['totals'];self.assertEqual(t['assets'],109000);self.assertEqual(t['liabilities'],1500);self.assertEqual(t['earnings'],7500);self.assertEqual(t['difference'],0)
 def test_opening_balances_and_dates(self):
  r=self.req('reports/general-ledger?from=2026-02-01&to=2026-12-31');self.assertEqual(r['entries'],[]);self.assertTrue(any(a['opening']==103000 for a in r['balances']))
  self.reject('reports/trial-balance?from=2026-12-01&to=2026-01-01',None,'GET')
 def test_card_subledger(self):
  r=self.req('reports/subsidiary-ledger?card=customer-1');self.assertTrue(all(e['cardId']=='customer-1' for e in r['entries']));self.assertEqual(sum(a['opening'] for a in self.req('reports/subsidiary-ledger?card=customer-1&from=2026-02-01')['balances']),0)
 def test_link_required_and_overpayment_rejected(self):
  self.reject('documents',self.doc(type='Collection',cardId='customer-1'))
  self.reject('documents',self.doc(type='Collection',cardId='customer-1',sourceId='0002',amount=6001))
  self.reject('documents',self.doc(type='Collection',cardId='supplier-1',sourceId='0002'))
  self.reject('documents',self.doc(type='Purchase Order',cardId='supplier-1',sourceId='0004',amount=1))
 def test_drafts_do_not_affect_reports(self):
  before=self.req('reports/trial-balance')['totals'];self.req('documents','POST',self.doc());self.assertEqual(before,self.req('reports/trial-balance')['totals'])
 def test_invalid_posting_rejected(self):
  d=self.req('documents','POST',self.doc());self.reject('documents/'+d['id']+'/post',{})
  self.reject('documents/0001/post',{});self.reject('documents/0004/post',{})
 def test_balanced_posting(self):
  accounts=self.req('state')['accounts'];cash=next(a['code'] for a in accounts if a['name']=='Cash in bank');capital=next(a['code'] for a in accounts if a['name']=='Owner capital')
  d=self.req('documents','POST',self.doc(lines=[dict(account=cash,debit=10,credit=0,cardId=None),dict(account=capital,debit=0,credit=10,cardId=None)]));self.req('documents/'+d['id']+'/post','POST',{});self.assertEqual(self.req('reports/balance-sheet')['totals']['difference'],0)
 def test_precision(self):self.reject('documents',self.doc(amount=10.001))
if __name__=='__main__':unittest.main(verbosity=2)
