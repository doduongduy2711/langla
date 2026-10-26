$cnRow=$grid.Rows[0];$cnTag=$cnRow.Tag;$cnMappings=$script:accountJobs;$cnCursor=$script:autoSyncCursor;$cnName=$cnRow.Cells['character'].Value;$cnCount=$grid.Rows.Count
$cnCtor=[TGLL.NativeJob].GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')[0];$cnJob=$cnCtor.Invoke(@('C:\synthetic.exe','names-fixture','unused',$null,$true));$cnJob.State='waiting_popup';$cnJob.CharacterName='EarlyName'
$cnJobs=[TGLL.NativeBackend].GetField('jobs',[Reflection.BindingFlags]'Static,NonPublic').GetValue($null);$null=$cnJobs.TryAdd($cnJob.Id,$cnJob)
$cnSummary=(Get-Item Function:\Update-UiSummary).ScriptBlock;$cnTick=$script:uiTicks;$cnDrag=$script:uiDragging
try {
 $cnRow.Tag=[pscustomobject]@{id='name-fixture';username='synthetic'};$script:accountJobs=@{'name-fixture'=$cnJob.Id};$script:uiDragging=$false
 $null=$form.Handle;$null=$grid.Handle;$cnRow.Cells['character'].Value=''
 Update-VisibleCharacterNames
 if($cnRow.Cells['character'].Value -ne 'EarlyName'){throw 'Visible name waits for full summary or GameSettingsReady'}
 $cnRow.Cells['character'].Value='';$cnJob.CharacterName='BatchName';Sync-AutoRows 24
 if($cnRow.Cells['character'].Value -ne 'BatchName'){throw 'Auto batch does not publish early identity'}
 $script:cnFullSummary=0;function Update-UiSummary {$script:cnFullSummary++}
 $cnJob.CharacterName='TimerName';$script:uiTicks=0;Invoke-ManagerTick
 if($cnRow.Cells['character'].Value -ne 'TimerName' -or $script:cnFullSummary -ne 0){throw 'Fast timer still waits for full summary cadence'}
 $cnJob.CharacterName='';Update-VisibleCharacterNames
 if($cnRow.Cells['character'].Value -ne ''){throw 'Exited/cleared identity remains displayed'}
 for($n=$cnCount;$n -lt 96;$n++){$idx=$grid.Rows.Add();$grid.Rows[$idx].Tag=[pscustomobject]@{id=('scroll-name-'+$n);username='synthetic'};$script:accountJobs[('scroll-name-'+$n)]=$cnJob.Id}
 $cnJob.CharacterName='ScrollName';$cnJob.State='opened';$grid.Rows[40].Cells['status'].Value='stale';$grid.Rows[95].Cells['status'].Value='offscreen-stale';$grid.FirstDisplayedScrollingRowIndex=40
 if($grid.Rows[40].Cells['character'].Value -ne 'ScrollName' -or $script:cnFullSummary -ne 0){throw 'Scroll event did not immediately show cached name without a timer or full summary'}
 if($grid.Rows[40].Cells['status'].Value -ne 'Đã mở'){throw 'Scroll leaves visible status stale'}
 Update-ManagerStatus
 if($grid.Rows[95].Cells['status'].Value -ne 'offscreen-stale'){throw 'Bulk status still repaints offscreen rows'}
 Write-Output 'PASS: early names before settings ready, visible names on fast timer, account batches, cleared identities, immediate scroll to row 40 without full summary.'
}finally{
 while($grid.Rows.Count -gt $cnCount){$grid.Rows.RemoveAt($grid.Rows.Count-1)}
 Set-Item Function:\Update-UiSummary $cnSummary;$cnRow.Tag=$cnTag;$cnRow.Cells['character'].Value=$cnName;$script:accountJobs=$cnMappings;$script:autoSyncCursor=$cnCursor;$script:uiTicks=$cnTick;$script:uiDragging=$cnDrag
 $cnRemoved=$null;$null=$cnJobs.TryRemove($cnJob.Id,[ref]$cnRemoved)
}
