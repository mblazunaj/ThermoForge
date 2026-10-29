param([string]$PackageRoot = "$PSScriptRoot\packages", [string]$Destination = "$PSScriptRoot\publish")
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force "$Destination\licenses" | Out-Null
$assets = Get-Content "$PSScriptRoot\ThermoForge\obj\project.assets.json" -Raw | ConvertFrom-Json -AsHashtable
$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('ThermoForge — third-party notices')
$lines.Add('Original application code: copyright mblazunaj. All rights reserved unless separately licensed.')
$lines.Add('All bundled third-party libraries and signed installers are unmodified.')
$lines.Add('')
foreach ($name in ($assets.libraries.Keys | Sort-Object)) {
  $package = Join-Path $PackageRoot $name.ToLowerInvariant()
  $spec = Get-ChildItem -LiteralPath $package -Filter '*.nuspec' -ErrorAction SilentlyContinue | Select-Object -First 1
  if (!$spec) { continue }
  [xml]$xml = Get-Content -LiteralPath $spec.FullName
  $m = $xml.package.metadata
  $lines.Add("$($m.id) $($m.version)")
  $lines.Add("  Authors: $($m.authors)")
  $lines.Add("  Copyright: $($m.copyright)")
  $license = if ($m.license) { $m.license.InnerText } else { $m.licenseUrl }
  $lines.Add("  License: $license")
  $source = $m.repository.url
  if ($source -and $m.repository.commit) { $source = "$source/tree/$($m.repository.commit)" }
  if (!$source) { $source = $m.projectUrl }
  $lines.Add("  Source: $source")
  foreach ($file in (Get-ChildItem -LiteralPath $package -File | Where-Object { $_.Name -match '^(LICENSE|COPYING|THIRD-PARTY)' })) {
    $target = "$($m.id)-$($m.version)-$($file.Name)"
    Copy-Item -LiteralPath $file.FullName -Destination (Join-Path "$Destination\licenses" $target)
    $lines.Add("  License file: licenses/$target")
  }
  $lines.Add('')
}
foreach ($runtime in @('microsoft.netcore.app.runtime.win-x64','microsoft.windowsdesktop.app.runtime.win-x64')) {
  $folder = Get-ChildItem -LiteralPath (Join-Path $PackageRoot $runtime) -Directory | Sort-Object Name | Select-Object -Last 1
  if ($folder) {
    foreach ($file in (Get-ChildItem -LiteralPath $folder.FullName -File | Where-Object {$_.Name -match '^(LICENSE|THIRD-PARTY)'})) {
      Copy-Item -LiteralPath $file.FullName -Destination "$Destination\licenses\$runtime-$($file.Name)"
    }
  }
}
$downloads = @{
 'MPL-2.0.txt' = 'https://raw.githubusercontent.com/LibreHardwareMonitor/LibreHardwareMonitor/v0.9.6/LICENSE';
 'MIT.txt' = 'https://raw.githubusercontent.com/amerkoleci/Vortice.Windows/main/LICENSE';
 'PawnIO-GPL-2.0.txt' = 'https://raw.githubusercontent.com/namazso/PawnIO/master/COPYING';
 'PawnIO-Modules-LGPL-2.1.txt' = 'https://raw.githubusercontent.com/LibreHardwareMonitor/LibreHardwareMonitor/v0.9.6/LibreHardwareMonitorLib/Resources/PawnIo/COPYING';
 'PawnIO-Modules-README.txt' = 'https://raw.githubusercontent.com/LibreHardwareMonitor/LibreHardwareMonitor/v0.9.6/LibreHardwareMonitorLib/Resources/PawnIo/README'
}
foreach ($file in $downloads.Keys) { Invoke-WebRequest $downloads[$file] -OutFile "$Destination\licenses\$file" }
$lines.Add('PawnIO 2.2.0 — copyright namazso; GPL-2.0-or-later with module linking exception.')
$lines.Add('  Official signed installer, redistributed unchanged: https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0')
$lines.Add('  Driver source and exception: https://github.com/namazso/PawnIO')
$lines.Add('  Official modules source: https://github.com/namazso/PawnIO.Modules/releases/tag/0.1.6 (LGPL-2.1-or-later).')
$lines.Add('  Full module license and source reference are included in licenses/.')
$lines.Add('  PawnIO is installed independently and is retained when ThermoForge is uninstalled.')
$lines.Add('')
$lines.Add('The .NET runtime is redistributed under its included MIT license and third-party notices.')
$lines.Add('Inno Setup creates this installer: https://jrsoftware.org/isinfo.php (compiler not distributed).')
$lines.Add('SHA-256 checks detect download corruption. They are not a substitute for publisher code signing.')
[IO.File]::WriteAllLines("$Destination\THIRD-PARTY-NOTICES.txt", $lines)
