$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$source=[IO.File]::ReadAllText((Join-Path $root 'NativeBackend.cs'))+[Environment]::NewLine+[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'SettingsHookTests.cs'))
Add-Type -TypeDefinition $source -ReferencedAssemblies System.dll,System.Core.dll
[TGLL.SettingsHookTests]::Run()
