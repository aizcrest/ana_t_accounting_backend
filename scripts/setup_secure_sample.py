"""Prepare an authenticated loopback demo. Bootstrap password is entered privately.
Existing accounting/JSON data is not imported or overwritten; demo school is a separate DB.
"""
import os,pathlib,subprocess,sys
ROOT=pathlib.Path(__file__).resolve().parents[1]
def main():
 subprocess.run([sys.executable,'scripts/setup_postgres.py'],cwd=ROOT,check=True)
 if not (ROOT/'.local/secure-runtime.json').exists():subprocess.run([sys.executable,'scripts/security_cli.py','--control-init'],cwd=ROOT,check=True)
 subprocess.run([sys.executable,'scripts/security_cli.py','--demo-school'],cwd=ROOT,check=True)
 print('Secure sample prepared. Run python3 scripts/run_secure_api.py, sign in as your administrator, enroll MFA and invite a school Operator. No financial data is available anonymously.')
if __name__=='__main__':main()
