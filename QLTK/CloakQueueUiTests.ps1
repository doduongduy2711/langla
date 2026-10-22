$cqOldMax=$cloakMax.Value;$cloakMax.Value=1;$cqOldRun=$script:cloakRun;$cqDefs=@{};foreach($name in @('Start-CloakQueueMember','Get-CloakMemberJob','Test-CloakMemberRunning','Begin-CloakMemberClose')){$cqDefs[$name]=(Get-Item ('Function:'+ $name)).ScriptBlock}
$cqCtor=[TGLL.NativeJob].GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')[0];$cqJ=$cqCtor.Invoke(@('C:\synthetic.exe','queue-test','unused',$null,$true));$cqJ.TrainReady=$true;$cqJ.GameSettingsReady=$true;$cqJ.ReceiveKeyEnabled=$false;$cqJ.State='opened';$cqJ.CloakEquipAfter=$true
$cqJobs=[TGLL.NativeBackend].GetField('jobs',[Reflection.BindingFlags]'Static,NonPublic').GetValue($null);$null=$cqJobs.TryAdd($cqJ.Id,$cqJ)
$script:cqLaunches=0;$script:cqCloses=0;$script:cqRunning=$true;$script:cqClose=[pscustomobject]@{Completed=$false;Failed=0;Error=''}
function Start-CloakQueueMember($m){$script:cqLaunches++;$m.job=$cqJ.Id}
function Get-CloakMemberJob($m){return $cqJ}
function Test-CloakMemberRunning($m){return $script:cqRunning}
function Begin-CloakMemberClose($m){$script:cqCloses++;return $script:cqClose}
try{
 $script:cloakRun=[pscustomobject]@{active=$true;cursor=0;templateId=131;quantity=1;price=900;members=@([pscustomobject]@{id='synthetic-a';username='A';job='';state='waiting';status='';closeOperation=$null},[pscustomobject]@{id='synthetic-b';username='B';job='';state='waiting';status='';closeOperation=$null})}
 Sync-CloakQueue;Sync-CloakQueue;if($script:cqLaunches -ne 1 -or $script:cloakRun.members[0].state -ne 'running'){throw 'Queue failed to launch/start one account'}
 $cqJ.CloakEnabled=$false;$cqJ.CloakDone=$true;Sync-CloakQueue;Sync-CloakQueue
 if($script:cqCloses -ne 1 -or $script:cqLaunches -ne 1){throw 'Close requested repeatedly or next account opened too early'}
 $script:cqClose.Completed=$true;Sync-CloakQueue;if($script:cqLaunches -ne 1 -or $script:cloakRun.members[0].state -ne 'closing'){throw 'Must wait for actual process exit'}
 $script:cqRunning=$false;Sync-CloakQueue;Sync-CloakQueue;if($script:cqLaunches -ne 2){throw 'Did not advance to next account after exit'}
 $script:cloakRun.members[1].state='running';$cqJ.CloakDone=$false;$cqJ.CloakEnabled=$false;Sync-CloakQueue;if($script:cloakRun.members[1].state -ne 'blocked' -or $script:cqCloses -ne 1){throw 'Unverified equip must retain account and queue'}
 $cqJ.CloakQueueOnly=$true;[TGLL.NativeBackend]::SetReceiveKey($cqJ.Id,$true);[TGLL.NativeBackend]::SetDungeonHigh($cqJ.Id,$true);[TGLL.NativeBackend]::SetIzanami($cqJ.Id,$true);[TGLL.NativeBackend]::SetAutoQuest($cqJ.Id,$true);[TGLL.NativeBackend]::SetSonCap($cqJ.Id,$true)
 if($cqJ.ReceiveKeyEnabled -or $cqJ.DungeonHighEnabled -or $cqJ.IzanamiEnabled -or $cqJ.QuestEnabled -or $cqJ.SonCapEnabled){throw 'Dedicated cloak queue must suppress other activity settings'}
 Stop-CloakSelected;if($script:cloakRun.active){throw 'Queue stop failed'}
 Write-Output 'PASS: cloak FIFO queue, one account at a time, close once after verified equip, actual exit before next launch, failed equip hold, other-auto isolation and Stop.'
}finally{foreach($name in $cqDefs.Keys){Set-Item ('Function:'+ $name) $cqDefs[$name]};$script:cloakRun=$cqOldRun;$cloakMax.Value=$cqOldMax;$cqRemoved=$null;$null=$cqJobs.TryRemove($cqJ.Id,[ref]$cqRemoved)}
