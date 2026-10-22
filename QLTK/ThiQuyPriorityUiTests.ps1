$tqAccounts=$script:accounts;$tqJobs=$script:accountJobs;$tqClipboard=$script:copiedAccountConfig
$tqNativeJobs=[TGLL.NativeBackend].GetField('jobs',[Reflection.BindingFlags]'NonPublic,Static').GetValue($null)
$tqCtor=[TGLL.NativeJob].GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')[0];$tqFixtures=@()
try{
 $a=[pscustomobject]@{id='thi-quy-a';username='thi-quy-a';secret='unused'}
 $b=[pscustomobject]@{id='thi-quy-b';username='thi-quy-b';secret='unused'}
 $script:accounts=@($a,$b);$script:accountJobs=@{};Refresh-Grid;$grid.ClearSelection();$grid.Rows[0].Selected=$true;Update-TrainUi
 if($trainPriorityThiQuy.Checked){throw 'Thi Quy priority must default off'}
 $trainPriorityThiQuy.Checked=$true;Update-TrainUi
 if(!$a.trainPriorityThiQuy -or !$trainPriorityThiQuy.Checked){throw 'Offline preference not retained'}
 $grid.ClearSelection();$grid.Rows[1].Selected=$true;Update-TrainUi
 if($trainPriorityThiQuy.Checked -or $b.trainPriorityThiQuy){throw 'Preference leaked across accounts'}
 $j=$tqCtor.Invoke(@('C:\synthetic.exe','thi-quy','unused',$null,$true));$tqFixtures+=@($j);$null=$tqNativeJobs.TryAdd($j.Id,$j);$script:accountJobs[$a.id]=$j.Id
 Sync-AutoRows
 if(!$j.PrioritizeThiQuy){throw 'Open/reopened account did not restore saved priority'}
 $field=[TGLL.NativeJob].GetField('ThiQuyPriorityAt',[Reflection.BindingFlags]'NonPublic,Instance');$sentinel=[datetime]::UtcNow;$field.SetValue($j,$sentinel);Sync-AutoRows
 if($field.GetValue($j) -ne $sentinel){throw 'UI refresh resets priority poll timer'}
 $grid.ClearSelection();$grid.Rows[0].Selected=$true;Update-TrainUi;Copy-AccountConfig
 $json=$script:copiedAccountConfig | ConvertFrom-Json
 if(!$json.trainPriorityThiQuy){throw 'Config copy omitted Thi Quy preference'}
 Paste-AccountConfigAll
 if(!$b.trainPriorityThiQuy){throw 'Config paste omitted Thi Quy preference'}
 $trainPriorityThiQuy.Checked=$false
 if($a.trainPriorityThiQuy -or $j.PrioritizeThiQuy){throw 'Untick did not save/apply'}
 if($trainPriorityThiQuy.Parent -ne $trainPage -or $trainPriorityThiQuy.Bounds.IntersectsWith($startTrain.Bounds) -or $trainPriorityThiQuy.Bounds.IntersectsWith($stopTrain.Bounds)){throw 'Priority checkbox overlaps train actions'}
 Write-Output 'PASS: per-account Thi Quy checkbox, offline save, reopen restore, stable polling, copy/paste and immediate untick. No game action.'
}finally{
 foreach($j in $tqFixtures){$ignored=$null;$null=$tqNativeJobs.TryRemove($j.Id,[ref]$ignored)}
 $script:accounts=$tqAccounts;$script:accountJobs=$tqJobs;$script:copiedAccountConfig=$tqClipboard;Refresh-Grid
}
