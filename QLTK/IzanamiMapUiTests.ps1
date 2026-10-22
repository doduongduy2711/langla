$imSource=$script:accounts[0];$imKey=[string]$imSource.id;$imSavedJob=$script:accountJobs[$imKey];$imHadJob=$script:accountJobs.ContainsKey($imKey)
$imJobs=[TGLL.NativeBackend].GetField('jobs',[Reflection.BindingFlags]'Static,NonPublic').GetValue($null);$imCtor=[TGLL.NativeJob].GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')[0]
$imJ=$imCtor.Invoke(@('C:\synthetic.exe','map89-test','unused',$null,$true));$imJ.State='opened';$imJ.GameSettingsReady=$true;$imJ.TrainReady=$true;$imJ.ReceiveKeyEnabled=$false;$imJ.IzanamiEnabled=$true;$null=$imJobs.TryAdd($imJ.Id,$imJ);$script:accountJobs[$imKey]=$imJ.Id
try{
 $imObserve=[TGLL.NativeBackend].GetMethod('ObserveIzanamiMapAuto',[Reflection.BindingFlags]'Static,NonPublic');$null=$imObserve.Invoke($null,@($imJ,89,[datetime]::UtcNow.AddSeconds(-4)));$null=$imObserve.Invoke($null,@($imJ,89,[datetime]::UtcNow))
 $script:autoTrainWanted[$imKey]=$false;$script:autoTrainApplied[$imKey]='previous-job';Sync-AutoRows
 if(!$imJ.GameAutoWanted -or !$grid.Rows[0].Cells['auto'].Value -or !$script:autoTrainWanted[$imKey]){throw 'Map 89 auto lost to old UI state'}
 Write-Output 'PASS: map 89 confirmed combat overrides stale Auto UI preference without toggling idle off.'
}finally{$removed=$null;$null=$imJobs.TryRemove($imJ.Id,[ref]$removed);if($imHadJob){$script:accountJobs[$imKey]=$imSavedJob}else{$script:accountJobs.Remove($imKey)}}
