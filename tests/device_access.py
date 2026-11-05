"""Exercise the actual Kestrel mutual-TLS device gate with generated temporary certificates.
Runs loopback Development with the production certificate gate forced on; production startup
requirements are also checked. This does not configure the user's Tailscale infrastructure.
"""
from isolation import Client
import copy,hashlib,json,os,pathlib,secrets,ssl,subprocess,tempfile,time,urllib.error,urllib.request,uuid
ROOT=pathlib.Path(__file__).resolve().parents[1];DOTNET=os.environ.get('DOTNET_EXECUTABLE','dotnet')
class TLSClient(Client):
 def __init__(self,base,ca,cert=None,key=None):
  super().__init__(base);context=ssl.create_default_context(cafile=str(ca))
  if cert:context.load_cert_chain(str(cert),str(key))
  self.opener=urllib.request.build_opener(urllib.request.HTTPCookieProcessor(self.jar),urllib.request.HTTPSHandler(context=context))
def main():
 control='ledger_auth_test_'+uuid.uuid4().hex;owner=json.loads((ROOT/'.local/postgres-connection.json').read_text())['ConnectionStrings__Accounting'];password='Aa1!'+secrets.token_urlsafe(24);process=None;schools=[]
 with tempfile.TemporaryDirectory() as tmp:
  temp=pathlib.Path(tmp);env=os.environ.copy();env.update(ConnectionStrings__Provisioning=owner,Ledger__Mode='Development',ASPNETCORE_ENVIRONMENT='Development',Ledger__ControlDatabaseName=control,Ledger__KeyPath=str(temp/'keys'),Ledger__RuntimeConfigPath=str(temp/'runtime.json'),Ledger__BootstrapEmail='owner@example.invalid',Ledger__BootstrapPassword=password,Logging__LogLevel__Default='Error',DOTNET_CLI_HOME='/workspace/.dotnet-home')
  def cli(*args):
   r=subprocess.run([DOTNET,str(ROOT/'bin/Debug/net10.0/Accounting.Api.dll'),*args],env=env,cwd=ROOT,capture_output=True,text=True)
   assert r.returncode==0,r.stdout[-1000:]+r.stderr[-1000:]
  def sql(db,q):return subprocess.check_output(['docker','--host=unix:///var/run/docker.sock','exec','ledger-ph-postgres','psql','-U','ledger','-d',db,'-At','-v','ON_ERROR_STOP=1','-c',q],text=True).strip()
  def openssl(*args):subprocess.run(['openssl',*map(str,args)],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
  def cert(name,usage):
   key=temp/(name+'.key');csr=temp/(name+'.csr');pem=temp/(name+'.pem');ext=temp/(name+'.ext');ext.write_text('basicConstraints=critical,CA:FALSE\nkeyUsage=critical,digitalSignature,keyEncipherment\nextendedKeyUsage='+usage+('\nsubjectAltName=DNS:localhost,IP:127.0.0.1' if usage=='serverAuth' else '')+'\n')
   openssl('req','-new','-newkey','rsa:2048','-nodes','-keyout',key,'-out',csr,'-subj','/CN='+name);openssl('x509','-req','-in',csr,'-CA',temp/'ca.pem','-CAkey',temp/'ca.key','-CAcreateserial','-out',pem,'-days','1','-extfile',ext);return pem,key
  def start(tls=False):
   runtime={k:v for k,v in env.items() if not k.startswith('ConnectionStrings__') and not k.startswith('Ledger__Bootstrap')};runtime.update(json.load(open(temp/'runtime.json')));runtime['ASPNETCORE_URLS']='https://127.0.0.1:5086' if tls else 'http://127.0.0.1:5086'
   if tls:runtime.update(Ledger__RequireClientCertificate='true',Ledger__ClientCaFile=str(temp/'ca.pem'),Kestrel__Certificates__Default__Path=str(temp/'server.pfx'),Kestrel__Certificates__Default__Password=pfx_password)
   return subprocess.Popen([DOTNET,str(ROOT/'bin/Debug/net10.0/Accounting.Api.dll')],env=runtime,cwd=ROOT,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
  def ready(client):
   for _ in range(150):
    try:client.call('health');return
    except Exception:time.sleep(.1)
   raise AssertionError('Device-test API did not start')
  try:
   cli('--control-init');process=start();admin=Client('http://127.0.0.1:5086');ready(admin);_,admin_recovery=admin.enroll('owner@example.invalid',password)
   for code in ['A','B']:schools.append(admin.call('admin/schools','POST',{'name':'Device School '+code,'code':'DEV-'+code}))
   cli('--provision-pending');invite=admin.call('admin/users','POST',{'email':'operator@example.invalid','schoolId':schools[0]['id'],'role':'Operator'});user=Client('http://127.0.0.1:5086');user.call('auth/accept-invitation','POST',dict(userId=invite['userId'],token=invite['token'],password=password));_,recovery=user.enroll('operator@example.invalid',password)
   openssl('req','-x509','-newkey','rsa:2048','-nodes','-keyout',temp/'ca.key','-out',temp/'ca.pem','-days','1','-subj','/CN=Temporary Test CA','-addext','basicConstraints=critical,CA:TRUE','-addext','keyUsage=critical,keyCertSign,cRLSign')
   server,server_key=cert('server','serverAuth');a_cert,a_key=cert('school-a-workstation','clientAuth');b_cert,b_key=cert('school-b-workstation','clientAuth');unknown_cert,unknown_key=cert('unapproved-workstation','clientAuth');admin_cert,admin_key=cert('administrator-workstation','clientAuth')
   pfx_password=secrets.token_urlsafe(24);openssl('pkcs12','-export','-inkey',server_key,'-in',server,'-out',temp/'server.pfx','-passout','pass:'+pfx_password)
   def fingerprint(pem):return hashlib.sha256(ssl.PEM_cert_to_DER_cert(pem.read_text())).hexdigest().upper()
   admin.call('admin/devices','POST',{'id':fingerprint(a_cert),'name':'Workstation A','schoolId':schools[0]['id'],'enabled':True});admin.call('admin/devices','POST',{'id':fingerprint(b_cert),'name':'Workstation B','schoolId':schools[1]['id'],'enabled':True})
   admin.call('admin/devices','POST',{'id':fingerprint(admin_cert),'name':'Administrator workstation','schoolId':None,'enabled':True})
   process.terminate();process.wait(timeout=10);process=start(True)
   a=TLSClient('https://127.0.0.1:5086',temp/'ca.pem',a_cert,a_key);b=TLSClient('https://127.0.0.1:5086',temp/'ca.pem',b_cert,b_key);unknown=TLSClient('https://127.0.0.1:5086',temp/'ca.pem',unknown_cert,unknown_key);ready(a)
   tls_admin=TLSClient('https://127.0.0.1:5086',temp/'ca.pem',admin_cert,admin_key);tls_admin.call('auth/login','POST',dict(email='owner@example.invalid',password=password));tls_admin.call('auth/mfa/recovery','POST',{'code':admin_recovery[0]});tls_admin.call('admin/overview')
   sql(control,'UPDATE "Devices" SET "SchoolId"=\''+schools[0]['id']+"' WHERE \"Id\"='"+fingerprint(admin_cert)+"'");tls_admin.denied('admin/overview',403)
   unknown.denied('auth/csrf',403);unknown.denied('state',403,headers={'X-Device-Id':fingerprint(a_cert),'Tailscale-User-Login':'operator@example.invalid'})
   b.denied('auth/login',403,'POST',dict(email='operator@example.invalid',password=password))
   a.call('auth/login','POST',dict(email='operator@example.invalid',password=password));a.call('auth/mfa/recovery','POST',{'code':recovery[0]});assert a.call('canteen')['profile']['schoolId']=='DEV-A'
   for cookie in a.jar:b.jar.set_cookie(copy.copy(cookie))
   b.denied('state',403)
   no_cert=TLSClient('https://127.0.0.1:5086',temp/'ca.pem')
   try:no_cert.call('state');raise AssertionError('Missing certificate accepted')
   except (urllib.error.URLError,ssl.SSLError,ConnectionError):pass
   # Revocation is checked on every request, even for an existing signed-in session.
   sql(control,'UPDATE "Devices" SET "Enabled"=false WHERE "Id"=\''+fingerprint(a_cert)+"'");a.denied('state',403)
   process.terminate();process.wait(timeout=10);process=None
   production={k:v for k,v in env.items() if not k.startswith('ConnectionStrings__') and not k.startswith('Ledger__Bootstrap')};production.update(json.load(open(temp/'runtime.json')));production.update(Ledger__Mode='Production',ASPNETCORE_ENVIRONMENT='Production');production.pop('Ledger__ClientCaFile',None)
   result=subprocess.run([DOTNET,str(ROOT/'bin/Debug/net10.0/Accounting.Api.dll')],env=production,cwd=ROOT,capture_output=True,text=True,timeout=20);assert result.returncode!=0 and 'Production requires' in result.stderr+result.stdout
   print('PASS real mutual-TLS gate: unapproved/no certificate, forged headers, wrong-school device, cookie replay on another device, revocation and fail-closed production configuration')
  finally:
   if process:process.terminate();process.wait(timeout=10)
   for school in schools:
    suffix=school['id'].replace('-','');sql('postgres',f'DROP DATABASE IF EXISTS ledger_school_{suffix} WITH (FORCE)');sql('postgres',f'DROP ROLE IF EXISTS ledger_app_{suffix}')
   sql('postgres',f'DROP DATABASE IF EXISTS {control} WITH (FORCE)');sql('postgres','DROP ROLE IF EXISTS ledger_control_'+hashlib.sha256(control.encode()).hexdigest()[:16])
if __name__=='__main__':main()
