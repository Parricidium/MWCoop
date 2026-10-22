@echo off
rem MWCoop network check & fix -- double-click. Asks for administrator rights (firewall rules, Defender exclusion).
rem Keep MWCoop-network-fix.ps1 next to this file. Report: Desktop\MWCoop-network-report.txt
powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process powershell -Verb RunAs -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File \"%~dp0MWCoop-network-fix.ps1\"'"
