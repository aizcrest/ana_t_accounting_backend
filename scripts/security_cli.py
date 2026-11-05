"""Offline provisioning only. Never use the superuser configuration in the web process.
Examples: python3 scripts/security_cli.py --control-init / --provision-pending.
Bootstrap credentials come from environment or a hidden prompt, never command-line arguments.
"""
import getpass,json,os,pathlib,sys
ROOT=pathlib.Path(__file__).resolve().parents[1];env=os.environ.copy()
source=ROOT/'.local/postgres-connection.json'
if 'ConnectionStrings__Provisioning' not in env:
 env['ConnectionStrings__Provisioning']=json.loads(source.read_text())['ConnectionStrings__Accounting']
env.setdefault('Ledger__Mode','Development');env.setdefault('ASPNETCORE_ENVIRONMENT','Development')
if '--control-init' in sys.argv:
 if 'Ledger__BootstrapEmail' not in env:env['Ledger__BootstrapEmail']=input('Administrator email: ').strip()
 if 'Ledger__BootstrapPassword' not in env:env['Ledger__BootstrapPassword']=getpass.getpass('Administrator password (12+ characters): ')
if '--reset-password' in sys.argv or '--reset-mfa' in sys.argv:
 if 'Ledger__ResetUserEmail' not in env:env['Ledger__ResetUserEmail']=input('Account email to recover: ').strip()
 if '--reset-password' in sys.argv and 'Ledger__ResetPassword' not in env:env['Ledger__ResetPassword']=getpass.getpass('New password (12+ characters): ')
dotnet='/workspace/.dotnet/dotnet' if pathlib.Path('/workspace/.dotnet/dotnet').exists() else 'dotnet'
if dotnet.startswith('/workspace'):env.setdefault('DOTNET_CLI_HOME','/workspace/.dotnet-home')
os.chdir(ROOT);os.execvpe(dotnet,[dotnet,str(ROOT/'bin/Debug/net10.0/Accounting.Api.dll'),*sys.argv[1:]],env)
