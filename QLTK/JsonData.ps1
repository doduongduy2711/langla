if(!('TGLL.StoredJson' -as [type])){Add-Type -Path (Join-Path $PSScriptRoot 'StoredJson.cs') -ReferencedAssemblies System.dll,System.Web.Extensions.dll,([System.Management.Automation.PSObject].Assembly.Location)}
function Read-JsonData([string]$path){return ,([TGLL.StoredJson]::Read($path))}
