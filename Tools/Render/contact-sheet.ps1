# Builds the review sheet: every new render next to the current in-game sprite, at large and real size.
param([string]$Out = "$PSScriptRoot\out", [string]$Old = "$PSScriptRoot\out\old")
Add-Type -AssemblyName System.Drawing
$modules = 'generator', 'reactor', 'relay', 'pulse', 'cryo', 'tesla', 'wall', 'repair'
$enemies = 'scout', 'runner', 'armor', 'jammer', 'splitter', 'sapper', 'bulwark', 'thermal', 'grounder', 'hunter', 'commander', 'tank', 'helicopter', 'bomber'
$labels = @{ generator = 'Generador'; reactor = 'Núcleo'; relay = 'Relé'; pulse = 'Pulso'; cryo = 'Criogénico'; tesla = 'Tesla'; wall = 'Barrera'; repair = 'Taller';
  scout = 'Explorador'; runner = 'Corredor'; armor = 'Blindado'; jammer = 'Interferente'; splitter = 'Divisor'; sapper = 'Saboteador'; bulwark = 'Coraza'; thermal = 'Térmico'; grounder = 'Aislante';
  hunter = 'Cazador'; commander = 'Centinela'; tank = 'Tanque'; helicopter = 'Helicóptero'; bomber = 'Bombardero' }
$cols = 4; $tileW = 330; $tileH = 300
$rows = [math]::Ceiling(($modules.Count + $enemies.Count) / $cols)
$bmp = New-Object Drawing.Bitmap ($cols * $tileW), ($rows * $tileH)
$g = [Drawing.Graphics]::FromImage($bmp)
$g.InterpolationMode = 'HighQualityBicubic'; $g.SmoothingMode = 'AntiAlias'
$g.Clear([Drawing.Color]::FromArgb(10, 36, 54))
$titleFont = New-Object Drawing.Font 'Segoe UI', 13, ([Drawing.FontStyle]::Bold)
$smallFont = New-Object Drawing.Font 'Segoe UI', 9
$alt = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(14, 44, 65))
$muted = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(153, 181, 199))
function Paint($path, $x, $y, $size) { $i = [Drawing.Image]::FromFile($path); $g.DrawImage($i, $x, $y, $size, $size); $i.Dispose() }
$n = 0
foreach ($name in $modules + $enemies) {
  $x = ($n % $cols) * $tileW; $y = [math]::Floor($n / $cols) * $tileH
  if ((($n % $cols) + [math]::Floor($n / $cols)) % 2 -eq 0) { $g.FillRectangle($alt, $x, $y, $tileW, $tileH) }
  $isEnemy = $enemies -contains $name
  $g.DrawString($labels[$name], $titleFont, [Drawing.Brushes]::White, ($x + 10), ($y + 6))
  $big = 240
  Paint "$Out\$name.png" ($x + 4) ($y + 40) $big
  $real = if ($isEnemy) { 112 } else { 75 }
  $folder = if ($isEnemy) { 'Enemies' } else { 'Modules' }
  $g.DrawString('actual', $smallFont, $muted, ($x + 262), ($y + 42))
  Paint "$Old\Assets\Resources\Sprites\$folder\$name.png" ($x + 332 - $real - 2) ($y + 60) $real
  $g.DrawString('nuevo', $smallFont, $muted, ($x + 264), ($y + 172))
  Paint "$Out\$name.png" ($x + 332 - $real - 2) ($y + 188) $real
  $n++
}
$g.Dispose()
$bmp.Save("$Out\bocetos.png", [Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
