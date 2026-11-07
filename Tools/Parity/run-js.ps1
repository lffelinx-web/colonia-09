# Runs the original HTML prototype headless in Edge. Writes reference traces to Tools/Parity/out,
# the 12 embedded sprites to Assets/Resources/Sprites, and rasterized enemies.
param([string]$Html = "$PSScriptRoot\..\Reference\Colonia09_v091.html")
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path "$PSScriptRoot\..\..").Path
$outDir = "$PSScriptRoot\out"
New-Item -ItemType Directory -Force $outDir, "$root\Assets\Resources\Sprites\Modules", "$root\Assets\Resources\Sprites\Enemies" | Out-Null

$text = [IO.File]::ReadAllText((Resolve-Path $Html).Path, [Text.Encoding]::UTF8)
$scenarios = [IO.File]::ReadAllText("$PSScriptRoot\scenarios.txt", [Text.Encoding]::UTF8)
$inject = [IO.File]::ReadAllText("$PSScriptRoot\harness_inject.js", [Text.Encoding]::UTF8)
$jsScenarios = '`' + $scenarios + '`'
$inject = $inject.Replace('__SCENARIOS__', $jsScenarios)
$marker = "requestAnimationFrame(loop);`n})();"
if (-not $text.Contains($marker)) { $marker = "requestAnimationFrame(loop);`r`n})();" }
if (-not $text.Contains($marker)) { throw 'Could not find the end of the prototype script.' }
$patched = $text.Replace($marker, "requestAnimationFrame(loop);`n$inject`n})();")
# Lets the art export skip the dynamic health bars and labels drawn over each enemy.
$overlay = "   const bw=Math.min(cw,ch)*((e.kind==='commander'"
if (-not $patched.Contains($overlay)) { throw 'Could not find the enemy overlay code.' }
$patched = $patched.Replace($overlay, "   if(window.__noOverlay)continue;`n" + $overlay)
$harness = "$outDir\harness.html"
[IO.File]::WriteAllText($harness, $patched, (New-Object Text.UTF8Encoding $false))

$edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw 'Microsoft Edge not found.' }
$dump = "$outDir\dom.txt"
$profile = "$outDir\edge-profile"
$url = 'file:///' + ($harness -replace '\\', '/' -replace ' ', '%20')
$args = "--headless=new --disable-gpu --no-first-run --no-default-browser-check --disable-extensions --user-data-dir=`"$profile`" --virtual-time-budget=20000 --dump-dom `"$url`""
$psi = New-Object Diagnostics.ProcessStartInfo $edge, $args
$psi.RedirectStandardOutput = $true; $psi.UseShellExecute = $false; $psi.StandardOutputEncoding = [Text.Encoding]::UTF8
$p = [Diagnostics.Process]::Start($psi)
$dom = $p.StandardOutput.ReadToEnd(); $p.WaitForExit()
[IO.File]::WriteAllText($dump, $dom, [Text.Encoding]::UTF8)

$m = [regex]::Match($dom, '<pre id="harness">(.*?)</pre>', 'Singleline')
if (-not $m.Success) { throw "Harness produced no output (dom length $($dom.Length)). See $dump" }
$sections = $m.Groups[1].Value -split '=== FILE '
foreach ($s in $sections) {
  if (-not $s.Trim()) { continue }
  $nl = $s.IndexOf("`n"); $name = $s.Substring(0, $nl).Trim(); $body = $s.Substring($nl + 1).Trim()
  if ($name -like 'enemy_*.b64') {
    if ($body.StartsWith('ERROR')) { Write-Output "$name : $body"; continue }
    $kind = $name.Substring(6).Replace('.b64', '')
    [IO.File]::WriteAllBytes("$root\Assets\Resources\Sprites\Enemies\$kind.png", [Convert]::FromBase64String($body))
  } else {
    [IO.File]::WriteAllText("$outDir\$name", $body + "`n", (New-Object Text.UTF8Encoding $false))
    Write-Output ("{0}: {1} lines" -f $name, ($body -split "`n").Count)
  }
}
# Module sprites embedded in the prototype.
foreach ($sm in [regex]::Matches($text, '"(generator|reactor|relay|pulse|cryo|tesla|wall|repair)":"data:image/png;base64,([^"]+)"')) {
  [IO.File]::WriteAllBytes("$root\Assets\Resources\Sprites\Modules\$($sm.Groups[1].Value).png", [Convert]::FromBase64String($sm.Groups[2].Value))
}
Remove-Item -Recurse -Force $profile -ErrorAction SilentlyContinue
Remove-Item -Force $dump -ErrorAction SilentlyContinue
Write-Output 'OK'
