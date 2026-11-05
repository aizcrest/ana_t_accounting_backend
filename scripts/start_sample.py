"""Build and start the local demo; then open localhost:5080 in your browser."""
import os,pathlib,shutil,subprocess,sys
ROOT=pathlib.Path(__file__).resolve().parents[1];FRONTEND=ROOT.parent/'ana_t_accounting_frontend'
def main():
 dotnet='/workspace/.dotnet/dotnet' if pathlib.Path('/workspace/.dotnet/dotnet').exists() else 'dotnet'
 env=os.environ.copy()
 if dotnet.startswith('/workspace'):
  env.setdefault('DOTNET_CLI_HOME','/workspace/.dotnet-home');env.setdefault('NUGET_PACKAGES','/workspace/.nuget/packages')
 npm=shutil.which('npm.cmd' if os.name=='nt' else 'npm')
 if not npm:raise SystemExit('Install Node.js 22+ (with npm) first.')
 for command in [[npm,'ci','--no-audit','--no-fund'],[npm,'run','build']]:subprocess.run(command,cwd=FRONTEND,check=True,env=env)
 assets=ROOT/'wwwroot';assets.mkdir(exist_ok=True);shutil.copytree(FRONTEND/'dist',assets,dirs_exist_ok=True)
 subprocess.run([dotnet,'restore','--locked-mode'],cwd=ROOT,check=True,env=env)
 subprocess.run([dotnet,'build','--no-restore'],cwd=ROOT,check=True,env=env)
 subprocess.run([sys.executable,'scripts/setup_postgres.py'],cwd=ROOT,check=True,env=env)
 print('\nSample ready: open http://localhost:5080 on this computer. Press Ctrl+C to stop the API.\n',flush=True)
 subprocess.run([sys.executable,'scripts/run_postgres_api.py'],cwd=ROOT,env=env,check=True)
if __name__=='__main__':main()
