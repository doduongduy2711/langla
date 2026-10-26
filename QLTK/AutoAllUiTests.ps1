# Synthetic orchestration: no real processes, network or account writes.
$aaFunctions=@{};$aaVariables=@{}
$aaNames=@('accounts','autoAllRun','accountJobs','soncapBatches','izanamiBatches','dungeonBatches','questBatches','beastBatches','mailRun','distributionRun','cloakRun','closingAll')
foreach($name in $aaNames){$aaVariables[$name]=Get-Variable -Scope Script -Name $name -ValueOnly -ErrorAction SilentlyContinue}
$aaMocks=@('Add-SonCapQueue','Add-IzanamiQueue','Add-DungeonQueue','Add-QuestQueue','Add-BeastQueue','Reset-SonCapQueue','Reset-IzanamiQueue','Reset-DungeonQueue','Reset-QuestQueue','Reset-BeastQueue','Save-SonCapConfig','Save-IzanamiQueueSettings','Save-DungeonQueueSettings','Save-QuestQueueSettings','Save-BeastSettings','Test-AutoAllAccountOpen')
foreach($name in $aaMocks){$aaFunctions[$name]=(Get-Item ('Function:\'+$name)).ScriptBlock}
$aaQuestApplied=$script:questApplied
$aaNative=[TGLL.NativeBackend].GetField('jobs',[Reflection.BindingFlags]'NonPublic,Static').GetValue($null);$aaFixture=$null
$aaLimits=@($soncapMax,$izanamiMax,$dungeonMax,$questMax,$beastMax);$aaLimitValues=@($aaLimits | ForEach-Object {$_.Value})
try{
 if(!$autoLoginCheck.Checked -or $autoLoginCheck.Enabled -or !$autoReconnect.Checked -or $autoReconnect.Enabled -or ![TGLL.NativeBackend]::AutoReconnectEnabled){throw 'Login defaults must be enabled and locked'}
 $script:accountJobs=@{};$script:mailRun=$null;$script:distributionRun=$null;$script:cloakRun=$null;$script:closingAll=$false
 $script:accounts=@(1..3 | ForEach-Object {[pscustomobject]@{id=('aa-'+$_);username=('synthetic-'+$_);autoBeast=$false;soncap=$false;upEnabled=$true}})
 Refresh-Grid;foreach($r in $grid.Rows){$r.Cells['pick'].Value=$false}
 $script:aaCalls=@();$script:aaOpen=@{}
 function Test-AutoAllAccountOpen([string]$id){return [bool]$script:aaOpen[$id]}
 function Add-AaTestStage($kind,$accounts){
  $script:aaCalls+=@($kind)
  if(@($accounts).Count -ne 3){throw 'Auto all did not submit all accounts'}
  foreach($a in $accounts){$a.autoBeast=$true;$a.soncap=$true}
  Set-Variable -Scope Script -Name ($kind+'Batches') -Value @([pscustomobject]@{state='running';members=@();closeOperation=$null})
 }
 function Add-SonCapQueue($accounts){Add-AaTestStage 'soncap' $accounts}
 function Add-IzanamiQueue($accounts){Add-AaTestStage 'izanami' $accounts}
 function Add-DungeonQueue($accounts){Add-AaTestStage 'dungeon' $accounts}
 function Add-QuestQueue($accounts){Add-AaTestStage 'quest' $accounts}
 function Add-BeastQueue($accounts){Add-AaTestStage 'beast' $accounts}
 function Reset-SonCapQueue {$script:soncapBatches=@()}
 function Reset-IzanamiQueue {$script:izanamiBatches=@()}
 function Reset-DungeonQueue {$script:dungeonBatches=@()}
 function Reset-QuestQueue {$script:questBatches=@()}
 function Reset-BeastQueue {$script:beastBatches=@()}
 foreach($name in @('Save-SonCapConfig','Save-IzanamiQueueSettings','Save-DungeonQueueSettings','Save-QuestQueueSettings','Save-BeastSettings')){Set-Item ('Function:\'+$name) {}}
 Reset-AutoAllQueues
 $soncapMax.Value=18;$izanamiMax.Value=24;$dungeonMax.Value=7;$questMax.Value=9;$beastMax.Value=11
 $expectedLimits=@($aaLimits | ForEach-Object {$_.Value})
 Start-AutoAll
 if(!$autoAllCheck.Checked -or $script:aaCalls.Count -ne 1 -or $script:aaCalls[0] -ne 'soncap'){throw ('First stage failed: '+$status.Text)}
 if(@($grid.Rows | Where-Object {$_.Cells['pick'].Value -ne $true}).Count){throw 'Auto all did not select every account'}
 foreach($c in @($soncapCloseAfter,$izanamiCloseAfter,$dungeonCloseAfter,$questCloseAfter,$beastCloseAfter)){if(!$c.Checked){throw 'Missing close after activity'}}
 for($stage=0;$stage -lt 5;$stage++){
  if($stage){Pump-AutoAll}
  if($script:aaCalls.Count -ne ($stage+1) -or $script:aaCalls[$stage] -ne $script:autoAllKinds[$stage]){throw ('Stage order failed: '+$status.Text)}
  if($stage -eq 3){
   $ctor=[TGLL.NativeJob].GetConstructors([Reflection.BindingFlags]'NonPublic,Instance')[0];$aaFixture=$ctor.Invoke(@('C:\synthetic.exe','synthetic','unused',$null,$true));$aaFixture.State='opened';$aaFixture.QuestQueueOnly=$true;$null=$aaNative.TryAdd($aaFixture.Id,$aaFixture);$script:accountJobs['aa-1']=$aaFixture.Id;$script:questApplied=@{}
   Sync-QuestPreferences;if(!$aaFixture.QuestEnabled){throw 'Saved unchecked quest disabled Auto all quest stage'}
   $script:accountJobs.Clear();$removed=$null;$null=$aaNative.TryRemove($aaFixture.Id,[ref]$removed);$aaFixture=$null
  }
  if((Get-AccountLaunchMode $script:accounts[0]) -ne $script:autoAllKinds[$stage]){throw 'Stage did not override launch preferences'}
  Pump-AutoAll;if($script:autoAllRun.stage -ne $stage){throw 'Advanced unfinished stage'}
  $batch=@(Get-AutoAllBatches $script:autoAllKinds[$stage])[0];$batch.state='done'
  $script:aaOpen['aa-1']=$true;Pump-AutoAll
  if($script:autoAllRun.stage -ne $stage){throw 'Advanced with live/pending game'}
  $script:aaOpen.Clear();Pump-AutoAll
  if($script:autoAllRun.stage -ne ($stage+1)){throw ('Did not advance after exit: '+$status.Text)}
 }
 if($autoAllCheck.Checked -or $script:autoAllRun.active -or $script:aaCalls.Count -ne 5){throw 'Completion did not stop master'}
 foreach($a in $script:accounts){if($a.autoBeast -or $a.soncap -or !$a.upEnabled){throw 'Queue copy changed saved activity or Up preferences'}}
 for($i=0;$i -lt 5;$i++){if($aaLimits[$i].Value -ne $expectedLimits[$i]){throw 'Activity limit was changed'}}
 Start-AutoAll;$callCount=$script:aaCalls.Count;Stop-AutoAll;Pump-AutoAll
 if($script:autoAllRun.active -or $autoAllCheck.Checked -or $script:aaCalls.Count -ne $callCount){throw 'Stop allowed another stage'}
 # An earlier asynchronous close must finish, and its game must exit, before adopting any session.
 $op=[pscustomobject]@{Completed=$false};$script:soncapBatches=@([pscustomobject]@{closeOperation=$op;members=@([pscustomobject]@{id='aa-1'})})
 Start-AutoAll;if($script:aaCalls.Count -ne $callCount){throw 'Started during previous close worker'}
 $op.Completed=$true;$script:aaOpen['aa-1']=$true;Pump-AutoAll
 if($script:aaCalls.Count -ne $callCount){throw 'Adopted a game still being closed'}
 $script:aaOpen.Clear();Pump-AutoAll
 if($script:aaCalls.Count -ne ($callCount+1)){throw 'Did not start after old close completed'}
 Stop-AutoAll
 Write-Output 'PASS: Auto all five-stage order, select all, independent limits, forced close, process exit gate, saved preferences, cancellation and previous close drain; locked login defaults.'
}finally{
 if($script:autoAllRun -and $script:autoAllRun.active){Stop-AutoAll}
 if($aaFixture){$removed=$null;$null=$aaNative.TryRemove($aaFixture.Id,[ref]$removed)};$script:questApplied=$aaQuestApplied
 foreach($name in $aaMocks){Set-Item ('Function:\'+$name) $aaFunctions[$name]}
 foreach($name in $aaNames){Set-Variable -Scope Script -Name $name -Value $aaVariables[$name]}
 for($i=0;$i -lt 5;$i++){$aaLimits[$i].Value=$aaLimitValues[$i]}
 Refresh-Grid
}

. (Join-Path $PSScriptRoot 'AutoAllBeastTransitionTests.ps1')
