# Actual scheduler, fake game lifecycle; no real launch or close.
$savedDungeonAccounts=$script:accounts;$savedDungeonMax=$dungeonMax.Value;$savedDungeonClose=$dungeonCloseAfter.Checked
$dungeonFunctions=@{};foreach($n in @('Start-DungeonMember','Get-DungeonMemberJob','Begin-DungeonTeamClose','Test-DungeonMemberRunning','Test-AccountOpenBlocked')){$dungeonFunctions[$n]=(Get-Item ('Function:\'+$n)).ScriptBlock}
try {
    $script:dungeonFakeJobs=@{};$script:dungeonStarts=@()
    function Start-DungeonMember($m){if($null -ne (Get-DungeonQueuedMember $m.id)){throw 'Admitted task still blocked by queue guard'};$m.job=$m.id;$script:dungeonStarts+=@($m.id);$script:dungeonFakeJobs[$m.id]=[pscustomobject]@{State='opened';ProcessId=1;DungeonFinished=$false;CloseRedungeoned=$false;CancelRedungeoned=$false}}
    function Get-DungeonMemberJob($m){return $script:dungeonFakeJobs[$m.job]}
    function Test-AccountOpenBlocked($a){return ($script:dungeonFakeJobs.ContainsKey([string]$a.id) -and $script:dungeonFakeJobs[[string]$a.id].State -eq 'opened')}
    function Test-DungeonMemberRunning($m){return ($script:dungeonFakeJobs[$m.job].State -eq 'opened')}
    function Begin-DungeonTeamClose($b){if($b.members.Count -ne 1){throw 'Weekly tasks must release individually'};return [pscustomobject]@{Completed=$false;Failed=0;Error=''}}
    Reset-DungeonQueue;$dungeonMax.Value=12;$dungeonCloseAfter.Checked=$true
    $script:accounts=@(1..137 | ForEach-Object {[pscustomobject]@{id="dungeon-$_";username="dungeon-$_";dungeonHigh=$true}})
    Add-DungeonQueue $script:accounts;for($i=0;$i -lt 30;$i++){Pump-DungeonQueue}
    if($script:dungeonStarts.Count -ne 12 -or $script:dungeonWaiting.Count -ne 125){throw '137 selected / max 12 did not hold remaining accounts'}
    $first=$script:dungeonBatches[0];Pump-DungeonQueue
    if($first.state -ne 'running'){throw 'Unconfirmed task prematurely finished'}
    $script:dungeonFakeJobs[$first.members[0].job].DungeonFinished=$true;Pump-DungeonQueue
    if($first.state -ne 'closing' -or $script:dungeonStarts.Count -ne 12){throw 'Close in progress released account slot'}
    $first.closeOperation.Completed=$true;Pump-DungeonQueue
    if($script:dungeonStarts.Count -ne 12){throw 'Live process released account slot'}
    $script:dungeonFakeJobs[$first.members[0].job].State='closed';Pump-DungeonQueue
    if($script:dungeonStarts.Count -ne 13 -or $script:dungeonStarts[-1] -ne 'dungeon-13'){throw 'Single completed account did not release one slot in FIFO order'}
    for($tick=0;$tick -lt 1000;$tick++){
        foreach($b in $script:dungeonBatches){if($b.state -eq 'running'){$script:dungeonFakeJobs[$b.members[0].job].DungeonFinished=$true};if($b.state -eq 'closing'){$b.closeOperation.Completed=$true;$script:dungeonFakeJobs[$b.members[0].job].State='closed'}}
        Pump-DungeonQueue
        if(@($script:dungeonFakeJobs.Values | Where-Object {$_.State -eq 'opened'}).Count -gt 12){throw 'Dungeon queue exceeded maximum live games'}
        if(@($script:dungeonBatches | Where-Object {$_.state -ne 'done'}).Count -eq 0){break}
    }
    if($script:dungeonStarts.Count -ne 137 -or $tick -ge 1000){throw 'Final account did not run'}
    if(($script:dungeonStarts -join ',') -ne ((1..137 | ForEach-Object {"dungeon-$_"}) -join ',')){throw 'Dungeon account order changed'}
    Add-DungeonQueue $script:accounts;if($script:dungeonStarts.Count -ne 137){throw 'Duplicate submission reran accounts'}
    Reset-DungeonQueue;$script:dungeonFakeJobs=@{};$script:dungeonStarts=@();$dungeonMax.Value=1;$dungeonCloseAfter.Checked=$false
    Add-DungeonQueue @($script:accounts[0..2]);for($i=0;$i -lt 5;$i++){Pump-DungeonQueue};$script:dungeonFakeJobs['dungeon-1'].DungeonFinished=$true;Pump-DungeonQueue
    if($script:dungeonStarts.Count -ne 1 -or $script:dungeonBatches[0].state -ne 'finished'){throw 'Auto close off must hold the open game slot'}
    Reset-DungeonQueue;for($i=0;$i -lt 5;$i++){Pump-DungeonQueue};if($script:dungeonStarts.Count -ne 1){throw 'Cancel continued launching tasks'}
    Write-Output 'PASS: 137 accounts / max 12, one-account completion and close, exit verification, FIFO, final account, duplicate submission, max 1, auto-close off and cancel.'
}finally {
    Reset-DungeonQueue;$script:accounts=$savedDungeonAccounts
    foreach($n in $dungeonFunctions.Keys){Set-Item ('Function:\'+$n) $dungeonFunctions[$n]}
    $dungeonMax.Value=$savedDungeonMax;$dungeonCloseAfter.Checked=$savedDungeonClose;Update-DungeonQueueStatus
}
