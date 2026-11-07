# Generates the Xcode project with Unity and publishes it as a GitHub release, which starts the
# iOS workflow. Close the Unity Editor first: batch builds cannot run while this project is open.
param([string]$Account = 'lffelinx-web', [string]$Repo = 'lffelinx-web/colonia-09', [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path "$PSScriptRoot\..").Path
if (-not $SkipBuild) {
  $unity = 'C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor\Unity.exe'
  $log = "$root\Logs\colonia-ios-build.log"
  $p = Start-Process $unity -ArgumentList @('-batchmode', '-projectPath', "`"$root`"", '-buildTarget', 'iOS', '-executeMethod', 'Colonia09.EditorTools.ProjectSetup.BuildIosProject', '-logFile', "`"$log`"") -Wait -PassThru -NoNewWindow
  if ($p.ExitCode -ne 0) { Select-String -Path $log -Pattern 'error CS|Error building' | Select-Object -First 20 | ForEach-Object { $_.Line }; throw "Unity failed ($($p.ExitCode)). See $log" }
}
$archive = "$root\Builds\colonia09-xcode.tar.gz"
tar.exe -czf $archive -C "$root\Builds\iOS" .
if ($LASTEXITCODE -ne 0) { throw 'Could not create the archive.' }
Write-Output ("Archive: {0:N0} MB" -f ((Get-Item $archive).Length / 1MB))
$env:GH_TOKEN = (gh auth token --user $Account)
try {
  $tag = 'ios-' + (Get-Date -Format 'yyyyMMdd-HHmm')
  gh release create $tag $archive -R $Repo --title "iOS $tag" --notes 'Proyecto de Xcode generado por Unity para la compilación de iOS.' --prerelease
  if ($LASTEXITCODE -ne 0) { throw 'Could not create the release.' }
} finally { Remove-Item Env:GH_TOKEN }
Write-Output "Published $tag"
