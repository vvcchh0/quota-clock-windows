$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'build.ps1') -Output 'QuotaClock.test.exe'
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$fixture=Join-Path $PSScriptRoot 'research-v0.4\FakeCli.cs'
& (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 /optimize+ ('/reference:'+(Join-Path $framework 'System.Web.Extensions.dll')) ('/out:'+(Join-Path $PSScriptRoot 'research-v0.4\FakeCli.exe')) $fixture
if($LASTEXITCODE -ne 0){throw 'Fixture compilation failed'}
foreach($mode in @('--test-wave','--test-client','--test-calibration','--test-shell')){
    $p=Start-Process -FilePath (Join-Path $PSScriptRoot 'QuotaClock.test.exe') -ArgumentList $mode -WindowStyle Hidden -Wait -PassThru
    if($p.ExitCode -ne 0){throw ('Check failed: '+$mode)}
}
Get-Content -LiteralPath (Join-Path $PSScriptRoot 'test-results.txt'),(Join-Path $PSScriptRoot 'wave-test-results.txt'),(Join-Path $PSScriptRoot 'client-test-results.txt'),(Join-Path $PSScriptRoot 'calibration-test-results.txt'),(Join-Path $PSScriptRoot 'shell-test-results.txt')
