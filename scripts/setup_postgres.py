"""Prepare a local PostgreSQL sample without printing database credentials."""
import json,os,pathlib,secrets,subprocess,sys
ROOT=pathlib.Path(__file__).resolve().parents[1];LOCAL=ROOT/'.local'
def private_write(path,text):
 fd=os.open(path,os.O_WRONLY|os.O_CREAT|os.O_EXCL,0o600)
 with os.fdopen(fd,'w') as f:f.write(text)
def main():
 LOCAL.mkdir(mode=0o700,exist_ok=True)
 for key in ['DOCKER_HOST','DOCKER_CONTEXT','DOCKER_TLS','DOCKER_TLS_VERIFY','DOCKER_CERT_PATH']:os.environ.pop(key,None)
 docker=['docker']+(['--host=unix:///var/run/docker.sock'] if pathlib.Path('/var/run/docker.sock').exists() else [])
 password_file=LOCAL/'postgres.env'
 if not password_file.exists():private_write(password_file,'POSTGRES_PASSWORD='+secrets.token_urlsafe(40)+'\n')
 connection_file=LOCAL/'postgres-connection.json'
 if not connection_file.exists():
  password=password_file.read_text().strip().split('=',1)[1]
  private_write(connection_file,json.dumps({'ConnectionStrings__Accounting':f'Host=127.0.0.1;Port=5432;Database=accounting;Username=ledger;Password={password}'}))
 subprocess.run(docker+['compose','--env-file',str(password_file),'up','-d','--wait'],cwd=ROOT,check=True)
 print('PostgreSQL server ready. Use scripts/setup_secure_sample.py to initialize authenticated access; legacy books are left unchanged.')
if __name__=='__main__':main()
