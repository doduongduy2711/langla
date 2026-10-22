# Exercise the actual scheduler with fake launch/close adapters; never touches a real game.
$queueSavedGroupSize=$soncapGroupSize.Value;$queueSavedAccounts=$script:accounts;$queueSavedMax=$soncapMax.Value;$queueSavedClose=$soncapCloseAfter.Checked
$queueFunctions=@{};foreach($name in @('Start-SonCapMember','Get-SonCapMemberJob','Begin-SonCapTeamClose','Test-SonCapMemberRunning','Test-AccountOpenBlocked')){$queueFunctions[$name]=(Get-Item ('Function:\'+$name)).ScriptBlock}
try {
    $script:queueFakeJobs=@{};$script:queueStarted=@();$script:queueClosedSizes=@()
    function Start-SonCapMember($m){if($null -ne (Get-SonCapQueuedMember $m.id)){throw 'Admitted member still blocked by queue OPEN guard'};$m.job=$m.id;$script:queueStarted+=@($m.id);$script:queueFakeJobs[$m.id]=[pscustomobject]@{State='opened';ProcessId=1;SonCapFinished=$false;CloseRequested=$false;CancelRequested=$false}}
    function Get-SonCapMemberJob($m){return $script:queueFakeJobs[$m.job]}
    function Test-AccountOpenBlocked($a){return ($script:queueFakeJobs.ContainsKey([string]$a.id) -and $script:queueFakeJobs[[string]$a.id].State -eq 'opened')}
    function Test-SonCapMemberRunning($m){return ($script:queueFakeJobs[$m.job].State -eq 'opened')}
    function Begin-SonCapTeamClose($b){$script:queueClosedSizes+=@($b.members.Count);return [pscustomobject]@{Completed=$false;Failed=0;Error=''}}
    $script:accounts=@(1..152 | ForEach-Object {[pscustomobject]@{id="queue-$_";username="queue-$_";soncap=$true}})
    Reset-SonCapQueue;$soncapMax.Value=12;$soncapCloseAfter.Checked=$true
    Add-SonCapQueue $script:accounts
    for($i=0;$i -lt 30;$i++){Pump-SonCapQueue}
    if($script:queueStarted.Count -ne 12 -or $script:soncapWaiting.Count -ne 140){throw '152 accounts / max 12 did not keep 140 waiting'}
    if(($script:queueStarted -join ',') -ne ((1..12 | ForEach-Object {"queue-$_"}) -join ',')){throw 'Queue did not preserve selected table order'}
    $first=$script:soncapBatches[0]
    foreach($m in $first.members[0..4]){$script:queueFakeJobs[$m.job].SonCapFinished=$true};Pump-SonCapQueue
    if($script:queueClosedSizes.Count){throw 'One unfinished member must prevent team close'}
    $script:queueFakeJobs[$first.members[5].job].SonCapFinished=$true;Pump-SonCapQueue
    if($first.state -ne 'closing' -or $script:queueStarted.Count -ne 12){throw 'Close in progress released slots prematurely'}
    $first.closeOperation.Completed=$true;Pump-SonCapQueue
    if($script:queueStarted.Count -ne 12){throw 'Process still running released slots prematurely'}
    foreach($m in $first.members){$script:queueFakeJobs[$m.job].State='closed'};Pump-SonCapQueue
    if($script:queueStarted.Count -ne 18 -or $script:queueStarted[12] -ne 'queue-13'){throw 'Confirmed team close did not start the next team'}
    # Complete every admitted team. Only running teams may finish; fake closes take a separate tick.
    for($tick=0;$tick -lt 1000;$tick++){
        foreach($b in $script:soncapBatches){
            if($b.state -eq 'running'){foreach($m in $b.members){$script:queueFakeJobs[$m.job].SonCapFinished=$true}}
            if($b.state -eq 'closing'){$b.closeOperation.Completed=$true;foreach($m in $b.members){$script:queueFakeJobs[$m.job].State='closed'}}
        }
        Pump-SonCapQueue
        if(@($script:queueFakeJobs.Values | Where-Object {$_.State -eq 'opened'}).Count -gt 12){throw 'Live game count exceeded max 12'}
        if(@($script:soncapBatches | Where-Object {$_.state -ne 'done'}).Count -eq 0){break}
    }
    if($script:queueStarted.Count -ne 152 -or $tick -ge 1000 -or $script:queueClosedSizes[-1] -ne 2){throw 'Queue over 100 accounts or final two-person team failed'}
    Add-SonCapQueue $script:accounts
    if($script:queueStarted.Count -ne 152){throw 'Duplicate queue submission reran completed accounts'}
    # With auto close off, finishing does not release occupied slots.
    Reset-SonCapQueue;$script:queueFakeJobs=@{};$script:queueStarted=@();$script:queueClosedSizes=@();$soncapCloseAfter.Checked=$false
    Add-SonCapQueue @($script:accounts[0..17]);for($i=0;$i -lt 30;$i++){Pump-SonCapQueue}
    foreach($m in $script:soncapBatches[0].members){$script:queueFakeJobs[$m.job].SonCapFinished=$true};Pump-SonCapQueue
    if($script:queueClosedSizes.Count -or $script:queueStarted.Count -ne 12){throw 'Unticked auto close closed games or exceeded capacity'}
    $limitBatch=$script:soncapBatches[0];$script:queueFakeJobs[$limitBatch.members[0].job] | Add-Member NoteProperty SonCapNoAttempts $true -Force
    Pump-SonCapQueue
    if($limitBatch.state -ne 'closing' -or $script:queueClosedSizes.Count -ne 1){throw 'No attempts must close finished refused team with option off'}
    for($n=0;$n -lt 3;$n++){Pump-SonCapQueue}
    if($script:queueClosedSizes.Count -ne 1 -or $script:queueStarted.Count -ne 12){throw 'No-attempt close duplicated or released occupied slots'}
    # Cancellation stops pending accounts without closing active games.
    Reset-SonCapQueue;for($i=0;$i -lt 10;$i++){Pump-SonCapQueue}
    if($script:queueStarted.Count -ne 12 -or $script:soncapWaiting.Count){throw 'Cancelled queue continued launching'}
    if(@($script:soncapBatches | Where-Object {$_.groupSize -ne 6}).Count){throw 'SonCap teams must stay fixed at six'}
    Write-Output 'PASS: SonCap fixed groups of six, selected row order, final partial team and cancellation.'
    Write-Output 'PASS: 152 selected / max 12; FIFO teams, all-member completion, asynchronous closing, process-exit confirmation, final two members, duplicate submission, auto-close off and cancellation.'
}finally {
    Reset-SonCapQueue;$script:accounts=$queueSavedAccounts
    foreach($name in $queueFunctions.Keys){Set-Item ('Function:\'+$name) $queueFunctions[$name]}
    $soncapGroupSize.Value=$queueSavedGroupSize;$soncapMax.Value=$queueSavedMax;$soncapCloseAfter.Checked=$queueSavedClose;Update-SonCapQueueStatus
}
