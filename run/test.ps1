# Copie build\ dans les instances, lance -Players instances hors ecran, attend -Seconds, les ferme
# (sauf -KeepOpen) et affiche la fin des journaux. Chaque instance : profil isole + arriere-plan.
param([int]$Players = 1, [int]$Seconds = 60, [switch]$KeepOpen, [switch]$NoBuild,
      [string]$Racine = 'D:\Games\COOPTEST\My Winter Car', [int]$Lignes = 25, [string]$Taille = '960x540')
$root = Split-Path $PSScriptRoot
if (-not $NoBuild) {
    $o = cmd /c "`"$root\run\build-mod.cmd`"" 2>&1
    if ($LASTEXITCODE -ne 0) { $o | Select-String 'error|erreur'; throw 'echec de compilation' }
}
Get-Process mywintercar -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$Racine\MWCoop-Joueur*" } | Stop-Process -Force   # jamais les copies de JD (MWCoop-JD-*)
Start-Sleep -Milliseconds 500
$procs = @()
for ($i = 1; $i -le $Players; $i++) {
    $d = Join-Path $Racine "MWCoop-Joueur$i"
    Copy-Item "$root\build\version.dll" $d -Force
    Copy-Item "$root\build\MWCoop.dll" "$d\MWCoop" -Force
    $p = "$d\MWCoop\profils\Joueur$i"
    Remove-Item "$p\logs\*" -ErrorAction SilentlyContinue
    $w, $h = $Taille.Split('x')
    $args = "-screen-fullscreen 0 -screen-width $w -screen-height $h"
    $procs += Start-Process "$d\mywintercar.exe" -ArgumentList $args -WorkingDirectory $d -PassThru
    if ($i -lt $Players) { Start-Sleep 4 }
}
$fin = (Get-Date).AddSeconds($Seconds)
while ((Get-Date) -lt $fin -and ($procs | Where-Object { -not $_.HasExited })) { Start-Sleep 1 }
foreach ($pr in $procs) {
    if ($pr.HasExited) { "Joueur : processus termine tout seul (code $($pr.ExitCode))" }
    elseif (-not $KeepOpen) { Stop-Process -Id $pr.Id -Force }
}
for ($i = 1; $i -le $Players; $i++) {
    $p = Join-Path $Racine "MWCoop-Joueur$i\MWCoop\profils\Joueur$i\logs"
    foreach ($f in 'chargeur.log', 'mwcoop.log') {
        "===== Joueur$i $f"
        if (Test-Path "$p\$f") { Get-Content "$p\$f" -Tail $Lignes } else { '(absent)' }
    }
}
