# Copie build\ dans les instances, lance -Players instances hors ecran, attend -Seconds, les ferme
# (sauf -KeepOpen) et affiche la fin des journaux. Chaque instance : profil isole + arriere-plan.
# -Msc : les instances de My Summer Car (run\make-testinstances.ps1 -Msc).
param([int]$Players = 1, [int]$Seconds = 60, [switch]$KeepOpen, [switch]$NoBuild, [switch]$Msc,
      [string]$Racine = '', [int]$Lignes = 25, [string]$Taille = '960x540')
$exe = if ($Msc) { 'mysummercar' } else { 'mywintercar' }
if (-not $Racine) { $Racine = if ($Msc) { 'D:\Games\COOPTEST\My Summer Car' } else { 'D:\Games\COOPTEST\My Winter Car' } }
$root = Split-Path $PSScriptRoot
if (-not $NoBuild) {
    $o = cmd /c "`"$root\run\build-mod.cmd`"" 2>&1
    if ($LASTEXITCODE -ne 0) { $o | Select-String 'error|erreur'; throw 'echec de compilation' }
}
# (seulement les instances de cet essai, Joueur1..N : une autre session fait tourner Joueur3/4 en meme temps -- 22/10 ; jamais les copies de JD, MWCoop-JD-*)
Get-Process $exe -ErrorAction SilentlyContinue | Where-Object { $_.Path -match ('^' + [regex]::Escape("$Racine\MWCoop-Joueur") + '(\d+)\\') -and [int]$Matches[1] -le $Players } | Stop-Process -Force
Start-Sleep -Milliseconds 500
$procs = @()
for ($i = 1; $i -le $Players; $i++) {
    $d = Join-Path $Racine "MWCoop-Joueur$i"
    Copy-Item "$root\build\version.dll" $d -Force
    Copy-Item "$root\build\MWCoop.dll" "$d\MWCoop" -Force
    Copy-Item "$root\build\MWCoopRT.dll" "$d\MWCoop" -Force
    $p = "$d\MWCoop\profils\Joueur$i"
    Remove-Item "$p\logs\*.log" -ErrorAction SilentlyContinue   # (pas logs\sessions : les parties rangees par le chargeur)
    $w, $h = $Taille.Split('x')
    $args = "-screen-fullscreen 0 -screen-width $w -screen-height $h"
    if ($env:MWC_SANSMSC -eq "1") { $args += " -mscloader-disable" }   # (captures sans les fenetres de MSCLoader)
    if ($env:MWC_ARGS) { $args += " " + $env:MWC_ARGS }   # (options d'Unity en plus, ex. -force-gfx-direct pour le ray tracing)
    $procs += Start-Process "$d\$exe.exe" -ArgumentList $args -WorkingDirectory $d -PassThru
    if ($i -lt $Players) { Start-Sleep $(if ($env:MWC_DELAI) { [int]$env:MWC_DELAI } else { 4 }) }   # (MWC_DELAI : arrivee tardive d'un invite)
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
