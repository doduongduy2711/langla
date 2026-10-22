$cpRun=$script:cloakRun;$cpMax=$cloakMax.Value;$cpConcurrency=$launchConcurrency.Value;$cpDefs=@{}
foreach($name in @('Start-CloakQueueMember','Get-CloakMemberJob','Test-CloakMemberRunning','Begin-CloakMemberClose')){$cpDefs[$name]=(Get-Item ('Function:'+ $name)).ScriptBlock}
$cpCtor=[TGLL.NativeJob].GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')[0];$cpRegistry=[TGLL.NativeBackend].GetField('jobs',[Reflection.BindingFlags]'Static,NonPublic').GetValue($null)
$script:cpJobs=@{};$script:cpLive=@{};$script:cpOps=@{};$script:cpStarts=@();$script:cpCloses=@{}
function Start-CloakQueueMember($m){
 $j=$cpCtor.Invoke(@('C:\synthetic.exe',$m.id,'unused',$null,$true));$j.State='opened';$j.GameSettingsReady=$true;$j.TrainReady=$true;$j.ReceiveKeyEnabled=$false;$j.CloakEquipAfter=$true;$j.CloakQueueOnly=$true
 $null=$cpRegistry.TryAdd($j.Id,$j);$script:cpJobs[$m.id]=$j;$script:cpLive[$m.id]=$true;$m.job=$j.Id;$script:cpStarts+=@($m.id)
}
function Get-CloakMemberJob($m){return $script:cpJobs[$m.id]}
function Test-CloakMemberRunning($m){return [bool]$script:cpLive[$m.id]}
function Begin-CloakMemberClose($m){if(!$script:cpCloses.ContainsKey($m.id)){$script:cpCloses[$m.id]=0};$script:cpCloses[$m.id]++;$op=[pscustomobject]@{Completed=$false;Failed=0;Error=''};$script:cpOps[$m.id]=$op;return $op}
function New-CloakParallelFixture($count,[bool]$owned=$false){$script:cloakRun=[pscustomobject]@{active=$true;cursor=0;useOwned=$owned;templateId=131;quantity=1;price=900;members=@(1..$count | ForEach-Object {[pscustomobject]@{id="parallel-$_";username="parallel-$_";job='';state='waiting';status='';closeOperation=$null}})}}
try{
 $cloakMax.Value=12;$launchConcurrency.Value=6;New-CloakParallelFixture 137
 Sync-CloakQueue;if($script:cpStarts.Count -ne 6){throw 'Launch budget should admit 6 in first tick'}
 Sync-CloakQueue;Sync-CloakQueue;if($script:cpStarts.Count -ne 12){throw 'Max 12 must admit 12 accounts concurrently'}
 $first=$script:cloakRun.members[0];$j=$script:cpJobs[$first.id];$j.CloakEnabled=$false;$j.CloakDone=$true;Sync-CloakQueue;Sync-CloakQueue
 if($script:cpCloses[$first.id] -ne 1 -or $script:cpStarts.Count -ne 12){throw 'Pending close must retain slot and never duplicate close'}
 $first.closeOperation.Completed=$true;Sync-CloakQueue;if($script:cpStarts.Count -ne 12){throw 'Close callback alone must not release a live process slot'}
 $script:cpLive[$first.id]=$false;Sync-CloakQueue;if($script:cpStarts.Count -ne 13 -or $script:cpStarts[-1] -ne 'parallel-13'){throw 'Actual exit should replace only the completed slot, FIFO'}
 $cloakMax.Value=5;Sync-CloakQueue;if($script:cpStarts.Count -ne 13){throw 'Reducing limit must drain, not open additional accounts'}
 $cloakMax.Value=12
 for($t=0;$t -lt 500;$t++){
  foreach($m in $script:cloakRun.members){if($m.state -eq 'running'){$script:cpJobs[$m.id].CloakEnabled=$false;$script:cpJobs[$m.id].CloakDone=$true};if($m.state -eq 'closing'){$m.closeOperation.Completed=$true;$script:cpLive[$m.id]=$false}}
  Sync-CloakQueue
  if(@($script:cloakRun.members | Where-Object {$_.state -notin @('waiting','done')}).Count -gt 12){throw 'Parallel queue exceeded limit'}
  if(!$script:cloakRun.active){break}
 }
 if($script:cpStarts.Count -ne 137 -or $script:cloakRun.cursor -ne 137 -or $script:cloakRun.active){throw 'Final partial group must finish all 137 accounts'}
 if(($script:cpStarts -join ',') -ne ((1..137 | ForEach-Object {"parallel-$_"}) -join ',')){throw 'Admission order changed'}
 foreach($n in $script:cpCloses.Values){if($n -ne 1){throw 'A completed account was closed more than once'}}
 foreach($j in $script:cpJobs.Values){$remove=$null;$null=$cpRegistry.TryRemove($j.Id,[ref]$remove)};$script:cpJobs=@{};$script:cpLive=@{};$script:cpStarts=@()
 $cloakMax.Value=2;New-CloakParallelFixture 3 $true;Sync-CloakQueue;Sync-CloakQueue
 if($script:cpStarts.Count -ne 2 -or $script:cpJobs['parallel-1'].CloakPhase -ne 9 -or $script:cpJobs['parallel-1'].CloakQuantity -ne 0){throw 'Owned-only parallel queue must enter use stage without purchase'}
 $script:cpJobs['parallel-1'].CloakEnabled=$false;$script:cpJobs['parallel-1'].CloakDone=$false;Sync-CloakQueue
 if($script:cloakRun.members[0].state -ne 'blocked' -or $script:cpStarts.Count -ne 2){throw 'Unverified equip must retain slot'}
 Stop-CloakSelected;Sync-CloakQueue;if($script:cloakRun.active -or $script:cpStarts.Count -ne 2){throw 'Stop must prevent additional launches'}
 Write-Output 'PASS: 137 cloak accounts / max12, startup budget6, concurrent progress, verified-close/actual-exit slot replacement, FIFO, lower limit drains, final partial group, close once, owned-only parallel mode, failed-equip hold and Stop.'
}finally{
 foreach($j in $script:cpJobs.Values){$remove=$null;$null=$cpRegistry.TryRemove($j.Id,[ref]$remove)}
 foreach($name in $cpDefs.Keys){Set-Item ('Function:'+ $name) $cpDefs[$name]}
 $script:cloakRun=$cpRun;$cloakMax.Value=$cpMax;$launchConcurrency.Value=$cpConcurrency
}
