# Actual scheduler, fake game lifecycle; no real launch or close.
$savedQuestAccounts=$script:accounts;$savedQuestMax=$questMax.Value;$savedQuestClose=$questCloseAfter.Checked
$questFunctions=@{};foreach($n in @('Start-QuestMember','Get-QuestMemberJob','Begin-QuestTeamClose','Test-QuestMemberRunning','Test-AccountOpenBlocked')){$questFunctions[$n]=(Get-Item ('Function:\'+$n)).ScriptBlock}
try {
    $script:questFakeJobs=@{};$script:questStarts=@()
    function Start-QuestMember($m){if($null -ne (Get-QuestQueuedMember $m.id)){throw 'Admitted task still blocked by queue guard'};$m.job=$m.id;$script:questStarts+=@($m.id);$script:questFakeJobs[$m.id]=[pscustomobject]@{State='opened';ProcessId=1;QuestFinished=$false;CloseRequested=$false;CancelRequested=$false}}
    function Get-QuestMemberJob($m){return $script:questFakeJobs[$m.job]}
    function Test-AccountOpenBlocked($a){return ($script:questFakeJobs.ContainsKey([string]$a.id) -and $script:questFakeJobs[[string]$a.id].State -eq 'opened')}
    function Test-QuestMemberRunning($m){return ($script:questFakeJobs[$m.job].State -eq 'opened')}
    function Begin-QuestTeamClose($b){if($b.members.Count -ne 1){throw 'Weekly tasks must release individually'};return [pscustomobject]@{Completed=$false;Failed=0;Error=''}}
    Reset-QuestQueue;$questMax.Value=12;$questCloseAfter.Checked=$true
    $script:accounts=@(1..137 | ForEach-Object {[pscustomobject]@{id="weekly-$_";username="weekly-$_";autoQuest=$true}})
    Add-QuestQueue $script:accounts;for($i=0;$i -lt 30;$i++){Pump-QuestQueue}
    if($script:questStarts.Count -ne 12 -or $script:questWaiting.Count -ne 125){throw '137 selected / max 12 did not hold remaining accounts'}
    $first=$script:questBatches[0];Pump-QuestQueue
    if($first.state -ne 'running'){throw 'Unconfirmed task prematurely finished'}
    $script:questFakeJobs[$first.members[0].job].QuestFinished=$true;Pump-QuestQueue
    if($first.state -ne 'closing' -or $script:questStarts.Count -ne 12){throw 'Close in progress released account slot'}
    $first.closeOperation.Completed=$true;$first.closeOperation.Failed=1;Pump-QuestQueue
    if($script:questStarts.Count -ne 12){throw 'Live process released account slot'}
    $script:questFakeJobs[$first.members[0].job].State='closed';Pump-QuestQueue
    if($script:questStarts.Count -ne 13 -or $script:questStarts[-1] -ne 'weekly-13'){throw 'Single completed account did not release one slot in FIFO order'}
    for($tick=0;$tick -lt 1000;$tick++){
        foreach($b in $script:questBatches){if($b.state -eq 'running'){$script:questFakeJobs[$b.members[0].job].QuestFinished=$true};if($b.state -eq 'closing'){$b.closeOperation.Completed=$true;$script:questFakeJobs[$b.members[0].job].State='closed'}}
        Pump-QuestQueue
        if(@($script:questFakeJobs.Values | Where-Object {$_.State -eq 'opened'}).Count -gt 12){throw 'Quest queue exceeded maximum live games'}
        if(@($script:questBatches | Where-Object {$_.state -ne 'done'}).Count -eq 0){break}
    }
    if($script:questStarts.Count -ne 137 -or $tick -ge 1000){throw 'Final account did not run'}
    if(($script:questStarts -join ',') -ne ((1..137 | ForEach-Object {"weekly-$_"}) -join ',')){throw 'Quest account order changed'}
    Add-QuestQueue $script:accounts;if($script:questStarts.Count -ne 137){throw 'Duplicate submission reran accounts'}
    # Reopened completed accounts count outside the active queue; drain before admitting more.
    Reset-QuestQueue;$script:questFakeJobs=@{};$script:questStarts=@();$questMax.Value=2
    Add-QuestQueue @($script:accounts[0..4]);for($n=0;$n -lt 8;$n++){Pump-QuestQueue}
    $retired=$script:questBatches[0];$script:questFakeJobs[$retired.members[0].job].QuestFinished=$true;Pump-QuestQueue
    $retired.closeOperation.Completed=$true;$script:questFakeJobs[$retired.members[0].job].State='closed';for($n=0;$n -lt 4;$n++){Pump-QuestQueue}
    $script:questFakeJobs[$retired.members[0].job].State='opened'
    $second=$script:questBatches[1];$script:questFakeJobs[$second.members[0].job].QuestFinished=$true;Pump-QuestQueue
    $second.closeOperation.Completed=$true;$script:questFakeJobs[$second.members[0].job].State='closed';for($n=0;$n -lt 4;$n++){Pump-QuestQueue}
    if($script:questStarts.Count -ne 3){throw 'Reopened completed account was omitted from capacity'}
    Reset-QuestQueue;$script:questFakeJobs=@{};$script:questStarts=@();$questMax.Value=1;$questCloseAfter.Checked=$false
    Add-QuestQueue @($script:accounts[0..2]);for($i=0;$i -lt 5;$i++){Pump-QuestQueue};$script:questFakeJobs['weekly-1'].QuestFinished=$true;Pump-QuestQueue
    if($script:questStarts.Count -ne 1 -or $script:questBatches[0].state -ne 'finished'){throw 'Auto close off must hold the open game slot'}
    # Timeout marks the queue blocked. Completion after manual resume must still close.
    $questCloseAfter.Checked=$true;$script:questBatches[0].state='blocked';$script:questBatches[0].error='timeout';Pump-QuestQueue
    if($script:questBatches[0].state -ne 'closing' -or $script:questBatches[0].error){throw 'Finished quest stuck in blocked timeout queue'}
    $script:questBatches[0].state='blocked';$script:questBatches[0].error='close failed';$script:questBatches[0].closeOperation.Completed=$true;$script:questBatches[0].closeOperation.Failed=1;Pump-QuestQueue
    if($script:questBatches[0].state -ne 'blocked'){throw 'Failed close auto-retried without review'}
    Reset-QuestQueue;for($i=0;$i -lt 5;$i++){Pump-QuestQueue};if($script:questStarts.Count -ne 1){throw 'Cancel continued launching tasks'}
    Write-Output 'PASS: 137 accounts / max 12, one-account completion and close, exit verification, FIFO, final account, duplicate submission, max 1, auto-close off and cancel.'
}finally {
    Reset-QuestQueue;$script:accounts=$savedQuestAccounts
    foreach($n in $questFunctions.Keys){Set-Item ('Function:\'+$n) $questFunctions[$n]}
    $questMax.Value=$savedQuestMax;$questCloseAfter.Checked=$savedQuestClose;Update-QuestQueueStatus
}
