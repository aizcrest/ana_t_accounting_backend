"""Start the API or database checks using ignored local PostgreSQL credentials."""
import json,os,pathlib,sys
ROOT=pathlib.Path(__file__).resolve().parents[1];env=os.environ.copy();env.update(json.loads((ROOT/'.local/postgres-connection.json').read_text()))
env.setdefault('ASPNETCORE_URLS','http://127.0.0.1:5080')
dotnet='/workspace/.dotnet/dotnet' if pathlib.Path('/workspace/.dotnet/dotnet').exists() else 'dotnet'
if dotnet.startswith('/workspace'):env.setdefault('DOTNET_CLI_HOME','/workspace/.dotnet-home')
os.chdir(ROOT);os.execvpe(dotnet,[dotnet,str(ROOT/'bin/Debug/net10.0/Accounting.Api.dll'),*sys.argv[1:]],env)
