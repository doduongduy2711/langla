$script:uiDragging=$false;$script:uiRefreshPending=$false
[TGLL.UiPerformance]::Buffer($form)
foreach($panel in @($top,$bottom,$tabs,$detailsGroup)){[TGLL.UiPerformance]::Buffer($panel)}
[TGLL.UiPerformance]::Buffer($inventoryGrid);[TGLL.UiPerformance]::Buffer($marketResultsGrid)
$form.Add_ResizeBegin({$script:uiDragging=$true})
$form.Add_ResizeEnd({$script:uiDragging=$false})
function Update-ActiveTabUi {
    if([TGLL.UiPerformance]::IsScrolling -or $script:uiDragging -or $form.WindowState -eq [Windows.Forms.FormWindowState]::Minimized){return}
    $selected=$tabs.SelectedTab
    if($script:gameSettingPages -contains $selected){Update-GameSettingsUi}
    elseif($selected -eq $soncapPage){Update-SonCapUi}
    elseif($selected -eq $chestPage){Update-StoneChestUi}
    elseif($selected -eq $marketPage){Update-MarketUi}
    elseif($selected -eq $gemPage){Update-GemCombineUi}
    elseif($selected -eq $cloakPage){Update-CloakUi}
    elseif($selected -eq $trainPage){Update-TrainUi}
    elseif($selected -eq $farmPage){Update-FarmUi}
    elseif($selected -eq $inventoryPage){Update-InventoryUi}
    elseif($selected -eq $izanamiPage){Update-IzanamiUi}
    elseif($selected -eq $itemPage){Update-AutoItemUi}
    elseif($selected -eq $beastPage){Update-BeastUi}
    elseif($selected -eq $questPage){Update-QuestUi}
    elseif($selected -eq $giftPage){Update-GiftUi}
    elseif($selected -eq $mailPage){Update-MailUi}
    elseif($selected -eq $distributionPage){Update-DistributionUi}
}
$tabs.Add_SelectedIndexChanged({Update-ActiveTabUi})

# A large list must yield back to the WinForms message pump between expensive scans.
$script:largeRefreshPhase=0;$script:autoSyncCursor=0
$script:largeRefreshTasks=@('Sync-IzanamiTeams','Sync-DailyPriorityPreferences','Sync-SonCapPreferences','Sync-CloakQueue','Sync-AutoItemPreferences','Sync-QuestPreferences','Sync-GiftPreferences','Sync-MailCollection','Sync-Distribution','Update-ActiveTabUi','Update-UiSummary','Update-ManagerStatus')
# Keep each queue on its 500 ms cadence, but put it on a separate UI timer tick.
function Invoke-ActivityQueuePhase {
 switch($script:uiTicks % 5){
  0 {Invoke-UiTimedStep 'Pump-DungeonQueue';[TGLL.NativeBackend]::PulseSonCapLimitClose()}
  1 {Invoke-UiTimedStep 'Pump-BeastQueue';Invoke-UiTimedStep 'Pump-AutoAll'}
  2 {Invoke-UiTimedStep 'Pump-SonCapQueue';[TGLL.NativeBackend]::PulseActivityWatchdog()}
  3 {Invoke-UiTimedStep 'Pump-IzanamiQueue';[TGLL.NativeBackend]::PulseIzanamiRounds()}
  4 {Invoke-UiTimedStep 'Pump-QuestQueue';[TGLL.NativeBackend]::PulseQuestAutoClose()}
 }
}
function Invoke-UiTimedStep([string]$stepName){
 $clock=[Diagnostics.Stopwatch]::StartNew()
 try{& $stepName}
 finally{
  $elapsed=$clock.ElapsedMilliseconds
  if(!$UiTest -and $elapsed -ge 100){
   try{
    if(!$script:uiSlowStepAt){$script:uiSlowStepAt=@{}}
    $now=[datetime]::UtcNow
    if(!$script:uiSlowStepAt.ContainsKey($stepName) -or ($now-$script:uiSlowStepAt[$stepName]).TotalSeconds -ge 30){
     $script:uiSlowStepAt[$stepName]=$now
     $record=@{time=$now.ToString('o');event='ui_slow_step';step=$stepName;elapsed_ms=$elapsed;accounts=$grid.Rows.Count}
     $folder=Join-Path $PSScriptRoot 'logs';$null=[IO.Directory]::CreateDirectory($folder)
     [IO.File]::AppendAllText((Join-Path $folder 'ui-performance.jsonl'),((ConvertTo-Json $record -Compress)+[Environment]::NewLine),(New-Object Text.UTF8Encoding($false)))
    }
   }catch{}
  }
 }
}
function Invoke-LargeManagerRefresh {
    # All account Auto/Up intentions are checked in bounded batches, independently of rendering.
    Sync-AutoRows 8
    $task=$script:largeRefreshTasks[$script:largeRefreshPhase]
    $script:largeRefreshPhase=($script:largeRefreshPhase+1)%$script:largeRefreshTasks.Count
    if($task -like 'Update-*' -and ([TGLL.UiPerformance]::IsScrolling -or $script:uiDragging -or $form.WindowState -eq [Windows.Forms.FormWindowState]::Minimized)){return}
    Invoke-UiTimedStep $task
}
