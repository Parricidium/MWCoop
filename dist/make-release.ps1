# Compile MWCoop puis assemble le paquet d'un flux dans dist\out (sans aucune donnee du jeu).
# -Jeu (1.15.0, demande de JD du 24/10 : chaque jeu se met a jour a part) :
#   tout    : release globale vX.Y.Z (zip avec tout : lanceur, chargeur, mod ; + editeur et convertisseur). La 1.15.0 est la
#             DERNIERE : ensuite, chaque flux part de 1.15.0 et avance seul.
#   mwc     : tag mwc-X.Y.Z, paquet MWCoop-mwc-X.Y.Z.mwc  (MWCoop\MWCoop.dll, MWCoopRT.dll, version.txt, fichiers du mod)
#   msc     : tag msc-X.Y.Z, paquet MWCoop-msc-X.Y.Z.msc  (le meme mod, publie pour My Summer Car)
#   lanceur : tag lanceur-X.Y.Z, paquet MWCoop-lanceur-X.Y.Z.lanceur (MWCoop.exe, qui porte version.dll ; LISEZMOI, README)
#             + le « pont » vX.Y.N (voir plus bas)
#   outils  : tag outils-X.Y.Z, EditeurCarte-X.Y.Z.editeur et ConvertisseurVoyage-X.Y.Z.convertisseur
# Le numero est pose ici (src\MWCoop\Version.cs pour le mod, launcher\version.txt pour le lanceur), compile, puis commite.
# Les lanceurs d'avant 1.15.0 ne prennent qu'un .zip ou un .nightly : jamais ces extensions pour un flux. Ils ne lisent que
# les 40 dernieres releases : le pont vX.Y.N (zip classique : lanceur courant + mod 1.15.0 fige, version.txt « 1.15.0 »)
# est republie a chaque release du lanceur, et par tout flux quand le dernier pont n'est plus dans les 30 dernieres.
#
# -Publier : verifications (dossier propre, a jour d'origin/main, tag libre), commit du numero pousse sur main AVANT la
# release GitHub (le push sert de verrou : si main a bouge, rien n'est publie), release, puis versions.json commite et
# pousse tout seul. Une release part toujours de main (sauf Nightly). Annoncer la release aux autres sessions avant.
# -Notes : puces en francais, une ligne ---, les memes en anglais, ---, en russe, ---, en arabe (chaque puce commence par le
# jeu). Le lanceur les affiche dans sa langue ; l'annonce Discord prend l'anglais, le russe et l'arabe.
# -AFaire : seulement quand les joueurs ont une manipulation a faire apres le correctif (meme format). Mis en tete des notes
# ("A faire : ..." / "To do: ...") et en encadre "What you need to do" dans l'annonce Discord.
# -Merci : remerciement ajoute a l'annonce Discord (anglais), mentions <@id> comprises (la personne est notifiee).
# -Ping : force la mention du role Update Pings (sinon seulement a la premiere version d'une famille X.Y, par flux).
# -Nightly : version x.y.z-nightly.N, pre-release GitHub, paquet .<flux>-nightly (.nightly pour tout), branche courante
# poussee (pas main), PAS d'annonce Discord ni de pont. Le lanceur ne la propose qu'en canal Nightly.
# -SansPont : -Jeu lanceur sans republier le pont.
# -SansAnnonce : pas d'annonce Discord, tout le reste pareil (release, versions.json, pont). Pour une version qui ne
# concerne que la Golf privee de JD (JD, 31/10 : pas d'annonce Discord quand ca ne concerne que la Golf).
param(
    [Parameter(Mandatory = $true)][ValidateSet('tout', 'mwc', 'msc', 'lanceur', 'outils')][string]$Jeu,
    [Parameter(Mandatory = $true)][string]$Version,
    [switch]$Publier, [string]$Notes = '', [string]$AFaire = '', [string]$Merci = '', [switch]$Nightly, [switch]$Ping, [string]$Image = '', [switch]$SansPont, [switch]$SansAnnonce)
if ($Nightly -ne ($Version -match 'nightly')) { throw "-Nightly va avec une version 'x.y.z-nightly.N' (et seulement elle)" }
if ($Version -notmatch '^\d+\.\d+\.\d+(-nightly\.\d+)?$') { throw "version attendue : X.Y.Z (ou X.Y.Z-nightly.N)" }
if ($SansAnnonce -and ($Ping -or $Merci -or $Image)) { throw "-SansAnnonce : -Ping, -Merci et -Image ne servent qu'a l'annonce Discord" }
$root = Split-Path $PSScriptRoot
$repo = 'Parricidium/MWCoop'
$kLastGlobal = '1.15.0'
$prefix = @{ tout = 'v'; mwc = 'mwc-'; msc = 'msc-'; lanceur = 'lanceur-'; outils = 'outils-' }[$Jeu]
$tag = "$prefix$Version"
$streamName = @{ tout = 'MWCoop'; mwc = 'MWCoop for My Winter Car'; msc = 'MWCoop for My Summer Car'; lanceur = 'MWCoop Launcher'; outils = 'MWCoop Tools' }[$Jeu]
function GitDo([string[]]$a) { $ErrorActionPreference = 'Continue'; $o = & git -C $root @a 2>&1; if ($LASTEXITCODE -ne 0) { throw "git $($a -join ' ') : $o" }; $o }
function CmpVer($a, $b) { $x = [version](($a -split '-')[0]); $y = [version](($b -split '-')[0]); $x.CompareTo($y) }
function Zip($dir, $file) {
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    if (Test-Path $file) { Remove-Item $file -Force }
    $z = [System.IO.Compression.ZipFile]::Open($file, 'Create')
    foreach ($f in Get-ChildItem $dir -File -Recurse) {
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, $f.FullName, $f.FullName.Substring($dir.Length + 1).Replace('\', '/')) | Out-Null
    }
    $z.Dispose()
    "Ecrit : $file ($([math]::Round((Get-Item $file).Length / 1KB)) Ko)"
}
function NewStage($name) { $s = "$PSScriptRoot\out\$name"; if (Test-Path $s) { Remove-Item $s -Recurse -Force }; New-Item -ItemType Directory -Force $s | Out-Null; $s }
if ($Jeu -eq 'tout' -and -not $Nightly -and (CmpVer $Version $kLastGlobal) -gt 0) { throw "-Jeu tout : plus de release globale apres $kLastGlobal (flux mwc / msc / lanceur / outils)" }

# ---- avant tout : la release partira de ce code-la
if ($Publier) {
    if (-not $Nightly) {
        GitDo @('fetch', '-q', 'origin') | Out-Null
        $h = (GitDo @('rev-parse', 'HEAD')).Trim(); $m = (GitDo @('rev-parse', 'origin/main')).Trim()
        if ($h -ne $m) { throw "HEAD n'est pas origin/main a jour : fusionne ta branche dans main (git push origin HEAD:main), puis git merge --ff-only origin/main, et recommence" }
    }
    # Rien de non commite : le tag doit pointer sur le code du paquet (0.26.3 : commit rate, release partie quand meme).
    if (git -C $root status --porcelain --untracked-files=no) { throw "modifications non commitees : committer avant de publier" }
    # (fichiers non suivis ignores, sauf sous src\ et launcher\, qui seraient compiles dans le paquet)
    $nonSuivis = @(git -C $root ls-files --others --exclude-standard -- src launcher)
    if ($nonSuivis.Count -gt 0) { throw "fichiers non suivis compiles dans le paquet : $($nonSuivis -join ', ')" }
    # Lien du bouton Discord (fichier local ignore par git, jamais sur GitHub) : sans lui, le lanceur ouvre GitHub (JD, 29/10 :
    # lanceur-1.16.x compiles depuis un worktree qui ne l'avait pas).
    if ($Jeu -in 'lanceur', 'tout' -and -not (Test-Path "$root\launcher\discord.local.h")) { throw "launcher\discord.local.h absent : le bouton Discord ouvrirait GitHub (copie-le depuis le depot principal)" }
    gh release view $tag --repo $repo 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) { throw "la release $tag existe deja sur GitHub" }
    "Releases GitHub (les 8 dernieres) :"
    gh release list --repo $repo --limit 8
}

# ---- le numero, pose dans les sources (mod : Version.cs ; lanceur : launcher\version.txt)
$verCs = "$root\src\MWCoop\Version.cs"; $verLanc = "$root\launcher\version.txt"
$stamped = @()
$built = $false
$saved = @{}
function Stamp($file, $text) {
    $old = [System.IO.File]::ReadAllText($file)
    if ($old -ne $text) { $saved[$file] = $old; [System.IO.File]::WriteAllText($file, $text, (New-Object System.Text.UTF8Encoding $true)); $script:stamped += $file }
}
$modVer = $Jeu -in 'tout', 'mwc', 'msc'
$lancVer = $Jeu -in 'tout', 'lanceur'
if ($modVer) {
    $code = [System.IO.File]::ReadAllText($verCs)
    if ($code -notmatch 'Text = "[^"]*"') { throw "src\MWCoop\Version.cs : ligne Text = ... introuvable" }
    Stamp $verCs ($code -replace 'Text = "[^"]*"', "Text = `"$Version`"")
}
if ($lancVer) {
    $saved[$verLanc] = [System.IO.File]::ReadAllText($verLanc)
    if ($saved[$verLanc].Trim() -ne $Version) { [System.IO.File]::WriteAllText($verLanc, $Version, (New-Object System.Text.ASCIIEncoding)); $stamped += $verLanc } else { $saved.Remove($verLanc) }
}
function Unstamp { foreach ($f in @($saved.Keys)) { [System.IO.File]::WriteAllText($f, $saved[$f], (New-Object System.Text.UTF8Encoding ($f -eq $verCs))) } }

try {
    $out = cmd /c "`"$root\build.cmd`"" 2>&1
    if ($LASTEXITCODE -ne 0) { $out | Select-String 'error'; throw "echec de compilation" }
    $ErrorActionPreference = 'Stop'   # apres la compilation : vcvars ecrit sur stderr
    $assets = @()
    $hashFiles = @()
    $nightlyExt = if ($Nightly) { '-nightly' } else { '' }

    if ($Jeu -eq 'tout') {
        $stage = NewStage "MWCoop-$Version"
        New-Item -ItemType Directory -Force "$stage\MWCoop" | Out-Null
        Copy-Item "$root\build\version.dll" $stage
        Copy-Item "$root\build\MWCoop.exe" $stage
        Copy-Item "$root\build\MWCoop.dll" "$stage\MWCoop"
        Copy-Item "$root\build\MWCoopRT.dll" "$stage\MWCoop"   # ray tracing (DXR), charge par le mod
        Copy-Item "$PSScriptRoot\files\*" $stage -Recurse -Force
        Set-Content "$stage\MWCoop\version.txt" $Version -NoNewline -Encoding ASCII
        $zip = "$PSScriptRoot\out\MWCoop-$Version.zip"
        Zip $stage $zip
        if ($Nightly) { $nz = "$PSScriptRoot\out\MWCoop-$Version.nightly"; Copy-Item $zip $nz -Force; Remove-Item $zip; $zip = $nz }
        $assets += $zip
        $hashFiles += @($zip, "$stage\MWCoop.exe", "$stage\version.dll", "$stage\MWCoop\MWCoop.dll", "$stage\MWCoop\MWCoopRT.dll")
    }
    elseif ($Jeu -in 'mwc', 'msc') {
        # (sans version.dll : le lanceur pose la sienne, LoaderEnsure ; sans MWCoop.exe : flux lanceur-)
        $stage = NewStage "MWCoop-$Jeu-$Version"
        New-Item -ItemType Directory -Force "$stage\MWCoop" | Out-Null
        Copy-Item "$root\build\MWCoop.dll" "$stage\MWCoop"
        Copy-Item "$root\build\MWCoopRT.dll" "$stage\MWCoop"
        Copy-Item "$PSScriptRoot\files\MWCoop\*" "$stage\MWCoop" -Recurse -Force
        Set-Content "$stage\MWCoop\version.txt" $Version -NoNewline -Encoding ASCII
        $pkg = "$PSScriptRoot\out\MWCoop-$Jeu-$Version.$Jeu$nightlyExt"
        Zip $stage $pkg
        $assets += $pkg
        $hashFiles += @($pkg, "$stage\MWCoop\MWCoop.dll", "$stage\MWCoop\MWCoopRT.dll")
    }
    elseif ($Jeu -eq 'lanceur') {
        $stage = NewStage "MWCoop-lanceur-$Version"
        Copy-Item "$root\build\MWCoop.exe" $stage
        foreach ($t in 'LISEZMOI.txt', 'README.txt') { Copy-Item "$PSScriptRoot\files\$t" $stage }
        $pkg = "$PSScriptRoot\out\MWCoop-lanceur-$Version.lanceur$nightlyExt"
        Zip $stage $pkg
        $assets += $pkg
        $hashFiles += @($pkg, "$stage\MWCoop.exe")
    }
    if ($Jeu -in 'tout', 'outils') {
        # Editeur de carte (editor\, demande de JD du 20/10) : EditeurCarte-<version>.editeur (un zip sous un autre nom). Le
        # lanceur (page Outils) le telecharge a la demande dans %LOCALAPPDATA%\MWCoop\editeur.
        $edStage = NewStage "EditeurCarte-$Version"
        foreach ($f in 'EditeurCarte.exe', 'EditeurCarte.exe.config', 'Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.WinForms.dll', 'WebView2Loader.dll') { Copy-Item "$root\build\editeur\$f" $edStage }
        Copy-Item "$root\build\editeur\ui" $edStage -Recurse
        Set-Content "$edStage\version.txt" $Version -NoNewline -Encoding ASCII
        $edPack = "$PSScriptRoot\out\EditeurCarte-$Version.editeur"
        Zip $edStage $edPack
        # Convertisseur de circuits (mods\Voyage\convertisseur, demande de JD du 24/10, « Early ») : circuits d'Assetto Corsa ->
        # destinations du mod Voyage ; ConvertisseurVoyage-<version>.convertisseur avec le mod (mod\Voyage.dll). Le lanceur
        # (page Outils) le telecharge dans %LOCALAPPDATA%\MWCoop\voyage.
        $ErrorActionPreference = 'Continue'
        $vo = cmd /c "`"$root\mods\Voyage\build.cmd`"" 2>&1
        if ($LASTEXITCODE -ne 0) { $vo | Select-Object -Last 5; throw "echec de compilation du mod Voyage" }
        $vo = dotnet build -c Release -v q -nologo "$root\mods\Voyage\convertisseur\Convertisseur.csproj" 2>&1
        if ($LASTEXITCODE -ne 0) { $vo | Select-String 'error'; throw "echec de compilation du convertisseur" }
        $ErrorActionPreference = 'Stop'
        $cvBuild = "$root\mods\Voyage\build\convertisseur"
        $cvStage = NewStage "ConvertisseurVoyage-$Version"
        New-Item -ItemType Directory -Force "$cvStage\mod" | Out-Null
        foreach ($f in 'ConvertisseurVoyage.exe', 'ConvertisseurVoyage.exe.config', 'Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.WinForms.dll', 'WebView2Loader.dll') { Copy-Item "$cvBuild\$f" $cvStage }
        Copy-Item "$cvBuild\ui" $cvStage -Recurse
        Copy-Item "$root\mods\Voyage\build\Voyage.dll" "$cvStage\mod"
        Set-Content "$cvStage\version.txt" $Version -NoNewline -Encoding ASCII
        $cvPack = "$PSScriptRoot\out\ConvertisseurVoyage-$Version.convertisseur"
        Zip $cvStage $cvPack
        $assets += @($edPack, $cvPack)
        $hashFiles += @($edPack, $cvPack)
        # Outil des places, pour les personnes choisies par JD (spec de la cle, 26/10) : paquet CHIFFRE outil-places-<ver>.mwcp et son
        # manifeste signe outil-places.json, faits sur le PC de JD (outils-prives/places/paquet.ps1, puis chiffrer-outil de
        # outils-prives/cles) ; joints s'ils sont la (le lanceur ne lit que la derniere release outils-). Jamais l'outil en clair.
        $opDir = "$root\outils-prives\places\paquet"
        $opMan = "$opDir\outil-places.json"
        $opPack = Get-ChildItem "$opDir\outil-places-*.mwcp" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($opPack -and (Test-Path $opMan)) {
            $assets += @($opPack.FullName, $opMan)
            $hashFiles += @($opPack.FullName)
            Write-Host "outil des places joint : $($opPack.Name) + outil-places.json"
        }
        else { Write-Host "outil des places : pas de paquet chiffre ni de manifeste dans $opDir (non joint)" }
    }
    # Empreintes SHA-256 publiees a cote des paquets : chacun peut verifier ses fichiers (et les chercher sur VirusTotal)
    # quand un antivirus a apprentissage automatique s'inquiete d'un exe non signe.
    $sums = "$PSScriptRoot\out\SHA256SUMS-$($tag).txt"
    $lines = foreach ($f in $hashFiles) {
        $h = (Get-FileHash $f -Algorithm SHA256).Hash.ToLower()
        $n = if ($assets -contains $f) { Split-Path $f -Leaf } else { ($f -replace '^.*\\out\\[^\\]+\\', '').Replace('\', '/') }
        "$h  $n"
    }
    [System.IO.File]::WriteAllText($sums, ($lines -join "`r`n") + "`r`n", (New-Object System.Text.UTF8Encoding $false))
    $assets += $sums
    $lines
    $built = $true
}
finally {
    # (essai sans -Publier, ou echec avant la publication : le numero remis comme avant)
    if (-not $Publier -or -not $built) { Unstamp; if ($stamped) { "Numero remis comme avant dans : $($stamped -join ', ')" } }
}
# ---- pont pour les anciens lanceurs : zip classique vX.Y.N = lanceur courant + mod de 1.15.0 (version.txt « 1.15.0 »).
# -Essai : le zip seulement, dans dist\out (essai sans -Publier de -Jeu lanceur : a installer avec un vieux lanceur).
function Publish-Pont($exe, $lancVersion, [switch]$Essai) {
    $base = "$PSScriptRoot\out\pont-base"
    if (Test-Path $base) { Remove-Item $base -Recurse -Force }
    New-Item -ItemType Directory -Force $base | Out-Null
    gh release download "v$kLastGlobal" --repo $repo --pattern "MWCoop-$kLastGlobal.zip" --dir $base
    if ($LASTEXITCODE -ne 0) { Write-Warning "pont : zip v$kLastGlobal introuvable, pas de pont"; return }
    $tags = @(gh release list --repo $repo --limit 200 --json tagName --jq '.[].tagName')
    $n = 0
    foreach ($t in $tags) { if ($t -match "^v$([regex]::Escape(($kLastGlobal -split '\.')[0..1] -join '.'))\.(\d+)$") { $n = [math]::Max($n, [int]$Matches[1]) } }
    $xy = ($kLastGlobal -split '\.')[0..1] -join '.'
    $pv = "$xy.$($n + 1)"
    $stage = NewStage "MWCoop-$pv"
    [System.IO.Compression.ZipFile]::ExtractToDirectory("$base\MWCoop-$kLastGlobal.zip", $stage)
    Copy-Item $exe "$stage\MWCoop.exe" -Force
    Set-Content "$stage\MWCoop\version.txt" $kLastGlobal -NoNewline -Encoding ASCII   # (toujours 1.15.0 : le nouveau lanceur reprend ensuite le flux du jeu)
    $pz = "$PSScriptRoot\out\MWCoop-$pv.zip"
    Zip $stage $pz
    # (empreintes, comme les autres releases ; les anciens lanceurs ne prennent que le premier .zip, le .txt ne les gene pas)
    $ps = "$PSScriptRoot\out\SHA256SUMS-v$pv.txt"
    $pl = foreach ($f in @($pz, "$stage\MWCoop.exe", "$stage\version.dll", "$stage\MWCoop\MWCoop.dll", "$stage\MWCoop\MWCoopRT.dll")) {
        "$((Get-FileHash $f -Algorithm SHA256).Hash.ToLower())  $(if ($f -eq $pz) { Split-Path $f -Leaf } else { $f.Substring($stage.Length + 1).Replace('\', '/') })"
    }
    [System.IO.File]::WriteAllText($ps, ($pl -join "`r`n") + "`r`n", (New-Object System.Text.UTF8Encoding $false))
    if ($Essai) { "Pont d'essai : $pz (v$pv, lanceur $lancVersion, mod $kLastGlobal) + $(Split-Path $ps -Leaf) -- pas publie"; return }
    $pn = "- MWC et MSC : nouveau lanceur $lancVersion. Les mises à jour de My Winter Car, de My Summer Car et du lanceur sont maintenant séparées : mets à jour, puis le lanceur propose la version de ton jeu.`n---`n" +
          "- MWC and MSC: new launcher $lancVersion. My Winter Car, My Summer Car and launcher updates are now separate: update, then the launcher offers your game's version.`n---`n" +
          "- MWC и MSC: новый лаунчер $lancVersion. Обновления My Winter Car, My Summer Car и лаунчера теперь отдельные: обновись, затем лаунчер предложит версию твоей игры.`n---`n" +
          "- MWC وMSC: مشغّل جديد $lancVersion. أصبحت تحديثات My Winter Car وMy Summer Car والمشغّل منفصلة: حدّث، ثم يعرض المشغّل إصدار لعبتك."
    $nf = [System.IO.Path]::GetTempFileName()
    [System.IO.File]::WriteAllText($nf, $pn, (New-Object System.Text.UTF8Encoding $false))
    gh release create "v$pv" $pz $ps --repo $repo --title "MWCoop $pv (launcher $lancVersion)" --notes-file $nf --target main --latest
    $rc = $LASTEXITCODE
    Remove-Item $nf
    if ($rc -ne 0) { Write-Warning "pont v$pv : echec de la publication" } else { "Pont publie : v$pv (lanceur $lancVersion)" }
}
if (-not $Publier) {
    if ($SansAnnonce) { "Annonce Discord : sautee (-SansAnnonce)" }
    if ($Jeu -eq 'lanceur' -and -not $Nightly -and -not $SansPont) { Publish-Pont "$root\build\MWCoop.exe" $Version -Essai }
    return
}
$Notes = if ($Notes) { $Notes } else { "$streamName $Version" }
# " --- " au milieu d'une ligne vaut une ligne --- (0.63.0 : tout etait reste colle, francais et anglais, dans les
# notes du lanceur comme dans l'annonce Discord, qui ne doit avoir que l'anglais)
$Notes = $Notes -replace '[ \t]+---+[ \t]+', "`n---`n"
$AFaire = $AFaire -replace '[ \t]+---+[ \t]+', "`n---`n"
$todoEn = ''
if ($AFaire.Trim()) {
    # (parties : francais, anglais, russe, arabe ; une partie absente prend l'anglais)
    $tp = @($AFaire -split '(?m)^\s*---+\s*$')
    $np = @($Notes -split '(?m)^\s*---+\s*$')
    $tEn = if ($tp.Count -ge 2) { $tp[1] } else { $tp[0] }; $tRu = if ($tp.Count -ge 3) { $tp[2] } else { $tEn }; $tAr = if ($tp.Count -ge 4) { $tp[3] } else { $tEn }
    $lines = { param($t, $pre) (@($t -split "`r?`n" | ForEach-Object { $_.Trim() -replace '^- *', '' } | Where-Object { $_ }) | ForEach-Object { "- $pre$_" }) -join "`n" }
    $fr = & $lines $tp[0] ("$([char]0xC0) faire : ")
    $todoEn = (@($tEn -split "`r?`n" | ForEach-Object { $_.Trim() -replace '^- *', '' } | Where-Object { $_ }) | ForEach-Object { "- $_" }) -join "`n"
    $todoRu = (@($tRu -split "`r?`n" | ForEach-Object { $_.Trim() -replace '^- *', '' } | Where-Object { $_ }) | ForEach-Object { "- $_" }) -join "`n"
    $en = & $lines $tEn 'To do: '
    $ru = & $lines $tRu ("$([char]0x041D)$([char]0x0443)$([char]0x0436)$([char]0x043D)$([char]0x043E): ")
    $todoAr = (@($tAr -split "`r?`n" | ForEach-Object { $_.Trim() -replace '^- *', '' } | Where-Object { $_ }) | ForEach-Object { "- $_" }) -join "`n"
    $ar = & $lines $tAr ("$([char]0x0645)$([char]0x0637)$([char]0x0644)$([char]0x0648)$([char]0x0628): ")
    if ($np.Count -ge 4) { $Notes = "$fr`n$($np[0].Trim())`n---`n$en`n$($np[1].Trim())`n---`n$ru`n$($np[2].Trim())`n---`n$ar`n$($np[3].Trim())" }
    elseif ($np.Count -ge 3) { $Notes = "$fr`n$($np[0].Trim())`n---`n$en`n$($np[1].Trim())`n---`n$ru`n$($np[2].Trim())" }
    elseif ($np.Count -ge 2) { $Notes = "$fr`n$($np[0].Trim())`n---`n$en`n$($np[1].Trim())" } else { $Notes = "$fr`n$en`n$($Notes.Trim())" }
}

# ---- le verrou : commit du numero pousse AVANT la release (si main a bouge, le push est refuse et rien n'est publie)
$branch = if ($Nightly) { (git -C $root rev-parse --abbrev-ref HEAD).Trim() } else { 'main' }
if ($Nightly -and $branch -eq 'main') { throw "une Nightly se publie depuis sa branche, pas main" }
if ($stamped) {
    GitDo (@('add', '--') + $stamped) | Out-Null
    GitDo (@('commit', '-q', '-m', "$tag : numero de version", '--') + $stamped) | Out-Null
}
$po = cmd /c "git -C `"$root`" push origin HEAD:$branch 2>&1"   # par cmd : git ecrit sur stderr, ce qui arreterait le script
if ($LASTEXITCODE -ne 0) {
    if ($stamped) { GitDo @('reset', '-q', '--keep', 'HEAD~1') | Out-Null }
    throw "push de $branch refuse ($po) : main a bouge (une autre release ?). git fetch, git merge --ff-only origin/main, et recommence -- rien n'a ete publie"
}
$head = (GitDo @('rev-parse', 'HEAD')).Trim()
$nf = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($nf, $Notes, (New-Object System.Text.UTF8Encoding $false))
$title = "$streamName $Version" + $(if ($Nightly) { ' (Nightly)' } else { '' })
# (Latest de GitHub : seulement les releases completes -- globale, pont -- que les nouveaux joueurs telechargent)
$latest = if ($Jeu -eq 'tout' -and -not $Nightly) { '--latest' } else { '--latest=false' }
if ($Nightly) { gh release create $tag @assets --repo $repo --title $title --notes-file $nf --target $head --prerelease $latest }
else { gh release create $tag @assets --repo $repo --title $title --notes-file $nf --target $head $latest }
$rc = $LASTEXITCODE
Remove-Item $nf
if ($rc -ne 0) { throw "echec de la publication GitHub" }
$url = "https://github.com/$repo/releases/tag/$tag"
"Publie : $url"

# ---- pont : a chaque release du lanceur ; et par tout flux quand le dernier pont n'est plus dans les 30 dernieres
if (-not $Nightly -and $Jeu -ne 'tout') {
    $recent = @(gh release list --repo $repo --limit 30 --json tagName --jq '.[].tagName')
    $pontVu = @($recent | Where-Object { $_ -match '^v\d+\.\d+\.\d+$' }).Count -gt 0
    if ($Jeu -eq 'lanceur' -and -not $SansPont) { Publish-Pont "$root\build\MWCoop.exe" $Version }
    elseif (-not $pontVu) {
        # (le lanceur publie : celui de la derniere release lanceur-, sinon rien -- jamais un lanceur pas publie)
        $lt = @(gh release list --repo $repo --limit 200 --json tagName,isPrerelease --jq '.[] | select(.isPrerelease | not) | .tagName' | Where-Object { $_ -like 'lanceur-*' }) | Select-Object -First 1
        if ($lt) {
            $ld = "$PSScriptRoot\out\pont-lanceur"
            if (Test-Path $ld) { Remove-Item $ld -Recurse -Force }
            New-Item -ItemType Directory -Force $ld | Out-Null
            gh release download $lt --repo $repo --pattern '*.lanceur' --dir $ld
            $lp = Get-ChildItem $ld -Filter '*.lanceur' | Select-Object -First 1
            if ($lp) {
                [System.IO.Compression.ZipFile]::ExtractToDirectory($lp.FullName, "$ld\x")
                Publish-Pont "$ld\x\MWCoop.exe" ($lt -replace '^lanceur-', '')
            }
        } else { Write-Warning "pont : aucune release lanceur- encore, rien a republier" }
    }
}

# ---- versions.json : la liste des releases (meme forme que l'API) lue par le lanceur quand l'API de GitHub est injoignable
# ou saturee (joueurs russes, 21/10). Commite et pousse ici, trois essais si main bouge entre-temps.
if (-not $Nightly) {
    $vj = "$PSScriptRoot\versions.json"
    $pushed = $false
    for ($k = 0; $k -lt 3 -and -not $pushed; $k++) {
        try {
            GitDo @('fetch', '-q', 'origin') | Out-Null
            GitDo @('merge', '-q', '--ff-only', 'origin/main') | Out-Null
            gh api "repos/$repo/releases?per_page=100" | Out-File -FilePath $vj -Encoding utf8
            if (-not (git -C $root status --porcelain -- $vj)) { $pushed = $true; break }
            GitDo @('add', '--', $vj) | Out-Null
            GitDo @('commit', '-q', '-m', "versions.json $tag", '--', $vj) | Out-Null
            $po = cmd /c "git -C `"$root`" push origin HEAD:main 2>&1"
            if ($LASTEXITCODE -eq 0) { $pushed = $true; "versions.json : $tag en tete, pousse" }
            else { GitDo @('reset', '-q', '--keep', 'HEAD~1') | Out-Null; Start-Sleep -Seconds 2 }
        } catch { Write-Warning "versions.json : $_"; Start-Sleep -Seconds 2 }
    }
    if (-not $pushed) { Write-Warning "versions.json PAS pousse : le faire a la main (git push origin HEAD:main)" }
}

# ---- annonce Discord (#announcements) si discord-webhook.txt existe (hors depot, ecrit par MWCoopDiscord\setup_server.py) :
# ligne 1 = webhook, ligne 2 = role Update Pings. Cherche dans dist\, puis dans le dist\ du dossier principal (worktrees).
$wf = "$PSScriptRoot\discord-webhook.txt"
if (-not (Test-Path $wf)) {
    $common = (git -C $root rev-parse --path-format=absolute --git-common-dir 2>$null)
    if ($common) { $wf2 = Join-Path (Split-Path $common.Trim()) 'dist\discord-webhook.txt'; if (Test-Path $wf2) { $wf = $wf2 } }
}
if ($Nightly) { "Nightly : pas d'annonce Discord" }
elseif ($SansAnnonce) { "Annonce Discord : sautee (-SansAnnonce)" }
elseif (Test-Path $wf) {
    try {
        $w = @(Get-Content $wf -Encoding UTF8 | Where-Object { $_.Trim() })
        $parts = @($Notes -split '(?m)^\s*---+\s*$')
        $en = $(if ($parts.Count -ge 2) { $parts[1] } else { $parts[0] }).Trim()
        $ruN = $(if ($parts.Count -ge 3) { $parts[2] } else { '' }).Trim()
        $arN = $(if ($parts.Count -ge 4) { $parts[3] } else { '' }).Trim()
        if ($todoEn) {   # (dans l'encadre)
            $en = (@($en -split "`n") | Where-Object { $_ -notmatch '^- To do: ' }) -join "`n"
            $ruN = (@($ruN -split "`n") | Where-Object { $_ -notmatch "^- $([char]0x041D)$([char]0x0443)$([char]0x0436)$([char]0x043D)$([char]0x043E): " }) -join "`n"
            $arN = (@($arN -split "`n") | Where-Object { $_ -notmatch "^- $([char]0x0645)$([char]0x0637)$([char]0x0644)$([char]0x0648)$([char]0x0628): " }) -join "`n"
        }
        # Notes longues : chaque langue dans son propre message a la suite (russe, arabe), elle-meme decoupee par lignes si besoin.
        $chunks = { param($t) $acc = @(); $cur = ''; foreach ($l in @($t -split "`n")) { if (($cur.Length + $l.Length + 1) -gt 3900 -and $cur) { $acc += $cur; $cur = '' }; $cur = if ($cur) { "$cur`n$l" } else { $l } }; if ($cur) { $acc += $cur }; $acc }
        $ruTitle = "**$([char]0x0420)$([char]0x0443)$([char]0x0441)$([char]0x0441)$([char]0x043A)$([char]0x0438)$([char]0x0439)**"   # (Russkiy)
        $arTitle = "**$([char]0x0627)$([char]0x0644)$([char]0x0639)$([char]0x0631)$([char]0x0628)$([char]0x064A)$([char]0x0629)**"   # (al-arabiya)
        $desc = $en
        $follow = @()   # messages a la suite : @(titre, texte)
        if ($ruN) { if (($desc.Length + $ruN.Length + 20) -le 3900) { $desc = "$en`n`n$ruTitle`n$ruN" } else { $follow += , @($ruTitle, $ruN) } }
        if ($arN) { if (-not $follow -and ($desc.Length + $arN.Length + 20) -le 3900) { $desc += "`n`n$arTitle`n$arN" } else { $follow += , @($arTitle, $arN) } }
        $enParts = @(& $chunks $desc)
        $desc = $enParts[0]
        for ($k = 1; $k -lt $enParts.Count; $k++) { $follow = @(, @('', $enParts[$k])) + $follow }
        $embed = @{
            title = "$streamName $Version"; url = $url; color = 7912959; description = $desc
            footer = @{ text = 'Update from the launcher: NEW VERSION - UPDATE' }
        }
        # -Image <url> : grande image sous le texte de l'annonce (1.11.0, magasin CARDINAL : JD, 23/10)
        if ($Image) { $embed.image = @{ url = $Image } }
        # Role Update Pings : seulement a la premiere version d'une famille X.Y, par flux (mwc-1.16.0, msc-1.16.0, lanceur-1.16.0...),
        # pas aux X.Y.1, X.Y.2... (JD, 21/10 et 24/10) ; -Ping force la mention. Famille connue par les releases du flux deja publiees.
        # (les pre-versions ne comptent pas : la 1.10.0 etait partie sans mention a cause de sa nightly, 22/10)
        $xy = (($Version -split '\.')[0..1]) -join '.'
        $family = @(gh release list --repo $repo --limit 200 --json tagName --jq '.[].tagName' | Where-Object { $_ -like "$prefix$xy.*" -and $_ -ne $tag -and $_ -notmatch 'nightly' })
        $big = $Ping -or ($family.Count -eq 0)
        "Annonce Discord : " + $(if ($big) { "avec @Update Pings (premiere version $prefix$xy)" } else { "sans mention (famille $prefix$xy deja annoncee : $($family -join ', '))" })
        $head = $(if ($big) { "<@&$($w[1].Trim())> " } else { '' }) + "**$streamName $Version is out!**"
        if ($todoEn) {   # (consignes : encadre a part, et un mot dans le message)
            if ($todoEn.Length -gt 1000) { $todoEn = $todoEn.Substring(0, 1000) + '...' }
            $embed.fields = @(@{ name = "$([char]0x26A0)$([char]0xFE0F) What you need to do"; value = $todoEn })
            if ($ruN -and $todoRu -and $todoRu -ne $todoEn) {
                if ($todoRu.Length -gt 1000) { $todoRu = $todoRu.Substring(0, 1000) + '...' }
                $embed.fields += @{ name = "$([char]0x26A0)$([char]0xFE0F) $([char]0x0427)$([char]0x0442)$([char]0x043E) $([char]0x043D)$([char]0x0443)$([char]0x0436)$([char]0x043D)$([char]0x043E) $([char]0x0441)$([char]0x0434)$([char]0x0435)$([char]0x043B)$([char]0x0430)$([char]0x0442)$([char]0x044C)"; value = $todoRu }
            }
            if ($arN -and $todoAr -and $todoAr -ne $todoEn) {
                if ($todoAr.Length -gt 1000) { $todoAr = $todoAr.Substring(0, 1000) + '...' }
                $embed.fields += @{ name = "$([char]0x26A0)$([char]0xFE0F) $([char]0x0645)$([char]0x0627) $([char]0x064A)$([char]0x062C)$([char]0x0628) $([char]0x0641)$([char]0x0639)$([char]0x0644)$([char]0x0647)"; value = $todoAr }
            }
            $head += ' **Action needed after updating: see below.**'
        }
        $users = @()
        if ($Merci.Trim()) {
            $head += "`n$($Merci.Trim())"
            $users = @([regex]::Matches($Merci, '<@!?(\d+)>') | ForEach-Object { $_.Groups[1].Value })
        }
        $msg = @{
            content          = $head
            allowed_mentions = @{ roles = @($w[1].Trim()); users = $users }
            embeds           = @($embed)
        }
        $body = [System.Text.Encoding]::UTF8.GetBytes(($msg | ConvertTo-Json -Depth 6))
        Invoke-RestMethod -Method Post -Uri $w[0].Trim() -Body $body -ContentType 'application/json; charset=utf-8' | Out-Null
        foreach ($f in $follow) {   # (suite : un message par langue, sans mention)
            foreach ($part in @(& $chunks $f[1])) {
                $d2 = if ($f[0]) { "$($f[0])`n$part" } else { $part }
                $m2 = @{ allowed_mentions = @{ parse = @() }; embeds = @(@{ title = "$streamName $Version"; url = $url; color = 7912959; description = $d2 }) }
                Start-Sleep -Milliseconds 800
                Invoke-RestMethod -Method Post -Uri $w[0].Trim() -Body ([System.Text.Encoding]::UTF8.GetBytes(($m2 | ConvertTo-Json -Depth 6))) -ContentType 'application/json; charset=utf-8' | Out-Null
            }
        }
        "Annonce Discord envoyee" + $(if ($follow) { " (+ $($follow.Count) suite(s))" })
    } catch { Write-Warning "annonce Discord ratee : $_" }
}
else { Write-Warning "pas de discord-webhook.txt (dist\ ni dossier principal) : pas d'annonce" }
"Rappel : dire a [Bugs dispatcher] quels bugs partent dans $tag, et a [Directives] que la release est faite."
