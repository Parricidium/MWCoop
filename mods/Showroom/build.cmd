@echo off
rem Compile le mod Showroom (MSCLoader, .NET 3.5 / Mono d'Unity) : build\Showroom.dll, pour My Summer Car. Unity et PlayMaker :
rem ceux de My Winter Car (MWC_JEU, les memes) ; MSCLoader : la copie de My Summer Car du lanceur (MSC_LOADER) -- celle de
rem My Winter Car, meme numero 1.4.2, n'a pas la meme API des touches (Keybind).
setlocal
cd /d "%~dp0"
if not defined MWC_JEU set "MWC_JEU=C:\Program Files (x86)\Steam\steamapps\common\My Winter Car"
set "MANAGED=%MWC_JEU%\mywintercar_Data\Managed"
if not defined MSC_LOADER set "MSC_LOADER=%LOCALAPPDATA%\MWCoop\mscloader-mwcoop-msc\Managed\MSCLoader.dll"
if not exist "%MSC_LOADER%" goto :sansloader
if not exist build mkdir build
dotnet "%ProgramFiles%\dotnet\sdk\8.0.424\Roslyn\bincore\csc.dll" -nologo -noconfig -nostdlib -target:library -optimize -langversion:7.3 ^
   -nowarn:1701,1702 -out:build\Showroom.dll ^
   -r:"%MANAGED%\mscorlib.dll" -r:"%MANAGED%\System.dll" -r:"%MANAGED%\System.Core.dll" ^
   -r:"%MANAGED%\UnityEngine.dll" -r:"%MANAGED%\PlayMaker.dll" ^
   -r:"%MSC_LOADER%" ^
   -recurse:src\*.cs || exit /b 1
echo OK build\Showroom.dll
exit /b 0
:sansloader
echo MSCLoader de My Summer Car introuvable : %MSC_LOADER%
exit /b 1
