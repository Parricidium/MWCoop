# Compile MWCoop puis assemble dist\out\MWCoop-<version>.zip (sans aucune donnee du jeu).
# -Publier : cree la release v<Version> sur GitHub (Parricidium/MWCoop) avec le zip. -Notes : texte de la
# release (puces en francais, une ligne ---, les memes puces en anglais : le lanceur les affiche).
param([Parameter(Mandatory = $true)][string]$Version, [switch]$Publier, [string]$Notes = '')
$root = Split-Path $PSScriptRoot
$code = Get-Content "$root\src\MWCoop\Version.cs" -Raw
if ($code -notmatch "Text = `"$([regex]::Escape($Version))`"") { throw "src\MWCoop\Version.cs ne dit pas $Version" }
$out = cmd /c "`"$root\build.cmd`"" 2>&1
if ($LASTEXITCODE -ne 0) { $out | Select-String 'error'; throw "echec de compilation" }
$ErrorActionPreference = 'Stop'   # apres la compilation : vcvars ecrit sur stderr

$stage = "$PSScriptRoot\out\MWCoop-$Version"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force "$stage\MWCoop" | Out-Null
Copy-Item "$root\build\version.dll" $stage
Copy-Item "$root\build\MWCoop.exe" $stage   # lanceur : mises a jour automatiques
Copy-Item "$root\build\MWCoop.dll" "$stage\MWCoop"
Copy-Item "$PSScriptRoot\files\*" $stage -Recurse -Force
Set-Content "$stage\MWCoop\version.txt" $Version -NoNewline -Encoding ASCII

$zip = "$PSScriptRoot\out\MWCoop-$Version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$z = [System.IO.Compression.ZipFile]::Open($zip, 'Create')
foreach ($f in Get-ChildItem $stage -File -Recurse) {
    $rel = $f.FullName.Substring($stage.Length + 1).Replace('\', '/')
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, $f.FullName, $rel) | Out-Null
}
$z.Dispose()
"Ecrit : $zip ($([math]::Round((Get-Item $zip).Length / 1KB)) Ko)"
if ($Publier) {
    if (-not $Notes) { $Notes = "MWCoop $Version" }
    $nf = [System.IO.Path]::GetTempFileName()
    [System.IO.File]::WriteAllText($nf, $Notes, (New-Object System.Text.UTF8Encoding $false))
    # Le tag est cree sur GitHub : le code doit y etre avant (sinon il pointe sur l'ancien main).
    $head = (git -C $root rev-parse HEAD).Trim()
    git -C $root push origin HEAD:main
    if ($LASTEXITCODE -ne 0) { Remove-Item $nf; throw "echec du push de main" }
    gh release create "v$Version" $zip --repo Parricidium/MWCoop --title "MWCoop $Version (pre-alpha)" --notes-file $nf --prerelease --target $head
    $rc = $LASTEXITCODE
    Remove-Item $nf
    if ($rc -ne 0) { throw "echec de la publication GitHub" }
    "Publie : https://github.com/Parricidium/MWCoop/releases/tag/v$Version"
}
