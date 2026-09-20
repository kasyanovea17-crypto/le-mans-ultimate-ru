param([switch]$Test)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$build=Join-Path $root 'build'
New-Item -ItemType Directory -Force -Path $build | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$payload=Join-Path $build 'payload.zip'
if(Test-Path -LiteralPath $payload){Remove-Item -LiteralPath $payload}
[IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $root 'payload'),$payload,[IO.Compression.CompressionLevel]::Optimal,$false)
$hash=(Get-FileHash -LiteralPath (Join-Path $root 'payload\manifest.json') -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $build 'BuildInfo.cs'),('namespace LmuRu { public static class BuildInfo { public const string ManifestHash="'+$hash+'"; } }'))
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$refs=@('/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Web.Extensions.dll','/reference:System.IO.Compression.dll','/reference:System.IO.Compression.FileSystem.dll')
$sources=@((Join-Path $root 'src\Engine.cs'),(Join-Path $root 'src\NativePatch.cs'),(Join-Path $root 'src\Launcher.cs'),(Join-Path $build 'BuildInfo.cs'))
& $csc /nologo /target:winexe /platform:x64 /optimize+ ('/win32manifest:'+(Join-Path $root 'src\app.manifest')) ('/resource:'+$payload+',LMU.Payload') @refs ('/out:'+(Join-Path $build 'LMU-RU-Launcher.exe')) @sources
if($LASTEXITCODE -ne 0){throw 'Launcher compilation failed'}
$testFile=Join-Path $build 'UI_TEST.txt'
$p=Start-Process -FilePath (Join-Path $build 'LMU-RU-Launcher.exe') -ArgumentList ('--self-test "'+$testFile+'"') -WindowStyle Hidden -PassThru -Wait
if($p.ExitCode -ne 0){throw ('UI tests failed: '+$testFile+'.error.txt')}
Get-Content -LiteralPath $testFile
if($Test){
 & $csc /nologo /target:exe /platform:x64 /define:TESTS /main:LmuRu.EngineTests ('/resource:'+$payload+',LMU.Payload') @refs ('/out:'+(Join-Path $build 'EngineTests.exe')) @sources (Join-Path $root 'tests\EngineTests.cs')
 if($LASTEXITCODE -ne 0){throw 'Engine test compilation failed'}
 & (Join-Path $build 'EngineTests.exe')
 if($LASTEXITCODE -ne 0){throw 'Engine tests failed'}
 & $csc /nologo /target:exe /platform:x64 /main:LmuRu.NativeTests ('/resource:'+$payload+',LMU.Payload') @refs ('/out:'+(Join-Path $build 'NativeTests.exe')) @sources (Join-Path $root 'tests\NativeTests.cs')
 if($LASTEXITCODE -ne 0){throw 'Native tests compilation failed'}
 & (Join-Path $build 'NativeTests.exe')
 if($LASTEXITCODE -ne 0){throw 'Native tests failed'}
}
Write-Output 'BUILD_PASS launcher=1.1.0-rc.1 payload=embedded dependencies=NET_Framework'
