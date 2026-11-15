$ErrorActionPreference = 'Stop'
$install = Join-Path $env:LOCALAPPDATA 'Gamepad2KBM'
$stage = Join-Path $install 'monitor-build'
$exe = Join-Path $install 'ControllerMonitor.exe'
$running = @(Get-Process -Name ControllerMonitor -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    $shell = New-Object -ComObject WScript.Shell
    $shell.AppActivate($running[0].Id) | Out-Null
    return
}
New-Item -ItemType Directory -Force -Path $stage | Out-Null
foreach ($file in @('Core.cs', 'Native.cs', 'Monitor.cs')) {
    Copy-Item (Join-Path $PSScriptRoot "src\$file") $stage -Force
}
$sources = @('Core.cs', 'Native.cs', 'Monitor.cs' | ForEach-Object { Join-Path $stage $_ })
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:winexe /platform:x64 /codepage:65001 "/out:$exe" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll @sources
if ($LASTEXITCODE -ne 0) { throw 'Monitor compilation failed' }
Start-Process -FilePath $exe -WorkingDirectory $install
Write-Output "Opened: $exe"
