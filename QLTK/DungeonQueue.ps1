# Queue membership is a snapshot of selected table rows. Capacity is held for a whole team.
$script:dungeonBatches=@();$script:dungeonQueuePaused=$false;$script:dungeonWaiting=@{};$script:dungeonAccountIndex=@{}
function Get-DungeonQueuedMember($id) {
    if($script:dungeonWaiting.ContainsKey([string]$id)){return $script:dungeonWaiting[[string]$id]}
    return $null
}
function Get-DungeonMemberJob($member) {if($member.job){return Get-AccountJob $member.job};return $null}
function Start-DungeonMember($member) {
    $a=$script:dungeonAccountIndex[$member.id]
    if(!$a){throw 'Tài khoản trong hàng chờ đã bị xóa.'}
    if(Test-AccountOpenBlocked $a){
        if(!$script:accountJobs.ContainsKey($member.id)){throw 'Game mở thủ công chưa hỗ trợ tài khoản tự động.'}
        $member.job=$script:accountJobs[$member.id];$j=Get-AccountJob $member.job;$j.DungeonQueueOnly=$true;$j.QuestQueueOnly=$false;$j.IzanamiQueueOnly=$false;[TGLL.NativeBackend]::SetAutoQuest($member.job,$false);[TGLL.NativeBackend]::SetIzanami($member.job,$false);[TGLL.NativeBackend]::SetCloseAfterKey($member.job,$false);[TGLL.NativeBackend]::SetReceiveKey($member.job,[bool]$a.receiveKey);[TGLL.NativeBackend]::SetDungeonHigh($member.job,$true);return
    }
    $script:dungeonLaunchingAccountId=$member.id
    try{Open-Account $a}finally{$script:dungeonLaunchingAccountId=''}
    if(!$script:accountJobs.ContainsKey($member.id)){throw 'Cần bật đăng nhập trực tiếp để chạy hàng chờ.'}
    $member.job=$script:accountJobs[$member.id]
}
function Begin-DungeonTeamClose($batch) {return [TGLL.NativeBackend]::BeginAutoCloseBatch([string[]]@($batch.members | ForEach-Object {$_.job}))}
function Test-DungeonMemberRunning($member) {
    $j=Get-DungeonMemberJob $member
    return ($null -ne $j -and $j.ProcessId -gt 0 -and [TGLL.ManagedGames]::IsRunning($j.ProcessId))
}
function Add-DungeonQueue($accounts) {
    if($script:distributionRun -and $script:distributionRun.active){throw 'Dừng auto chia đồ trước.'}
    foreach($a in $accounts){foreach($b in $script:soncapBatches){if($b.state -ne 'done' -and @($b.members | Where-Object {$_.id -eq [string]$a.id}).Count){throw 'Tài khoản đang trong hàng chờ Sơn Cáp.'}}}
    if($script:mailRun -and $script:mailRun.active){throw 'Dừng hàng chờ gom đồ trước khi chạy hàng chờ khác.'}
    if(!$autoLoginCheck.Checked){throw 'Bật Đăng nhập trực tiếp trước khi chạy hàng chờ địa cung.'}
    foreach($a in $accounts){foreach($b in $script:izanamiBatches){if($b.state -ne 'done' -and @($b.members | Where-Object {$_.id -eq [string]$a.id}).Count){throw 'Tài khoản đang có trong hàng chờ cấm thuật. Hủy hàng chờ đó trước.'}}}
    foreach($a in $accounts){foreach($b in $script:questBatches){if($b.state -ne 'done' -and @($b.members | Where-Object {$_.id -eq [string]$a.id}).Count){throw 'Tài khoản đang trong hàng chờ nhiệm vụ.'}}}
    $known=@{};foreach($b in $script:dungeonBatches){foreach($m in $b.members){$known[$m.id]=$true}}
    $fresh=@($accounts | Where-Object {!$known.ContainsKey([string]$_.id)})
    if(!$fresh.Count){$status.Text='Các tài khoản đã có trong hàng chờ địa cung.';return}
    $tail=$null
    foreach($a in $fresh){
        $a | Add-Member NoteProperty dungeonHigh $true -Force
        if($null -eq $tail -or $tail.members.Count -eq 1){$tail=[pscustomobject]@{members=@();state='waiting';error='';closeOperation=$null};$script:dungeonBatches+=@($tail)}
        $tail.members+=@([pscustomobject]@{id=[string]$a.id;job=''})
    }
    if(!$UiTest){Save-Accounts};$script:dungeonQueuePaused=$false;Pump-DungeonQueue
}
function Reset-DungeonQueue {
    # An already-started close operation may finish. Cancelling prevents new launches.
    foreach($b in $script:dungeonBatches){foreach($m in $b.members){if($m.job){$j=Get-AccountJob $m.job;if($j -and $j.State -eq 'failed'){$j.CancelRequested=$true}}}}
    $script:dungeonQueuePaused=$true;$script:dungeonBatches=@();$script:dungeonWaiting=@{}
}
function Pump-DungeonQueue {
    [TGLL.NativeBackend]::PulseDungeonAutoClose()
    if($script:dungeonQueuePaused -or !$script:dungeonBatches.Count){return}
    $script:dungeonAccountIndex=@{};foreach($a in $script:accounts){$script:dungeonAccountIndex[[string]$a.id]=$a}
    foreach($batch in $script:dungeonBatches){
        if($batch.state -eq 'running'){
            $allComplete=$true;$unavailable=$false
            foreach($m in $batch.members){$j=Get-DungeonMemberJob $m
                if($null -eq $j -or !$j.DungeonFinished){$allComplete=$false}
                if($null -eq $j -or $j.CloseRequested -or $j.CancelRequested -or $j.State -in @('closed','cancelled') -or ($j.State -notin @('failed','reconnecting') -and $j.GameSettingsReady -and !$j.DungeonHighEnabled -and !$j.DungeonFinished)){$unavailable=$true}
            }
            if($allComplete){$batch.state='finished'}
            elseif($unavailable){$batch.state='blocked';$batch.error='Tài khoản lỗi/thoát trước khi hoàn tất; giữ lượt để kiểm tra.'}
        }
        if($batch.state -eq 'finished' -and $dungeonCloseAfter.Checked -and [TGLL.NativeBackend]::CanAutoCloseBatch([string[]]@($batch.members | ForEach-Object {$_.job}))){
            try{$batch.closeOperation=Begin-DungeonTeamClose $batch;$batch.state='closing'}catch{$batch.state='blocked';$batch.error='Không bắt đầu được việc đóng tài khoản.'}
        }
        if($batch.state -eq 'closing' -and $batch.closeOperation.Completed){
            if($batch.closeOperation.Failed -or $batch.closeOperation.Error){$batch.state='blocked';$batch.error='Chưa tắt đủ game của tài khoản. Hàng chờ giữ lượt của tài khoản.'}
            else{$anyRunning=$false;foreach($m in $batch.members){if(Test-DungeonMemberRunning $m){$anyRunning=$true}}
                if(!$anyRunning){$batch.state='done'}
            }
        }
    }
    $active=0;foreach($b in $script:dungeonBatches){if($b.state -notin @('waiting','done')){$active+=$b.members.Count}}
    # Live Dungeon accounts outside this queue also consume slots.
    $included=@{};foreach($b in $script:dungeonBatches){if($b.state -ne 'waiting'){foreach($m in $b.members){$included[$m.id]=$true}}}
    $outside=0;foreach($a in $script:accounts){if(!$included.ContainsKey([string]$a.id) -and $a.dungeonHigh -and (Test-AccountOpenBlocked $a)){$outside++}}
    $limit=[int]$dungeonMax.Value
    foreach($batch in $script:dungeonBatches){
        if($batch.state -ne 'waiting'){continue}
        $alreadyOpen=0;foreach($m in $batch.members){$a=$script:dungeonAccountIndex[$m.id];if($a -and (Test-AccountOpenBlocked $a)){$alreadyOpen++}}
        if($active+$outside+$batch.members.Count-$alreadyOpen -gt $limit){break}
        $batch.state='launching';$active+=$batch.members.Count;$outside-=$alreadyOpen
        foreach($m in $batch.members){$script:dungeonWaiting.Remove($m.id)}
    }
    # Submit at most one account per timer tick; native login still runs six at a time.
    for($launchBudget=0;$launchBudget -lt [int]$launchConcurrency.Value;$launchBudget++){
    foreach($batch in $script:dungeonBatches){if($batch.state -ne 'launching'){continue}
        $member=@($batch.members | Where-Object {!$_.job}) | Select-Object -First 1
        if($member){try{Start-DungeonMember $member}catch{$batch.state='blocked';$batch.error=[string]$_.Exception.Message};break}
        $batch.state='running'
    }
    }
    Update-DungeonQueueStatus
}
function Update-DungeonQueueStatus {
    $script:dungeonWaiting=@{};foreach($b in $script:dungeonBatches){if($b.state -eq 'waiting'){foreach($m in $b.members){$script:dungeonWaiting[$m.id]=$m}}}
    $active=0;$waiting=0;$done=0;foreach($b in $script:dungeonBatches){if($b.state -eq 'waiting'){$waiting+=$b.members.Count}elseif($b.state -eq 'done'){$done+=$b.members.Count}else{$active+=$b.members.Count}}
    $dungeonQueueHint.Text="Đang giữ lượt: $active / $([int]$dungeonMax.Value) • Hàng chờ: $waiting • Đã đóng: $done"
    $errors=@($script:dungeonBatches | Where-Object {$_.error});if($errors.Count){$dungeonQueueHint.Text+="`r`n"+$errors[0].error}
    foreach($row in $grid.Rows){if($null -ne (Get-DungeonQueuedMember ([string]$row.Tag.id))){$row.Cells['status'].Value='Chờ lượt địa cung';Update-AccountOpen $row}}
}
