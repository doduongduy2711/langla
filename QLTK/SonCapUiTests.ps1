$scSavedAccounts=$script:accounts;$scSource=$script:accounts[0];$scKey=[string]$scSource.id;$scHadJob=$script:accountJobs.ContainsKey($scKey);$scPriorJob=$script:accountJobs[$scKey]
$scJobs=[TGLL.NativeBackend].GetField('jobs',[Reflection.BindingFlags]'Static,NonPublic').GetValue($null);$scCtor=[TGLL.NativeJob].GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')[0]
$scFixture=$scCtor.Invoke(@('C:\synthetic.exe','soncap-test','unused',$null,$true));$scFixture.State='opened';$scFixture.TrainReady=$true;$scFixture.GameSettingsReady=$true;$scFixture.ReceiveKeyEnabled=$false;$null=$scJobs.TryAdd($scFixture.Id,$scFixture);$script:accountJobs[$scKey]=$scFixture.Id
try{
 $scSource | Add-Member NoteProperty soncap $false -Force;$grid.ClearSelection();$grid.Rows[0].Selected=$true;Update-SonCapUi;$soncapCheck.Checked=$true
 if(!$scSource.soncap -or !$scFixture.SonCapEnabled -or !$scFixture.IzanamiEnabled){throw 'SonCap checkbox did not queue native flow'}
 if($scFixture.ReceiveKeyEnabled -or $scFixture.DungeonHighEnabled -or $scFixture.QuestEnabled){throw 'SonCap enabled unrelated activities'}
 $scFixture.IzanamiRequested=$false;for($i=0;$i -lt 5;$i++){Sync-SonCapPreferences};if($scFixture.IzanamiRequested){throw 'SonCap refresh repeated entry request'}
 if((Get-AccountLaunchMode $scSource) -ne 'soncap'){throw 'SonCap account routed to another activity queue'}
 if($soncapRouteGrid.Rows.Count -ne 5 -or $soncapRouteGrid.Rows[0].Cells['x'].Value -ne 2294 -or $soncapRouteGrid.Rows[0].Cells['y'].Value -ne 264){throw 'Portal defaults wrong'}
 $scFixture.SonCapStage=2;$scFixture.GameAutoWanted=$false;$script:autoTrainWanted[$scKey]=$true;$script:autoTrainApplied[$scKey]=$scFixture.Id;Sync-AutoRows
 if($scFixture.GameAutoWanted){throw 'Generic Auto overrode SonCap portal pause'}
 Set-AccountAuto $grid.Rows[0] $false;if($scSource.soncap -or $scFixture.SonCapEnabled){throw 'Manual Auto untick restarted SonCap'};Update-SonCapUi;Sync-SonCapPreferences;
 $soncapCheck.Checked=$false;if($scFixture.SonCapEnabled -or $scFixture.IzanamiEnabled){throw 'SonCap untick failed'}
 Write-Output 'PASS: SonCap tab, per-account preference, pending start, exact portal defaults, no duplicate entry, launch isolation, generic Auto isolation and manual stop.'
}finally{
 $scSource | Add-Member NoteProperty soncap $false -Force;$script:soncapApplied.Remove($scKey);$removed=$null;$null=$scJobs.TryRemove($scFixture.Id,[ref]$removed);if($scHadJob){$script:accountJobs[$scKey]=$scPriorJob}else{$script:accountJobs.Remove($scKey)};$script:accounts=$scSavedAccounts
}
