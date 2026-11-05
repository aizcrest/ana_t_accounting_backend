"""Compatibility alias: authenticated access is now mandatory."""
import pathlib,runpy
runpy.run_path(str(pathlib.Path(__file__).with_name('run_secure_api.py')),run_name='__main__')
