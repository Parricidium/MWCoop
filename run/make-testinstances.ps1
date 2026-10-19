# Cree les instances de test MWCoop-Joueur1..N : exe et steam_api copies, gros dossiers du jeu
# relies par jonctions (rien n'est ecrit dans le jeu), steam_appid.txt (pas de relance par Steam),
# MWCoop\mwcoop.ini en profil isole + arriere-plan + muet (sauvegardes, registre, ecran et son de JD intouches).
# -Msc : celles de My Summer Car (D:\Games\COOPTEST\My Summer Car, switch MSC / MWC, 19/10).
param([int]$Count = 2, [switch]$Msc, [string]$Jeu = '', [string]$Racine = '')
$ErrorActionPreference = 'Stop'
if ($Msc) { $nom = 'My Summer Car'; $exe = 'mysummercar.exe'; $data = 'mysummercar_Data'; $appid = '516750'; $port = 7881 }
else { $nom = 'My Winter Car'; $exe = 'mywintercar.exe'; $data = 'mywintercar_Data'; $appid = '4164420'; $port = 7871 }
if (-not $Jeu) { $Jeu = "C:\Program Files (x86)\Steam\steamapps\common\$nom" }
if (-not $Racine) { $Racine = "D:\Games\COOPTEST\$nom" }
for ($i = 1; $i -le $Count; $i++) {
    $d = Join-Path $Racine "MWCoop-Joueur$i"
    New-Item -ItemType Directory -Force "$d\MWCoop" | Out-Null
    foreach ($f in $exe, 'steam_api64.dll', 'changelog.txt') { Copy-Item "$Jeu\$f" $d -Force }
    foreach ($j in $data, 'CD1', 'CD2', 'CD3', 'Extra', 'Images', 'Radio') {
        if ((Test-Path "$Jeu\$j") -and -not (Test-Path "$d\$j")) { cmd /c mklink /J "$d\$j" "$Jeu\$j" | Out-Null }
    }
    Set-Content "$d\steam_appid.txt" $appid -NoNewline -Encoding ASCII
    $ini = "$d\MWCoop\mwcoop.ini"
    if (-not (Test-Path $ini)) {
        @"
[Coop]
Pseudo=Joueur$i
Port=$port

[Test]
Profil=Joueur$i
ArrierePlan=1
Muet=1
FenetreX=$(-3000 + ($i - 1) * 1000)
FenetreY=100
"@ | Set-Content $ini -Encoding ASCII
    }
    "OK $d"
}
