$scrollTicks=$script:uiTicks;$scrollPending=$script:uiRefreshPending;$scrollDrag=$script:uiDragging;$scrollSize=$form.ClientSize
$scrollFunctions=@{};foreach($n in @('Poll-SocketRequests','Pump-SonCapQueue','Pump-IzanamiQueue','Pump-QuestQueue','Pump-DungeonQueue','Retry-FailedAccounts','Sync-IzanamiTeams','Sync-DailyPriorityPreferences','Sync-SonCapPreferences','Sync-CloakQueue','Sync-AutoRows','Sync-AutoItemPreferences','Sync-QuestPreferences','Sync-GiftPreferences','Sync-MailCollection','Sync-Distribution','Update-ActiveTabUi','Update-UiSummary')){$scrollFunctions[$n]=(Get-Item ('Function:\'+$n)).ScriptBlock}
try {
 $script:scrollSocket=0;$script:scrollQueues=0;$script:scrollSync=0;$script:scrollPaint=0
 foreach($n in $scrollFunctions.Keys){Set-Item ('Function:\'+$n) {}}
 function Poll-SocketRequests {$script:scrollSocket++};function Pump-DungeonQueue {$script:scrollQueues++};function Sync-IzanamiTeams {$script:scrollSync++};function Update-UiSummary {$script:scrollPaint++}
 $script:uiDragging=$false;$script:uiTicks=9;[TGLL.UiPerformance]::NoteScroll();Invoke-ManagerTick
 if($script:scrollSocket -ne 1 -or $script:scrollQueues -ne 1 -or $script:scrollSync -ne 0 -or $script:scrollPaint -ne 0 -or !$script:uiRefreshPending){throw 'Scrolling must preserve socket/queue work and defer bulk refresh'}
 [Threading.Thread]::Sleep(300);Invoke-ManagerTick
 if($script:scrollSync -ne 1 -or $script:scrollPaint -ne 1 -or $script:uiRefreshPending){throw 'Deferred pass did not resume promptly after scroll stopped'}
 if($grid -isnot [TGLL.ResponsiveGrid] -or $inventoryGrid -isnot [TGLL.ResponsiveGrid] -or $marketResultsGrid -isnot [TGLL.ResponsiveGrid]){throw 'Native scroll tracking missing'}
 foreach($size in @((New-Object Drawing.Size(900,640)),(New-Object Drawing.Size(1100,800)))){
  $form.ClientSize=$size;Resize-UiLayout
  if($grid.Bottom -ge $tabs.Top -or $tabs.Right -ge $detailsGroup.Left -or $detailsGroup.Right -gt $form.ClientSize.Width -or $options.Right -gt $form.ClientSize.Width -or $tabs.Height -lt 100){throw 'Compact layout overlaps or clips panels'}
 }
 if($grid.Columns.Count -ne 14 -or $tabs.TabPages.Count -ne 25 -or $grid.RowTemplate.Height -ne 22){throw 'Compact mode removed features'}
 Write-Output 'PASS: native scroll tracking, queues/socket preserved while scrolling, deferred bulk refresh resumes after 250ms quiet, 900/1100px compact layout with all columns/tabs retained.'
}finally{foreach($n in $scrollFunctions.Keys){Set-Item ('Function:\'+$n) $scrollFunctions[$n]};$script:uiTicks=$scrollTicks;$script:uiRefreshPending=$scrollPending;$script:uiDragging=$scrollDrag;$form.ClientSize=$scrollSize;Resize-UiLayout}
