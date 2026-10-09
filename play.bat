@echo off
rem One command for a test run: builds the mod, patches the engine, starts RimWorld
rem (unless it is already open) and the engine on the rimworld:flat map.
cd /d "%~dp0"
python scripts\setup.py play %*
