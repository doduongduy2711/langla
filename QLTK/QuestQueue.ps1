# Queue membership is a snapshot of selected table rows. Capacity is held for a whole team.
$script:questBatches=@();$script:questQueuePaused=$false;$script:questWaiting=@{};$script:questAccountIndex=@{}
function Get-QuestQueuedMember($id) {
    if($script:questWaiting.ContainsKey([string]$id)){return $script:questWaiting[[string]$id]}
    return $null
}
function Get-QuestMemberJob($member) {if($member.job){return Get-AccountJob $member.job};return $null}
function Start-QuestMember($member) {
    $a=$script:questAccountIndex[$member.id]
    if(!$a){throw 'Tài khoản trong hàng chờ đã bị xóa.'}
    if(Test-AccountOpenBlocked $a){
        if(!$script:accountJobs.ContainsKey($member.id)){throw 'Game mở thủ công chưa hỗ trợ tài khoản tự động.'}
        $member.job=$script:accountJobs[$member.id];$j=Get-AccountJob $member.job;$j.QuestQueueOnly=$true;$j.IzanamiQueueOnly=$false;[TGLL.NativeBackend]::SetReceiveKey($member.job,$false);[TGLL.NativeBackend]::SetDungeonHigh($member.job,$false);[TGLL.NativeBackend]::SetCloseAfterKey($member.job,$false);[TGLL.NativeBackend]::SetIzanami($member.job,$false);[TGLL.NativeBackend]::SetAutoQuest($member.job,$true);return
    }
    $script:questLaunchingAccountId=$member.id
    try{Open-Account $a}finally{$script:questLaunchingAccountId=''}
    if(!$script:accountJobs.ContainsKey($member.id)){throw 'Cần bật đăng nhập trực tiếp để chạy hàng chờ.'}
    $member.job=$script:accountJobs[$member.id]
}
function Begin-QuestTeamClose($batch) {return [TGLL.NativeBackend]::BeginAutoCloseBatch([string[]]@($batch.members | ForEach-Object {$_.job}))}
function Test-QuestMemberRunning($member) {
    $j=Get-QuestMemberJob $member
    return ($null -ne $j -and $j.ProcessId -gt 0 -and [TGLL.ManagedGames]::IsRunning($j.ProcessId))
}
function Add-QuestQueue($accounts) {
    if(Get-Command Assert-BeastAccountsAvailable -ErrorAction SilentlyContinue){Assert-BeastAccountsAvailable $accounts}
    if($script:distributionRun -and $script:distributionRun.active){throw 'Dừng auto chia đồ trước.'}
    foreach($a in $accounts){foreach($b in $script:soncapBatches){if($b.state -ne 'done' -and @($b.members | Where-Object {$_.id -eq [string]$a.id}).Count){throw 'Tài khoản đang trong hàng chờ Sơn Cáp.'}}}
    if($script:mailRun -and $script:mailRun.active){throw 'Dừng hàng chờ gom đồ trước khi chạy hàng chờ khác.'}
    if(!$autoLoginCheck.Checked){throw 'Bật Đăng nhập trực tiếp trước khi chạy hàng chờ nhiệm vụ.'}
    foreach($a in $accounts){foreach($b in $script:izanamiBatches){if($b.state -ne 'done' -and @($b.members | Where-Object {$_.id -eq [string]$a.id}).Count){throw 'Tài khoản đang có trong hàng chờ cấm thuật. Hủy hàng chờ đó trước.'}}}
    foreach($a in $accounts){foreach($b in $script:dungeonBatches){if($b.state -ne 'done' -and @($b.members | Where-Object {$_.id -eq [string]$a.id}).Count){throw 'Tài khoản đang trong hàng chờ địa cung.'}}}
    $known=@{};foreach($b in $script:questBatches){foreach($m in $b.members){$known[$m.id]=$true}}
    $fresh=@($accounts | Where-Object {!$known.ContainsKey([string]$_.id)})
    if(!$fresh.Count){$status.Text='Các tài khoản đã có trong hàng chờ nhiệm vụ.';return}
    $tail=$null
    foreach($a in $fresh){
        $a | Add-Member NoteProperty autoQuest $true -Force
        if($null -eq $tail -or $tail.members.Count -eq 1){$tail=[pscustomobject]@{members=@();state='waiting';error='';closeOperation=$null};$script:questBatches+=@($tail)}
        $tail.members+=@([pscustomobject]@{id=[string]$a.id;job=''})
    }
    if(!$UiTest){Save-Accounts};$script:questQueuePaused=$false;Pump-QuestQueue
}
function Reset-QuestQueue {
    # An already-started close operation may finish. Cancelling prevents new launches.
    foreach($b in $script:questBatches){foreach($m in $b.members){if($m.job){$j=Get-AccountJob $m.job;if($j -and $j.State -eq 'failed'){$j.CancelRequested=$true}}}}
    $script:questQueuePaused=$true;$script:questBatches=@();$script:questWaiting=@{}
}
function Pump-QuestQueue {
    if($script:questQueuePaused -or !$script:questBatches.Count){return}
    $script:questAccountIndex=@{};foreach($a in $script:accounts){$script:questAccountIndex[[string]$a.id]=$a}
    foreach($batch in $script:questBatches){
        if($batch.state -eq 'done'){continue}
        Sync-ActivityCapacity Quest $batch $script:questAccountIndex
        if($batch.state -eq 'done'){continue}
        # Fault shutdown is per account; free a whole batch only after every game really exited.
        if($batch.state -ne 'waiting' -and $batch.state -ne 'done'){
            $faultCount=0;$allFaultClosed=$true
            foreach($m in $batch.members){$faultJob=Get-QuestMemberJob $m
                if($faultJob -is [TGLL.NativeJob] -and $faultJob.ActivityTimeoutClosed){$faultCount++}
                else{$allFaultClosed=$false}
                if(!$faultJob -or (Test-QuestMemberRunning $m)){$allFaultClosed=$false}
                $faultAccount=$script:questAccountIndex[$m.id]
                if($faultAccount -and (Test-AccountOpenBlocked $faultAccount)){$allFaultClosed=$false}
            }
            if($faultCount -gt 0 -and $allFaultClosed){$batch.state='done';$batch.error='Đã tắt do auto lỗi/chờ quá 10 phút';continue}
        }
        if($batch.state -eq 'running' -or ($batch.state -eq 'blocked' -and !$batch.closeOperation)){
            $allComplete=$true;$unavailable=$false
            foreach($m in $batch.members){if(Test-ActivityMemberExited $m){continue};$j=Get-QuestMemberJob $m
                if(($null -eq $j -or !$j.QuestFinished) -and !($j -is [TGLL.NativeJob] -and $j.ActivityTimeoutClosed)){$allComplete=$false}
                if(!($j -is [TGLL.NativeJob] -and $j.ActivityTimeoutClosed) -and ($null -eq $j -or $j.CloseRequested -or $j.CancelRequested -or $j.State -in @('closed','cancelled') -or ($j.State -notin @('failed','reconnecting') -and $j.GameSettingsReady -and !$j.QuestEnabled -and !$j.QuestFinished))){$unavailable=$true}
            }
            if($allComplete){$batch.state='finished';$batch.error=''}
            elseif(!$unavailable -and $batch.state -eq 'blocked'){$batch.state='running';$batch.error=''}
            elseif($unavailable){$batch.state='blocked';$batch.error='Tài khoản lỗi/thoát trước khi hoàn tất; giữ lượt để kiểm tra.'}
        }
        if($batch.state -eq 'finished' -and $questCloseAfter.Checked -and [TGLL.NativeBackend]::CanAutoCloseBatch([string[]]@($batch.members | ForEach-Object {$_.job}))){
            try{$batch.closeOperation=Begin-QuestTeamClose $batch;$batch.state='closing'}catch{$batch.state='blocked';$batch.error='Không bắt đầu được việc đóng tài khoản.'}
        }
        if($batch.state -in @('closing','blocked') -and $batch.closeOperation -and $batch.closeOperation.Completed){
            # A close timeout is historical; processes can exit after the close worker finishes.
            # Recheck blocked closes too, and never release a reopened/retrying session.
            $allClosed=$true
            foreach($m in $batch.members){
                $j=Get-QuestMemberJob $m;$a=$script:questAccountIndex[$m.id]
                if(!$j -or (Test-QuestMemberRunning $m) -or ($a -and (Test-AccountOpenBlocked $a)) -or
                    (!$j.CloseRequested -and !$j.CancelRequested -and $j.State -notin @('closed','cancelled'))){$allClosed=$false}
            }
            if($allClosed){$batch.state='done';$batch.error=''}
            elseif($batch.closeOperation.Failed -or $batch.closeOperation.Error){$batch.state='blocked';$batch.error='Chưa tắt đủ game. Hàng chờ giữ lượt để kiểm tra.'}
        }
    }
    $active=0;foreach($b in $script:questBatches){if($b.state -notin @('waiting','done')){$active+=(Get-ActivityReservedCount $b)}}
    # Live Quest accounts outside this queue also consume slots.
    $included=@{};foreach($b in $script:questBatches){if($b.state -notin @('waiting','done')){foreach($m in $b.members){$included[$m.id]=$true}}}
    $outside=0;foreach($a in $script:accounts){if(!$included.ContainsKey([string]$a.id) -and (Test-AccountOpenBlocked $a) -and ($a.autoQuest -or (Get-AccountLaunchMode $a) -eq 'quest')){$outside++}}
    $limit=[int]$questMax.Value
    $active+=(Restore-ActivityReservations $script:questBatches ($limit-$active-$outside))
    foreach($batch in $script:questBatches){
        if($batch.state -ne 'waiting'){continue}
        $alreadyOpen=0;foreach($m in $batch.members){$a=$script:questAccountIndex[$m.id];if($a -and (Test-AccountOpenBlocked $a)){$alreadyOpen++}}
        if($active+$outside+$batch.members.Count-$alreadyOpen -gt $limit){continue}
        $batch.state='launching';$active+=(Get-ActivityReservedCount $batch);$outside-=$alreadyOpen
        foreach($m in $batch.members){$script:questWaiting.Remove($m.id)}
    }
    # Submit at most one account per timer tick; native login still runs six at a time.
    for($launchBudget=0;$launchBudget -lt [int]$launchConcurrency.Value;$launchBudget++){
    $submitted=$false
    foreach($batch in $script:questBatches){if($batch.state -ne 'launching'){continue}
        $member=@($batch.members | Where-Object {!$_.job -and !(Test-ActivityMemberYielded $_) -and !(Test-ActivityMemberExited $_)}) | Select-Object -First 1
        if($member){$submitted=$true;try{Start-QuestMember $member}catch{$batch.state='blocked';$batch.error=[string]$_.Exception.Message;Handle-ActivityLaunchError Quest $batch $member $script:questAccountIndex};break}
        if(@($batch.members | Where-Object {Test-ActivityMemberYielded $_}).Count -eq 0){$batch.state='running'}
    }
    if(!$submitted){break}
    }
    Update-QuestQueueStatus
}
function Update-QuestQueueStatus {
    $script:questWaiting=@{};foreach($b in $script:questBatches){if($b.state -eq 'waiting'){foreach($m in $b.members){$script:questWaiting[$m.id]=$m}}}
    $active=0;$waiting=0;$done=0;foreach($b in $script:questBatches){if($b.state -eq 'waiting'){$waiting+=$b.members.Count}elseif($b.state -eq 'done'){$done+=$b.members.Count}else{$active+=(Get-ActivityReservedCount $b);$waiting+=@($b.members | Where-Object {Test-ActivityMemberYielded $_}).Count}}
    $questQueueHint.Text="Đang giữ lượt: $active / $([int]$questMax.Value) • Hàng chờ: $waiting • Đã đóng: $done"
    $errors=@($script:questBatches | Where-Object {$_.error});if($errors.Count){$questQueueHint.Text+="`r`n"+$errors[0].error}
    if(!$script:questWaiting.Count){return}
    foreach($row in $grid.Rows){if($null -ne (Get-QuestQueuedMember ([string]$row.Tag.id))){$row.Cells['status'].Value='Chờ lượt nhiệm vụ';Update-AccountOpen $row}}
}
