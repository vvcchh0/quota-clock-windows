param([string]$Output='QuotaClock.exe')
$ErrorActionPreference = 'Stop'
$projectDir = $PSScriptRoot
if ($Output -notmatch '^[A-Za-z0-9.-]+[.]exe$') { throw 'Output must be a local exe filename.' }
$frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $frameworkDir 'csc.exe'
$references = @('WPF\PresentationFramework.dll','WPF\PresentationCore.dll','WPF\WindowsBase.dll','System.Xaml.dll','System.Web.Extensions.dll','System.Core.dll','System.Windows.Forms.dll','System.Drawing.dll')
$compilerArgs = @('/nologo','/target:winexe','/platform:x64','/optimize+','/utf8output',("/out:" + (Join-Path $projectDir $Output)),("/win32manifest:" + (Join-Path $projectDir 'app.manifest')))
foreach ($reference in $references) { $compilerArgs += "/reference:$(Join-Path $frameworkDir $reference)" }
foreach ($name in @('QuotaCore.cs','CodexClient.cs','ClientChecks.cs','WaveTrail.cs','WaveChecks.cs','LocalQuotaMeter.cs','Visuals.cs','AppShell.cs','NativeTaskbar.cs','WindowsUI.cs','Program.cs','SelfTests.cs','ShellChecks.cs','CalibrationTests.cs')) { $compilerArgs += Join-Path $projectDir $name }
& $compiler @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
$test = Start-Process -FilePath (Join-Path $projectDir $Output) -ArgumentList '--test' -WindowStyle Hidden -Wait -PassThru
Get-Content -LiteralPath (Join-Path $projectDir 'test-results.txt')
if ($test.ExitCode -ne 0) { throw 'Tests failed.' }


