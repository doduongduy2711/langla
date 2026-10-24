# Per-account checkbox, direct/bulk launch routing and config copy. No real game is opened.
$bpAccounts=$script:accounts;$bpBatches=$script:beastBatches;$bpApplied=$script:beastApplied;$bpClip=$script:copiedAccountConfig
$bpJobs=@{};foreach($key in $script:accountJobs.Keys){$bpJobs[$key]=$script:accountJobs[$key]}
$bpAdd=(Get-Item Function:\Add-BeastQueue).ScriptBlock;$bpNative=[TGLL.NativeBackend].GetField('jobs',[Reflection.BindingFlags]'NonPublic,Static').GetValue($null)
$bpCtor=[TGLL.NativeJob].GetConstructors([Reflection.BindingFlags]'NonPublic,Instance')[0];$bpFixtures=@()
try{
 $script:beastBatches=@();$script:beastApplied=@{};$script:accountJobs.Clear();$script:bpQueueCalls=@()
 function Add-BeastQueue($accounts){foreach($a in $accounts){$script:bpQueueCalls+=@([string]$a.id)}}
 $script:accounts=@([pscustomobject]@{id='beast-pref-one';username='pref-one';autoBeast=$false},[pscustomobject]@{id='beast-pref-two';username='pref-two';autoBeast=$false})
 Refresh-Grid;$grid.ClearSelection();$grid.Rows[0].Selected=$true;Update-BeastUi
 if(!$beastCheck.Enabled -or $beastCheck.Checked -or $beastAccount.Text -notmatch 'pref-one'){throw 'Beast per-account checkbox not initialized'}
 $beastCheck.Checked=$true
 if(!$script:accounts[0].autoBeast -or $script:accounts[1].autoBeast -or $script:bpQueueCalls.Count){throw 'Offline Beast toggle opened a game or changed another account'}
 if((Get-AccountLaunchMode $script:accounts[0]) -ne 'beast'){throw 'Saved Beast preference not routed on open'}
 Open-Account $script:accounts[0]
 if($script:bpQueueCalls.Count -ne 1 -or $script:bpQueueCalls[0] -ne 'beast-pref-one'){throw 'Open did not use capped Beast queue'}
 foreach($row in $grid.Rows){$row.Cells['pick'].Value=$false};$grid.Rows[0].Cells['pick'].Value=$true;$null=[Windows.Forms.Button].GetMethod('OnClick',[Reflection.BindingFlags]'Instance,NonPublic').Invoke($bulkOpen,@([EventArgs]::Empty))
 if($script:bpQueueCalls.Count -ne 2){throw 'Bulk open skipped saved Beast checkbox'}
 $grid.ClearSelection();$grid.Rows[1].Selected=$true;Update-BeastUi;if($beastCheck.Checked){throw 'Beast checkbox leaked across accounts'}
 $grid.ClearSelection();$grid.Rows[0].Selected=$true;Update-BeastUi;if(!$beastCheck.Checked){throw 'Beast checkbox lost preference on selection change'}
 foreach($a in $script:accounts){$j=$bpCtor.Invoke(@('C:\synthetic.exe',$a.username,'unused',$null,$true));$j.State='opened';$j.GameSettingsReady=$true;$j.TrainReady=$true;$null=$bpNative.TryAdd($j.Id,$j);$bpFixtures+=@($j);$script:accountJobs[$a.id]=$j.Id;[TGLL.NativeBackend]::SetAutoBeast($j.Id,$true)}
 $script:beastBatches=@(foreach($a in $script:accounts){[pscustomobject]@{state='running';members=@([pscustomobject]@{id=$a.id;job=$script:accountJobs[$a.id]});closeOperation=$null;error=''}})
 $script:accounts[1].autoBeast=$true;$script:beastApplied['beast-pref-two']=$bpFixtures[1].Id+':True'
 $beastCheck.Checked=$false
 if($bpFixtures[0].BeastQuestEnabled -or $bpFixtures[0].CloseRequested -or !$bpFixtures[1].BeastQuestEnabled -or $script:beastBatches.Count -ne 1 -or $script:beastBatches[0].members[0].id -ne 'beast-pref-two'){throw 'Untick did not stop only the selected Beast account without closing game'}
 $beastCheck.Checked=$true;Copy-AccountConfig;$copied=$script:copiedAccountConfig | ConvertFrom-Json
 if(!$copied.autoBeast){throw 'Copy config lost Beast toggle'}
 $script:accounts[1].autoBeast=$false;Paste-AccountConfigAll
 if(!$script:accounts[1].autoBeast){throw 'Paste config lost Beast toggle'}
 $serialized=@(($script:accounts | ConvertTo-Json -Depth 20 | ConvertFrom-Json));if(!$serialized[0].autoBeast -or !$serialized[1].autoBeast){throw 'Beast toggle not serializable in saved accounts'}
 Write-Output 'PASS: per-account Beast checkbox, offline persistence, direct/bulk capped-queue routing, selection isolation, stop only selected activity without closing games, and config copy/paste.'
}finally{
 foreach($j in $bpFixtures){$removed=$null;$null=$bpNative.TryRemove($j.Id,[ref]$removed)}
 Set-Item Function:\Add-BeastQueue $bpAdd;$script:accounts=$bpAccounts;$script:beastBatches=$bpBatches;$script:beastApplied=$bpApplied;$script:copiedAccountConfig=$bpClip
 $script:accountJobs.Clear();foreach($key in $bpJobs.Keys){$script:accountJobs[$key]=$bpJobs[$key]};Refresh-Grid;Update-BeastUi
}
