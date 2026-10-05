# Compose les fonds du lanceur (launcher\launcher.png et launcher-sombre.png, 1000x620 en double resolution, avec
# transparence ; integres a MWCoop.exe par launcher.rc) et son icone (launcher\mwcoop.ico) : carte arrondie + ombre
# douce, nuit d'hiver en Finlande dessinee ici (ciel, collines, sapins, maison en bois eclairee, vieille voiture
# sous la neige), panneau depoli a gauche, logo qui depasse de la carte.
# Ce qui bouge (neige qui tombe, fumee de la cheminee et du pot, fenetres, etoiles) est dessine par le lanceur
# par-dessus ce fond (launcher.cpp, DrawScene) : memes coordonnees.
# Aucune image du jeu, aucun logo d'Amistech. Accents bleu glacier / blanc (pas de vert).
# Le lanceur dessine ses textes et boutons par-dessus (coordonnees fixes, voir launcher.cpp).
# Logo : launcher\logo.png s'il existe (logo en couleurs sur fond transparent, dessine tel quel avec une ombre douce) ;
# sinon un logo provisoire (texte) est dessine.
Add-Type -AssemblyName System.Drawing
$W = 1000; $H = 620
$S = 2   # fonds en double resolution (ecrans a 150-200 %) ; le lanceur les dessine en 1000x620
$card = New-Object System.Drawing.RectangleF 20, 60, 960, 540
$panel = New-Object System.Drawing.RectangleF 48, 88, 360, 500

function C($a, $r, $g, $b) { [System.Drawing.Color]::FromArgb([int]$a, [int]$r, [int]$g, [int]$b) }
function P([float]$x, [float]$y) { New-Object System.Drawing.PointF $x, $y }
function Brush($c) { New-Object System.Drawing.SolidBrush $c }

function AlphaBox($img) {
    $b = New-Object System.Drawing.Bitmap $img
    $x0 = $b.Width; $y0 = $b.Height; $x1 = -1; $y1 = -1
    for ($y = 0; $y -lt $b.Height; $y += 2) { for ($x = 0; $x -lt $b.Width; $x += 2) {
        if ($b.GetPixel($x, $y).A -gt 8) { if ($x -lt $x0) { $x0 = $x }; if ($x -gt $x1) { $x1 = $x }; if ($y -lt $y0) { $y0 = $y }; if ($y -gt $y1) { $y1 = $y } }
    } }
    $b.Dispose()
    return New-Object System.Drawing.RectangleF ($x0 - 2), ($y0 - 2), ($x1 - $x0 + 5), ($y1 - $y0 + 5)
}

function RoundPath([System.Drawing.RectangleF]$r, [float]$rad) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $rad * 2
    $p.AddArc($r.X, $r.Y, $d, $d, 180, 90)
    $p.AddArc($r.Right - $d, $r.Y, $d, $d, 270, 90)
    $p.AddArc($r.Right - $d, $r.Bottom - $d, $d, $d, 0, 90)
    $p.AddArc($r.X, $r.Bottom - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

# --- Logo provisoire ("MW" / "COOP", blanc vers bleu glacier, contour bleu nuit), si launcher\logo.png manque ---
$logoFile = Join-Path $PSScriptRoot 'logo.png'
$provisoire = -not (Test-Path $logoFile)
if (-not $provisoire) { $logo = [System.Drawing.Image]::FromFile($logoFile) }
else {
    $logo = New-Object System.Drawing.Bitmap 600, 520, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $lg = [System.Drawing.Graphics]::FromImage($logo)
    $lg.SmoothingMode = 'AntiAlias'; $lg.Clear([System.Drawing.Color]::Transparent)
    $fam = New-Object System.Drawing.FontFamily 'Arial Black'
    $sf = New-Object System.Drawing.StringFormat; $sf.Alignment = 'Center'
    foreach ($t in @(@('MW', 250, 10), @('COOP', 150, 290))) {
        $p = New-Object System.Drawing.Drawing2D.GraphicsPath
        $p.AddString($t[0], $fam, 0, $t[1], (New-Object System.Drawing.RectangleF 0, $t[2], 600, 300), $sf)
        $pen = New-Object System.Drawing.Pen (C 255 10 20 38), 22
        $pen.LineJoin = 'Round'
        $lg.DrawPath($pen, $p)
        $gr = New-Object System.Drawing.Drawing2D.LinearGradientBrush (P 0 ($t[2] + $t[1] * 0.2)), (P 0 ($t[2] + $t[1] * 1.2)), (C 255 255 255 255), (C 255 150 205 250)
        $lg.FillPath($gr, $p)
    }
    $lg.Dispose()
    "logo provisoire (deposer le vrai dans launcher\logo.png puis relancer ce script)"
}

# --- Paysage : nuit d'hiver (sombre) ou matin d'hiver (clair) ---
function Spruce($g, [float]$x, [float]$base, [float]$h, $ink, $snow) {
    # sapin : etages de triangles, neige sur le haut de chaque etage
    $g.FillRectangle($ink, $x - $h * 0.03, $base - $h * 0.12, $h * 0.06, $h * 0.14)
    $tiers = 5
    for ($i = 0; $i -lt $tiers; $i++) {
        $k = $i / ($tiers - 1)
        $top = $base - $h + $h * 0.17 * $i
        $bot = $top + $h * 0.34
        $half = $h * (0.12 + 0.19 * $k)
        $tri = @((P $x $top), (P ($x + $half) $bot), (P ($x + $half * 0.35) ($bot - $h * 0.03)), (P $x $bot), (P ($x - $half * 0.35) ($bot - $h * 0.03)), (P ($x - $half) $bot))
        $g.FillPolygon($ink, $tri)
        $cap = @((P $x $top), (P ($x + $half * 0.55) ($top + $h * 0.19)), (P ($x + $half * 0.15) ($top + $h * 0.15)), (P ($x - $half * 0.2) ($top + $h * 0.2)), (P ($x - $half * 0.5) ($top + $h * 0.17)))
        $g.FillPolygon($snow, $cap)
    }
}

function DrawWinter($g, [bool]$dark) {
    $rnd = New-Object System.Random 1990
    if ($dark) {
        $skyA = C 255 6 12 28; $skyB = C 255 34 56 96
        $far = C 255 30 46 78; $mid = C 255 20 32 56
        $snowA = C 255 74 96 136; $snowB = C 255 40 56 88
        $tree = C 255 8 14 28; $treeFar = C 255 22 34 58; $treeSnow = C 200 150 175 215
        $wall = C 255 96 38 32; $wallDark = C 255 62 24 22; $trim = C 255 200 210 225
        $roofSnow = C 255 176 196 226; $win = C 255 255 196 110
    } else {
        $skyA = C 255 150 190 232; $skyB = C 255 236 243 251
        $far = C 255 186 205 228; $mid = C 255 160 182 210
        $snowA = C 255 252 253 255; $snowB = C 255 214 226 242
        $tree = C 255 40 60 86; $treeFar = C 255 120 146 178; $treeSnow = C 235 250 252 255
        $wall = C 255 168 64 50; $wallDark = C 255 128 46 38; $trim = C 255 250 250 250
        $roofSnow = C 255 252 253 255; $win = C 255 255 214 140
    }
    $sky = New-Object System.Drawing.Drawing2D.LinearGradientBrush (P 0 60), (P 0 440), $skyA, $skyB
    $g.FillRectangle($sky, 20, 60, 960, 400)

    if ($dark) {
        # etoiles
        for ($i = 0; $i -lt 260; $i++) {
            $x = 20 + $rnd.NextDouble() * 960; $y = 62 + [math]::Pow($rnd.NextDouble(), 1.6) * 300
            $a = 60 + $rnd.Next(0, 170); $s = 0.6 + $rnd.NextDouble() * 1.3
            $g.FillEllipse((Brush (C $a 220 232 255)), $x, $y, $s, $s)
        }
        # lune et halo
        for ($i = 12; $i -ge 1; $i--) {
            $r = 22 + $i * 9
            $g.FillEllipse((Brush (C ([int](12 - $i) + 3) 190 210 245)), 905 - $r, 128 - $r, 2 * $r, 2 * $r)
        }
        $g.FillEllipse((Brush (C 255 236 242 252)), 883, 106, 44, 44)
        $g.FillEllipse((Brush (C 40 160 180 215)), 893, 116, 10, 9); $g.FillEllipse((Brush (C 34 160 180 215)), 908, 130, 8, 8)
    } else {
        # soleil bas et pale
        for ($i = 12; $i -ge 1; $i--) {
            $r = 26 + $i * 12
            $g.FillEllipse((Brush (C ([int](16 - $i)) 255 252 238)), 880 - $r, 300 - $r, 2 * $r, 2 * $r)
        }
        $g.FillEllipse((Brush (C 255 255 253 244)), 852, 272, 56, 56)
    }

    # collines lointaines puis proches
    foreach ($layer in @(@(330, 22, 61, 17, $far), @(372, 16, 43, 9, $mid))) {
        $pts = @(); for ($x = 20; $x -le 980; $x += 10) { $pts += (P $x ($layer[0] + $layer[1] * [math]::Sin($x / $layer[2]) + $layer[3] * [math]::Sin($x / 17.0 + 1.3))) }
        $pts += (P 980 480); $pts += (P 20 480)
        $g.FillPolygon((Brush $layer[4]), $pts)
    }
    # foret lointaine (petits sapins voiles)
    $farInk = Brush $treeFar; $farSnow = Brush (C 120 ($treeSnow.R) ($treeSnow.G) ($treeSnow.B))
    for ($x = 430; $x -lt 990; $x += 9 + $rnd.Next(0, 9)) { Spruce $g $x (402 + $rnd.Next(-4, 6)) (28 + $rnd.Next(0, 22)) $farInk $farSnow }

    # sol enneige
    $ground = New-Object System.Drawing.Drawing2D.LinearGradientBrush (P 0 428), (P 0 600), $snowA, $snowB
    $gp = New-Object System.Drawing.Drawing2D.GraphicsPath
    $pts = @(); for ($x = 20; $x -le 980; $x += 10) { $pts += (P $x (440 + 6 * [math]::Sin($x / 53.0) + 3 * [math]::Sin($x / 19.0))) }
    $pts += (P 980 600); $pts += (P 20 600)
    $gp.AddPolygon($pts)
    $g.FillPath($ground, $gp)

    # maison en bois (rouge de Falun), toit couvert de neige, fenetres eclairees
    $hx = 640; $hy = 452; $hw = 150; $hh = 78
    $wallR = New-Object System.Drawing.RectangleF $hx, ($hy - $hh), $hw, $hh
    $g.FillRectangle((New-Object System.Drawing.Drawing2D.LinearGradientBrush $wallR, $wall, $wallDark, ([System.Drawing.Drawing2D.LinearGradientMode]::Horizontal)), $wallR)
    $plank = New-Object System.Drawing.Pen (C 60 0 0 0), 1
    for ($y = $hy - $hh + 7; $y -lt $hy; $y += 7) { $g.DrawLine($plank, $hx, $y, $hx + $hw, $y) }
    $trimPen = New-Object System.Drawing.Pen $trim, 3
    $g.DrawLine($trimPen, $hx + 1.5, $hy - $hh, $hx + 1.5, $hy); $g.DrawLine($trimPen, $hx + $hw - 1.5, $hy - $hh, $hx + $hw - 1.5, $hy)
    # pignon
    $gable = @((P ($hx - 4) ($hy - $hh)), (P ($hx + $hw / 2) ($hy - $hh - 52)), (P ($hx + $hw + 4) ($hy - $hh)))
    $g.FillPolygon((Brush $wallDark), $gable)
    # cheminee (la fumee est animee par le lanceur)
    $g.FillRectangle((Brush (C 255 ([int]($wallDark.R * 0.7)) ([int]($wallDark.G * 0.7)) ([int]($wallDark.B * 0.7)))), $hx + 104, $hy - $hh - 52, 14, 34)
    $g.FillRectangle((Brush $roofSnow), $hx + 102, $hy - $hh - 56, 18, 6)
    # toit (neige epaisse qui deborde)
    $roof = New-Object System.Drawing.Drawing2D.GraphicsPath
    $roof.AddPolygon(@((P ($hx - 16) ($hy - $hh + 4)), (P ($hx + $hw / 2) ($hy - $hh - 60)), (P ($hx + $hw + 16) ($hy - $hh + 4)), (P ($hx + $hw + 10) ($hy - $hh + 11)), (P ($hx + $hw / 2) ($hy - $hh - 49)), (P ($hx - 10) ($hy - $hh + 11))))
    $g.FillPath((Brush $roofSnow), $roof)
    # fenetres : halo, vitre, croisillons, neige sur l'appui
    foreach ($wx in @(($hx + 18), ($hx + 104))) {
        $wy = $hy - $hh + 22
        for ($i = 8; $i -ge 1; $i--) { $r = 6 + $i * 5; $g.FillEllipse((Brush (C ([int](22 - $i * 2)) $win.R $win.G $win.B)), $wx + 14 - $r, $wy + 15 - $r, 2 * $r, 2 * $r) }
        $g.FillRectangle((Brush $win), $wx, $wy, 28, 30)
        $cross = New-Object System.Drawing.Pen $trim, 2
        $g.DrawRectangle($cross, $wx, $wy, 28, 30); $g.DrawLine($cross, $wx + 14, $wy, $wx + 14, $wy + 30); $g.DrawLine($cross, $wx, $wy + 15, $wx + 28, $wy + 15)
        $g.FillRectangle((Brush $roofSnow), $wx - 3, $wy + 30, 34, 4)
    }
    # porte + lumiere sur la neige
    $g.FillRectangle((Brush (C 255 ([int]($wallDark.R * 0.55)) ([int]($wallDark.G * 0.55)) ([int]($wallDark.B * 0.55)))), $hx + 63, $hy - 44, 24, 44)
    $g.FillEllipse((Brush (C 255 230 200 120)), $hx + 81, $hy - 24, 3, 3)
    foreach ($wx in @(($hx + 32), ($hx + 118))) { $g.FillEllipse((Brush (C ($(if ($dark) { 46 } else { 20 })) $win.R $win.G $win.B)), $wx - 40, $hy + 2, 80, 16) }
    $g.FillRectangle((Brush $roofSnow), $hx - 6, $hy - 4, $hw + 12, 6)

    # vieille voiture garee, neige sur le toit et le capot
    $cx = 806; $cy = 470
    $car = Brush $(if ($dark) { C 255 26 34 52 } else { C 255 70 86 110 })
    $body = New-Object System.Drawing.Drawing2D.GraphicsPath
    $body.AddPolygon(@((P ($cx) ($cy - 10)), (P ($cx + 4) ($cy - 24)), (P ($cx + 30) ($cy - 27)), (P ($cx + 44) ($cy - 46)), (P ($cx + 92) ($cy - 46)), (P ($cx + 108) ($cy - 27)), (P ($cx + 132) ($cy - 25)), (P ($cx + 136) ($cy - 10))))
    $g.FillPath($car, $body)
    $glass = Brush $(if ($dark) { C 255 54 70 98 } else { C 255 170 190 214 })
    $g.FillPolygon($glass, @((P ($cx + 48) ($cy - 42)), (P ($cx + 66) ($cy - 42)), (P ($cx + 66) ($cy - 29)), (P ($cx + 36) ($cy - 29))))
    $g.FillPolygon($glass, @((P ($cx + 71) ($cy - 42)), (P ($cx + 89) ($cy - 42)), (P ($cx + 101) ($cy - 29)), (P ($cx + 71) ($cy - 29))))
    $g.FillEllipse((Brush (C 255 8 10 16)), $cx + 16, $cy - 20, 22, 22); $g.FillEllipse((Brush (C 255 8 10 16)), $cx + 98, $cy - 20, 22, 22)
    $g.FillEllipse((Brush (C 255 120 130 150)), $cx + 23, $cy - 13, 8, 8); $g.FillEllipse((Brush (C 255 120 130 150)), $cx + 105, $cy - 13, 8, 8)
    $g.FillPolygon((Brush $roofSnow), @((P ($cx + 42) ($cy - 46)), (P ($cx + 46) ($cy - 51)), (P ($cx + 90) ($cy - 52)), (P ($cx + 95) ($cy - 46))))
    $g.FillPolygon((Brush $roofSnow), @((P ($cx + 108) ($cy - 27)), (P ($cx + 112) ($cy - 30)), (P ($cx + 131) ($cy - 28)), (P ($cx + 133) ($cy - 25))))
    $g.FillPolygon((Brush $roofSnow), @((P ($cx + 4) ($cy - 24)), (P ($cx + 8) ($cy - 28)), (P ($cx + 29) ($cy - 30)), (P ($cx + 30) ($cy - 27))))
    $g.FillEllipse((Brush $snowA), $cx - 10, $cy - 6, 160, 14)

    # grands sapins au premier plan
    $ink = Brush $tree; $tsnow = Brush $treeSnow
    foreach ($t in @(@(470, 470, 150), @(520, 458, 104), @(585, 450, 70), @(612, 446, 50), @(978, 492, 200))){ Spruce $g $t[0] $t[1] $t[2] $ink $tsnow }
}

function SpacedText($gr, [string]$t, $font, $brush, [float]$cx, [float]$y, [float]$gap) {
    $sf = [System.Drawing.StringFormat]::GenericTypographic
    $ws = @(); $tot = 0
    foreach ($ch in $t.ToCharArray()) { $w = $gr.MeasureString([string]$ch, $font, 1000, $sf).Width; if ($ch -eq ' ') { $w = $font.Size * 0.35 }; $ws += $w; $tot += $w + $gap }
    $x = $cx - ($tot - $gap) / 2
    $i = 0
    foreach ($ch in $t.ToCharArray()) { $gr.DrawString([string]$ch, $font, $brush, $x, $y, $sf); $x += $ws[$i] + $gap; $i++ }
}

$src = AlphaBox $logo
# Logo de la carte : launcher\logo-titre.png s'il existe (titre « my Winter Car coop », large) ; l'icone garde logo.png.
$titleFile = Join-Path $PSScriptRoot 'logo-titre.png'
$banner = if (Test-Path $titleFile) { [System.Drawing.Image]::FromFile($titleFile) } else { $logo }
$bsrc = AlphaBox $banner
foreach ($dark in $false, $true) {
    $top = if ($dark) { @(8, 14, 28) } else { @(250, 252, 255) }
    $bot = if ($dark) { @(18, 28, 48) } else { @(228, 238, 248) }
    $bmp = New-Object System.Drawing.Bitmap ($W * $S), ($H * $S), ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.ScaleTransform($S, $S)
    $g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    # Ombre douce
    for ($i = 18; $i -ge 1; $i--) {
        $r = New-Object System.Drawing.RectangleF ($card.X - $i), ($card.Y - $i + 8), ($card.Width + 2 * $i), ($card.Height + 2 * $i)
        $a = [int](9 * (1 - $i / 19.0) + 1)
        $g.FillPath((Brush (C $a 6 12 24)), (RoundPath $r (26 + $i)))
    }

    $cardPath = RoundPath $card 26
    $g.SetClip($cardPath)
    DrawWinter $g $dark
    # A gauche, sous le panneau : degrade qui se fond dans le paysage vers le milieu.
    for ($x = 20; $x -lt 580; $x += 2) {
        $k = if ($x -lt 400) { 1.0 } else { 1.0 - ($x - 400) / 180.0 }
        $k = $k * $k * (3 - 2 * $k)
        $a = [int](255 * $k)
        $vg = New-Object System.Drawing.Drawing2D.LinearGradientBrush (P 0 59), (P 0 601), (C $a $top[0] $top[1] $top[2]), (C $a $bot[0] $bot[1] $bot[2])
        $g.FillRectangle($vg, $x, 60, 2, 540)
        $vg.Dispose()
    }

    # Accroche a droite, facon carte postale
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $ink = Brush $(if ($dark) { C 240 240 246 255 } else { C 235 16 34 58 })
    $f1 = New-Object System.Drawing.Font 'Segoe UI Light', 30, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
    $f2 = New-Object System.Drawing.Font 'Segoe UI', 13, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
    SpacedText $g 'GREETINGS FROM' $f1 $ink 715 150 9
    SpacedText $g 'FINLAND' $f1 $ink 715 190 13
    $lb = New-Object System.Drawing.Drawing2D.LinearGradientBrush (P 575 0), (P 855 0), $(if ($dark) { C 255 240 246 255 } else { C 255 16 34 58 }), $(if ($dark) { C 255 120 180 240 } else { C 255 60 130 200 })
    $g.FillRectangle($lb, 575, 238, 280, 2)
    SpacedText $g ('A WINTER IN CO-OP  ' + [char]0xB7 + '  PRE-ALPHA') $f2 (Brush $(if ($dark) { C 215 190 205 225 } else { C 225 50 70 96 })) 715 252 3.2

    # Panneau depoli : le fond sous le panneau, reduit puis agrandi (flou), voile clair ou sombre.
    $panelPath = RoundPath $panel 18
    $small = New-Object System.Drawing.Bitmap 45, 60
    $gs = [System.Drawing.Graphics]::FromImage($small)
    $gs.InterpolationMode = 'HighQualityBilinear'
    $gs.DrawImage($bmp, (New-Object System.Drawing.RectangleF 0, 0, 45, 60), (New-Object System.Drawing.RectangleF ($panel.X * $S), ($panel.Y * $S), ($panel.Width * $S), ($panel.Height * $S)), [System.Drawing.GraphicsUnit]::Pixel)
    $gs.Dispose()
    $g.SetClip($panelPath)
    $g.DrawImage($small, (New-Object System.Drawing.RectangleF ($panel.X - 6), ($panel.Y - 6), ($panel.Width + 12), ($panel.Height + 12)))
    $g.FillPath((Brush $(if ($dark) { C 205 10 18 34 } else { C 205 250 252 255 })), $panelPath)
    $g.ResetClip()
    $g.DrawPath((New-Object System.Drawing.Pen $(if ($dark) { C 60 200 225 255 } else { C 150 255 255 255 }), 1.5), $panelPath)
    $g.DrawPath((New-Object System.Drawing.Pen $(if ($dark) { C 70 200 225 255 } else { C 110 190 205 225 }), 1.5), $cardPath)

    # Logo : depasse du haut de la carte ; ombre douce bleu nuit (un peu vers le bas) pour qu'il se detache du bureau
    # comme de la carte, puis le logo tel quel (il a son propre contour blanc).
    $maxW = if ($banner -ne $logo) { 272.0 } else { 220.0 }
    $dh = 172.0; $dw = $dh * $bsrc.Width / $bsrc.Height
    if ($dw -gt $maxW) { $dw = $maxW; $dh = $dw * $bsrc.Height / $bsrc.Width }
    $top = if ($banner -ne $logo) { 38.0 } else { 6.0 }   # titre large : plus bas, au ras de la pastille PRE-ALPHA
    $dst = New-Object System.Drawing.RectangleF (228 - $dw / 2), $top, $dw, $dh
    for ($i = 7; $i -ge 1; $i--) {
        $ia = New-Object System.Drawing.Imaging.ImageAttributes
        $cm = New-Object System.Drawing.Imaging.ColorMatrix
        $cm.Matrix00 = 0; $cm.Matrix11 = 0; $cm.Matrix22 = 0; $cm.Matrix33 = $(if ($dark) { 0.16 } else { 0.06 })
        $cm.Matrix40 = 4 / 255.0; $cm.Matrix41 = 10 / 255.0; $cm.Matrix42 = 24 / 255.0
        $ia.SetColorMatrix($cm)
        $d = $i * 0.7
        foreach ($o in @(@(-$i, 0), @($i, 0), @(0, -$i), @(0, $i), @(-$d, -$d), @($d, $d), @(-$d, $d), @($d, -$d))) {
            $r = New-Object System.Drawing.Rectangle ([int]($dst.X + $o[0])), ([int]($dst.Y + $o[1] + 4)), ([int]$dst.Width), ([int]$dst.Height)
            $g.DrawImage($banner, $r, $bsrc.X, $bsrc.Y, $bsrc.Width, $bsrc.Height, [System.Drawing.GraphicsUnit]::Pixel, $ia)
        }
    }
    $g.DrawImage($banner, $dst, $bsrc, [System.Drawing.GraphicsUnit]::Pixel)

    $g.Dispose()
    $out = Join-Path $PSScriptRoot $(if ($dark) { 'launcher-sombre.png' } else { 'launcher.png' })
    $bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    "Ecrit : $out ($([int]((Get-Item $out).Length / 1024)) Ko)"
}

# Icone du lanceur (launcher\mwcoop.ico) : le logo tel quel, centre dans un carre, en PNG 256/48/32/16 dans un .ico.
$side = [math]::Max($src.Width, $src.Height) + 2
$cx = $src.X + $src.Width / 2; $cy = $src.Y + $src.Height / 2
$imgs = @()
foreach ($s in 256, 48, 32, 16) {
    $b = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $gg = [System.Drawing.Graphics]::FromImage($b)
    $gg.InterpolationMode = 'HighQualityBicubic'; $gg.SmoothingMode = 'AntiAlias'; $gg.PixelOffsetMode = 'HighQuality'
    $gg.Clear([System.Drawing.Color]::Transparent)
    $m = 0
    $gg.DrawImage($logo, (New-Object System.Drawing.RectangleF $m, $m, ($s - 2 * $m), ($s - 2 * $m)), (New-Object System.Drawing.RectangleF ($cx - $side / 2), ($cy - $side / 2), $side, $side), [System.Drawing.GraphicsUnit]::Pixel)
    $gg.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $imgs += , @($s, $ms.ToArray())
    $b.Dispose()
}
$logo.Dispose()
$fs = [System.IO.File]::Create((Join-Path $PSScriptRoot 'mwcoop.ico'))
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$imgs.Count)
$off = 6 + 16 * $imgs.Count
foreach ($i in $imgs) {
    $s = $i[0]; $len = $i[1].Length
    $w.Write([byte]($s % 256)); $w.Write([byte]($s % 256)); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$len); $w.Write([uint32]$off)
    $off += $len
}
foreach ($i in $imgs) { $w.Write($i[1]) }
$w.Close()
"Ecrit : mwcoop.ico"
