"""Canteen memo integration checks on a disposable PostgreSQL database."""
import json,os,pathlib,subprocess,sys,tempfile,time,urllib.request,urllib.error,uuid,zipfile,io,xml.etree.ElementTree as ET
ROOT=pathlib.Path(__file__).resolve().parents[1]
DOTNET=os.environ.get('DOTNET_EXECUTABLE','dotnet')

def main():
 name='accounting_test_'+uuid.uuid4().hex
 env=os.environ.copy();env.update(Ledger__Mode="LegacyTest",ASPNETCORE_ENVIRONMENT="Development");binding=json.loads((ROOT/'.local/postgres-connection.json').read_text())['ConnectionStrings__Accounting'];env.update(ConnectionStrings__Accounting=binding.replace('Database=accounting;',f'Database={name};'),ASPNETCORE_URLS='http://127.0.0.1:5082')
 with tempfile.TemporaryDirectory() as tmp:
  env['ACCOUNTING_DATA_PATH']=tmp+'/ledger.json';process=subprocess.Popen([DOTNET,str(ROOT/'bin/Debug/net10.0/Accounting.Api.dll')],env=env,cwd=ROOT,stdout=subprocess.DEVNULL)
  def req(path,method='GET',body=None,raw=False):
   q=urllib.request.Request('http://127.0.0.1:5082/api/'+path,data=None if body is None else json.dumps(body).encode(),method=method,headers={'Content-Type':'application/json'})
   with urllib.request.urlopen(q) as r:
    data=r.read();return data if raw else json.loads(data) if data else None
  def reject(path,body,method='POST'):
   try:req(path,method,body);raise AssertionError('Expected rejection: '+path)
   except urllib.error.HTTPError as e:assert e.code==400,(path,e.code,e.read())
  try:
   for _ in range(100):
    try:req('health');break
    except Exception:time.sleep(.1)
   else:raise RuntimeError('Test API did not start')
   base=req('canteen?month=2026-01');active=[r for r in base['catalog'] if r['applicable']]
   assert len([r for r in active if r['category']=='A-1'])==5
   assert len([r for r in active if r['category']=='A-2'])==4
   assert not next(r for r in base['catalog'] if r['id']=='school-share')['applicable']
   for month,end,due in [('2026-01','2026-03-31','2026-05-15'),('2026-06','2026-06-30','2026-08-15'),('2026-09','2026-09-30','2026-11-15'),('2026-12','2026-12-31','2027-02-15')]:
    cat=req('canteen?month='+month)['catalog'];a=next(r for r in cat if r['id']=='position');assert(a['periodEnd'],a['dueDate'])==(end,due)
   assert next(r for r in req('canteen?month=2026-12')['catalog'] if r['id']=='bank-reconciliation')['dueDate']=='2027-01-10'
   print('PASS profile applicability and five period/deadline boundaries')
   # Preview every applicable report and validate the real container formats.
   count=0
   for listing in active:
    report=req('canteen/reports/'+listing['id']+'?month=2026-01');assert report['sections'];assert report['status']=='Draft'
    for fmt in ['pdf','docx','xlsx']:
     data=req('canteen/reports/'+listing['id']+'/export/'+fmt+'?month=2026-01',raw=True)
     if fmt=='pdf':assert data.startswith(b'%PDF-')
     else:
      with zipfile.ZipFile(io.BytesIO(data)) as z:
       assert z.testzip() is None
       if fmt=='docx':
        doc=ET.fromstring(z.read('word/document.xml'));ns={'w':'http://schemas.openxmlformats.org/wordprocessingml/2006/main'};page=doc.find('.//w:pgSz',ns);assert page.attrib['{'+ns['w']+'}orient']=='portrait'
       else:
        sheet=ET.fromstring(z.read('xl/worksheets/sheet1.xml'));ns={'x':'http://schemas.openxmlformats.org/spreadsheetml/2006/main'};assert sheet.find('x:pageSetup',ns).attrib['orientation']=='portrait'
     count+=1
   print(f'PASS {len(active)} previews and {count} PDF/DOCX/XLSX exports; Office files portrait')
   p=base['profile'];p.update(schoolId='DEMO-001',schoolHead='Demo School Head',preparedBy='Demo Bookkeeper');req('canteen/profile','PUT',p)
   assert req('canteen/reports/position?month=2026-01')['status']=='For review'
   condensed=req('canteen/reports/position?month=2026-01&detail=Condensed');assert len(condensed['sections'][0]['rows'])==1
   balance=req('canteen/reports/position?month=2026-01')['sections'];assert balance[0]['rows'][-1][-1]==109000
   def input(kind,amount=0,**extra):
    d=dict(id='',kind=kind,date='2026-01-31',name='Verified demo record',reference='DEMO',amount=amount,quantity=0,unitCost=0,details='Test source',classification='Operating',seriesFrom='',seriesTo='',count=0);d.update(extra);return req('canteen/inputs','POST',d)
   input('Bank statement balance',103000)
   bank=req('canteen/reports/bank-reconciliation?month=2026-01');assert bank['status']=='For review';assert bank['sections'][0]['rows'][-1][-1]==0
   input('Deposit in transit',100);assert req('canteen/reports/bank-reconciliation?month=2026-01')['status']=='Draft'
   input('Verified closing inventory',0);assert req('canteen/reports/monthly-cost?month=2026-01')['status']=='For review'
   reject('canteen/inputs',dict(id='',kind='Verified closing inventory',date='2026-01-15',name='Wrong cut-off',reference='',amount=0,quantity=0,unitCost=0,details='',classification='',seriesFrom='',seriesTo='',count=0))
   input('Inventory receipt',quantity=10,unitCost=5,name='Rice');input('Inventory issue',quantity=3,unitCost=5,name='Rice');stock=req('canteen/reports/stock-card?month=2026-01');assert stock['sections'][0]['rows'][-1][-1]==7
   input('Financial statement note',details='=HYPERLINK("https://example.com")')
   data=req('canteen/reports/notes/export/xlsx?month=2026-01',raw=True)
   with zipfile.ZipFile(io.BytesIO(data)) as z:assert b'<f>' not in z.read('xl/worksheets/sheet1.xml')
   before=len(req('canteen')['submissions']);req('canteen/reports/position/export/pdf?month=2026-01',raw=True);assert len(req('canteen')['submissions'])==before
   bad=dict(id='',category='A-2',periodEnd='2026-01-31',recordsReceivedOn=None,accountingRoutedOn='2026-02-10',auditorTransmittedOn=None,reference='TRACK-1',remarks='');reject('canteen/submissions',bad,'PUT');bad.update(recordsReceivedOn='2026-02-09');req('canteen/submissions','PUT',bad)
   p.update(unitType='IU',operation='Cooperative');req('canteen/profile','PUT',p);catalog=req('canteen')['catalog'];assert next(r for r in catalog if r['id']=='check-ada')['applicable'];assert next(r for r in catalog if r['id']=='school-share')['applicable'];assert not next(r for r in catalog if r['id']=='bank-register')['applicable'];assert req('canteen')['profile']['focalPerson']=='Eureka Shane Alpha P. Bool'
   p.update(unitType='Non-IU',hasBankAccount=False,operation='Laboratory');req('canteen/profile','PUT',p);catalog=req('canteen')['catalog'];assert next(r for r in catalog if r['id']=='cash-register')['applicable'];assert not next(r for r in catalog if r['id']=='bank-reconciliation')['applicable'];assert next(r for r in catalog if r['id']=='laboratory-income')['applicable']
   process.terminate();process.wait(timeout=10);process=subprocess.Popen([DOTNET,str(ROOT/'bin/Debug/net10.0/Accounting.Api.dll')],env=env,cwd=ROOT,stdout=subprocess.DEVNULL)
   for _ in range(100):
    try:loaded=req('canteen');break
    except Exception:time.sleep(.1)
   assert loaded['profile']['schoolId']=='DEMO-001' and len(loaded['inputs'])>=6 and len(loaded['submissions'])==1
   print('PASS ledger arithmetic, condensed layouts, bank reconciliation, stock counts, formula safety, submission sequencing, optional profiles and PostgreSQL restart persistence')
  finally:
   process.terminate();process.wait(timeout=10)
   subprocess.run(['docker','--host=unix:///var/run/docker.sock','exec','ledger-ph-postgres','psql','-U','ledger','-d','postgres','-v','ON_ERROR_STOP=1','-c',f'DROP DATABASE {name} WITH (FORCE)'],check=True,stdout=subprocess.DEVNULL)
if __name__=='__main__':main()
