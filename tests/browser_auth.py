"""Real browser authentication flow; requires Playwright and Chromium installed separately."""
import hashlib,json,os,pathlib,secrets,subprocess,tempfile,time,urllib.request,uuid
root=pathlib.Path(__file__).resolve().parents[1];dotnet=os.environ.get('DOTNET_EXECUTABLE','dotnet');control='ledger_auth_test_'+uuid.uuid4().hex;password='Aa1!'+secrets.token_urlsafe(24);owner=json.loads((root/'.local/postgres-connection.json').read_text())['ConnectionStrings__Accounting'];process=None
with tempfile.TemporaryDirectory() as temp:
 env=os.environ.copy();env.update(ConnectionStrings__Provisioning=owner,Ledger__Mode='Development',ASPNETCORE_ENVIRONMENT='Development',Ledger__ControlDatabaseName=control,Ledger__KeyPath=temp+'/keys',Ledger__RuntimeConfigPath=temp+'/runtime.json',Ledger__BootstrapEmail='owner@example.invalid',Ledger__BootstrapPassword=password,DOTNET_CLI_HOME='/workspace/.dotnet-home',Logging__LogLevel__Default='Error')
 def cli(*args,**values):
  r=subprocess.run([dotnet,str(root/'bin/Debug/net10.0/Accounting.Api.dll'),*args],cwd=root,env={**env,**values},capture_output=True,text=True)
  if r.returncode:raise RuntimeError(r.stdout[-1000:]+r.stderr[-1000:])
 def sql(db,q):return subprocess.check_output(['docker','--host=unix:///var/run/docker.sock','exec','ledger-ph-postgres','psql','-U','ledger','-d',db,'-At','-c',q],text=True).strip()
 try:
  cli('--control-init')
  for code in ['A','B']:cli('--demo-school',Ledger__SchoolCode='BROW-'+code,Ledger__SchoolName='Browser School '+code)
  runtime={k:v for k,v in env.items() if not k.startswith('ConnectionStrings__') and not k.startswith('Ledger__Bootstrap')};runtime.update(json.load(open(temp+'/runtime.json')));runtime['ASPNETCORE_URLS']='http://127.0.0.1:5085'
  process=subprocess.Popen([dotnet,str(root/'bin/Debug/net10.0/Accounting.Api.dll')],cwd=root,env=runtime,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
  for _ in range(150):
   try:urllib.request.urlopen('http://127.0.0.1:5085/api/health');break
   except Exception:time.sleep(.1)
  subprocess.run(['node',str(root/'tests/browser_auth.cjs')],env={**os.environ,'BROWSER_ADMIN_PASSWORD':password},check=True)
 finally:
  if process:process.terminate();process.wait(timeout=10)
  for school in sql(control,'SELECT "Id" FROM "Schools"').splitlines():
   suffix=school.replace('-','');sql('postgres',f'DROP DATABASE IF EXISTS ledger_school_{suffix} WITH (FORCE)');sql('postgres',f'DROP ROLE IF EXISTS ledger_app_{suffix}')
  sql('postgres',f'DROP DATABASE IF EXISTS {control} WITH (FORCE)');sql('postgres','DROP ROLE IF EXISTS ledger_control_'+hashlib.sha256(control.encode()).hexdigest()[:16])
