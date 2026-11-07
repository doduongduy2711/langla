param([string]$DataDirectory='')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing,System.Web.Extensions,System.Security
if(!$DataDirectory){$DataDirectory=Join-Path (Split-Path $PSScriptRoot -Parent) 'Data'}
Add-Type -Path (Join-Path $PSScriptRoot 'ClientTabs.cs') -ReferencedAssemblies System.dll,System.Core.dll,System.Drawing.dll,System.Windows.Forms.dll,System.Web.Extensions.dll,System.Security.dll
[Windows.Forms.Application]::EnableVisualStyles()
[TGLL.ClientTabs.WindowHost]::ConfigureDpi()
$hasLock=$false
$hash=[Security.Cryptography.SHA256]::Create()
try{$key=[BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes([IO.Path]::GetFullPath($DataDirectory).ToLowerInvariant()))).Replace('-','')}finally{$hash.Dispose()}
$mutex=New-Object Threading.Mutex($false,('Local\QLTKClientTabs-'+$key))
try{
    try{$hasLock=$mutex.WaitOne(0)}catch [Threading.AbandonedMutexException]{$hasLock=$true}
    if(!$hasLock){[Windows.Forms.MessageBox]::Show('Cửa sổ gom client đang mở.','QLTK • Client theo tab') | Out-Null;return}
    [Windows.Forms.Application]::Run((New-Object TGLL.ClientTabs.TabsForm($DataDirectory)))
}catch{[Windows.Forms.MessageBox]::Show($_.Exception.Message,'QLTK • Client theo tab') | Out-Null}
finally{if($hasLock){$mutex.ReleaseMutex()};$mutex.Dispose()}
