if(!(Get-Command Read-JsonData -ErrorAction SilentlyContinue)){. (Join-Path $PSScriptRoot 'JsonData.ps1')}
# Dữ liệu portable: giữ nguyên mật khẩu dạng rõ, không mã hóa mới.
function Protect-Password([string]$value) {return $value}
function Test-LegacySecret([string]$value) {
    try {
        $bytes=[Convert]::FromBase64String($value)
        $magic=[byte[]]@(1,0,0,0,208,140,157,223,1,21,209,17,140,122,0,192,79,194,151,235)
        if($bytes.Length -lt $magic.Length){return $false}
        for($i=0;$i -lt $magic.Length;$i++){if($bytes[$i] -ne $magic[$i]){return $false}}
        return $true
    }catch{return $false}
}
function Unprotect-LegacyPassword([string]$value) {
    return [Text.Encoding]::UTF8.GetString([Security.Cryptography.ProtectedData]::Unprotect([Convert]::FromBase64String($value),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser))
}
function Unprotect-Password([string]$value) {
    if(!(Test-LegacySecret $value)){return $value}
    try {return Unprotect-LegacyPassword $value}
    catch {throw 'Dữ liệu cũ được mã hóa trên máy khác. Chạy v84 trên máy gốc để chuyển đổi trước, hoặc nhập lại tài khoản trên VPS.'}
}
function Convert-PortableAccounts($accounts) {
    $result=New-Object 'System.Collections.Generic.List[object]'
    foreach($account in @($accounts)){
        if($null -eq $account){continue}
        # Sao chép trước: nếu một mật khẩu cũ lỗi, dữ liệu nguồn vẫn nguyên vẹn.
        $copy=[TGLL.StoredJson]::Parse((ConvertTo-Json -InputObject $account -Depth 16))
        if($copy.PSObject.Properties['secret']){$copy.secret=Unprotect-Password ([string]$copy.secret)}
        if($copy.proxy -and $copy.proxy.PSObject.Properties['secret']){$copy.proxy.secret=Unprotect-Password ([string]$copy.proxy.secret)}
        $result.Add($copy)
    }
    return $result.ToArray()
}
function Initialize-PortableData {
    $accountSource=if(Test-Path -LiteralPath $script:storePath){$script:storePath}else{Join-Path $script:legacyDataDir 'accounts.json'}
    if(Test-Path -LiteralPath $accountSource){
        $source=Read-JsonData $accountSource
        $converted=@(Convert-PortableAccounts @($source))
        if($accountSource -ne $script:storePath -or (ConvertTo-Json -InputObject @($source) -Depth 16 -Compress) -cne (ConvertTo-Json -InputObject $converted -Depth 16 -Compress)){
            Write-JsonAtomic $script:storePath $converted
        }
    }
    if(!(Test-Path -LiteralPath $script:configPath)){
        $oldConfig=Join-Path $script:legacyDataDir 'settings.json'
        if(Test-Path -LiteralPath $oldConfig){$settings=Read-JsonData $oldConfig;Write-JsonAtomic $script:configPath $settings}
    }
    $tokenPath=Join-Path $script:dataDir 'api-token.json'
    $tokenSource=if(Test-Path -LiteralPath $tokenPath){$tokenPath}else{Join-Path $script:legacyDataDir 'api-token.json'}
    if(Test-Path -LiteralPath $tokenSource){
        $oldToken=Get-Content -LiteralPath $tokenSource -Raw -Encoding UTF8 | ConvertFrom-Json
        try {$token=Unprotect-Password ([string]$oldToken.secret)}catch{
            # Token API có thể tạo lại; không thay đổi mật khẩu tài khoản khi lỗi.
            $bytes=New-Object byte[] 32;$rng=[Security.Cryptography.RandomNumberGenerator]::Create()
            try{$rng.GetBytes($bytes);$token=[Convert]::ToBase64String($bytes)}finally{$rng.Dispose()}
        }
        if($tokenSource -ne $tokenPath -or $token -cne [string]$oldToken.secret){Write-JsonAtomic $tokenPath @{secret=$token}}
    }
}
