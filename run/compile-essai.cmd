@echo off
rem Compilation d'essai dans un dossier a part (agents en parallele : jamais build\ directement).
rem   run\compile-essai.cmd <dossier de sortie> [lanceur|mod|tout]
rem Le lanceur : <sortie>\MWCoop.exe ; le mod : <sortie>\MWCoop.dll (memes sources et ressources que build.cmd).
setlocal
cd /d "%~dp0.."
set "OUT=%~1"
if "%OUT%"=="" (echo usage : run\compile-essai.cmd ^<dossier^> [lanceur^|mod^|tout] & exit /b 2)
set "WHAT=%~2"
if "%WHAT%"=="" set "WHAT=tout"
if not exist "%OUT%" mkdir "%OUT%"
if not defined MWC_JEU set "MWC_JEU=C:\Program Files (x86)\Steam\steamapps\common\My Winter Car"
set "MANAGED=%MWC_JEU%\mywintercar_Data\Managed"
call "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat" >nul || exit /b 1
if /i "%WHAT%"=="mod" goto :mod
rem (le lanceur embarque build\version.dll et build\mwversion.h : faits par build.cmd)
rc /nologo /i build /fo "%OUT%\launcher.res" launcher\launcher.rc || exit /b 1
cl /nologo /O2 /MT /W3 /EHsc /utf-8 /DUNICODE /D_UNICODE /D_CRT_SECURE_NO_WARNINGS /Fo"%OUT%\\" launcher\launcher.cpp "%OUT%\launcher.res" ^
   /Fe"%OUT%\MWCoop.exe" /link /SUBSYSTEM:WINDOWS user32.lib gdi32.lib gdiplus.lib winhttp.lib comdlg32.lib shell32.lib ole32.lib advapi32.lib shlwapi.lib ws2_32.lib winmm.lib oleaut32.lib uuid.lib delayimp.lib /DELAYLOAD:winhttp.dll /DELAYLOAD:winmm.dll || exit /b 1
echo OK %OUT%\MWCoop.exe
if /i "%WHAT%"=="lanceur" exit /b 0
:mod
rem les ressources du mod : la meme liste que build.cmd (lue dans build.cmd, ligne -resource:...)
for /f "usebackq tokens=* delims=" %%L in (`findstr /c:"-resource:launcher\logo-titre.png" build.cmd`) do set "RES=%%L"
set "RES=%RES:-recurse:src\MWCoop\*.cs || exit /b 1=%"
dotnet "%ProgramFiles%\dotnet\sdk\8.0.424\Roslyn\bincore\csc.dll" -nologo -noconfig -nostdlib -target:library -optimize -langversion:7.3 ^
   -nowarn:1701,1702 -out:"%OUT%\MWCoop.dll" ^
   -r:"%MANAGED%\mscorlib.dll" -r:"%MANAGED%\System.dll" -r:"%MANAGED%\System.Core.dll" ^
   -r:"%MANAGED%\UnityEngine.dll" -r:"%MANAGED%\PlayMaker.dll" -r:"%MANAGED%\Assembly-CSharp.dll" -r:"%MANAGED%\Assembly-CSharp-firstpass.dll" ^
   %RES% -recurse:src\MWCoop\*.cs || exit /b 1
echo OK %OUT%\MWCoop.dll
exit /b 0
