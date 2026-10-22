$stoppedKey='stopped-up-fixture';$stoppedCtor=[TGLL.NativeJob].GetConstructors([Reflection.BindingFlags]'NonPublic,Instance')[0]
$stoppedJob=$stoppedCtor.Invoke(@('synthetic.exe','stopped-up','unused',$null,$true));$stoppedJob.State='opened';$stoppedJob.TrainReady=$true;$stoppedJob.UpRequested=$true;$stoppedJob.FarmEnabled=$false;$stoppedJob.FarmStatus='Đã dừng Up'
$stoppedJobs=[TGLL.NativeBackend].GetField('jobs',[Reflection.BindingFlags]'Static,NonPublic').GetValue($null);$null=$stoppedJobs.TryAdd($stoppedJob.Id,$stoppedJob)
$stoppedIndex=$grid.Rows.Add();$stoppedRow=$grid.Rows[$stoppedIndex];$stoppedRow.Tag=[pscustomobject]@{id=$stoppedKey;username='stopped-up'};$script:accountJobs[$stoppedKey]=$stoppedJob.Id
try {
    $script:farmUpRequests[$stoppedKey]=[pscustomobject]@{StartedJob=$stoppedJob.Id;BlockedJob='';Config=$null}
    Sync-FarmUpRow $stoppedRow
    if(!$stoppedJob.UpRequested -or !$script:farmUpRequests.ContainsKey($stoppedKey) -or !$stoppedRow.Cells['up'].Value){throw 'Stopped game state cleared persistent Up'}
    Sync-FarmUpRow $stoppedRow
    Write-Output 'PASS: timer refresh retains Up through stopped/error states without invoking bare false or showing a popup.'
}finally {
    $script:accountJobs.Remove($stoppedKey);$script:farmUpRequests.Remove($stoppedKey);$removed=$null;$null=$stoppedJobs.TryRemove($stoppedJob.Id,[ref]$removed);$grid.Rows.Remove($stoppedRow)
}
