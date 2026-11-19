@echo off
rem Engega Hearth amb una finestra visible (per veure errors). Normalment ja s'engega sol.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0servidor.ps1" -Visible
