$lrCount=$grid.Rows.Count;$lrMappings=$script:accountJobs;$lrPhase=$script:largeRefreshPhase;$lrCursor=$script:autoSyncCursor;$lrDrag=$script:uiDragging;$lrTicks=$script:uiTicks
$lrNames=@($script:largeRefreshTasks)+@('Sync-AutoRows','Sync-FarmUpRow','Poll-SocketRequests','Retry-FailedAccounts','Pump-SonCapQueue','Pump-IzanamiQueue','Pump-QuestQueue','Pump-DungeonQueue','Update-AccountVisibility')
$lrOriginal=@{};foreach($n in $lrNames){$lrOriginal[$n]=(Get-Item ('Function:\'+$n)).ScriptBlock}
try {
 $script:accountJobs=@{}
 for($i=$lrCount;$i -lt 96;$i++){$idx=$grid.Rows.Add();$grid.Rows[$idx].Tag=[pscustomobject]@{id=('large-'+$i);username=('large-'+$i)}}
 $script:lrFarmRows=New-Object 'System.Collections.Generic.List[int]'
 function Sync-FarmUpRow($row){$script:lrFarmRows.Add($row.Index)}
 $script:autoSyncCursor=0;Sync-AutoRows 24;Sync-AutoRows 24
 if($script:lrFarmRows.Count -ne 48 -or @($script:lrFarmRows | Select-Object -Unique).Count -ne 48){throw 'Auto/Up synchronization is not bounded or skips account order'}
 $script:lrCalls=@{};foreach($n in $lrNames){$script:lrCalls[$n]=0;Set-Item ('Function:\'+$n) ([scriptblock]::Create('$script:lrCalls['''+$n+''']++'))}
 function Sync-AutoRows([int]$maxRows){if($maxRows -ne 24){throw 'Unbounded Auto scan in large timer'};$script:lrCalls['Sync-AutoRows']++}
 $script:largeRefreshPhase=0;$script:uiDragging=$false;$script:uiTicks=0
 [Threading.Thread]::Sleep(300)
 for($i=0;$i -lt 24;$i++){Invoke-ManagerTick}
 foreach($n in $script:largeRefreshTasks){if($script:lrCalls[$n] -ne 2){throw ('Large refresh phase missing/duplicated: '+$n)}}
 if($script:lrCalls['Poll-SocketRequests'] -ne 24 -or $script:lrCalls['Sync-AutoRows'] -ne 24 -or $script:lrCalls['Pump-DungeonQueue'] -ne 4){throw 'Large refresh changed socket/queue cadence'}
 $script:largeRefreshPhase=0;$script:uiDragging=$true
 foreach($n in $script:lrCalls.Keys.Clone()){$script:lrCalls[$n]=0}
 for($i=0;$i -lt 12;$i++){Invoke-ManagerTick}
 if($script:lrCalls['Sync-AutoRows'] -ne 12 -or $script:lrCalls['Sync-Distribution'] -ne 1 -or $script:lrCalls['Update-UiSummary'] -ne 0){throw 'Dragging stopped business work or repainted large table'}
 foreach($n in $lrNames){Set-Item ('Function:\'+$n) $lrOriginal[$n]}
 $script:lrVisible=0;function Update-AccountVisibility($row){$script:lrVisible++}
 $null=$form.Handle;$null=$grid.Handle;$script:uiDragging=$false;Update-UiSummary
 if($script:lrVisible -ge $grid.Rows.Count -or $summaryLabel.Text -notmatch 'Tài khoản: 96'){throw ('Summary: painted '+$script:lrVisible+' of '+$grid.Rows.Count+' rows; text '+$summaryLabel.Text)}
 Write-Output 'PASS: 96-account timer phases yield between scans, Auto/Up batches cover rows in order, socket/queues unchanged, dragging retains automation, summary refresh limited to visible rows.'
}finally {
 foreach($n in $lrNames){Set-Item ('Function:\'+$n) $lrOriginal[$n]}
 while($grid.Rows.Count -gt $lrCount){$grid.Rows.RemoveAt($grid.Rows.Count-1)}
 $script:accountJobs=$lrMappings;$script:largeRefreshPhase=$lrPhase;$script:autoSyncCursor=$lrCursor;$script:uiDragging=$lrDrag;$script:uiTicks=$lrTicks
}
