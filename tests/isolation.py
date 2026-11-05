"""Real cookie/MFA/CSRF authentication against two separately permissioned school databases.
Uses a generated control database and restricted runtime roles; never touches school books.
"""
import concurrent.futures,copy,base64,hashlib,hmac,http.cookiejar,json,os,pathlib,secrets,struct,subprocess,tempfile,time,urllib.error,urllib.parse,urllib.request,uuid
ROOT=pathlib.Path(__file__).resolve().parents[1];DOTNET=os.environ.get('DOTNET_EXECUTABLE','dotnet')
class Client:
 def __init__(self,base):self.base=base;self.jar=http.cookiejar.CookieJar();self.opener=urllib.request.build_opener(urllib.request.HTTPCookieProcessor(self.jar))
 def call(self,path,method='GET',body=None,headers=None,raw=False,csrf=True):
  hs={'Content-Type':'application/json',**(headers or {})}
  if method not in ['GET','HEAD','OPTIONS'] and csrf:hs['X-CSRF-TOKEN']=self.call('auth/csrf')['token']
  request=urllib.request.Request(self.base+'/api/'+path,method=method,headers=hs,data=None if body is None else json.dumps(body).encode())
  with self.opener.open(request) as r:
   data=r.read();return data if raw else json.loads(data) if data else None
 def denied(self,path,status=403,method='GET',body=None,**kw):
  try:self.call(path,method,body,**kw);raise AssertionError('Access unexpectedly allowed: '+path)
  except urllib.error.HTTPError as e:
   content=e.read();assert e.code==status,(path,e.code,content[:300])
 def enroll(self,email,password):
  self.call('auth/login','POST',dict(email=email,password=password));self.denied('state');setup=self.call('auth/mfa/setup','POST',{});code=totp(setup['key']);verified=self.call('auth/mfa/verify','POST',{'code':code});assert verified['verified'];assert len(verified['recoveryCodes'])==10;return setup['key'],verified['recoveryCodes']
def totp(key):
 secret=base64.b32decode(key+'='*((8-len(key)%8)%8));digest=hmac.new(secret,struct.pack('>Q',int(time.time())//30),hashlib.sha1).digest();offset=digest[-1]&15;return str((struct.unpack('>I',digest[offset:offset+4])[0]&0x7fffffff)%1000000).zfill(6)
def main():
 control='ledger_auth_test_'+uuid.uuid4().hex;admin_email='owner@example.invalid';password='Aa1!'+secrets.token_urlsafe(24);base='http://127.0.0.1:5084';process=None;schools=[]
 owner=json.loads((ROOT/'.local/postgres-connection.json').read_text())['ConnectionStrings__Accounting']
 with tempfile.TemporaryDirectory() as temp:
  env=os.environ.copy();env.update(ConnectionStrings__Provisioning=owner,Ledger__Mode='Development',ASPNETCORE_ENVIRONMENT='Development',Ledger__ControlDatabaseName=control,Ledger__KeyPath=temp+'/keys',Ledger__RuntimeConfigPath=temp+'/runtime.json',Ledger__BootstrapEmail=admin_email,Ledger__BootstrapPassword=password,Logging__LogLevel__Default='Warning',DOTNET_CLI_HOME=os.environ.get('DOTNET_CLI_HOME','/workspace/.dotnet-home'))
  def cli(*args,**extra):
   result=subprocess.run([DOTNET,str(ROOT/'bin/Debug/net10.0/Accounting.Api.dll'),*args],env={**env,**extra},cwd=ROOT,capture_output=True,text=True)
   if result.returncode:raise RuntimeError('Provisioning command failed: '+result.stdout[-1800:]+result.stderr[-1800:])
  def sql(db,statement):return subprocess.check_output(['docker','--host=unix:///var/run/docker.sock','exec','ledger-ph-postgres','psql','-U','ledger','-d',db,'-At','-v','ON_ERROR_STOP=1','-c',statement],text=True).strip()
  def start():
   runtime={k:v for k,v in env.items() if not k.startswith('ConnectionStrings__') and not k.startswith('Ledger__Bootstrap')};runtime.update(json.load(open(temp+'/runtime.json')));runtime['ASPNETCORE_URLS']=base
   return subprocess.Popen([DOTNET,str(ROOT/'bin/Debug/net10.0/Accounting.Api.dll')],env=runtime,cwd=ROOT,stdout=log,stderr=log)
  with open(temp+'/api.log','w+') as log:
   try:
    cli('--control-init');process=start()
    for _ in range(150):
     try:urllib.request.urlopen(base+'/api/health');break
     except Exception:time.sleep(.1)
    else:log.seek(0);raise RuntimeError(log.read()[-4000:])
    admin=Client(base);admin.denied('state',401);admin.denied('banks',401);admin.denied('canteen/reports/position/export/pdf',401)
    admin.denied('auth/login',401,'POST',dict(email=None,password=None));admin.call('auth/csrf');admin.denied('auth/login',400,'POST',dict(email=admin_email,password=password),csrf=False)
    admin_key,_=admin.enroll(admin_email,password);admin.denied('state');assert admin.call('auth/me')['platformAdmin']
    for suffix in ['A','B']:
     school=admin.call('admin/schools','POST',{'name':'School '+suffix,'code':'TEST-'+suffix});schools.append(school)
    admin.denied('admin/schools',409,'POST',{'name':'Duplicate school','code':'TEST-A'});cli('--provision-pending');overview=admin.call('admin/overview');assert all(s['provisioningStatus']=='Ready' for s in overview['schools'])
    school_clients=[]
    for index,school in enumerate(schools):
     email=f'operator{index}@example.invalid';invite=admin.call('admin/users','POST',dict(email=email,schoolId=school['id'],role='Operator'));client=Client(base);client.call('auth/accept-invitation','POST',dict(userId=invite['userId'],token=invite['token'],password=password));client.enroll(email,password);school_clients.append((client,invite['userId']))
    a,aid=school_clients[0];b,bid=school_clients[1];tampered=Client(base)
    for cookie in a.jar:
     if cookie.name.startswith('Ledger.Session'):
      altered=copy.copy(cookie);altered.value=('A' if cookie.value[0]!='A' else 'B')+cookie.value[1:];tampered.jar.set_cookie(altered)
    tampered.denied('state',401)
    assert a.call('auth/me')['school']['id']==schools[0]['id'];assert a.call('state')['documents']==[] and b.call('state')['documents']==[]
    a.denied('state',403,headers={'X-School-Id':schools[1]['id']});a.denied('state?schoolId='+schools[1]['id']);a.denied('admin/overview');a.denied('access',403,'PUT',[])
    cards=[];banks=[]
    for index,(client,_) in enumerate(school_clients):
     state=client.call('state');cash=next(x for x in state['accounts'] if x['name']=='Cash in bank')['code'];capital=next(x for x in state['accounts'] if x['name']=='Owner capital')['code'];amount=1000*(index+1)
     card=client.call('cards','POST',dict(id='',name='Private card '+str(index),type='Supplier',tin='',address=''));cards.append(card)
     bank=client.call('banks','POST',dict(id='',name='Private bank '+str(index),branch='',accountName='School '+str(index),accountNumber='masked',glAccount=cash,active=True));banks.append(bank)
     doc=client.call('documents','POST',dict(id='',number='',type='Adjustment/Journal',date='2026-01-31',cardId=None,sourceId=None,memo='Private opening '+str(index),amount=amount,status='Draft',lines=[dict(account=cash,debit=amount,credit=0,cardId=None),dict(account=capital,debit=0,credit=amount,cardId=None)],approvalNames=[]));client.call('documents/'+doc['id']+'/post','POST',{})
    # Deliberately reuse a record ID across databases; identity remains school-local.
    db_b='ledger_school_'+schools[1]['id'].replace('-','')
    sql(db_b,"UPDATE cards SET id='"+cards[0]['id']+"' WHERE id='"+cards[1]['id']+"'")
    assert b.call('state')['cards'][0]['id']==cards[0]['id'] and b.call('state')['cards'][0]['name']=='Private card 1'
    a.call('cards','POST',dict(id='',name='School A only',type='Other',tin='',address='',schoolId=schools[1]['id']))
    assert len(b.call('state')['cards'])==1
    assert a.call('reports/balance-sheet')['totals']['assets']==1000;assert b.call('reports/balance-sheet')['totals']['assets']==2000
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
     results=list(pool.map(lambda item:item[0].call('reports/balance-sheet')['totals']['assets']==item[1],[(a,1000),(b,2000)]*8));assert all(results),'Concurrent requests mixed school contexts'
    assert set(x['name'] for x in a.call('state')['cards'])=={'Private card 0','School A only'};a.denied('banks/'+banks[1]['id']+'/reconciliation?month=2026-01',404)
    for fmt in ['pdf','docx','xlsx']:a.denied('canteen/reports/bank-reconciliation/export/'+fmt+'?month=2026-01&bankId='+banks[1]['id'],404)
    profile=a.call('canteen')['profile'];profile['name']='School B';a.denied('canteen/profile',400,'PUT',profile);assert a.call('canteen')['profile']['name']=='School A'
    view_invite=admin.call('admin/users','POST',dict(email='viewer@example.invalid',schoolId=schools[0]['id'],role='Viewer'));viewer=Client(base);viewer.call('auth/accept-invitation','POST',dict(userId=view_invite['userId'],token=view_invite['token'],password=password));viewer.enroll('viewer@example.invalid',password);assert any(x['name']=='Private card 0' for x in viewer.call('state')['cards']);viewer.denied('cards',403,'POST',cards[0]);viewer.call('canteen/reports/income-statement/export/pdf?month=2026-01',raw=True)
    grant=admin.call('admin/support-grants','POST',dict(schoolId=schools[0]['id'],reason='Isolation test',minutes=5));admin.call('auth/support-context','POST',{'grantId':grant['id']});assert any(x['name']=='Private card 0' for x in admin.call('state')['cards']);admin.denied('cards',403,'POST',cards[0]);admin.call('admin/support-grants/'+grant['id'],'DELETE');admin.denied('state');admin.call('auth/support-context','POST',{'grantId':None})
    db_a='ledger_school_'+schools[0]['id'].replace('-','');db_b='ledger_school_'+schools[1]['id'].replace('-','');role_a='ledger_app_'+schools[0]['id'].replace('-','')
    assert sql('postgres',f"SELECT has_database_privilege('{role_a}','{db_b}','CONNECT')")=='f'
    assert sql('postgres',f"SELECT has_database_privilege('{role_a}','{control}','CONNECT')")=='f'
    assert sql(db_a,f"SELECT has_table_privilege('{role_a}','tenant_identity','UPDATE'),has_table_privilege('{role_a}','audit_events','DELETE')")=='f|f'
    assert int(sql(db_a,'SELECT count(*) FROM audit_events'))>0
    sql(db_a,"UPDATE tenant_identity SET school_id='"+schools[1]['id']+"'");a.denied('state',403);sql(db_a,"UPDATE tenant_identity SET school_id='"+schools[0]['id']+"'")
    admin.call('admin/users/'+aid+'/status','PUT',{'enabled':False});a.denied('state',401);assert b.call('reports/balance-sheet')['totals']['assets']==2000
    admin.call('admin/schools/'+schools[1]['id']+'/status','PUT',{'enabled':False});b.denied('state',403);admin.call('admin/schools/'+schools[1]['id']+'/status','PUT',{'enabled':True})
    process.terminate();process.wait(timeout=10);process=start()
    for _ in range(150):
     try:urllib.request.urlopen(base+'/api/health');break
     except Exception:time.sleep(.1)
    assert b.call('reports/balance-sheet')['totals']['assets']==2000
    admin.call('admin/schools/'+schools[1]['id']+'/status','PUT',{'enabled':False})
    backup=temp+'/school-b.ledgerbak';cli('--backup-school',Ledger__TargetSchoolId=schools[1]['id'],Ledger__BackupPath=backup)
    assert b'Private card' not in pathlib.Path(backup).read_bytes()
    bad=subprocess.run([DOTNET,str(ROOT/'bin/Debug/net10.0/Accounting.Api.dll'),'--verify-restore'],env={**env,'Ledger__TargetSchoolId':schools[0]['id'],'Ledger__BackupPath':backup},cwd=ROOT,capture_output=True,text=True);assert bad.returncode!=0
    cli('--verify-restore',Ledger__TargetSchoolId=schools[1]['id'],Ledger__BackupPath=backup)
    # Only this test generated quarantine databases referencing its school marker.
    restores=sql('postgres',"SELECT datname FROM pg_database WHERE datname LIKE 'ledger_restore_%'").splitlines()
    for restored in restores:
     if sql(restored,'SELECT school_id FROM tenant_identity')==schools[1]['id']:sql('postgres',f'DROP DATABASE {restored} WITH (FORCE)')
    admin.call('admin/schools/'+schools[1]['id']+'/status','PUT',{'enabled':True});assert b.call('reports/balance-sheet')['totals']['assets']==2000
    sql(control,'''UPDATE "Sessions" SET "MfaAt"=now()-interval '11 minutes' WHERE "UserId"=(SELECT "Id" FROM "AspNetUsers" WHERE "Email"='owner@example.invalid')''')
    admin.denied('admin/schools',403,'POST',{'name':'Stale MFA write','code':'STALE'})
    sql(control,'''UPDATE "Sessions" SET "MfaAt"=now() WHERE "UserId"=(SELECT "Id" FROM "AspNetUsers" WHERE "Email"='owner@example.invalid')''')
    cli('--flush-audit');assert any(e['outcome']=='Committed' for e in admin.call('admin/overview')['audit'])
    cli('--reset-password',Ledger__ResetUserEmail='operator1@example.invalid',Ledger__ResetPassword='Aa1!'+secrets.token_urlsafe(24));b.denied('state',401)
    print('PASS real login/MFA/CSRF, two-school isolation, bank/export tampering, immutable school branding, viewer/admin permissions, role/database grants, identity-marker rejection, revocation, restart and audit outbox')
   except Exception:
    log.flush();log.seek(0);print(log.read()[-5000:]);raise
   finally:
    if process:process.terminate();process.wait(timeout=10)
    for school in schools:
     db_name='ledger_school_'+school['id'].replace('-','');role='ledger_app_'+school['id'].replace('-','');sql('postgres',f'DROP DATABASE IF EXISTS {db_name} WITH (FORCE)');sql('postgres',f'DROP ROLE IF EXISTS {role}')
    sql('postgres',f'DROP DATABASE IF EXISTS {control} WITH (FORCE);')
    role='ledger_control_'+hashlib.sha256(control.encode()).hexdigest()[:16];sql('postgres',f'DROP ROLE IF EXISTS {role};')
if __name__=='__main__':main()
