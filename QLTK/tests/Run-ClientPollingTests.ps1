$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$source=[IO.File]::ReadAllText((Join-Path $root 'NativeBackend.cs'))+[Environment]::NewLine+[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'ClientPollingTests.cs'))
Add-Type -TypeDefinition $source -ReferencedAssemblies System.dll,System.Core.dll
$logs=Join-Path ([IO.Path]::GetTempPath()) ('QLTK-poll-tests-'+[guid]::NewGuid().ToString('N'))
try{[TGLL.ClientPollingTests]::Run($logs)}finally{if(Test-Path -LiteralPath $logs){Get-ChildItem -LiteralPath $logs -File | Remove-Item -Force;Remove-Item -LiteralPath $logs}}
