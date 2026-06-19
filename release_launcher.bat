@echo off
chcp 65001 > nul
title Publicador de Launcher - One Piece
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0release_launcher.ps1"
pause
