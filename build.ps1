param([string]$IsccPath)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
  [xml]$project = Get-Content -LiteralPath 'ThermoForge\ThermoForge.csproj'
  $version = $project.Project.PropertyGroup.Version
  New-Item -ItemType Directory -Force vendor, publish, '..\outputs' | Out-Null
  $driver = Join-Path $PSScriptRoot 'vendor\PawnIO_setup.exe'
  if (!(Test-Path -LiteralPath $driver)) { Invoke-WebRequest 'https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe' -OutFile $driver }
  $expected = '1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032'
  if ((Get-FileHash -LiteralPath $driver -Algorithm SHA256).Hash -ne $expected) { throw 'PawnIO SHA-256 mismatch' }
  if ((Get-AuthenticodeSignature -LiteralPath $driver).Status -ne 'Valid') { throw 'PawnIO signature is not valid' }
  dotnet restore ThermoForge\ThermoForge.csproj -r win-x64 --packages packages --configfile NuGet.Config
  if ($LASTEXITCODE) { throw 'NuGet restore failed' }
  dotnet publish ThermoForge\ThermoForge.csproj -c Release -r win-x64 --self-contained true --no-restore -o publish
  if ($LASTEXITCODE) { throw 'Publish failed' }
  & "$PSScriptRoot\Generate-Notices.ps1"
  if (!$IsccPath) {
    foreach ($candidate in @("$PSScriptRoot\tools\Inno\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "${env:ProgramFiles}\Inno Setup 7\ISCC.exe")) {
      if (Test-Path -LiteralPath $candidate) { $IsccPath = $candidate; break }
    }
  }
  if (!$IsccPath) { throw 'Install Inno Setup 6.7+ / 7 or pass -IsccPath.' }
  & $IsccPath "/DMyAppVersion=$version" installer.iss
  if ($LASTEXITCODE) { throw 'Installer compilation failed' }
  $setup = Join-Path $PSScriptRoot "..\outputs\ThermoForge-Setup-$version.exe"
  "$((Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($setup))" | Set-Content -LiteralPath "$setup.sha256" -Encoding ascii
  Write-Output "Built: $setup"
}
finally { Pop-Location }
