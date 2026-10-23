# Queue membership is a snapshot of selected table rows. Capacity is held for a whole team.
$script:soncapBatches=@();$script:soncapQueuePaused=$false;$script:soncapWaiting=@{};$script:soncapAccountIndex=@{}
function Get-SonCapQueuedMember($id) {
    if($script:soncapWaiting.ContainsKey([string]$id)){return $script:soncapWaiting[[string]$id]}
    return $null
}
function Get-SonCapMemberJob($member) {
    if($member.job -and $script:accountJobs.ContainsKey([string]$member.id)){
        $current=[string]$script:accountJobs[[string]$member.id]
        if($current -and $current -ne $member.job){
            $next=Get-AccountJob $current
            if($next -is [TGLL.NativeJob] -and !$next.CloseRequested -and !$next.CancelRequested -and $next.State -notin @('closed','failed','cancelled')){
                [TGLL.NativeBackend]::RebindSonCapJob([string]$member.job,$current);$member.job=$current
            }
        }
    }
    if($member.job){return Get-AccountJob $member.job};return $null
}
function Start-SonCapMember($member) {
    $a=$script:soncapAccountIndex[$member.id]
    if(!$a){throw 'Tài khoản trong hàng chờ đã bị xóa.'}
    if(Test-AccountOpenBlocked $a){
        if(!$script:accountJobs.ContainsKey($member.id)){throw 'Game mở thủ công chưa hỗ trợ tổ đội tự động.'}
        $member.job=$script:accountJobs[$member.id];$j=Get-AccountJob $member.job;$j.IzanamiQueueOnly=$false;$j.DungeonQueueOnly=$false;$j.QuestQueueOnly=$false;[TGLL.NativeBackend]::SetReceiveKey($member.job,$false);[TGLL.NativeBackend]::SetDungeonHigh($member.job,$false);[TGLL.NativeBackend]::SetCloseAfterKey($member.job,$false);[TGLL.NativeBackend]::SetAutoQuest($member.job,$false);[TGLL.NativeBackend]::SetSonCap($member.job,$true);return
    }
    $script:soncapLaunchingAccountId=$member.id
    try{Open-Account $a}finally{$script:soncapLaunchingAccountId=''}
    if(!$script:accountJobs.ContainsKey($member.id)){throw 'Cần bật đăng nhập trực tiếp để chạy hàng chờ.'}
    $member.job=$script:accountJobs[$member.id]
}
function Begin-SonCapTeamClose($batch) {if($batch.limitClose){return [TGLL.NativeBackend]::BeginCloseBatch([string[]]@($batch.members | ForEach-Object {$_.job}))};return [TGLL.NativeBackend]::BeginAutoCloseBatch([string[]]@($batch.members | ForEach-Object {$_.job}))}
function Test-SonCapMemberRunning($member) {
    $j=Get-SonCapMemberJob $member
    return ($null -ne $j -and $j.ProcessId -gt 0 -and [TGLL.ManagedGames]::IsRunning($j.ProcessId))
}
function Add-SonCapQueue($accounts) {
    if(Get-Command Assert-BeastAccountsAvailable -ErrorAction SilentlyContinue){Assert-BeastAccountsAvailable $accounts}
    if($script:distributionRun -and $script:distributionRun.active){throw 'Dừng auto chia đồ trước.'}
    if($script:mailRun -and $script:mailRun.active){throw 'Dừng hàng chờ gom đồ trước khi chạy hàng chờ khác.'}
    if(!$autoLoginCheck.Checked){throw 'Bật Đăng nhập trực tiếp trước khi chạy hàng chờ Sơn Cáp.'}
    foreach($a in $accounts){foreach($b in $script:questBatches){if($b.state -ne 'done' -and @($b.members | Where-Object {$_.id -eq [string]$a.id}).Count){throw 'Tài khoản đang có trong hàng chờ nhiệm vụ. Hủy hàng chờ đó trước.'}}}
    foreach($a in $accounts){foreach($b in $script:dungeonBatches){if($b.state -ne 'done' -and @($b.members | Where-Object {$_.id -eq [string]$a.id}).Count){throw 'Tài khoản đang trong hàng chờ địa cung.'}}}
    foreach($a in $accounts){foreach($b in $script:izanamiBatches){if($b.state -ne 'done' -and @($b.members | Where-Object {$_.id -eq [string]$a.id}).Count){throw 'Tài khoản đang trong hàng chờ cấm thuật.'}}}
    $known=@{};foreach($b in $script:soncapBatches){foreach($m in $b.members){$known[$m.id]=$true}}
    $groupSize=6
    $fresh=@($accounts | Where-Object {!$known.ContainsKey([string]$_.id)})
    if(!$fresh.Count){$status.Text='Các tài khoản đã có trong hàng chờ Sơn Cáp.';return}
    # Only a waiting team can accept more members; active team membership stays fixed.
    $tail=$null;if($script:soncapBatches.Count){$last=$script:soncapBatches[-1];if($last.state -eq 'waiting' -and $last.groupSize -eq $groupSize -and $last.members.Count -lt $groupSize){$tail=$last}}
    foreach($a in $fresh){
        $a | Add-Member NoteProperty soncap $true -Force
        if($null -eq $tail -or $tail.members.Count -eq $groupSize){$tail=[pscustomobject]@{members=@();groupSize=$groupSize;state='waiting';error='';closeOperation=$null};$script:soncapBatches+=@($tail)}
        $tail.members+=@([pscustomobject]@{id=[string]$a.id;job=''})
    }
    if(!$UiTest){Save-Accounts};$script:soncapQueuePaused=$false;Pump-SonCapQueue
}
function Reset-SonCapQueue {
    # An already-started close operation may finish. Cancelling prevents new launches.
    foreach($b in $script:soncapBatches){foreach($m in $b.members){if($m.job){$j=Get-AccountJob $m.job;if($j -is [TGLL.NativeJob]){$j.SonCapQueueOnly=$false};if($j -and $j.State -eq 'failed'){$j.CancelRequested=$true}}}}
    $script:soncapQueuePaused=$true;$script:soncapBatches=@();$script:soncapWaiting=@{}
}
function Pump-SonCapQueue {
    if($script:soncapQueuePaused -or !$script:soncapBatches.Count){return}
    $script:soncapAccountIndex=@{};foreach($a in $script:accounts){$script:soncapAccountIndex[[string]$a.id]=$a}
    foreach($batch in $script:soncapBatches){
        # Fault shutdown is per account; free a whole batch only after every game really exited.
        if($batch.state -ne 'waiting' -and $batch.state -ne 'done'){
            $faultCount=0;$allFaultClosed=$true
            foreach($m in $batch.members){$faultJob=Get-SonCapMemberJob $m
                if($faultJob -is [TGLL.NativeJob] -and $faultJob.ActivityTimeoutClosed){$faultCount++}
                else{$allFaultClosed=$false}
                if(!$faultJob -or (Test-SonCapMemberRunning $m)){$allFaultClosed=$false}
                $faultAccount=$script:soncapAccountIndex[$m.id]
                if($faultAccount -and (Test-AccountOpenBlocked $faultAccount)){$allFaultClosed=$false}
            }
            if($faultCount -gt 0 -and $allFaultClosed){$batch.state='done';$batch.error='Đã tắt do auto lỗi/chờ quá 10 phút';continue}
        }
        if($batch.state -in @('running','blocked','finished')){foreach($m in $batch.members){$j=Get-SonCapMemberJob $m;if($j -and $j.SonCapNoAttempts){$batch | Add-Member NoteProperty limitClose $true -Force;break}}}
        if($batch.state -eq 'running' -or ($batch.state -eq 'blocked' -and !$batch.closeOperation)){
            $allComplete=$true;$unavailable=$false
            foreach($m in $batch.members){$j=Get-SonCapMemberJob $m
                if(($null -eq $j -or !$j.SonCapFinished) -and !($j -is [TGLL.NativeJob] -and $j.ActivityTimeoutClosed)){$allComplete=$false}
                if(!($j -is [TGLL.NativeJob] -and $j.ActivityTimeoutClosed) -and ($null -eq $j -or $j.SonCapFailure -or $j.CloseRequested -or $j.CancelRequested -or $j.State -in @('closed','cancelled'))){$unavailable=$true}
            }
            if($allComplete){$batch.state='finished';$batch.error=''}
            elseif(!$unavailable -and $batch.state -eq 'blocked'){$batch.state='running';$batch.error=''}
            elseif($unavailable){$batch.state='blocked';$batch.error='Một thành viên lỗi/thoát trước khi hoàn tất. Giữ lượt của tổ, không mở đội khác.'}
        }
        if($batch.state -eq 'finished' -and ($batch.limitClose -or ($soncapCloseAfter.Checked -and [TGLL.NativeBackend]::CanAutoCloseBatch([string[]]@($batch.members | ForEach-Object {$_.job}))))){
            try{$batch.closeOperation=Begin-SonCapTeamClose $batch;$batch.state='closing';$batch.error=''}catch{$batch.state='blocked';$batch.error='Không bắt đầu được việc đóng tổ đội.'}
        }
        if($batch.state -in @('closing','blocked') -and $batch.closeOperation -and $batch.closeOperation.Completed){
            # A close timeout is historical; processes can exit after the close worker finishes.
            # Recheck blocked closes too, and never release a reopened/retrying session.
            $allClosed=$true
            foreach($m in $batch.members){
                $j=Get-SonCapMemberJob $m;$a=$script:soncapAccountIndex[$m.id]
                if(!$j -or (Test-SonCapMemberRunning $m) -or ($a -and (Test-AccountOpenBlocked $a)) -or
                    (!$j.CloseRequested -and !$j.CancelRequested -and $j.State -notin @('closed','cancelled'))){$allClosed=$false}
            }
            if($allClosed){$batch.state='done';$batch.error=''}
            elseif($batch.closeOperation.Failed -or $batch.closeOperation.Error){$batch.state='blocked';$batch.error='Chưa tắt đủ game của tổ. Hàng chờ giữ lượt của tổ.'}
        }
    }
    $active=0;foreach($b in $script:soncapBatches){if($b.state -notin @('waiting','done')){$active+=$b.members.Count}}
    # Live SonCap accounts outside this queue also consume slots.
    $included=@{};foreach($b in $script:soncapBatches){if($b.state -notin @('waiting','done')){foreach($m in $b.members){$included[$m.id]=$true}}}
    $outside=0;foreach($a in $script:accounts){if(!$included.ContainsKey([string]$a.id) -and $a.soncap -and (Test-AccountOpenBlocked $a)){$outside++}}
    $limit=[int]$soncapMax.Value
    foreach($batch in $script:soncapBatches){
        if($batch.state -ne 'waiting'){continue}
        $alreadyOpen=0;foreach($m in $batch.members){$a=$script:soncapAccountIndex[$m.id];if($a -and (Test-AccountOpenBlocked $a)){$alreadyOpen++}}
        if($active+$outside+$batch.members.Count-$alreadyOpen -gt $limit){break}
        $batch.state='launching';$active+=$batch.members.Count;$outside-=$alreadyOpen
        foreach($m in $batch.members){$script:soncapWaiting.Remove($m.id)}
    }
    # Submit at most one account per timer tick; native login still runs six at a time.
    for($launchBudget=0;$launchBudget -lt [int]$launchConcurrency.Value;$launchBudget++){
    foreach($batch in $script:soncapBatches){if($batch.state -ne 'launching'){continue}
        $member=@($batch.members | Where-Object {!$_.job}) | Select-Object -First 1
        if($member){try{Start-SonCapMember $member}catch{$batch.state='blocked';$batch.error=[string]$_.Exception.Message};Sync-SonCapTeams;break}
        $batch.state='running'
    }
    }
    Update-SonCapQueueStatus
}
function Update-SonCapQueueStatus {
    $script:soncapWaiting=@{};foreach($b in $script:soncapBatches){if($b.state -eq 'waiting'){foreach($m in $b.members){$script:soncapWaiting[$m.id]=$m}}}
    $active=0;$waiting=0;$done=0;foreach($b in $script:soncapBatches){if($b.state -eq 'waiting'){$waiting+=$b.members.Count}elseif($b.state -eq 'done'){$done+=$b.members.Count}else{$active+=$b.members.Count}}
    $soncapQueueHint.Text="Đang giữ lượt: $active / $([int]$soncapMax.Value) • Hàng chờ: $waiting • Đã đóng: $done"
    $errors=@($script:soncapBatches | Where-Object {$_.error});if($errors.Count){$soncapQueueHint.Text+="`r`n"+$errors[0].error}
    foreach($row in $grid.Rows){if($null -ne (Get-SonCapQueuedMember ([string]$row.Tag.id))){$row.Cells['status'].Value='Chờ lượt Sơn Cáp';Update-AccountOpen $row}}
}
