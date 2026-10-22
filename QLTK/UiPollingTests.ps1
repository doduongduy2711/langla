$pollSavedJobs=$script:accountJobs;$pollSavedLast=$script:lastLoginJob;$pollSavedTicks=$script:uiTicks
$pollFunctions=@{};foreach($n in @('Poll-SocketRequests','Pump-IzanamiQueue','Pump-QuestQueue','Retry-FailedAccounts','Update-UiSummary','Update-IzanamiQueueStatus','Update-QuestQueueStatus')){$pollFunctions[$n]=(Get-Item ('Function:\'+$n)).ScriptBlock}
try {
    $script:pollCounts=@{socket=0;izanami=0;quest=0;retry=0;ui=0;izanamiUi=0;questUi=0};$script:accountJobs=@{};$script:lastLoginJob='';$script:uiTicks=0
    function Poll-SocketRequests {$script:pollCounts.socket++}
    function Pump-IzanamiQueue {$script:pollCounts.izanami++}
    function Pump-QuestQueue {$script:pollCounts.quest++}
    function Retry-FailedAccounts {$script:pollCounts.retry++}
    function Update-UiSummary {$script:pollCounts.ui++}
    function Update-IzanamiQueueStatus {$script:pollCounts.izanamiUi++}
    function Update-QuestQueueStatus {$script:pollCounts.questUi++}
    for($i=0;$i -lt 100;$i++){Invoke-ManagerTick}
    if($script:pollCounts.socket -ne 100 -or $script:pollCounts.izanami -ne 20 -or $script:pollCounts.quest -ne 20 -or $script:pollCounts.retry -ne 10 -or $script:pollCounts.ui -ne 10 -or $script:pollCounts.izanamiUi -ne 10 -or $script:pollCounts.questUi -ne 10){throw 'UI timer still scans queues or paints every 100ms'}
    Write-Output 'PASS: actual 100 timer ticks: socket 100; queue scans 20; UI/row status/retry scans 10. Fast socket polling preserved.'
}finally {
    foreach($n in $pollFunctions.Keys){Set-Item ('Function:\'+$n) $pollFunctions[$n]}
    $script:accountJobs=$pollSavedJobs;$script:lastLoginJob=$pollSavedLast;$script:uiTicks=$pollSavedTicks
}
