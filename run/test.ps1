# Copie build\ dans les instances, lance -Players instances hors ecran, attend -Seconds, les ferme
# (sauf -KeepOpen) et affiche la fin des journaux. Chaque instance : profil isole + arriere-plan.
# -Msc : les instances de My Summer Car (run\make-testinstances.ps1 -Msc).
# -Premier n : Joueur<n>..Joueur<n+Players-1> (1 par defaut ; ex. -Premier 5 -Players 2 : Joueur5-6, instances de [Addons et Tools]).
param([int]$Players = 1, [int]$Seconds = 60, [switch]$KeepOpen, [switch]$NoBuild, [switch]$Msc,
      [string]$Racine = '', [int]$Lignes = 25, [string]$Taille = '960x540', [int]$Premier = 1)
$exe = if ($Msc) { 'mysummercar' } else { 'mywintercar' }
if (-not $Racine) { $Racine = if ($Msc) { 'D:\Games\COOPTEST\My Summer Car' } else { 'D:\Games\COOPTEST\My Winter Car' } }
$root = Split-Path $PSScriptRoot
$Dernier = $Premier + $Players - 1
if (-not $NoBuild) {
    $o = cmd /c "`"$root\run\build-mod.cmd`"" 2>&1
    if ($LASTEXITCODE -ne 0) { $o | Select-String 'error|erreur'; throw 'echec de compilation' }
}
# (seulement les instances de cet essai, Joueur<Premier>..<Dernier> : une autre session fait tourner Joueur3/4 en meme temps -- 22/10 ; jamais les copies de JD, MWCoop-JD-*)
Get-Process $exe -ErrorAction SilentlyContinue | Where-Object { $_.Path -match ('^' + [regex]::Escape("$Racine\MWCoop-Joueur") + '(\d+)\\') -and [int]$Matches[1] -ge $Premier -and [int]$Matches[1] -le $Dernier } | Stop-Process -Force
Start-Sleep -Milliseconds 500
$procs = @()
for ($i = $Premier; $i -le $Dernier; $i++) {
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
    if ($i -lt $Dernier) { Start-Sleep $(if ($env:MWC_DELAI) { [int]$env:MWC_DELAI } else { 4 }) }   # (MWC_DELAI : arrivee tardive d'un invite)
}
$fin = (Get-Date).AddSeconds($Seconds)
while ((Get-Date) -lt $fin -and ($procs | Where-Object { -not $_.HasExited })) { Start-Sleep 1 }
foreach ($pr in $procs) {
    if ($pr.HasExited) { "Joueur : processus termine tout seul (code $($pr.ExitCode))" }
    elseif (-not $KeepOpen) { Stop-Process -Id $pr.Id -Force }
}
for ($i = $Premier; $i -le $Dernier; $i++) {
    $p = Join-Path $Racine "MWCoop-Joueur$i\MWCoop\profils\Joueur$i\logs"
    foreach ($f in 'chargeur.log', 'mwcoop.log') {
        "===== Joueur$i $f"
        if (Test-Path "$p\$f") { Get-Content "$p\$f" -Tail $Lignes } else { '(absent)' }
    }
}
