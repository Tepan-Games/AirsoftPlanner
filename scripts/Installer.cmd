@echo off
rem Double-cliquer pour installer Airsoft Planner (ou ajouter -Desinstaller pour le supprimer).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Installer.ps1" %*
pause
