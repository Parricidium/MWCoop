@echo off
rem Compile le mod Voyage (MSCLoader, .NET 3.5 / Mono d'Unity) : build\Voyage.dll. MWC_JEU = dossier du jeu (MSCLoader installe).
rem Une seule DLL pour My Summer Car et My Winter Car : ni Assembly-CSharp ni firstpass (differents d'un jeu a l'autre) ;
rem Unity, PlayMaker et MSCLoader 1.4.2 sont les memes.
setlocal
cd /d "%~dp0"
if not defined MWC_JEU set "MWC_JEU=C:\Program Files (x86)\Steam\steamapps\common\My Winter Car"
set "MANAGED=%MWC_JEU%\mywintercar_Data\Managed"
if not exist "%MANAGED%\MSCLoader.dll" goto :sansloader
if not exist build mkdir build
dotnet "%ProgramFiles%\dotnet\sdk\8.0.424\Roslyn\bincore\csc.dll" -nologo -noconfig -nostdlib -target:library -optimize -langversion:7.3 ^
   -nowarn:1701,1702 -out:build\Voyage.dll ^
   -r:"%MANAGED%\mscorlib.dll" -r:"%MANAGED%\System.dll" -r:"%MANAGED%\System.Core.dll" ^
   -r:"%MANAGED%\UnityEngine.dll" -r:"%MANAGED%\PlayMaker.dll" ^
   -r:"%MANAGED%\MSCLoader.dll" ^
   -resource:modeles\cessna.vmesh,Voyage.cessna.vmesh -resource:modeles\cessna_altbody.jpg,Voyage.cessna_altbody.jpg ^
   -resource:modeles\cessna_interior.jpg,Voyage.cessna_interior.jpg -resource:modeles\cessna_main.jpg,Voyage.cessna_main.jpg ^
   -resource:modeles\cessna_seats_and_stuff.jpg,Voyage.cessna_seats_and_stuff.jpg -resource:modeles\cessna_windows.png,Voyage.cessna_windows.png ^
   -resource:modeles\cessna_xtra.jpg,Voyage.cessna_xtra.jpg ^
   -recurse:src\*.cs || exit /b 1
echo OK build\Voyage.dll
exit /b 0
:sansloader
echo MSCLoader introuvable dans le dossier du jeu
exit /b 1
