@echo off
title Popup Test
cd /d "%~dp0"
npx electron test-popup.mjs
pause
