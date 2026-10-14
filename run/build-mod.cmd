@echo off
rem Compile seulement le chargeur, le ray tracing et le mod (essais) : build.cmd sans le lanceur. Avant (revue du 14/10) :
rem une copie de la commande de build.cmd, qui avait diverge (ressource hintalappu-kartturi.png absente).
setlocal
set "MWC_SANSLANCEUR=1"
call "%~dp0..\build.cmd"
