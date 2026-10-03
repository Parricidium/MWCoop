# Deux copies jouables pour essayer seul a deux instances : MWCoop-JD-Hote et MWCoop-JD-Invite.
# Comme les instances de test (exe et steam_api copies, gros dossiers relies par jonctions, rien
# n'est ecrit dans le jeu, steam_appid.txt : pas besoin de passer par Steam), mais fenetres normales,
# chacune avec son lanceur MWCoop.exe et la derniere version du mod. Profils isoles : la vraie
# sauvegarde et le registre du jeu ne sont jamais touches.
param([string]$Jeu = 'C:\Program Files (x86)\Steam\steamapps\common\My Winter Car',
      [string]$Racine = 'D:\Games\COOPTEST\My Winter Car',
      [string]$Mod = (Join-Path (Split-Path $PSScriptRoot) 'dist\out\MWCoop-0.9.0-prealpha'))
$ErrorActionPreference = 'Stop'
$copies = @(
    @{ Nom = 'MWCoop-JD-Hote';   Pseudo = 'JD';        Profil = 'JD-Hote'; Adresse = '';          Apparence = 'char_shirt21' },
    @{ Nom = 'MWCoop-JD-Invite'; Pseudo = 'JD-Invite'; Profil = 'JD-Invite'; Adresse = '127.0.0.1'; Apparence = 'cop_shirt' }
)
foreach ($c in $copies) {
    $d = Join-Path $Racine $c.Nom
    New-Item -ItemType Directory -Force "$d\MWCoop" | Out-Null
    foreach ($f in 'mywintercar.exe', 'steam_api64.dll', 'changelog.txt') { Copy-Item "$Jeu\$f" $d -Force }
    foreach ($j in 'mywintercar_Data', 'CD1', 'CD2', 'CD3', 'Extra', 'Images', 'Radio') {
        if (-not (Test-Path "$d\$j")) { cmd /c mklink /J "$d\$j" "$Jeu\$j" | Out-Null }
    }
    Set-Content "$d\steam_appid.txt" '4164420' -NoNewline -Encoding ASCII
    Copy-Item "$Mod\version.dll", "$Mod\MWCoop.exe", "$Mod\LISEZMOI.txt", "$Mod\README.txt" $d -Force
    Copy-Item "$Mod\MWCoop\*" "$d\MWCoop" -Recurse -Force
    # Reglages : pseudo, adresse de l'hote pour l'invite ; profil isole (le lanceur n'en donne pas a l'hote).
    @"
[Coop]
Pseudo=$($c.Pseudo)
Adresse=$($c.Adresse)
Port=7870`r`nApparence=$($c.Apparence)

[Test]
Profil=$($c.Profil)
"@ | Set-Content "$d\MWCoop\mwcoop.ini" -Encoding ASCII
    "OK $d"
}
