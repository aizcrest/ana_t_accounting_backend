"""Bank and Income Statement checks on a disposable PostgreSQL database."""
import json,os,pathlib,subprocess,tempfile,time,urllib.request,urllib.error,uuid
ROOT=pathlib.Path(__file__).resolve().parents[1];DOTNET=os.environ.get('DOTNET_EXECUTABLE','dotnet')
def main():
 database='accounting_test_'+uuid.uuid4().hex;env=os.environ.copy();env.update(Ledger__Mode="LegacyTest",ASPNETCORE_ENVIRONMENT="Development");binding=json.loads((ROOT/'.local/postgres-connection.json').read_text())['ConnectionStrings__Accounting'];env.update(ConnectionStrings__Accounting=binding.replace('Database=accounting;',f'Database={database};'),ASPNETCORE_URLS='http://127.0.0.1:5083')
 with tempfile.TemporaryDirectory() as tmp:
  env['ACCOUNTING_DATA_PATH']=tmp+'/ledger.json'
  def start():return subprocess.Popen([DOTNET,str(ROOT/'bin/Debug/net10.0/Accounting.Api.dll')],env=env,cwd=ROOT,stdout=subprocess.DEVNULL)
  process=start()
  def req(path,method='GET',body=None,raw=False):
   q=urllib.request.Request('http://127.0.0.1:5083/api/'+path,data=None if body is None else json.dumps(body).encode(),method=method,headers={'Content-Type':'application/json'})
   with urllib.request.urlopen(q) as r:
    d=r.read();return d if raw else json.loads(d) if d else None
  def ready():
   for _ in range(100):
    try:req('health');return
    except Exception:time.sleep(.1)
   raise RuntimeError('API did not start')
  def reject(path,body,method='POST',status=400):
   try:req(path,method,body);raise AssertionError('Expected rejection '+path)
   except urllib.error.HTTPError as e:assert e.code==status,(path,e.code)
  try:
   ready();assert not req('banks')['banks'];state=req('state');cash=next(a for a in state['accounts'] if a['name']=='Cash in bank');expense=next(a for a in state['accounts'] if a['category']=='Expense' and a['level']==8)
   profile=req('canteen')['profile'];profile.update(schoolId='DEMO-BANK',schoolHead='Demo Head',preparedBy='Demo Preparer');req('canteen/profile','PUT',profile)
   def input(bank=None,amount=103000,kind='Bank statement balance',date='2026-01-31'):
    return dict(id='',kind=kind,date=date,name='Source balance',reference='DEMO-STATEMENT',amount=amount,quantity=0,unitCost=0,details='',classification='',seriesFrom='',seriesTo='',count=0,bankId=bank)
   legacy=req('canteen/inputs','POST',input())
   bank=dict(id='',name='Demo bank',branch='Main',accountName='Demo school',accountNumber='•••• 1234',glAccount=cash['code'],active=True)
   first=req('banks','POST',bank);reject('banks',bank);reject('banks',{**bank,'glAccount':expense['code']})
   recon=req('banks/'+first['id']+'/reconciliation?month=2026-01');assert len(recon['inputs'])==1 and recon['report']['sections'][0]['rows'][-1][-1]==0
   reject('canteen/inputs',input(first['id']))
   new_cash=cash['code'].rsplit('.',1)[0]+'.099';req('accounts','POST',dict(code=new_cash,name='Cash in bank - savings',category='Asset',level=8,parent=cash['parent']))
   second=req('banks','POST',{**bank,'name':'Second bank','glAccount':new_cash})
   req('canteen/inputs','POST',input(second['id'],0));second_recon=req('banks/'+second['id']+'/reconciliation?month=2026-01');assert len(second_recon['inputs'])==1;assert second_recon['report']['sections'][0]['rows'][4][-1]==0
   req('canteen/inputs','POST',input(second['id'],25,'Outstanding check'));assert req('banks/'+first['id']+'/reconciliation?month=2026-01')['report']['sections'][0]['rows'][-1][-1]==0
   assert req('banks/'+second['id']+'/reconciliation?month=2026-01')['report']['sections'][0]['rows'][-1][-1]==-25
   assert req('canteen/reports/bank-reconciliation?month=2026-01')['sections'][0]['rows'][4][-1]==103000
   reject('canteen/inputs',input('missing-bank'),status=404);reject('canteen/inputs',input(first['id'],0,'Financial statement note'))
   ranges=dict(id='',bankId=first['id'],prefix='CHK-',startNumber=1,endNumber=100,numberWidth=6,description='Book A',active=True)
   created=req('banks/check-ranges','POST',ranges);assert created['numberWidth']==6
   reject('banks/check-ranges',{**ranges,'startNumber':50,'endNumber':200})
   req('banks/check-ranges/'+created['id']+'/status','PUT',{'active':False});reject('banks/check-ranges',ranges)
   reject('banks/check-ranges',{**ranges,'startNumber':101,'endNumber':10});reject('banks/check-ranges',{**ranges,'startNumber':101,'endNumber':200,'numberWidth':1})
   other_range=req('banks/check-ranges','POST',{**ranges,'bankId':second['id']});reject('banks/check-ranges/'+created['id'],{**ranges,'bankId':second['id']},'PUT')
   reject('banks/'+first['id'],{**first,'glAccount':new_cash},'PUT')
   req('banks/'+second['id'],'PUT',{**second,'active':False});reject('banks/check-ranges',{**ranges,'bankId':second['id'],'startNumber':201,'endNumber':300})
   income=req('canteen/reports/income-statement?month=2026-01');assert income['periodEnd']=='2026-01-31' and income['category']=='Management';assert income['sections'][0]['rows'][-1][-1]==10000;assert income['sections'][2]['rows'][0][-1]==10000;assert income['sections'][-1]['rows'][0][-1]==7500
   # COGS posting reduces gross profit and net income; draft revenue must not affect either.
   cost=next(a['code'] for a in state['accounts'] if a['name']=='Cost of goods sold');inventory=next(a['code'] for a in state['accounts'] if a['name']=='Inventory')
   doc=dict(id='',number='',type='Adjustment/Journal',date='2026-01-31',cardId=None,sourceId=None,memo='COGS test',amount=1000,status='Draft',lines=[dict(account=cost,debit=1000,credit=0,cardId=None),dict(account=inventory,debit=0,credit=1000,cardId=None)],approvalNames=[])
   d=req('documents','POST',doc);assert req('canteen/reports/income-statement?month=2026-01')['sections'][-1]['rows'][0][-1]==7500
   req('documents/'+d['id']+'/post','POST',{});income=req('canteen/reports/income-statement?month=2026-01');assert income['sections'][2]['rows'][0][-1]==9000 and income['sections'][-1]['rows'][0][-1]==6500
   for fmt in ['pdf','docx','xlsx']:
    for path in ['income-statement','bank-reconciliation']:
     data=req(f'canteen/reports/{path}/export/{fmt}?month=2026-01'+('&bankId='+first['id'] if path=='bank-reconciliation' else ''),raw=True);assert data.startswith(b'%PDF-') if fmt=='pdf' else data.startswith(b'PK')
   assert len(req('canteen/reports/income-statement?month=2026-01&detail=Condensed')['sections'][0]['rows'])==1
   process.terminate();process.wait(timeout=10);process=start();ready();loaded=req('banks');assert len(loaded['banks'])==2 and len(loaded['checkRanges'])==2;assert req('banks/'+first['id']+'/reconciliation?month=2026-01')['report']['sections'][0]['rows'][-1][-1]==0
   print('PASS bank CRUD/GL validation, legacy compatibility, two-bank reconciliation isolation, check-range overlaps/leading zeros/status, income/COGS arithmetic, exports and PostgreSQL restart persistence')
  finally:
   process.terminate();process.wait(timeout=10);subprocess.run(['docker','--host=unix:///var/run/docker.sock','exec','ledger-ph-postgres','psql','-U','ledger','-d','postgres','-v','ON_ERROR_STOP=1','-c',f'DROP DATABASE {database} WITH (FORCE)'],check=True,stdout=subprocess.DEVNULL)
if __name__=='__main__':main()
