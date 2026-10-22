$viOldAccounts=$script:accounts;$viOldJobs=$script:accountJobs
$viJobs=[TGLL.NativeBackend].GetField('jobs',[Reflection.BindingFlags]'Static,NonPublic').GetValue($null)
$viCtor=[TGLL.NativeJob].GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')[0];$viVitals=[TGLL.VitalsSnapshot].GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')[0]
$viA=$viCtor.Invoke(@('C:\synthetic.exe','vitals-a','unused',$null,$true));$viB=$viCtor.Invoke(@('C:\synthetic.exe','vitals-b','unused',$null,$true));$viA.State='opened';$viB.State='opened'
$null=$viJobs.TryAdd($viA.Id,$viA);$null=$viJobs.TryAdd($viB.Id,$viB)
try{
 $script:accounts=@($viOldAccounts[0],[pscustomobject]@{id='vitals-other';username='vitals-other'});$script:accountJobs=@{};$script:accountJobs[[string]$script:accounts[0].id]=$viA.Id;$script:accountJobs['vitals-other']=$viB.Id
 $viA.Vitals=$viVitals.Invoke(@(5000,2000,6000,3000,[DateTime]::UtcNow));$viB.Vitals=$viVitals.Invoke(@(111,222,333,444,[DateTime]::UtcNow));Refresh-Grid
 $grid.ClearSelection();$grid.Rows[0].Selected=$true;Update-UiSummary;if($detailsLabel.Text -notmatch 'HP: 5000 / 6000' -or $detailsLabel.Text -notmatch 'MP: 2000 / 3000'){throw 'Account info HP/MP missing'}
 $grid.ClearSelection();$grid.Rows[1].Selected=$true;Update-UiSummary;if($detailsLabel.Text -notmatch 'HP: 111 / 333' -or $detailsLabel.Text -match '5000'){throw 'HP/MP leaked across selected accounts'}
 $viB.CloseRequested=$true;Update-UiSummary;if($detailsLabel.Text -match 'HP: 111'){throw 'Closed game retained live vitals'}
 Write-Output 'PASS: account info current/max HP/MP, selected account isolation and closed-state placeholder.'
}finally{
 $viRemoved=$null;$null=$viJobs.TryRemove($viA.Id,[ref]$viRemoved);$null=$viJobs.TryRemove($viB.Id,[ref]$viRemoved);$script:accounts=$viOldAccounts;$script:accountJobs=$viOldJobs;Refresh-Grid
}
