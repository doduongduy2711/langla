# Queue membership is a snapshot of selected table rows. Capacity is held for a whole team.
$script:izanamiBatches=@();$script:izanamiQueuePaused=$false;$script:izanamiWaiting=@{};$script:izanamiAccountIndex=@{}
function Get-IzanamiQueuedMember($id) {
    if($script:izanamiWaiting.ContainsKey([string]$id)){return $script:izanamiWaiting[[string]$id]}
    return $null
}
function Get-IzanamiMemberJob($member) {
    if($member.job -and $script:accountJobs.ContainsKey([string]$member.id)){
        $current=[string]$script:accountJobs[[string]$member.id]
        if($current -and $current -ne $member.job){$next=Get-AccountJob $current;if($next -is [TGLL.NativeJob] -and !$next.CloseRequested -and !$next.CancelRequested -and $next.State -notin @('closed','failed','cancelled')){[TGLL.NativeBackend]::RebindIzanamiJob([string]$member.job,$current);$member.job=$current}}
    }
    if($member.job){return Get-AccountJob $member.job};return $null
}
function Start-IzanamiMember($member) {
    $a=$script:izanamiAccountIndex[$member.id]
    if(!$a){throw 'Tài khoản trong hàng chờ đã bị xóa.'}
    if(Test-AccountOpenBlocked $a){
        if(!$script:accountJobs.ContainsKey($member.id)){throw 'Game mở thủ công chưa hỗ trợ tổ đội tự động.'}
        $member.job=$script:accountJobs[$member.id];$j=Get-AccountJob $member.job;$j.IzanamiQueueOnly=$true;$j.QuestQueueOnly=$false;[TGLL.NativeBackend]::SetReceiveKey($member.job,$false);[TGLL.NativeBackend]::SetDungeonHigh($member.job,$false);[TGLL.NativeBackend]::SetCloseAfterKey($member.job,$false);[TGLL.NativeBackend]::SetAutoQuest($member.job,$false);[TGLL.NativeBackend]::SetIzanami($member.job,$true);return
    }
    $script:izanamiLaunchingAccountId=$member.id
    try{Open-Account $a}finally{$script:izanamiLaunchingAccountId=''}
    if(!$script:accountJobs.ContainsKey($member.id)){throw 'Cần bật đăng nhập trực tiếp để chạy hàng chờ.'}
    $member.job=$script:accountJobs[$member.id]
}
function Begin-IzanamiTeamClose($batch) {if($batch.limitClose){return [TGLL.NativeBackend]::BeginCloseBatch([string[]]@($batch.members | ForEach-Object {$_.job} | Where-Object {$_}))};return [TGLL.NativeBackend]::BeginAutoCloseBatch([string[]]@($batch.members | ForEach-Object {$_.job}))}
function Test-IzanamiMemberRunning($member) {
    $j=Get-IzanamiMemberJob $member
    return ($null -ne $j -and $j.ProcessId -gt 0 -and [TGLL.ManagedGames]::IsRunning($j.ProcessId))
}
function Add-IzanamiQueue($accounts) {
    if($script:distributionRun -and $script:distributionRun.active){throw 'Dừng auto chia đồ trước.'}
    foreach($a in $accounts){foreach($b in $script:soncapBatches){if($b.state -ne 'done' -and @($b.members | Where-Object {$_.id -eq [string]$a.id}).Count){throw 'Tài khoản đang trong hàng chờ Sơn Cáp.'}}}
    if($script:mailRun -and $script:mailRun.active){throw 'Dừng hàng chờ gom đồ trước khi chạy hàng chờ khác.'}
    if(!$autoLoginCheck.Checked){throw 'Bật Đăng nhập trực tiếp trước khi chạy hàng chờ cấm thuật.'}
    foreach($a in $accounts){foreach($b in $script:questBatches){if($b.state -ne 'done' -and @($b.members | Where-Object {$_.id -eq [string]$a.id}).Count){throw 'Tài khoản đang có trong hàng chờ nhiệm vụ. Hủy hàng chờ đó trước.'}}}
    foreach($a in $accounts){foreach($b in $script:dungeonBatches){if($b.state -ne 'done' -and @($b.members | Where-Object {$_.id -eq [string]$a.id}).Count){throw 'Tài khoản đang trong hàng chờ địa cung.'}}}
    $known=@{};foreach($b in $script:izanamiBatches){foreach($m in $b.members){$known[$m.id]=$true}}
    $groupSize=[int]$izanamiGroupSize.Value
    $fresh=@($accounts | Where-Object {!$known.ContainsKey([string]$_.id)})
    if(!$fresh.Count){$status.Text='Các tài khoản đã có trong hàng chờ cấm thuật.';return}
    # Only a waiting team can accept more members; active team membership stays fixed.
    $tail=$null;if($script:izanamiBatches.Count){$last=$script:izanamiBatches[-1];if($last.state -eq 'waiting' -and $last.groupSize -eq $groupSize -and $last.members.Count -lt $groupSize){$tail=$last}}
    foreach($a in $fresh){
        $a | Add-Member NoteProperty izanami $true -Force
        if($null -eq $tail -or $tail.members.Count -eq $groupSize){$tail=[pscustomobject]@{members=@();groupSize=$groupSize;state='waiting';error='';closeOperation=$null};$script:izanamiBatches+=@($tail)}
        $tail.members+=@([pscustomobject]@{id=[string]$a.id;job=''})
    }
    if(!$UiTest){Save-Accounts};$script:izanamiQueuePaused=$false;Pump-IzanamiQueue
}
function Reset-IzanamiQueue {
    # An already-started close operation may finish. Cancelling prevents new launches.
    foreach($b in $script:izanamiBatches){foreach($m in $b.members){if($m.job){$j=Get-AccountJob $m.job;if($j -and $j.State -eq 'failed'){$j.CancelRequested=$true}}}}
    $script:izanamiQueuePaused=$true;$script:izanamiBatches=@();$script:izanamiWaiting=@{}
}
function Pump-IzanamiQueue {
    Sync-IzanamiCompletedClose
    if($script:izanamiQueuePaused -or !$script:izanamiBatches.Count){return}
    $script:izanamiAccountIndex=@{};foreach($a in $script:accounts){$script:izanamiAccountIndex[[string]$a.id]=$a}
    foreach($batch in $script:izanamiBatches){
        if($batch.state -in @('launching','running','finished','blocked')){
            $limited=$false;foreach($m in $batch.members){$j=Get-IzanamiMemberJob $m;if($j -and $j.IzanamiNoAttempts){$limited=$true}}
            if($limited){$batch | Add-Member NoteProperty limitClose $true -Force;$batch.state='finished';$batch.error='Hết lượt cấm thuật: tắt tổ đội, bỏ qua số lượt còn lại.'}
        }
        if($batch.state -eq 'running'){
            $allComplete=$true;$unavailable=$false
            foreach($m in $batch.members){$j=Get-IzanamiMemberJob $m
                if($null -eq $j -or !$j.IzanamiRoundComplete -or ($j.PSObject.Properties['IzanamiRunFinished'] -and !$j.IzanamiRunFinished)){$allComplete=$false}
                if($null -eq $j -or $j.CloseRequested -or $j.CancelRequested -or $j.State -in @('closed','cancelled')){$unavailable=$true}
            }
            if($allComplete){$batch.state='finished'}
            elseif($unavailable){$batch.state='blocked';$batch.error='Một thành viên lỗi/thoát trước khi hoàn tất. Giữ lượt của tổ, không mở đội khác.'}
        }
        if($batch.state -eq 'finished' -and ($batch.limitClose -or ($izanamiCloseAfter.Checked -and [TGLL.NativeBackend]::CanAutoCloseBatch([string[]]@($batch.members | ForEach-Object {$_.job}))))){
            try{$batch.closeOperation=Begin-IzanamiTeamClose $batch;$batch.state='closing';$batch.error=''}catch{$batch.state='blocked';$batch.error='Không bắt đầu được việc đóng tổ đội.'}
        }
        if($batch.state -eq 'closing' -and $batch.closeOperation.Completed){
            if($batch.closeOperation.Failed -or $batch.closeOperation.Error){$batch.state='blocked';$batch.error='Chưa tắt đủ game của tổ. Hàng chờ giữ lượt của tổ.'}
            else{$anyRunning=$false;foreach($m in $batch.members){if(Test-IzanamiMemberRunning $m){$anyRunning=$true}}
                if(!$anyRunning){$batch.state='done'}
            }
        }
    }
    $active=0;foreach($b in $script:izanamiBatches){if($b.state -notin @('waiting','done')){$active+=$b.members.Count}}
    # Live Izanami accounts outside this queue also consume slots.
    $included=@{};foreach($b in $script:izanamiBatches){if($b.state -ne 'waiting'){foreach($m in $b.members){$included[$m.id]=$true}}}
    $outside=0;foreach($a in $script:accounts){if(!$included.ContainsKey([string]$a.id) -and $a.izanami -and (Test-AccountOpenBlocked $a)){$outside++}}
    $limit=[int]$izanamiMax.Value
    foreach($batch in $script:izanamiBatches){
        if($batch.state -ne 'waiting'){continue}
        $alreadyOpen=0;foreach($m in $batch.members){$a=$script:izanamiAccountIndex[$m.id];if($a -and (Test-AccountOpenBlocked $a)){$alreadyOpen++}}
        if($active+$outside+$batch.members.Count-$alreadyOpen -gt $limit){break}
        $batch.state='launching';$active+=$batch.members.Count;$outside-=$alreadyOpen
        foreach($m in $batch.members){$script:izanamiWaiting.Remove($m.id)}
    }
    # Submit at most one account per timer tick; native login still runs six at a time.
    for($launchBudget=0;$launchBudget -lt [int]$launchConcurrency.Value;$launchBudget++){
    foreach($batch in $script:izanamiBatches){if($batch.state -ne 'launching'){continue}
        $member=@($batch.members | Where-Object {!$_.job}) | Select-Object -First 1
        if($member){try{Start-IzanamiMember $member}catch{$batch.state='blocked';$batch.error=[string]$_.Exception.Message};Sync-IzanamiTeams;break}
        $batch.state='running'
    }
    }
    Update-IzanamiQueueStatus
}
function Update-IzanamiQueueStatus {
    $script:izanamiWaiting=@{};foreach($b in $script:izanamiBatches){if($b.state -eq 'waiting'){foreach($m in $b.members){$script:izanamiWaiting[$m.id]=$m}}}
    $active=0;$waiting=0;$done=0;foreach($b in $script:izanamiBatches){if($b.state -eq 'waiting'){$waiting+=$b.members.Count}elseif($b.state -eq 'done'){$done+=$b.members.Count}else{$active+=$b.members.Count}}
    $izanamiQueueHint.Text="Đang giữ lượt: $active / $([int]$izanamiMax.Value) • Hàng chờ: $waiting • Đã đóng: $done"
    $errors=@($script:izanamiBatches | Where-Object {$_.error});if($errors.Count){$izanamiQueueHint.Text+="`r`n"+$errors[0].error}
    foreach($row in $grid.Rows){if($null -ne (Get-IzanamiQueuedMember ([string]$row.Tag.id))){$row.Cells['status'].Value='Chờ lượt cấm thuật';Update-AccountOpen $row}}
}

function Test-IzanamiCompletedClose($job,[datetime]$now) {
    return $null -ne $job -and $job -is [TGLL.NativeJob] -and $job.ProcessId -gt 0 -and $job.IzanamiEnabled -and $job.IzanamiRoundComplete -and $job.IzanamiRunFinished -and !$job.IzanamiQueueOnly -and !$job.SonCapEnabled -and !$job.QuestEnabled -and !$job.CloseRequested -and !$job.CancelRequested -and !$job.ReconnectRequested -and $job.State -notin @('closed','failed','cancelled','reconnecting') -and $job.MapId -eq 59 -and ($now-$job.ZoneReadAt).TotalSeconds -lt 5 -and [TGLL.NativeBackend]::CanAutoCloseBatch([string[]]@($job.Id))
}
function Begin-IzanamiCompletedClose($job) {
    return [TGLL.NativeBackend]::BeginAutoCloseBatch([string[]]@($job.Id))
}
function Sync-IzanamiCompletedClose {
    if(!$izanamiCloseAfter.Checked -or !$script:accountJobs.Count){return}
    if($null -eq $script:izanamiCompletedClose){$script:izanamiCompletedClose=@{}}
    $keys=@($script:accountJobs.Keys);$now=[datetime]::UtcNow
    for($n=0;$n -lt [Math]::Min(24,$keys.Count);$n++){
        if($script:izanamiCompletedCloseCursor -ge $keys.Count){$script:izanamiCompletedCloseCursor=0}
        $key=[string]$keys[[int]$script:izanamiCompletedCloseCursor];$script:izanamiCompletedCloseCursor++
        $job=Get-AccountJob $script:accountJobs[$key]
        if(!(Test-IzanamiCompletedClose $job $now)){continue}
        $issued=$script:izanamiCompletedClose[[string]$job.Id]
        if($issued -and ($issued.operation -or ($now-$issued.at).TotalSeconds -lt 5)){continue}
        $record=@{at=$now;operation=$null};$script:izanamiCompletedClose[[string]$job.Id]=$record
        try{$record.operation=Begin-IzanamiCompletedClose $job}catch{$status.Text='Chưa tắt được acc đã xong cấm thuật: '+$job.Username}
    }
}
