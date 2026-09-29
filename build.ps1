# Builds ExtraDim.exe with the C# compiler that ships with Windows (no SDK needed).
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc  = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$out  = Join-Path $root 'build'
New-Item -ItemType Directory -Force $out | Out-Null

$src  = Get-ChildItem (Join-Path $root 'src\*.cs') | ForEach-Object { $_.FullName }
$refs = '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll'
$common = @('/nologo', '/target:winexe', '/optimize+', '/codepage:65001') + $refs

# Pass 1: build once to render the icon from the same drawing code the tray uses.
$tmp = Join-Path $out 'icon-gen.exe'
& $csc @common "/out:$tmp" @src
if ($LASTEXITCODE) { throw 'compile failed' }
$ico = Join-Path $out 'ExtraDim.ico'
Start-Process $tmp -ArgumentList '--write-icon', "`"$ico`"" -Wait
Remove-Item $tmp

# Pass 2: final exe with icon and DPI-aware manifest.
$exe = Join-Path $out 'ExtraDim.exe'
& $csc @common "/out:$exe" "/win32icon:$ico" "/win32manifest:$(Join-Path $root 'src\app.manifest')" @src
if ($LASTEXITCODE) { throw 'compile failed' }
Write-Host "Built $exe"
