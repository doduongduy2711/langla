# Multi-activity selection uses one native sequence; no game is opened.
$asAccounts=$script:accounts;$asJobs=$script:accountJobs;$asRun=$script:autoAllRun;$asNative=[TGLL.NativeBackend].GetField('jobs',[Reflection.BindingFlags]'NonPublic,Static').GetValue($null);$asCtor=[TGLL.NativeJob].GetConstructors([Reflection.BindingFlags]'NonPublic,Instance')[0];$asFixtures=@()
try{
 $script:autoAllRun=$null;$script:accountJobs=@{};$script:accounts=@()
 for($n=0;$n -lt 3;$n++){
  $a=[pscustomobject]@{id=('seq-'+$n);username=('seq-'+$n);receiveKey=$true;dungeonHigh=$true;izanami=$true;autoQuest=$true;soncap=$true;autoBeast=$true};$script:accounts+=@($a)
  if((Get-AccountLaunchMode $a) -ne 'sequence'){throw 'Multiple selected activities must use a single sequence'}
  $j=$asCtor.Invoke(@('C:\synthetic.exe',$a.username,'unused',$null,$true));$asFixtures+=@($j);$null=$asNative.TryAdd($j.Id,$j);$script:accountJobs[$a.id]=$j.Id
 }
 Sync-ActivitySequences
 foreach($j in $asFixtures){if(!$j.SequenceManaged -or !$j.DailyManaged -or $j.DailyExecuting -or $j.BeastQuestEnabled -or $j.SonCapEnabled){throw 'Sequence launched an activity before login readiness'}}
 $groups=Get-SequenceGroups 'izanami' 3
 if($groups.Count -ne 1 -or ($groups[0] -join '|') -ne (($asFixtures | ForEach-Object {$_.Id}) -join '|')){throw 'Fixed group order changed'}
 $script:accounts[1].id='offline';$script:accountJobs.Remove('seq-1');$groups=Get-SequenceGroups 'izanami' 3
 if($groups[0].Count -ne 3 -or $groups[0][1] -ne '' -or $groups[0][2] -ne $asFixtures[2].Id){throw 'Offline member was silently replaced by the next account'}
 $single=[pscustomobject]@{id='only';username='only';autoBeast=$true};if((Get-AccountLaunchMode $single) -ne 'beast'){throw 'Single-activity route must remain unchanged'}
 $script:autoAllRun=[pscustomobject]@{active=$true;stage=2;ids=@{'only'=$true}};if((Get-AccountLaunchMode $single) -ne 'dungeon'){throw 'Explicit Auto all stage lost priority'}
 Write-Output 'PASS: all selected activities route through one sequence, idle login gate, fixed group order including offline placeholder, single activity and Auto all isolation. No game opened.'
}finally{
 foreach($j in $asFixtures){$out=$null;$null=$asNative.TryRemove($j.Id,[ref]$out)};$script:accounts=$asAccounts;$script:accountJobs=$asJobs;$script:autoAllRun=$asRun
}
