@echo off
rem Compile MWCoop : build\version.dll (chargeur x64), build\MWCoop.dll (mod, .NET 3.5 / Mono d'Unity 5)
rem et build\MWCoop.exe (lanceur). Le mod se compile contre les DLL du jeu : MWC_JEU = dossier du jeu.
setlocal
cd /d "%~dp0"
if not defined MWC_JEU set "MWC_JEU=C:\Program Files (x86)\Steam\steamapps\common\My Winter Car"
set "MANAGED=%MWC_JEU%\mywintercar_Data\Managed"
if not exist "%MANAGED%\UnityEngine.dll" goto :pasdejeu
if not exist build mkdir build

call "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat" >nul || exit /b 1
ml64 /nologo /c /Fobuild\exports.obj loader\exports.asm || exit /b 1
cl /nologo /O2 /MT /W3 /utf-8 /D_CRT_SECURE_NO_WARNINGS /DUNICODE /D_UNICODE /Fobuild\ loader\proxy.cpp build\exports.obj ^
   /LD /Febuild\version.dll /link /DEF:loader\version.def user32.lib kernel32.lib advapi32.lib shell32.lib shlwapi.lib ole32.lib || exit /b 1
echo OK build\version.dll

dotnet "%ProgramFiles%\dotnet\sdk\8.0.424\Roslyn\bincore\csc.dll" -nologo -noconfig -nostdlib -target:library -optimize -langversion:7.3 ^
   -nowarn:1701,1702 -out:build\MWCoop.dll ^
   -r:"%MANAGED%\mscorlib.dll" -r:"%MANAGED%\System.dll" -r:"%MANAGED%\System.Core.dll" ^
   -r:"%MANAGED%\UnityEngine.dll" -r:"%MANAGED%\PlayMaker.dll" -r:"%MANAGED%\Assembly-CSharp.dll" ^
   -recurse:src\MWCoop\*.cs || exit /b 1
echo OK build\MWCoop.dll

rem Lanceur (MWCoop.exe, x64) : fenetre PNG (fonds et icone : launcher\make-art.ps1), mises a jour depuis GitHub
rc /nologo /fo build\launcher.res launcher\launcher.rc || exit /b 1
cl /nologo /O2 /MT /W3 /EHsc /utf-8 /DUNICODE /D_UNICODE /D_CRT_SECURE_NO_WARNINGS /Fobuild\ launcher\launcher.cpp build\launcher.res ^
   /Febuild\MWCoop.exe /link /SUBSYSTEM:WINDOWS user32.lib gdi32.lib gdiplus.lib winhttp.lib comdlg32.lib shell32.lib ole32.lib advapi32.lib shlwapi.lib ws2_32.lib winmm.lib || exit /b 1
echo OK build\MWCoop.exe
exit /b 0

:pasdejeu
echo Jeu introuvable : "%MWC_JEU%" (variable MWC_JEU)
exit /b 1
