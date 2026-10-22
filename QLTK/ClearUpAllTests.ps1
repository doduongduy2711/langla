$cuSavedAccounts=$script:accounts;$cuSavedRequests=$script:farmUpRequests;$cuSavedJobs=$script:accountJobs
$cuJobs=[TGLL.NativeBackend].GetField('jobs',[Reflection.BindingFlags]'NonPublic,Static').GetValue($null);$cuCtor=[TGLL.NativeJob].GetConstructors([Reflection.BindingFlags]'NonPublic,Instance')[0]
$cuFarm=$cuCtor.Invoke(@('synthetic.exe','up-fixture','unused',$null,$true));$cuOther=$cuCtor.Invoke(@('synthetic.exe','other-fixture','unused',$null,$true))
try {
    $script:accounts=@(1..103 | ForEach-Object {[pscustomobject]@{id="clear-up-$_";username="clear-up-$_";autoQuest=$false;soncap=$false}});Refresh-Grid
    $script:accountJobs=@{};$script:farmUpRequests=@{}
    foreach($row in $grid.Rows){$script:farmUpRequests[[string]$row.Tag.id]=[pscustomobject]@{StartedJob='';BlockedJob='';Config=$null};Set-UpCell $row $true}
    $cuFarm.State='opened';$cuFarm.FarmEnabled=$true;$cuFarm.UpRequested=$true;$cuFarm.GameAutoWanted=$true;$null=$cuJobs.TryAdd($cuFarm.Id,$cuFarm);$script:accountJobs['clear-up-1']=$cuFarm.Id
    $cuOther.State='opened';$cuOther.IzanamiEnabled=$true;$cuOther.GameAutoWanted=$true;$null=$cuJobs.TryAdd($cuOther.Id,$cuOther);$script:accountJobs['clear-up-2']=$cuOther.Id
    $click=[Windows.Forms.Button].GetMethod('OnClick',[Reflection.BindingFlags]'Instance,NonPublic');$null=$click.Invoke($clearUpAllButton,@([EventArgs]::Empty))
    if($script:farmUpRequests.Count -or @($grid.Rows | Where-Object {$_.Cells['up'].Value -eq $true}).Count -or $cuFarm.FarmEnabled -or $cuFarm.UpRequested -or $cuFarm.GameAutoWanted){throw 'Clear Up all left active or pending Up requests'}
    if(!$cuOther.IzanamiEnabled -or !$cuOther.GameAutoWanted -or $cuOther.CloseRequested -or $cuFarm.CloseRequested){throw 'Clear Up all stopped unrelated auto or closed game'}
    foreach($row in $grid.Rows){Sync-FarmUpRow $row}
    if(@($grid.Rows | Where-Object {$_.Cells['up'].Value -eq $true}).Count){throw 'Up rearmed after clearing all'}
    if($clearUpAllButton.Bottom -gt $basic.ClientSize.Height){throw 'Clear Up all button clipped'}
    Write-Output 'PASS: Clear Up all button clears 103 active/pending/closed-account selections, stops farming, prevents rearm and preserves unrelated auto/game sessions.'
}finally {
    foreach($j in @($cuFarm,$cuOther)){$removed=$null;$null=$cuJobs.TryRemove($j.Id,[ref]$removed)};$script:accounts=$cuSavedAccounts;$script:farmUpRequests=$cuSavedRequests;$script:accountJobs=$cuSavedJobs;Refresh-Grid
}
