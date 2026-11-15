param([switch]$Run, [switch]$Test, [switch]$CoreOnly)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$install = Join-Path $env:LOCALAPPDATA 'Gamepad2KBM'
$stage = Join-Path $install 'build'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$running = @(Get-Process -Name Gamepad2KBM -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    if ($Run -and !$Test -and !$CoreOnly) {
        Write-Output 'Gamepad2KBM is already running. Use its system tray icon.'
        return
    }
    throw 'Quit Gamepad2KBM from the system tray before rebuilding.'
}
New-Item -ItemType Directory -Force -Path $install, $stage, (Join-Path $root 'dist') | Out-Null
Copy-Item (Join-Path $root 'src\*.cs') $stage -Force
$sources = @('Core.cs', 'Native.cs', 'App.cs' | ForEach-Object { Join-Path $stage $_ })
$refs = @('/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll', '/reference:System.Web.Extensions.dll')
$exe = Join-Path $install 'Gamepad2KBM.exe'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 "/out:$exe" @refs @sources
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
if (!(Test-Path (Join-Path $install 'settings.json'))) {
    Copy-Item (Join-Path $root 'settings.json') $install
}
Copy-Item $exe (Join-Path $root 'dist\Gamepad2KBM.exe') -Force
Copy-Item (Join-Path $root 'settings.json') (Join-Path $root 'dist\settings.json') -Force
if ($Test -or $CoreOnly) {
    Copy-Item (Join-Path $root 'tests\Tests.cs') $stage -Force
    $testExe = Join-Path $install 'Tests.exe'
    $testSources = @((Join-Path $stage 'Core.cs'), (Join-Path $stage 'Native.cs'), (Join-Path $stage 'App.cs'), (Join-Path $stage 'Tests.cs'))
    & $compiler /nologo /target:exe /platform:x64 /codepage:65001 /main:Gamepad2KBM.Tests "/out:$testExe" @refs @testSources
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
    if ($CoreOnly) { & $testExe --core-only } else { & $testExe }
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
}
Write-Output "Built: $exe"
if ($Run) { Start-Process -FilePath $exe -WorkingDirectory $install }
