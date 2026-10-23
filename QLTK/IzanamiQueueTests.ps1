# Exercise the actual scheduler with fake launch/close adapters; never touches a real game.
$queueSavedGroupSize=$izanamiGroupSize.Value;$queueSavedAccounts=$script:accounts;$queueSavedMax=$izanamiMax.Value;$queueSavedClose=$izanamiCloseAfter.Checked
$queueFunctions=@{};foreach($name in @('Start-IzanamiMember','Get-IzanamiMemberJob','Begin-IzanamiTeamClose','Test-IzanamiMemberRunning','Test-AccountOpenBlocked')){$queueFunctions[$name]=(Get-Item ('Function:\'+$name)).ScriptBlock}
try {
    $script:queueFakeJobs=@{};$script:queueStarted=@();$script:queueClosedSizes=@()
    function Start-IzanamiMember($m){if($null -ne (Get-IzanamiQueuedMember $m.id)){throw 'Admitted member still blocked by queue OPEN guard'};$m.job=$m.id;$script:queueStarted+=@($m.id);$script:queueFakeJobs[$m.id]=[pscustomobject]@{State='opened';ProcessId=1;IzanamiRoundComplete=$false;CloseRequested=$false;CancelRequested=$false}}
    function Get-IzanamiMemberJob($m){return $script:queueFakeJobs[$m.job]}
    function Test-AccountOpenBlocked($a){return ($script:queueFakeJobs.ContainsKey([string]$a.id) -and $script:queueFakeJobs[[string]$a.id].State -eq 'opened')}
    function Test-IzanamiMemberRunning($m){return ($script:queueFakeJobs[$m.job].State -eq 'opened')}
    function Begin-IzanamiTeamClose($b){$script:queueClosedSizes+=@($b.members.Count);return [pscustomobject]@{Completed=$false;Failed=0;Error=''}}
    $script:accounts=@(1..152 | ForEach-Object {[pscustomobject]@{id="queue-$_";username="queue-$_";izanami=$true}})
    Reset-IzanamiQueue;$izanamiMax.Value=12;$izanamiCloseAfter.Checked=$true
    Add-IzanamiQueue $script:accounts
    for($i=0;$i -lt 30;$i++){Pump-IzanamiQueue}
    if($script:queueStarted.Count -ne 12 -or $script:izanamiWaiting.Count -ne 140){throw '152 accounts / max 12 did not keep 140 waiting'}
    if(($script:queueStarted -join ',') -ne ((1..12 | ForEach-Object {"queue-$_"}) -join ',')){throw 'Queue did not preserve selected table order'}
    $first=$script:izanamiBatches[0]
    foreach($m in $first.members[0..4]){$script:queueFakeJobs[$m.job].IzanamiRoundComplete=$true};Pump-IzanamiQueue
    if($script:queueClosedSizes.Count){throw 'One unfinished member must prevent team close'}
    $script:queueFakeJobs[$first.members[5].job].IzanamiRoundComplete=$true;Pump-IzanamiQueue
    if($first.state -ne 'closing' -or $script:queueStarted.Count -ne 12){throw 'Close in progress released slots prematurely'}
    $first.closeOperation.Completed=$true;$first.closeOperation.Failed=1;Pump-IzanamiQueue
    if($script:queueStarted.Count -ne 12){throw 'Process still running released slots prematurely'}
    foreach($m in $first.members){$script:queueFakeJobs[$m.job].State='closed'};Pump-IzanamiQueue
    if($script:queueStarted.Count -ne 18 -or $script:queueStarted[12] -ne 'queue-13'){throw 'Confirmed team close did not start the next team'}
    # Complete every admitted team. Only running teams may finish; fake closes take a separate tick.
    for($tick=0;$tick -lt 1000;$tick++){
        foreach($b in $script:izanamiBatches){
            if($b.state -eq 'running'){foreach($m in $b.members){$script:queueFakeJobs[$m.job].IzanamiRoundComplete=$true}}
            if($b.state -eq 'closing'){$b.closeOperation.Completed=$true;foreach($m in $b.members){$script:queueFakeJobs[$m.job].State='closed'}}
        }
        Pump-IzanamiQueue
        if(@($script:queueFakeJobs.Values | Where-Object {$_.State -eq 'opened'}).Count -gt 12){throw 'Live game count exceeded max 12'}
        if(@($script:izanamiBatches | Where-Object {$_.state -ne 'done'}).Count -eq 0){break}
    }
    if($script:queueStarted.Count -ne 152 -or $tick -ge 1000 -or $script:queueClosedSizes[-1] -ne 2){throw 'Queue over 100 accounts or final two-person team failed'}
    Add-IzanamiQueue $script:accounts
    if($script:queueStarted.Count -ne 152){throw 'Duplicate queue submission reran completed accounts'}
    # With auto close off, finishing does not release occupied slots.
    Reset-IzanamiQueue;$script:queueFakeJobs=@{};$script:queueStarted=@();$script:queueClosedSizes=@();$izanamiCloseAfter.Checked=$false
    Add-IzanamiQueue @($script:accounts[0..17]);for($i=0;$i -lt 30;$i++){Pump-IzanamiQueue}
    foreach($m in $script:izanamiBatches[0].members){$script:queueFakeJobs[$m.job].IzanamiRoundComplete=$true};Pump-IzanamiQueue
    if($script:queueClosedSizes.Count -or $script:queueStarted.Count -ne 12){throw 'Unticked auto close closed games or exceeded capacity'}
    $limitBatch=$script:izanamiBatches[0];$script:queueFakeJobs[$limitBatch.members[0].job] | Add-Member NoteProperty IzanamiNoAttempts $true -Force
    Pump-IzanamiQueue
    if($limitBatch.state -ne 'closing' -or $script:queueClosedSizes.Count -ne 1 -or !$limitBatch.limitClose){throw 'No attempts must close exactly the refused team even with auto close off'}
    for($i=0;$i -lt 5;$i++){Pump-IzanamiQueue}
    if($script:queueClosedSizes.Count -ne 1 -or $script:queueStarted.Count -ne 12){throw 'Refusal repeated close or released a live team'}
    $limitBatch.closeOperation.Completed=$true;foreach($m in $limitBatch.members){$script:queueFakeJobs[$m.job].State='closed'};Pump-IzanamiQueue
    if($script:queueStarted.Count -ne 18){throw 'Refused team exit must admit next six accounts'}
    # Cancellation stops pending accounts without closing active games.
    Reset-IzanamiQueue;for($i=0;$i -lt 10;$i++){Pump-IzanamiQueue}
    if($script:queueStarted.Count -ne 18 -or $script:izanamiWaiting.Count){throw 'Cancelled queue continued launching'}
    # Kiểm tra ghép tổ linh hoạt bằng chính hàng chờ, không mở game thật.
    foreach($teamSize in @(1,2,4,6)){
        Reset-IzanamiQueue;$script:queueFakeJobs=@{};$script:queueStarted=@();$script:queueClosedSizes=@();$izanamiGroupSize.Value=$teamSize;$izanamiMax.Value=12;$izanamiCloseAfter.Checked=$false
        Add-IzanamiQueue @($script:accounts[0..9]);for($i=0;$i -lt 30;$i++){Pump-IzanamiQueue}
        if(($script:queueStarted -join ',') -ne ((1..10 | ForEach-Object {"queue-$_"}) -join ',')){throw 'Flexible grouping lost row order'}
        $sizes=@($script:izanamiBatches | ForEach-Object {$_.members.Count})
        if($sizes.Count -ne [Math]::Ceiling(10.0/$teamSize) -or @($sizes | Where-Object {$_ -gt $teamSize}).Count){throw 'Flexible team size/final partial group wrong'}
        $before=@($script:izanamiBatches | ForEach-Object {($_.members.id -join ',')}) -join '|';$izanamiGroupSize.Value=$(if($teamSize -eq 1){4}else{1});Pump-IzanamiQueue
        if($before -ne (@($script:izanamiBatches | ForEach-Object {($_.members.id -join ',')}) -join '|')){throw 'Changing size regrouped an existing queue'}
    }
    foreach($size in @(3,4,5,6)){
        $izanamiGroupSize.Value=$size;$izanamiMax.Value=$size
        $izanamiMax.UpButton();if($izanamiMax.Value -ne 2*$size){throw 'Maximum step must match team size'}
        $izanamiMax.UpButton();if($izanamiMax.Value -ne 3*$size){throw 'Second maximum step wrong'}
        $izanamiMax.DownButton();if($izanamiMax.Value -ne 2*$size){throw 'Down step wrong'}
    }
    $izanamiGroupSize.Value=3;$izanamiMax.Value=9;$izanamiGroupSize.Value=4
    if($izanamiMax.Value -ne 12){throw 'Changed team size must align maximum upward'}
    Write-Output 'PASS: max account arrows step by group size 3/4/5/6 and align when size changes.'
    Write-Output 'PASS: queue team sizes 1/2/4/6, selected row order, partial final teams and existing-team preservation.'
    Write-Output 'PASS: 152 selected / max 12; FIFO teams, all-member completion, asynchronous closing, process-exit confirmation, final two members, duplicate submission, auto-close off and cancellation.'
}finally {
    Reset-IzanamiQueue;$script:accounts=$queueSavedAccounts
    foreach($name in $queueFunctions.Keys){Set-Item ('Function:\'+$name) $queueFunctions[$name]}
    $izanamiGroupSize.Value=$queueSavedGroupSize;$izanamiMax.Value=$queueSavedMax;$izanamiCloseAfter.Checked=$queueSavedClose;Update-IzanamiQueueStatus
}
