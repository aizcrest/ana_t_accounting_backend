"""Start the authenticated app using restricted control credentials, never provisioning credentials."""
import json,os,pathlib,sys
ROOT=pathlib.Path(__file__).resolve().parents[1];env=os.environ.copy();config=pathlib.Path(env.get('Ledger__RuntimeConfigPath',str(ROOT/'.local/secure-runtime.json')))
env.update(json.loads(config.read_text()));env.pop('ConnectionStrings__Provisioning',None);env.pop('ConnectionStrings__Accounting',None);env.pop('Ledger__BootstrapPassword',None)
env.setdefault('ASPNETCORE_URLS','http://127.0.0.1:5080')
dotnet='/workspace/.dotnet/dotnet' if pathlib.Path('/workspace/.dotnet/dotnet').exists() else 'dotnet'
if dotnet.startswith('/workspace'):env.setdefault('DOTNET_CLI_HOME','/workspace/.dotnet-home')
os.chdir(ROOT);os.execvpe(dotnet,[dotnet,str(ROOT/'bin/Debug/net10.0/Accounting.Api.dll'),*sys.argv[1:]],env)
