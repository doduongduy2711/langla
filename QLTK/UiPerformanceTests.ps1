$perfSavedTab=$tabs.SelectedTab;$perfSavedTicks=$script:uiTicks;$perfSavedDrag=$script:uiDragging
$perfNames=@('Update-SonCapUi','Update-StoneChestUi','Update-MarketUi','Update-CloakUi','Update-TrainUi','Update-FarmUi','Update-InventoryUi','Update-IzanamiUi','Update-AutoItemUi','Update-QuestUi','Update-GiftUi','Update-MailUi','Update-DistributionUi')
$perfBusiness=@('Poll-SocketRequests','Retry-FailedAccounts','Pump-SonCapQueue','Pump-IzanamiQueue','Pump-QuestQueue','Pump-DungeonQueue','Sync-IzanamiTeams','Sync-DailyPriorityPreferences','Sync-SonCapPreferences','Sync-CloakQueue','Sync-AutoRows','Sync-AutoItemPreferences','Sync-QuestPreferences','Sync-GiftPreferences','Sync-MailCollection','Sync-Distribution','Update-UiSummary')
$perfOriginal=@{};foreach($n in ($perfNames+$perfBusiness)){$perfOriginal[$n]=(Get-Item ('Function:\'+$n)).ScriptBlock}
try {
 $script:perfCounts=@{};foreach($n in $perfNames){$script:perfCounts[$n]=0;Set-Item ('Function:\'+$n) ([scriptblock]::Create('$script:perfCounts['''+$n+''']++'))}
 $script:uiDragging=$false;$tabs.SelectedTab=$marketPage;foreach($n in $perfNames){$script:perfCounts[$n]=0};Update-ActiveTabUi
 if($script:perfCounts['Update-MarketUi'] -ne 1 -or @($perfNames | Where-Object {$_ -ne 'Update-MarketUi' -and $script:perfCounts[$_] -ne 0}).Count){throw 'Hidden tabs still refresh'}
 $tabs.SelectedTab=$trainPage;foreach($n in $perfNames){$script:perfCounts[$n]=0};Update-ActiveTabUi
 if($script:perfCounts['Update-TrainUi'] -ne 1 -or $script:perfCounts['Update-MarketUi'] -ne 0){throw 'Tab switch does not update current pane'}
 $script:perfBusinessCalls=0;$script:perfSummaryCalls=0
 foreach($n in $perfBusiness){Set-Item ('Function:\'+$n) {$script:perfBusinessCalls++}}
 function Update-UiSummary {$script:perfSummaryCalls++}
 $script:uiDragging=$true;$script:uiTicks=9;foreach($n in $perfNames){$script:perfCounts[$n]=0};Invoke-ManagerTick
 if($script:perfBusinessCalls -lt 10 -or $script:perfSummaryCalls -ne 0 -or @($perfNames | Where-Object {$script:perfCounts[$_] -ne 0}).Count){throw 'Dragging must retain automation and defer paint refresh'}
 $script:uiDragging=$false;$script:uiTicks=9;Invoke-ManagerTick
 if($script:perfSummaryCalls -ne 1 -or $script:perfCounts['Update-TrainUi'] -ne 1){throw 'Rendering did not resume after drag'}
 $bufferProperty=[Windows.Forms.Control].GetProperty('DoubleBuffered',[Reflection.BindingFlags]'Instance,NonPublic')
 if(!$bufferProperty.GetValue($form,$null) -or !$bufferProperty.GetValue($grid,$null) -or !$bufferProperty.GetValue($marketResultsGrid,$null)){throw 'Double buffering missing'}
 Write-Output 'PASS: visible-tab-only refresh, immediate tab switch, queue/preference/socket work during drag, rendering resumes afterward, form/account/market double buffering.'
}finally{foreach($n in $perfOriginal.Keys){Set-Item ('Function:\'+$n) $perfOriginal[$n]};$script:uiTicks=$perfSavedTicks;$script:uiDragging=$perfSavedDrag;$tabs.SelectedTab=$perfSavedTab}

# Compare the former per-cell PowerShell handler to the compiled handler on the same synthetic table.
$perfTables=@();$perfBitmap=$null
try {
 foreach($mode in @(0,1)){
  $t=New-Object Windows.Forms.DataGridView;$t.SetBounds(0,0,900,240);$t.AllowUserToAddRows=$false;$t.RowHeadersVisible=$false
  for($i=0;$i -lt 13;$i++){$col=New-Object Windows.Forms.DataGridViewTextBoxColumn;$col.Name=if($i -eq 10){'open'}else{'c'+$i};$col.Width=65;$null=$t.Columns.Add($col)}
  for($i=0;$i -lt 12;$i++){$null=$t.Rows.Add();$t.Rows[$i].Cells['open'].Tag=$true}
  if($mode -eq 0){$t.Add_CellPainting({param($sender,$e)
   if($e.RowIndex -lt 0 -or $e.ColumnIndex -lt 0 -or $sender.Columns[$e.ColumnIndex].Name -ne 'open'){return}
   if($sender.Rows[$e.RowIndex].Cells['open'].Tag -ne $true){return}
   $e.PaintBackground($e.ClipBounds,$true);$e.Paint($e.ClipBounds,[Windows.Forms.DataGridViewPaintParts]::Border);$rect=$e.CellBounds;$rect.Inflate(-2,-2)
   [Windows.Forms.ButtonRenderer]::DrawButton($e.Graphics,$rect,'OPEN',$sender.Font,$false,[Windows.Forms.VisualStyles.PushButtonState]::Disabled);$e.Handled=$true
  })}else{[TGLL.UiPerformance]::InstallOpenPainting($t)}
  $null=$t.Handle;$perfTables+=,$t
 }
 $perfBitmap=New-Object Drawing.Bitmap(900,240);$bounds=New-Object Drawing.Rectangle(0,0,900,240);$elapsed=@()
 foreach($t in $perfTables){$t.DrawToBitmap($perfBitmap,$bounds);$watch=[Diagnostics.Stopwatch]::StartNew();for($i=0;$i -lt 12;$i++){$t.DrawToBitmap($perfBitmap,$bounds)};$watch.Stop();$elapsed+=,$watch.ElapsedMilliseconds}
 Write-Output ('PERF: 12 synthetic grid repaints: PowerShell handler '+$elapsed[0]+' ms; compiled/buffered handler '+$elapsed[1]+' ms. No game action.')
}finally{if($perfBitmap){$perfBitmap.Dispose()};foreach($t in $perfTables){$t.Dispose()}}
