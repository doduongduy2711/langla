$pollSavedCadence=$script:uiNextPaintTicks;$pollSavedClock=$script:uiPaintClock
$pollSavedJobs=$script:accountJobs;$pollSavedLast=$script:lastLoginJob;$pollSavedTicks=$script:uiTicks
$pollFunctions=@{};foreach($n in @('Pump-BeastQueue','Pump-SonCapQueue','Pump-DungeonQueue','Poll-SocketRequests','Pump-IzanamiQueue','Pump-QuestQueue','Retry-FailedAccounts','Update-UiSummary','Update-IzanamiQueueStatus','Update-QuestQueueStatus')){$pollFunctions[$n]=(Get-Item ('Function:\'+$n)).ScriptBlock}
try {
    $script:uiNextPaintTicks=@{};$script:uiPaintClock=-1
    $script:pollCounts=@{socket=0;beast=0;soncap=0;dungeon=0;izanami=0;quest=0;retry=0;ui=0;izanamiUi=0;questUi=0};$script:accountJobs=@{};$script:lastLoginJob='';$script:uiTicks=0
    function Pump-BeastQueue {$script:pollCounts.beast++}
    function Pump-SonCapQueue {$script:pollCounts.soncap++}
    function Pump-DungeonQueue {$script:pollCounts.dungeon++}
    function Poll-SocketRequests {$script:pollCounts.socket++}
    function Pump-IzanamiQueue {$script:pollCounts.izanami++}
    function Pump-QuestQueue {$script:pollCounts.quest++}
    function Retry-FailedAccounts {$script:pollCounts.retry++}
    function Update-UiSummary {$script:pollCounts.ui++}
    function Update-IzanamiQueueStatus {$script:pollCounts.izanamiUi++}
    function Update-QuestQueueStatus {$script:pollCounts.questUi++}
    for($i=0;$i -lt 100;$i++){
        $beforeQueues=$script:pollCounts.beast+$script:pollCounts.soncap+$script:pollCounts.dungeon+$script:pollCounts.izanami+$script:pollCounts.quest
        Invoke-ManagerTick
        if(($script:pollCounts.beast+$script:pollCounts.soncap+$script:pollCounts.dungeon+$script:pollCounts.izanami+$script:pollCounts.quest-$beforeQueues) -gt 1){throw 'Multiple activity queues still execute on the same timer tick'}
    }
    foreach($queueName in @('beast','soncap','dungeon','izanami','quest')){if($script:pollCounts[$queueName] -ne 20){throw 'Queue cadence changed'}}
    if($script:pollCounts.socket -ne 100 -or $script:pollCounts.izanami -ne 20 -or $script:pollCounts.quest -ne 20 -or $script:pollCounts.retry -ne 10 -or $script:pollCounts.ui -ne 4 -or $script:pollCounts.izanamiUi -ne 4 -or $script:pollCounts.questUi -ne 4){throw ('Unexpected UI/automation cadence: '+($script:pollCounts | ConvertTo-Json -Compress))}
    Write-Output 'PASS: actual 100 timer ticks: socket 100; queue scans 20; UI paints 4; retry scans 10. Fast socket polling preserved.'
}finally {
    foreach($n in $pollFunctions.Keys){Set-Item ('Function:\'+$n) $pollFunctions[$n]}
    $script:uiNextPaintTicks=$pollSavedCadence;$script:uiPaintClock=$pollSavedClock
    $script:accountJobs=$pollSavedJobs;$script:lastLoginJob=$pollSavedLast;$script:uiTicks=$pollSavedTicks
}
