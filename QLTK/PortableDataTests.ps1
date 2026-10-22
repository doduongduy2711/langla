$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Security
. (Join-Path $PSScriptRoot 'PortableData.ps1')
# Dùng chính hàm ghi atomic của ứng dụng; không khởi động giao diện.
$parseTokens=$null;$parseErrors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'AccountManager.ps1'),[ref]$parseTokens,[ref]$parseErrors)
$writer=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Write-JsonAtomic'},$true)
Invoke-Expression $writer.Extent.Text
function Check($condition,$message){if(!$condition){throw $message}}
function Legacy($value){return [Convert]::ToBase64String([Security.Cryptography.ProtectedData]::Protect([Text.Encoding]::UTF8.GetBytes($value),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser))}
try{$null=Legacy 'profile-probe'}catch{
    # Sandbox không có profile DPAPI: chỉ giả lập bộ giải mã, vẫn chạy toàn bộ luồng file.
    $script:legacyFixtures=@{}
    function Legacy($value){
        $header=[byte[]]@(1,0,0,0,208,140,157,223,1,21,209,17,140,122,0,192,79,194,151,235)
        $blob=[Convert]::ToBase64String([byte[]]($header+[Text.Encoding]::UTF8.GetBytes($value)))
        $script:legacyFixtures[$blob]=$value;return $blob
    }
    function Unprotect-LegacyPassword([string]$value){if(!$script:legacyFixtures.ContainsKey($value)){throw 'fixture decode failed'};return $script:legacyFixtures[$value]}
    Write-Output 'NOTE: Windows profile unavailable; legacy DPAPI decoder mocked for migration tests.'
}
$script:legacyDataDir=Join-Path ([IO.Path]::GetTempPath()) ('qltk-portable-test-'+[guid]::NewGuid().ToString())
$script:dataDir=Join-Path $script:legacyDataDir 'portable'
$script:storePath=Join-Path $script:dataDir 'accounts.json';$script:configPath=Join-Path $script:dataDir 'settings.json'
$password='demo-only Unicode 漢字';$proxyPassword='fixture-proxy-only'
$oldSecret=Legacy $password
Check (Test-LegacySecret $oldSecret) 'Legacy blob not detected'
Check ((Protect-Password $password) -ceq $password) 'New value encrypted'
Check ((Unprotect-Password 'c2FtcGxl') -ceq 'c2FtcGxl') 'Ordinary base64 password changed'
$source=@([pscustomobject]@{id='fixture-id';username='fixture';secret=$oldSecret;proxy=[pscustomobject]@{type='socks5';host='example.invalid';port=1234;username='fixture';secret=(Legacy $proxyPassword)};trainSkills=@(295,1);autoQuest=$true})
$sourcePath=Join-Path $script:legacyDataDir 'accounts.json'
Write-JsonAtomic $sourcePath $source
Write-JsonAtomic (Join-Path $script:legacyDataDir 'settings.json') @{questMaxOnline=12;autoReconnect=$false}
Write-JsonAtomic (Join-Path $script:legacyDataDir 'api-token.json') @{secret=(Legacy ('fixture-api-token-'+('x'*32)))}
$before=[IO.File]::ReadAllText($sourcePath)
Initialize-PortableData
$migrated=@(Get-Content $script:storePath -Raw -Encoding UTF8 | ConvertFrom-Json)
Check ($migrated.Count -eq 1 -and $migrated[0].secret -ceq $password -and $migrated[0].proxy.secret -ceq $proxyPassword) 'Passwords not migrated'
Check ($migrated[0].id -eq 'fixture-id' -and ($migrated[0].trainSkills -join ',') -eq '295,1' -and $migrated[0].autoQuest) 'Preferences changed'
Check ([IO.File]::ReadAllText($sourcePath) -ceq $before) 'Source overwritten'
$token=Get-Content (Join-Path $script:dataDir 'api-token.json') -Raw -Encoding UTF8 | ConvertFrom-Json
Check ($token.secret -ceq ('fixture-api-token-'+('x'*32))) 'API token not portable'
$portableBefore=[IO.File]::ReadAllText($script:storePath)
Initialize-PortableData
Check ([IO.File]::ReadAllText($script:storePath) -ceq $portableBefore) 'Repeated conversion changed portable file'
Check ($source[0].secret -ceq $oldSecret) 'Conversion mutated source object'
# Blob hỏng có header DPAPI phải báo lỗi, không coi là mật khẩu rõ.
$badBytes=[Convert]::FromBase64String($oldSecret);$badBytes[$badBytes.Length-1]=$badBytes[$badBytes.Length-1] -bxor 255
$bad=ConvertFrom-Json (ConvertTo-Json $source[0] -Depth 8);$bad.secret=[Convert]::ToBase64String($badBytes)
$script:dataDir=Join-Path $script:legacyDataDir 'failed-conversion';$script:storePath=Join-Path $script:dataDir 'accounts.json';$script:configPath=Join-Path $script:dataDir 'settings.json'
Write-JsonAtomic $sourcePath @($bad);$badBefore=[IO.File]::ReadAllText($sourcePath);$rejected=$false
try{Initialize-PortableData}catch{$rejected=$true}
Check ($rejected -and !(Test-Path $script:storePath) -and [IO.File]::ReadAllText($sourcePath) -ceq $badBefore) 'Failed conversion overwrote credentials'
Write-Output 'PASS: plaintext storage, legacy account/proxy/token conversion, preferences, source preservation, idempotence and failed migration without overwrite.'
