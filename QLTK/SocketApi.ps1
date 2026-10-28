if (!('TGLL.SocketServer' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'SocketServer.cs') }
if (!('TGLL.AutoLogin' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'AutoLogin.cs') -ReferencedAssemblies System.dll,System.Core.dll,System.Drawing.dll }
if (!('TGLL.NativeBackend' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'NativeBackend.cs') -ReferencedAssemblies System.dll,System.Core.dll }
[TGLL.NativeBackend]::LogDirectory=Join-Path $PSScriptRoot 'logs'
if(!$UiTest){[TGLL.NativeBackend]::BiMaDataDirectory=Join-Path $script:dataDir 'bi-ma-counts'}
function Get-AccountJob([string]$id) {
    $job=[TGLL.NativeBackend]::Get($id)
    if ($null -eq $job) {$job=[TGLL.AutoLogin]::Get($id)}
    return $job
}
$script:socketServer=$null
$script:socketToken=''
function Normalize-Proxy($value) {
    if ($null -eq $value) { return $null }
    if ($value -is [string]) {
        $text=$value.Trim();$protocol='http';if($text -match '^(http|socks5)://'){$protocol=$Matches[1];$text=$text.Substring($Matches[0].Length)}
        $parts=$text.Split([char[]]':',4)
        if ($parts.Count -notin @(2,4)) {throw 'proxy_invalid: dùng host:port hoặc host:port:username:password.'}
        $userName=''; $password=''
        if ($parts.Count -eq 4) {$userName=$parts[2]; $password=$parts[3]}
        $value=[pscustomobject]@{type=$protocol;host=$parts[0];port=$parts[1];username=$userName;password=$password}
    }
    if ($value -isnot [pscustomobject] -and $value -isnot [hashtable]) { throw 'proxy_invalid: proxy phải là object hoặc null.' }
    if ($value.host -isnot [string]) { throw 'proxy_invalid: host phải là chuỗi.' }
    foreach ($field in @('username','password')) { if ($null -ne $value.$field -and $value.$field -isnot [string]) { throw 'proxy_invalid: username/password phải là chuỗi.' } }
    $protocol=([string]$value.type).ToLowerInvariant();if(!$protocol){$protocol='http'};if($protocol -notin @('http','socks5')){throw 'proxy_invalid: hỗ trợ HTTP hoặc SOCKS5.'}
    $hostName=([string]$value.host).Trim()
    if ($hostName.Length -gt 253 -or [Uri]::CheckHostName($hostName) -eq [UriHostNameType]::Unknown) { throw 'proxy_invalid: host không hợp lệ.' }
    if ([string]$value.port -notmatch '^[0-9]{1,5}$') { throw 'proxy_invalid: port phải là số 1–65535.' }
    $portNumber=[int]$value.port
    if ($portNumber -lt 1 -or $portNumber -gt 65535) { throw 'proxy_invalid: port phải là số 1–65535.' }
    $userName=[string]$value.username; $password=[string]$value.password
    if (($userName.Length -eq 0) -ne ($password.Length -eq 0)) { throw 'proxy_invalid: nhập đủ username/password proxy hoặc để trống cả hai.' }
    if ([Text.Encoding]::UTF8.GetByteCount($userName) -gt 255 -or [Text.Encoding]::UTF8.GetByteCount($password) -gt 255) { throw 'proxy_invalid: thông tin xác thực SOCKS5 quá dài.' }
    return [pscustomobject]@{type=$protocol;host=$hostName;port=$portNumber;username=$userName;password=$password}
}
function Protect-Proxy($proxy) {
    if ($null -eq $proxy) { return $null }
    $secret=''; if ($proxy.password.Length) { $secret=Protect-Password $proxy.password }
    [pscustomobject]@{type=$proxy.type;host=$proxy.host;port=$proxy.port;username=$proxy.username;secret=$secret}
}
function Read-Proxy($account) {
    if ($null -eq $account.proxy) { return $null }
    $password=''; if ($account.proxy.secret) { $password=Unprotect-Password $account.proxy.secret }
    Normalize-Proxy ([pscustomobject]@{type=$account.proxy.type;host=$account.proxy.host;port=$account.proxy.port;username=$account.proxy.username;password=$password})
}
function Proxy-Summary($proxy) {
    if ($null -eq $proxy) { return $null }
    [pscustomobject]@{type=$proxy.type;host=$proxy.host;port=$proxy.port;has_auth=([string]$proxy.username).Length -gt 0}
}
function Invoke-ProxyBridge($account,$proxy,[bool]$autoLogin=$false) {
    $bridge=[string]$script:settings.proxyBridge
    if ([string]::IsNullOrWhiteSpace($bridge) -or !(Test-Path -LiteralPath $bridge -PathType Leaf) -or [IO.Path]::GetExtension($bridge) -ne '.exe') {
        if ($null -eq $proxy -and $autoLogin) { throw 'login_bridge_required: cần bộ chuyển hỗ trợ tự điền đăng nhập cho game Unity. Game chưa được mở.' }
        throw 'proxy_bridge_required: game Unity chưa có tích hợp SOCKS5; cần cấu hình EXE bộ chuyển mạng hỗ trợ giao thức stdin tgll-launch-v1. Game chưa được mở.'
    }
    if ([IO.Path]::GetExtension([string]$script:settings.jar) -ne '.exe' -or !(Test-Path -LiteralPath $script:settings.jar -PathType Leaf)) { throw 'game_invalid: chọn EXE game hợp lệ.' }
    if ([IO.Path]::GetFullPath($bridge) -eq [IO.Path]::GetFullPath($script:settings.jar)) { throw 'proxy_bridge_invalid: bộ chuyển không được là chính EXE game.' }
    $payload=@{protocol='tgll-launch-v1';auto_login=$autoLogin;target=[IO.Path]::GetFullPath($script:settings.jar);working_directory=(Split-Path $script:settings.jar -Parent);account=@{username=$account.username;password=(Unprotect-Password $account.secret)};proxy=$proxy}
    $psi=New-Object Diagnostics.ProcessStartInfo
    $psi.FileName=[IO.Path]::GetFullPath($bridge); $psi.WorkingDirectory=Split-Path $psi.FileName -Parent
    $psi.UseShellExecute=$false; $psi.CreateNoWindow=$true
    $psi.RedirectStandardInput=$true; $psi.RedirectStandardOutput=$true; $psi.RedirectStandardError=$true
    $psi.StandardOutputEncoding=[Text.Encoding]::UTF8; $psi.StandardErrorEncoding=[Text.Encoding]::UTF8
    $process=New-Object Diagnostics.Process; $process.StartInfo=$psi
    try {
        $null=$process.Start()
        $output=$process.StandardOutput.ReadToEndAsync(); $errorOutput=$process.StandardError.ReadToEndAsync()
        $payloadJson=ConvertTo-Json -InputObject $payload -Depth 8 -Compress
        if ([Text.Encoding]::UTF8.GetByteCount($payloadJson) -gt 32768) { $process.Kill(); throw 'proxy_bridge_invalid: payload vượt 32 KiB.' }
        $writer=New-Object IO.StreamWriter($process.StandardInput.BaseStream,((New-Object System.Text.UTF8Encoding -ArgumentList $false)))
        try {
            $writer.AutoFlush=$true
            $writeTask=$writer.WriteLineAsync($payloadJson)
            if (!$writeTask.Wait(2000)) { $process.Kill(); throw 'proxy_bridge_timeout: bộ chuyển không đọc stdin.' }

        } finally { $writer.Dispose() }
        if (!$process.WaitForExit(12000)) { $process.Kill(); throw 'proxy_bridge_timeout: kết quả mở chưa xác định, kiểm tra game trước khi thử lại.' }
        if (!$output.Wait(1000) -or !$errorOutput.Wait(1000) -or $process.ExitCode -ne 0) { throw 'proxy_bridge_failed: bộ chuyển không trả kết quả thành công.' }
        try { $response=ConvertFrom-Json -InputObject $output.Result } catch { throw 'proxy_bridge_invalid: phản hồi phải là JSON.' }
        if ($response.ok -ne $true -or [string]$response.pid -notmatch '^[0-9]{1,10}$') { throw 'proxy_bridge_failed: cần ok, pid và proxy_applied=true.' }
        if ($null -ne $proxy -and $response.proxy_applied -ne $true) {throw 'proxy_bridge_failed: bộ chuyển chưa xác nhận áp dụng proxy.'}
        if ($autoLogin -and $response.login_applied -ne $true) {throw 'login_bridge_failed: bộ chuyển chưa xác nhận tự đăng nhập.'}
        $gameProcess=Get-Process -Id ([int]$response.pid) -ErrorAction Stop
        if ([IO.Path]::GetFullPath($gameProcess.MainModule.FileName) -ne [IO.Path]::GetFullPath($script:settings.jar)) { throw 'proxy_bridge_invalid: PID không thuộc EXE game đã chọn.' }
        [TGLL.ManagedGames]::Register($gameProcess,[string]$script:settings.jar)
        return [pscustomobject]@{pid=$gameProcess.Id;proxy_route=$(if ($null -ne $proxy) {'bridge_reported'} else {'none'});login_route=$(if ($autoLogin) {'bridge_reported'} else {'manual'})}
    } finally { $process.Dispose() }
}
$script:accountWindowPids=@{}
function Get-ActivityLaunchContext([string]$id){
 if(!$id){return ''}
 if([string]$script:beastLaunchingAccountId -eq $id){return 'beast'}
 if([string]$script:soncapLaunchingAccountId -eq $id){return 'soncap'}
 if([string]$script:izanamiLaunchingAccountId -eq $id){return 'izanami'}
 if([string]$script:dungeonLaunchingAccountId -eq $id){return 'dungeon'}
 if([string]$script:questLaunchingAccountId -eq $id){return 'quest'}
 return ''
}
function Get-AccountLaunchMode($account) {
    if(${function:Get-AutoAllStage}){$all=Get-AutoAllStage ([string]$account.id);if($all){return $all}}
    $queueStage=Get-ActivityLaunchContext ([string]$account.id);if($queueStage){return $queueStage}
    if([bool]$account.autoBeast){return 'beast'}
    if($script:accountJobs -and $script:accountJobs.ContainsKey([string]$account.id)){$bj=Get-AccountJob $script:accountJobs[[string]$account.id];if($bj -is [TGLL.NativeJob] -and $bj.BeastQuestEnabled -and !$bj.CloseRequested -and !$bj.CancelRequested -and $bj.State -notin @('closed','failed','cancelled') -and (Test-AccountOpenBlocked $account)){return 'beast'}}
    if($script:farmUpRequests -and $script:farmUpRequests.ContainsKey([string]$account.id)){return 'farm'}
    if([bool]$account.soncap){return 'soncap'}
    if($account.receiveKey -or $account.dungeonHigh){return 'dungeon'}
    if($account.izanami){return 'izanami'}
    if($account.autoQuest){return 'quest'}
    return 'normal'
}
function Get-LaunchQueueErrorCode($errorRecord) {
    $exception=$errorRecord.Exception
    while($exception){
        $message=[string]$exception.Message
        foreach($code in @('native_x64_required','game_invalid','login_length_invalid','login_queue_full','login_secret_unavailable')){if($message -match ('(^|[ :"' + [char]39 + '])'+[regex]::Escape($code)+'($|[ :"' + [char]39 + '])')){return $code}}
        if($exception -is [UnauthorizedAccessException]){return 'login_queue_access_denied'}
        if($exception -is [IO.IOException]){return 'login_queue_io_failed'}
        $exception=$exception.InnerException
    }
    return 'login_queue_failed'
}
function Write-LaunchQueueFailure($account,[string]$code,$errorRecord) {
    # Chỉ ghi mã lỗi, loại ngoại lệ và tên acc; không ghi đối số hoặc mật khẩu.
    if($UiTest){return}
    try{$folder=Join-Path $PSScriptRoot 'logs';$null=[IO.Directory]::CreateDirectory($folder)
        $record=@{time=[datetime]::UtcNow.ToString('o');event='login_queue_rejected';account=[string]$account.username;code=$code;exception_type=$errorRecord.Exception.GetType().FullName;host_64bit=([IntPtr]::Size -eq 8);game_exists=[IO.File]::Exists([string]$script:settings.jar)}
        [IO.File]::AppendAllText((Join-Path $folder 'launch-errors.jsonl'),((ConvertTo-Json $record -Compress)+[Environment]::NewLine),(New-Object Text.UTF8Encoding -ArgumentList $false))
    }catch{}
}
function Get-NativeLaunchCredentials($account,[string]$target) {
    if([IntPtr]::Size -ne 8){throw 'native_x64_required'}
    if(![IO.File]::Exists($target) -or [IO.Path]::GetFileName($target) -cne 'TheGioiLangLa.exe'){throw 'game_invalid'}
    try{$password=[string](Unprotect-Password ([string]$account.secret))}catch{throw 'login_secret_unavailable'}
    $username=[string]$account.username
    if([string]::IsNullOrWhiteSpace($username) -or [string]::IsNullOrEmpty($password) -or $username.Length -gt 128 -or $password.Length -gt 128 -or $username.IndexOf([char]0) -ge 0 -or $password.IndexOf([char]0) -ge 0){throw 'login_length_invalid'}
    return [pscustomobject]@{Username=$username;Password=$password}
}
function Start-ManagedAccount($account,$proxy,[bool]$autoLogin=$false) {
    [TGLL.NativeBackend]::SetLaunchDelayMs([int]([double]$script:settings.launchDelaySeconds*1000))
    $concurrency=6;if($script:settings.launchConcurrency){$concurrency=[Math]::Max(1,[Math]::Min(100,[int]$script:settings.launchConcurrency))};[TGLL.NativeBackend]::SetLaunchConcurrency($concurrency)
    if ($null -ne $proxy -and ![string]::IsNullOrWhiteSpace([string]$script:settings.proxyBridge)) {
        $bridgeResult=Invoke-ProxyBridge $account $proxy $autoLogin
        if($bridgeResult.pid -gt 0){$script:accountWindowPids[[string]$account.id]=[int]$bridgeResult.pid;if($script:accountJobs){$script:accountJobs.Remove([string]$account.id)}}
        return $bridgeResult
    }
    if ($autoLogin -or $null -ne $proxy) {
        $nativeProxy=$null
        if ($null -ne $proxy) {$nativeProxy=New-Object TGLL.SocksConfig([string]$proxy.host,[int]$proxy.port,[string]$proxy.username,[string]$proxy.password,[string]$proxy.type)}
        $allStage='';if(${function:Get-AutoAllStage}){$allStage=Get-AutoAllStage ([string]$account.id)}
        $queueStage=Get-ActivityLaunchContext ([string]$account.id);$launchStage=if($allStage){$allStage}else{$queueStage}
        $stageReceive=if($launchStage){$launchStage -eq 'dungeon' -and ([bool]$account.receiveKey -or [bool]$allStage)}else{[bool]$account.receiveKey};$stageDungeon=if($launchStage){$launchStage -eq 'dungeon'}else{[bool]$account.dungeonHigh};$stageIzanami=if($launchStage){$launchStage -eq 'izanami'}else{[bool]$account.izanami}
        $dungeonQueueLaunch=([string]$script:dungeonLaunchingAccountId -eq [string]$account.id)
        $dailyUp=([bool]$account.soncap -or [string]$script:beastLaunchingAccountId -eq [string]$account.id);if(!$allStage -and (Get-Command Sync-DailyPriorityPreferences -ErrorAction SilentlyContinue) -and $script:farmUpRequests.ContainsKey([string]$account.id)){$dailyUp=$true};if($launchStage){$dailyUp=$launchStage -in @('soncap','beast')}
        try {$credentials=Get-NativeLaunchCredentials $account ([string]$script:settings.jar);$job=[TGLL.NativeBackend]::Enqueue([string]$script:settings.jar,[string]$credentials.Username,[string]$credentials.Password,$nativeProxy,$autoLogin,($stageReceive -and !$dailyUp),($stageDungeon -and !$dailyUp),($stageIzanami -and !$dungeonQueueLaunch -and !$dailyUp),([string]$script:izanamiLaunchingAccountId -eq [string]$account.id),([string]$script:questLaunchingAccountId -eq [string]$account.id),([string]$script:mailLaunchingAccountId -eq [string]$account.id),([string]$script:cloakLaunchingAccountId -eq [string]$account.id))} catch {$queueCode=Get-LaunchQueueErrorCode $_;Write-LaunchQueueFailure $account $queueCode $_;throw ($queueCode+': '+(Get-LoginErrorText $queueCode))} finally {if($credentials){$credentials.Password=$null}}
        $job.UpRequested=!$allStage -and !$job.CloakQueueOnly -and $script:farmUpRequests.ContainsKey([string]$account.id)
        if([string]$script:beastLaunchingAccountId -eq [string]$account.id){[TGLL.NativeBackend]::SetAutoBeast($job.Id,$true)}
        $job.DungeonQueueOnly=$dungeonQueueLaunch
        [TGLL.NativeBackend]::SetCloseAfterKey($job.Id,([bool]$account.closeAfterKey -and !$dungeonQueueLaunch -and ![bool]$account.soncap))
        if($script:accountJobs -and $script:accountJobs.ContainsKey([string]$account.id)){[TGLL.NativeBackend]::RebindIzanamiJob([string]$script:accountJobs[[string]$account.id],$job.Id)}
        if($null -ne $script:accountJobs){$script:accountJobs[[string]$account.id]=$job.Id}
        return [pscustomobject]@{pid=0;proxy_route=$(if($null -ne $proxy){'native_tcp_queued'}else{'none'});login_route=$(if($autoLogin){'native_queued'}else{'manual'});job_id=$job.Id}
    }
    $info=Get-LaunchInfo $account $script:settings
    if([double]$script:settings.launchDelaySeconds -gt 0 -and [IO.Path]::GetExtension([string]$script:settings.jar) -eq '.exe'){
        $job=[TGLL.NativeBackend]::EnqueueManual($info,[string]$script:settings.jar,[string]$account.username);$script:accountJobs[[string]$account.id]=$job.Id
        return [pscustomobject]@{pid=0;proxy_route='none';login_route='manual_queued';job_id=$job.Id}
    }
    $process=[Diagnostics.Process]::Start($info)
    try { [TGLL.ManagedGames]::Register($process,[string]$script:settings.jar);$script:accountWindowPids[[string]$account.id]=$process.Id;if($script:accountJobs){$script:accountJobs.Remove([string]$account.id)}; return [pscustomobject]@{pid=$process.Id;proxy_route='none';login_route='manual'} } finally { $process.Dispose() }
}
function Build-CredentialAccount($inputValue,$existing) {
    if ($inputValue -isnot [pscustomobject]) { throw 'invalid_request: tài khoản phải là object.' }
    if ($inputValue.username -isnot [string] -or [string]::IsNullOrWhiteSpace($inputValue.username) -or $inputValue.username.Length -gt 256) { throw 'invalid_request: username phải là chuỗi 1-256 ký tự.' }
    if ($inputValue.password -isnot [string] -or $inputValue.password.Length -eq 0 -or $inputValue.password.Length -gt 4096) { throw 'invalid_request: password phải là chuỗi 1-4096 ký tự.' }
    $proxyStored=$null; $id=[guid]::NewGuid().ToString()
    if ($null -ne $existing) { $id=$existing.id; $proxyStored=$existing.proxy }
    if ($null -ne $inputValue.PSObject.Properties['proxy']) { $proxyStored=Protect-Proxy (Normalize-Proxy $inputValue.proxy) }
    $entry=[pscustomobject]@{id=$id;username=$inputValue.username.Trim();secret=(Protect-Password $inputValue.password);proxy=$proxyStored}
    if($null -ne $existing){foreach($property in $existing.PSObject.Properties){if($property.Name -notin @('id','username','secret','proxy')){$entry | Add-Member NoteProperty $property.Name $property.Value}}}
    return $entry
}
function Prepare-AccountBatch($items) {
    $byName=@{}; $positions=@{}; $updated=New-Object 'System.Collections.Generic.List[object]'
    foreach($a in $script:accounts) {$byName[$a.username]=$a; $positions[$a.id]=$updated.Count; $updated.Add($a)}
    $seen=@{}; $changed=New-Object 'System.Collections.Generic.List[object]'
    foreach($item in @($items)) {
        $name=([string]$item.username).Trim()
        if ($seen.ContainsKey($name)) { throw 'invalid_request: username bị trùng trong lần nhập này.' }
        $seen[$name]=$true
        $existing=$byName[$name]
        $entry=Build-CredentialAccount $item $existing
        if ($null -ne $existing) {$updated[[int]$positions[$existing.id]]=$entry}
        else {$updated.Add($entry)}
        $changed.Add($entry)
    }
    [pscustomobject]@{updated=$updated.ToArray();changed=$changed.ToArray()}
}
function Commit-AccountBatch($plan) {
    $old=$script:accounts; $script:accounts=@($plan.updated)
    try {Save-Accounts} catch {$script:accounts=$old; throw 'save_failed: không lưu được danh sách tài khoản.'}
    if (Get-Command Refresh-Grid -ErrorAction SilentlyContinue) {Refresh-Grid}
}
function Credential-Summary($account) {
    [pscustomobject]@{id=$account.id;username=$account.username;proxy=(Proxy-Summary $account.proxy)}
}
function Handle-SocketJson([string]$json) {
    try { $request=ConvertFrom-Json -InputObject $json -ErrorAction Stop } catch { return '{"ok":false,"error":"invalid_json"}' }
    if ($null -eq $request -or $request -is [array] -or !($request -is [pscustomobject])) { return '{"ok":false,"error":"invalid_request"}' }
    if (!($request.token -is [string]) -or [string]::IsNullOrEmpty($script:socketToken) -or $request.token -cne $script:socketToken) { return '{"ok":false,"error":"unauthorized"}' }
    try {
        switch -CaseSensitive ([string]$request.command) {
            'ping' { $result=@{ok=$true;version=89;proxy_types=@('http','socks5');login_backend='native';character_number=1;character_backend='native';npc_backend='native_entity_id';npc_capture=$true;dynamic_menu_log=$true;npc_name='Raikage Đệ Tam';npc_action='Nhận chìa khóa';train_backend='native_skill_bar_targeted';train_popup_independent=$true} }
            'game_auto' {
                if($request.enabled -isnot [bool]){throw 'invalid_request: enabled phải là true/false.'}
                [TGLL.NativeBackend]::SetGameAuto([string]$request.job_id,[bool]$request.enabled)
                $result=@{ok=$true;queued=$true;enabled=$request.enabled}
            }
            'game_settings' {
                $job=[TGLL.NativeBackend]::Get([string]$request.job_id)
                if($null -eq $job){throw 'invalid_request: job_id không tồn tại.'}
                $items=@($job.GameOptions | ForEach-Object {@{tab=$_.Tab;code=$_.Code;kind=$_.Kind;label=$_.Label;value=$_.Value;choices=@($_.Choices)}})
                $result=@{ok=$true;ready=$job.GameSettingsReady;auto=$job.GameAutoEnabled;auto_wanted=$job.GameAutoWanted;auto_status=$job.GameAutoStatus;options=$items;error=$job.SettingsError}
            }
            'game_option' {
                if($request.code -isnot [int] -or $request.value -isnot [int]){throw 'invalid_request: code/value phải là số nguyên.'}
                [TGLL.NativeBackend]::SetGameOption([string]$request.job_id,[int]$request.code,[int]$request.value)
                $result=@{ok=$true;queued=$true}
            }
            'skills' {
                [TGLL.NativeBackend]::ScanSkills([string]$request.job_id)
                $job=[TGLL.NativeBackend]::Get([string]$request.job_id)
                $result=@{ok=$true;items=@($job.Skills | ForEach-Object {@{id=$_.Id;template_id=$_.TemplateId;name=$_.Name;level=$_.Level;attack=$_.Attack;usable=$_.Usable}})}
            }
            'train_start' {
                $ids=@();if($null -ne $request.skill_ids){$ids=@($request.skill_ids)}else{$ids=@($request.skill_id)}
                if($ids.Count -lt 1 -or $ids.Count -gt 4){throw 'invalid_request: chọn 1 đến 4 chiêu.'}
                foreach($value in $ids){if([string]$value -notmatch '^[0-9]{1,5}$'){throw 'invalid_request: cần skill_ids đọc từ skills.'}}
                [TGLL.NativeBackend]::StartTraining([string]$request.job_id,[int[]]$ids)
                $result=@{ok=$true;job_id=[string]$request.job_id;status='train_started'}
            }
            'train_stop' {
                if ($null -eq [TGLL.NativeBackend]::Get([string]$request.job_id)) {throw 'invalid_request: job_id không tồn tại.'}
                [TGLL.NativeBackend]::StopTraining([string]$request.job_id)
                $result=@{ok=$true;job_id=[string]$request.job_id;status='train_stopped'}
            }
            'npc_replay' {
                [TGLL.NativeBackend]::ReplayCapturedNpc([string]$request.job_id)
                $result=@{ok=$true;job_id=[string]$request.job_id;status='npc_replay_queued'}
            }
            'dynamic_menu' {
                $job=Get-AccountJob ([string]$request.job_id)
                if ($null -eq $job -or !$job.PSObject.Properties['CapturedNpcId']) {throw 'invalid_request: job_id không tồn tại hoặc chưa dùng bộ nối trực tiếp.'}
                $result=@{ok=$true;npc_id=$job.CapturedNpcId;template_id=$job.CapturedTemplateId;npc_name=$job.CapturedNpcName;tag=$job.MenuTag;selected_index=$job.MenuSelectedIndex;options=@($job.MenuOptions);log_file=$job.LogFile;log_error=$job.LogError}
            }
            'job' {
                $job=Get-AccountJob ([string]$request.job_id)
                if ($null -eq $job) {throw 'invalid_request: job_id không tồn tại.'}
                $result=@{ok=$true;job_id=$job.Id;status=$job.State;pid=$job.ProcessId;error=$job.Error}
                if ($job.PSObject.Properties['TrainEnabled']) {$result.train_enabled=$job.TrainEnabled;$result.train_skill_ids=@($job.TrainSkillIds);$result.train_skill_id=$job.TrainSkillId;$result.train_attempts=$job.TrainAttempts;$result.train_status=$job.TrainStatus;$result.train_target_id=$job.TrainTargetId;$result.train_target_hp=$job.TrainTargetHp}
                if ($job.PSObject.Properties['GemEnabled']) {$result.gem_enabled=$job.GemEnabled;$result.gem_status=$job.GemStatus;$result.gem_completed=$job.GemCompleted;$result.gem_skipped=$job.GemSkipped;$result.auto_item_enabled=$job.AutoItemEnabled}
                if ($job.PSObject.Properties['ReadyPhase']) {$result.ready_phase=$job.ReadyPhase}
                if ($job.PSObject.Properties['NpcCount']) {$result.npc_count=$job.NpcCount}
                if ($job.PSObject.Properties['NpcId']) {$result.npc_id=$job.NpcId;$result.npc_template_id=$job.NpcTemplateId;$result.npc_candidates=$job.NpcDetails;$result.stage=$job.Stage}
                if ($job.PSObject.Properties['CapturedNpcId']) {$result.captured_npc_id=$job.CapturedNpcId;$result.captured_template_id=$job.CapturedTemplateId;$result.captured_npc_name=$job.CapturedNpcName;$result.dynamic_menu=$job.DynamicMenuJson;$result.log_file=$job.LogFile;$result.log_error=$job.LogError}
            }
            'list' {
                $items=@($script:accounts | ForEach-Object { [pscustomobject]@{id=$_.id;username=$_.username;proxy=(Proxy-Summary $_.proxy)} })
                $result=@{ok=$true;accounts=$items}
            }
            'upsert_account' {
                $plan=Prepare-AccountBatch @($request); Commit-AccountBatch $plan
                $result=@{ok=$true;account=(Credential-Summary $plan.changed[0])}
            }
            'import_accounts' {
                if ($request.accounts -isnot [array] -or $request.accounts.Count -lt 1 -or $request.accounts.Count -gt 200) {throw 'invalid_request: accounts phải là mảng 1-200 tài khoản mỗi yêu cầu.'}
                $plan=Prepare-AccountBatch $request.accounts; Commit-AccountBatch $plan
                $result=@{ok=$true;count=$plan.changed.Count;accounts=@($plan.changed | ForEach-Object {Credential-Summary $_})}
            }
            'set_proxy' {
                $matches=@($script:accounts | Where-Object { $_.id -ceq [string]$request.account_id })
                if ($matches.Count -ne 1) { throw 'account_not_found: account_id không tồn tại.' }
                if ($null -eq $request.PSObject.Properties['proxy']) { throw 'proxy_missing: cần trường proxy, hoặc null để xóa.' }
                $account=$matches[0]; $proxy=Normalize-Proxy $request.proxy; $old=$account.proxy
                $account | Add-Member -NotePropertyName proxy -NotePropertyValue (Protect-Proxy $proxy) -Force
                try { Save-Accounts } catch { $account.proxy=$old; throw 'save_failed: không lưu được proxy.' }
                if (Get-Command Refresh-Grid -ErrorAction SilentlyContinue) { Refresh-Grid }
                $result=@{ok=$true;account_id=$account.id;proxy=(Proxy-Summary $proxy)}
            }
            'open' {
                if ($null -ne $request.PSObject.Properties['dry_run'] -and $request.dry_run -isnot [bool]) { throw 'invalid_request: dry_run phải là boolean.' }
                if ($null -ne $request.PSObject.Properties['save_account'] -and $request.save_account -isnot [bool]) { throw 'invalid_request: save_account phải là boolean.' }
                $direct=($null -ne $request.PSObject.Properties['username'] -or $null -ne $request.PSObject.Properties['password'])
                if ($direct) {
                    if ($null -ne $request.PSObject.Properties['account_id']) {throw 'invalid_request: dùng account_id hoặc username/password, không dùng cả hai.'}
                    $plan=Prepare-AccountBatch @($request); $account=$plan.changed[0]
                    if ($request.save_account -eq $true -and $request.dry_run -ne $true) {Commit-AccountBatch $plan}
                } else {
                    if ($request.save_account -eq $true) {throw 'invalid_request: save_account chỉ dùng khi gửi username/password.'}
                    $matches=@($script:accounts | Where-Object { $_.id -ceq [string]$request.account_id })
                    if ($matches.Count -ne 1) { throw 'account_not_found: account_id không tồn tại.' }
                    $account=$matches[0]
                }
                if ($null -ne $request.PSObject.Properties['proxy']) { $proxy=Normalize-Proxy $request.proxy } else { $proxy=Read-Proxy $account }
                $autoLogin=$direct
                if ($null -ne $request.PSObject.Properties['auto_login']) {
                    if ($request.auto_login -isnot [bool]) {throw 'invalid_request: auto_login phải là boolean.'}
                    $autoLogin=$request.auto_login
                }
                if ($request.dry_run -eq $true) {
                    $result=@{ok=$true;dry_run=$true;account_id=$account.id;target=$script:settings.jar;proxy=(Proxy-Summary $proxy);auto_login=$autoLogin;bridge_required=$false;bridge_configured=$true;login_backend='native'}
                } else {
                    $launched=Start-ManagedAccount $account $proxy $autoLogin
                    if ($launched.job_id -and $null -ne $script:accountJobs) {$script:accountJobs[$account.id]=$launched.job_id; $script:lastLoginJob=$launched.job_id}
                    $result=@{ok=$true;account_id=$account.id;pid=$launched.pid;proxy=(Proxy-Summary $proxy);proxy_route=$launched.proxy_route;login_route=$launched.login_route;job_id=$launched.job_id}
                }
            }
            default { throw 'unknown_command: dùng ping, list, upsert_account, import_accounts, set_proxy hoặc open.' }
        }
        return ConvertTo-Json -InputObject $result -Depth 8 -Compress
    } catch {
        $message=$_.Exception.Message
        # Never return arbitrary OS/bridge errors: they may include private data.
        if ($message -match '^(proxy_[a-z_]+|login_[a-z_]+|account_not_found|invalid_request|game_invalid|unknown_command|save_failed):') {
            $code=$message.Split(':')[0]; return ConvertTo-Json -InputObject @{ok=$false;error=$code;message=$message} -Compress
        }
        return '{"ok":false,"error":"operation_failed","message":"Không thực hiện được lệnh. Kiểm tra dữ liệu và cấu hình trong QLTK."}'
    }
}
function Poll-SocketRequests {
    if ($null -eq $script:socketServer) { return }
    for ($i=0;$i -lt 8;$i++) {
        $item=$null
        if (!$script:socketServer.TryTake([ref]$item)) { break }
        if ($item.Cancelled -or [DateTime]::UtcNow -gt $item.Deadline) { continue }
        try { $item.Reply((Handle-SocketJson $item.Json)) } catch { $item.Reply('{"ok":false,"error":"internal_error"}') }
    }
}
function Start-SocketApi([int]$portNumber) {
    if ($null -ne $script:socketServer) { throw 'Socket đã chạy; dừng trước khi đổi port.' }
    $server=New-Object TGLL.SocketServer($portNumber)
    try { $server.Start(); $script:socketServer=$server } catch { $server.Dispose(); throw }
}
function Stop-SocketApi {
    if ($null -ne $script:socketServer) {$script:socketServer.Dispose();$script:socketServer=$null}
}
