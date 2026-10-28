param([string]$CacheDirectory=(Join-Path $PSScriptRoot '../work/net48-reference-1.0.3'))
$ErrorActionPreference='Stop'
$installed=Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies/Microsoft/Framework/.NETFramework/v4.8'
if(Test-Path -LiteralPath (Join-Path $installed 'mscorlib.dll')){return [IO.Path]::GetFullPath($installed)}
$cache=[IO.Path]::GetFullPath($CacheDirectory)
$reference=[IO.Path]::GetFullPath((Join-Path $cache 'build/.NETFramework/v4.8'))
if(Test-Path -LiteralPath (Join-Path $reference 'mscorlib.dll')){return $reference}
$null=[IO.Directory]::CreateDirectory($cache)
$package=Join-Path $cache 'reference-assemblies.nupkg'
$url='https://api.nuget.org/v3-flatcontainer/microsoft.netframework.referenceassemblies.net48/1.0.3/microsoft.netframework.referenceassemblies.net48.1.0.3.nupkg'
[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
if(!(Test-Path -LiteralPath $package)){Invoke-WebRequest -Uri $url -OutFile $package -UseBasicParsing}
# SHA-512 from the NuGet catalog for the pinned Microsoft 1.0.3 package.
$expected='XWKgyeNadNcTQaIVvQB8BrdCNrEar6fo/de1OdQRZ9HFy0jcBSaM8IV5q64ZampsSnC8AlTsACaGZUuoFw41RA=='
$algorithm=[Security.Cryptography.SHA512]::Create()
try{$stream=[IO.File]::OpenRead($package);try{$actual=[Convert]::ToBase64String($algorithm.ComputeHash($stream))}finally{$stream.Dispose()}}finally{$algorithm.Dispose()}
if($actual -cne $expected){throw 'NET 4.8 reference package checksum mismatch'}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=[IO.Compression.ZipFile]::OpenRead($package)
try{
 foreach($entry in $zip.Entries){
  if(!$entry.FullName.StartsWith('build/.NETFramework/v4.8/',[StringComparison]::Ordinal) -or !$entry.Name){continue}
  $target=[IO.Path]::GetFullPath((Join-Path $cache $entry.FullName))
  if(!$target.StartsWith($reference+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid reference package path'}
  $null=[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
  [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$target,$true)
 }
}finally{$zip.Dispose()}
if(!(Test-Path -LiteralPath (Join-Path $reference 'mscorlib.dll'))){throw 'NET Framework 4.8 reference assemblies missing'}
return $reference
