# Confirmed level guard, exact boundary, async close and team removal; no real game action.
$slAccounts=$script:accounts;$slJobs=$script:accountJobs;$slBatches=$script:soncapBatches;$slCloseJobs=$script:soncapLevelCloseJobs
$slBegin=(Get-Item Function:\Begin-SonCapLevelClose).ScriptBlock;$slRunning=(Get-Item Function:\Test-SonCapLevelProcessRunning).ScriptBlock
$slNative=[TGLL.NativeBackend].GetField('jobs',[Reflection.BindingFlags]'NonPublic,Static').GetValue($null);$slCtor=[TGLL.NativeJob].GetConstructors([Reflection.BindingFlags]'NonPublic,Instance')[0]
$slTeamField=[TGLL.NativeJob].GetField('TeamJobIds',[Reflection.BindingFlags]'NonPublic,Instance');$slFixtures=@()
try{
 $script:accounts=@();$script:accountJobs=@{};$script:soncapLevelCloseJobs=@{};$script:slCalls=@();$script:slAlive=@{}
 function Test-SonCapLevelProcessRunning($j){return [bool]$script:slAlive[$j.Id]}
 function Begin-SonCapLevelClose($j){$script:slCalls+=@($j.Id);return [pscustomobject]@{Completed=$false;Failed=0;Error=''}}
 foreach($level in @(49,50,0,48,47,46,45,44,51)){
  $j=$slCtor.Invoke(@('synthetic.exe',('level-'+$level),'unused',$null,$true));$slFixtures+=@($j);$null=$slNative.TryAdd($j.Id,$j)
  $j.State='opened';$j.ProcessId=123456;$j.CharacterLevel=$level;$j.SonCapEnabled=$true;$j.TrainReady=$true;$j.GameSettingsReady=$true;$j.MapId=59;$j.FarmPositionMap=59;$j.FarmPositionReadAt=[datetime]::UtcNow
  $script:slAlive[$j.Id]=$true;$script:accounts+=@([pscustomobject]@{id=$j.Id;username=$j.Username});$script:accountJobs[$j.Id]=$j.Id
 }
 $slFixtures[3].FarmPositionReadAt=[datetime]::UtcNow.AddSeconds(-15)
 $slFixtures[4].FarmPositionReadAt=[datetime]::MinValue
 $slFixtures[5].FarmPositionMap=60
 $slFixtures[6].SonCapEnabled=$false
 $slFixtures[7].GameSettingsReady=$false
 Sync-SonCapMinimumLevel
 if($script:slCalls.Count -ne 1 -or $script:slCalls[0] -ne $slFixtures[0].Id){throw 'SonCap level gate closed level50+, unknown, stale, wrong-map, inactive or unready account'}
 Sync-SonCapMinimumLevel;if($script:slCalls.Count -ne 1){throw 'SonCap level gate submitted duplicate async close'}
 $low=$slFixtures[0];$entry=$script:soncapLevelCloseJobs[$low.Id];$entry.operation.Completed=$true;$entry.operation.Failed=1;$entry.attemptAt=[datetime]::UtcNow.AddSeconds(-6)
 Sync-SonCapMinimumLevel;if($script:slCalls.Count -ne 2){throw 'Failed low-level close did not retry'}
 $script:slAlive[$low.Id]=$false;$entry.operation.Completed=$true;$entry.attemptAt=[datetime]::UtcNow.AddSeconds(-6)
 Sync-SonCapMinimumLevel;if($script:slCalls.Count -ne 2){throw 'Closed low-level session closed twice'}
 if($low.ActivityTimeoutClosed){throw 'Low-level exclusion would trigger timeout team recovery'}
 # Remove the ineligible leader; remaining eligible accounts can form their own team.
 $members=@([pscustomobject]@{id=$low.Id;job=$low.Id},[pscustomobject]@{id=$slFixtures[1].Id;job=$slFixtures[1].Id},[pscustomobject]@{id=$slFixtures[8].Id;job=$slFixtures[8].Id})
 $script:soncapBatches=@([pscustomobject]@{state='running';members=$members})
 [TGLL.NativeBackend]::ConfigureSonCapTeams([string[]]@($members | ForEach-Object {$_.job}));$slFixtures[1].IzanamiRequested=$false
 Sync-SonCapTeams
 $team=$slTeamField.GetValue($slFixtures[1]);if($team.Count -ne 2 -or $team -contains $low.Id -or $team[0] -ne $slFixtures[1].Id -or !$slFixtures[1].IzanamiRequested){throw 'Low-level leader still blocks eligible SonCap teammates'}
 Write-Output 'PASS: SonCap closes confirmed level49 only, preserves level50/51 and unknown/stale/unready/inactive sessions, deduplicates async close, retries failures, confirms process exit, avoids timeout recovery and removes rejected leader from eligible team. No game action.'
}finally{
 foreach($j in $slFixtures){$removed=$null;$null=$slNative.TryRemove($j.Id,[ref]$removed)}
 Set-Item Function:\Begin-SonCapLevelClose $slBegin;Set-Item Function:\Test-SonCapLevelProcessRunning $slRunning
 $script:accounts=$slAccounts;$script:accountJobs=$slJobs;$script:soncapBatches=$slBatches;$script:soncapLevelCloseJobs=$slCloseJobs
}
