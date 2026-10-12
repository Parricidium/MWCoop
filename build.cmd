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

rem Version du mod (src\MWCoop\Version.cs) dans les informations de version de version.dll et MWCoop.exe
for /f "tokens=2 delims==;" %%v in ('findstr /c:"Text = " src\MWCoop\Version.cs') do set "MWV=%%v"
set "MWV=%MWV:"=%"
set "MWV=%MWV: =%"
for /f "tokens=1-3 delims=.-" %%a in ("%MWV%") do set "MWVN=%%a,%%b,%%c,0"
> build\mwversion.h echo #define MWV_NUM %MWVN%
>> build\mwversion.h echo #define MWV_STR "%MWV%"
rc /nologo /i build /fo build\loader.res loader\loader.rc || exit /b 1
ml64 /nologo /c /Fobuild\exports.obj loader\exports.asm || exit /b 1
cl /nologo /O2 /MT /W3 /utf-8 /D_CRT_SECURE_NO_WARNINGS /DUNICODE /D_UNICODE /Fobuild\ loader\proxy.cpp build\exports.obj build\loader.res ^
   /LD /Febuild\version.dll /link /DEF:loader\version.def user32.lib kernel32.lib advapi32.lib shell32.lib shlwapi.lib ole32.lib || exit /b 1
echo OK build\version.dll
rem Ray tracing : MWCoopRT.dll (plugin natif d'Unity, mywintercar_Data\Plugins), DXR par Direct3D 12
"C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\dxc.exe" -nologo -T cs_6_5 -E Main -Fh build\rt_shadow.h -Vn g_rtShadow rt\shadow.hlsl || exit /b 1
"C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\dxc.exe" -nologo -T cs_6_5 -E Main -Fh build\rt_lamps.h -Vn g_rtLamps rt\lamps.hlsl || exit /b 1
cl /nologo /O2 /MT /W3 /EHsc /utf-8 /D_CRT_SECURE_NO_WARNINGS /I build /Fobuild\ rt\mwcrt.cpp /LD /Febuild\MWCoopRT.dll /link d3d11.lib d3d12.lib dxgi.lib d3dcompiler.lib || exit /b 1
echo OK build\MWCoopRT.dll

dotnet "%ProgramFiles%\dotnet\sdk\8.0.424\Roslyn\bincore\csc.dll" -nologo -noconfig -nostdlib -target:library -optimize -langversion:7.3 ^
   -nowarn:1701,1702 -out:build\MWCoop.dll ^
   -r:"%MANAGED%\mscorlib.dll" -r:"%MANAGED%\System.dll" -r:"%MANAGED%\System.Core.dll" ^
   -r:"%MANAGED%\UnityEngine.dll" -r:"%MANAGED%\PlayMaker.dll" -r:"%MANAGED%\Assembly-CSharp.dll" -r:"%MANAGED%\Assembly-CSharp-firstpass.dll" ^
   -resource:launcher\logo-titre.png,MWCoop.logo-titre.png -resource:assets\sac\sac-plie.mwm,MWCoop.sac-plie.mwm -resource:assets\sac\sac-deplie.mwm,MWCoop.sac-deplie.mwm -resource:assets\sac\sac.jpg,MWCoop.sac.jpg -resource:assets\drone\drone-corps.mwm,MWCoop.drone-corps.mwm -resource:assets\drone\drone-helice0.mwm,MWCoop.drone-helice0.mwm -resource:assets\drone\drone-helice1.mwm,MWCoop.drone-helice1.mwm -resource:assets\drone\drone-helice2.mwm,MWCoop.drone-helice2.mwm -resource:assets\drone\drone-helice3.mwm,MWCoop.drone-helice3.mwm -recurse:src\MWCoop\*.cs || exit /b 1
echo OK build\MWCoop.dll

rem Lanceur (MWCoop.exe, x64) : fenetre PNG (fonds et icone : launcher\make-art.ps1), mises a jour depuis GitHub
rc /nologo /i build /fo build\launcher.res launcher\launcher.rc || exit /b 1
cl /nologo /O2 /MT /W3 /EHsc /utf-8 /DUNICODE /D_UNICODE /D_CRT_SECURE_NO_WARNINGS /Fobuild\ launcher\launcher.cpp build\launcher.res ^
   /Febuild\MWCoop.exe /link /SUBSYSTEM:WINDOWS user32.lib gdi32.lib gdiplus.lib winhttp.lib comdlg32.lib shell32.lib ole32.lib advapi32.lib shlwapi.lib ws2_32.lib winmm.lib oleaut32.lib uuid.lib delayimp.lib /DELAYLOAD:winhttp.dll /DELAYLOAD:winmm.dll || exit /b 1
echo OK build\MWCoop.exe
exit /b 0

:pasdejeu
echo Jeu introuvable : "%MWC_JEU%" (variable MWC_JEU)
exit /b 1
