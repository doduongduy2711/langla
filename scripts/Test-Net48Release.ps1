param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output=[IO.Path]::GetFullPath($OutputDirectory)
$check=Join-Path ([IO.Path]::GetTempPath()) ('QLTK-net48-'+[guid]::NewGuid().ToString('N'))
$null=[IO.Directory]::CreateDirectory($check)
$zip=[IO.Compression.ZipFile]::OpenRead((Join-Path $output 'QLTK-portable.zip'))
try{
 foreach($name in @('QLTK.exe','QLTK.exe.config')){
  $entry=$zip.GetEntry($name);if(!$entry){throw ('Portable release missing '+$name)}
  [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,(Join-Path $check $name),$false)
 }
}finally{$zip.Dispose()}
$launcher=Join-Path $check 'QLTK.exe'
$assembly=[Reflection.Assembly]::ReflectionOnlyLoadFrom($launcher)
$target=@($assembly.GetCustomAttributesData() | Where-Object {$_.AttributeType.FullName -eq 'System.Runtime.Versioning.TargetFrameworkAttribute'})
if($target.Count -ne 1 -or $target[0].ConstructorArguments[0].Value -cne '.NETFramework,Version=v4.8'){throw 'Launcher does not target NET Framework 4.8'}
[xml]$config=Get-Content -LiteralPath ($launcher+'.config') -Raw
if($config.configuration.startup.supportedRuntime.sku -cne '.NETFramework,Version=v4.8' -or $config.configuration.startup.supportedRuntime.version -cne 'v4.0'){throw 'NET 4.8 runtime configuration missing'}
$zip=[IO.Compression.ZipFile]::OpenRead((Join-Path $output 'QLTK-net48-launcher.zip'))
try{if($zip.Entries.Count -ne 2 -or !$zip.GetEntry('QLTK.exe') -or !$zip.GetEntry('QLTK.exe.config')){throw 'Launcher-only package invalid'}}finally{$zip.Dispose()}

# Compile the dynamically loaded components against 4.8 reference APIs as well.
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$reference=& (Join-Path $PSScriptRoot 'Get-Net48References.ps1')
$references=@('mscorlib.dll','System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll') | ForEach-Object {'/r:'+(Join-Path $reference $_)}
$references+=('/r:'+([Management.Automation.PowerShell].Assembly.Location))
foreach($name in @('NativeBackend','AutoLogin','SocketServer','StoredJson','UiPerformance','ManagerStatusCache','NativeUiBridge')){
 & $compiler /nologo /noconfig /nostdlib+ /target:library /platform:x64 ('/out:'+(Join-Path $check ($name+'.dll'))) $references (Join-Path $root ('QLTK/'+$name+'.cs'))
 if($LASTEXITCODE -ne 0){throw ('Component does not compile against NET 4.8: '+$name)}
}
function Invoke-LauncherCheck([string]$arguments,[string]$logName){
 $info=New-Object Diagnostics.ProcessStartInfo;$info.FileName=$launcher;$info.Arguments=$arguments;$info.WorkingDirectory=$check
 $info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true;$info.StandardOutputEncoding=New-Object Text.UTF8Encoding($false);$info.StandardErrorEncoding=New-Object Text.UTF8Encoding($false)
 $process=[Diagnostics.Process]::Start($info)
 try{
  $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync();$timer=[Diagnostics.Stopwatch]::StartNew()
  while(!$process.WaitForExit(1000)){if($timer.Elapsed.TotalSeconds -gt 180){$process.Kill();throw 'NET 4.8 launcher check timed out'}}
  $text=$stdout.Result+[Environment]::NewLine+$stderr.Result
  [IO.File]::WriteAllText((Join-Path $check $logName),$text)
  if($process.ExitCode -ne 0){throw ('Launcher check failed: '+$text)}
 }finally{$process.Dispose()}
}
Invoke-LauncherCheck '--runtime-check' 'runtime-check.log'
$source=Join-Path $check 'QLTK';$null=[IO.Directory]::CreateDirectory($source)
Get-ChildItem -LiteralPath (Join-Path $root 'QLTK') -File | Where-Object {$_.Extension -in @('.ps1','.cs','.json','.py')} | ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination $source}
Invoke-LauncherCheck '--ui-test' 'host-ui-test.log'
Write-Output 'PASS: NET Framework 4.8 assembly target, CLR configuration, x64 launcher, launcher-only archive, all dynamic components using 4.8 reference APIs and WinForms checks through the actual EXE. No real game opened.'
