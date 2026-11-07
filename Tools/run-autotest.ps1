# Builds the Windows development player and runs the automated UI test on several screen shapes.
# Close the Unity Editor first: batch builds cannot run while this project is open.
param([switch]$SkipBuild)
$root = (Resolve-Path "$PSScriptRoot\..").Path
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor\Unity.exe'
if (-not $SkipBuild) {
  $log = "$root\Logs\colonia-build.log"
  $p = Start-Process $unity -ArgumentList @('-batchmode', '-projectPath', "`"$root`"", '-executeMethod', 'Colonia09.EditorTools.ProjectSetup.BuildWindowsDev', '-logFile', "`"$log`"") -Wait -PassThru -NoNewWindow
  if ($p.ExitCode -ne 0) { Select-String -Path $log -Pattern 'error CS' | Select-Object -First 20 | ForEach-Object { $_.Line }; throw "Build failed ($($p.ExitCode)). See $log" }
}
$runs = @(
  @{ Name = 'phone-tall'; W = 540; H = 1170; Extra = @() },
  @{ Name = 'phone-notch'; W = 540; H = 1170; Extra = @('-colonia-quick', '-colonia-inset-top', '66', '-colonia-inset-bottom', '40') },
  @{ Name = 'phone-16x9'; W = 540; H = 960; Extra = @('-colonia-quick') },
  @{ Name = 'tablet-4x3'; W = 768; H = 1024; Extra = @('-colonia-quick') }
)
$failed = 0
foreach ($run in $runs) {
  $out = "$root\Builds\Autotest\$($run.Name)"
  if (Test-Path $out) { Remove-Item -Recurse -Force $out }
  $args = @('-screen-fullscreen', '0', '-screen-width', $run.W, '-screen-height', $run.H, '-logFile', "`"$root\Logs\colonia-$($run.Name).log`"", '-colonia-autotest', "`"$out`"") + $run.Extra
  $p = Start-Process "$root\Builds\WindowsDev\Colonia09.exe" -ArgumentList $args -PassThru
  if (-not $p.WaitForExit(330000)) { $p.Kill(); Write-Output "$($run.Name): TIMEOUT"; $failed++; continue }
  if (-not (Test-Path "$out\report.txt")) { Write-Output "$($run.Name): no report"; $failed++; continue }
  $report = Get-Content "$out\report.txt" -Encoding UTF8
  Write-Output "--- $($run.Name): $($report[-1])"
  $report | Where-Object { $_ -like 'FAIL*' -or $_ -like 'LOG ERROR*' -or $_ -like 'Screen*' -or $_ -like 'Run ended*' -or $_ -like '*real touch*' }
  if ($report[-1] -ne 'AUTOTEST PASS') { $failed++ }
}
if ($failed -gt 0) { throw "$failed run(s) failed" }
Write-Output 'ALL AUTOTESTS PASS'
