# Cree les instances de test MWCoop-Joueur1..N : exe et steam_api copies, gros dossiers du jeu
# relies par jonctions (rien n'est ecrit dans le jeu), steam_appid.txt (pas de relance par Steam),
# MWCoop\mwcoop.ini en profil isole + arriere-plan (sauvegardes, registre et ecran de JD intouches).
param([int]$Count = 2,
      [string]$Jeu = 'C:\Program Files (x86)\Steam\steamapps\common\My Winter Car',
      [string]$Racine = 'D:\Games\COOPTEST\My Winter Car')
$ErrorActionPreference = 'Stop'
for ($i = 1; $i -le $Count; $i++) {
    $d = Join-Path $Racine "MWCoop-Joueur$i"
    New-Item -ItemType Directory -Force "$d\MWCoop" | Out-Null
    foreach ($f in 'mywintercar.exe', 'steam_api64.dll', 'changelog.txt') { Copy-Item "$Jeu\$f" $d -Force }
    foreach ($j in 'mywintercar_Data', 'CD1', 'CD2', 'CD3', 'Extra', 'Images', 'Radio') {
        if (-not (Test-Path "$d\$j")) { cmd /c mklink /J "$d\$j" "$Jeu\$j" | Out-Null }
    }
    Set-Content "$d\steam_appid.txt" '4164420' -NoNewline -Encoding ASCII
    $ini = "$d\MWCoop\mwcoop.ini"
    if (-not (Test-Path $ini)) {
        @"
[Coop]
Pseudo=Joueur$i
Port=7871

[Test]
Profil=Joueur$i
ArrierePlan=1
FenetreX=$(-3000 + ($i - 1) * 1000)
FenetreY=100
"@ | Set-Content $ini -Encoding ASCII
    }
    "OK $d"
}
