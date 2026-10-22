$ufAccounts=$script:accounts;$ufMappings=$script:accountJobs;$ufRequests=$script:farmUpRequests;$ufRepeat=$dailyPriority.Checked;$ufCount=$grid.Rows.Count;$ufSize=$izanamiGroupSize.Value
$ufCtor=[TGLL.NativeJob].GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')[0];$ufJobs=[TGLL.NativeBackend].GetField('jobs',[Reflection.BindingFlags]'Static,NonPublic').GetValue($null);$ufCreated=@()
try {
 $script:farmUpRequests=@{};$script:accountJobs=@{}
 $ufA=[pscustomobject]@{id='persistent-up-a';username='synthetic';soncap=$true;izanami=$true;autoQuest=$false;receiveKey=$false;dungeonHigh=$false}
 $script:accounts=@($ufA);$ufIdx=$grid.Rows.Add();$ufRow=$grid.Rows[$ufIdx];$ufRow.Tag=$ufA
 $ufSpot=New-Object TGLL.FarmSpot;$ufSpot.MapId=74;$ufSpot.MapName='normal';$ufSpot.MobId=-1;$ufSpot.MobName='fixture';$ufSpot.Level=54;$ufSpot.X=1490;$ufSpot.Y=565
 $ufConfig=[pscustomobject]@{Spots=[TGLL.FarmSpot[]]@($ufSpot);ReferenceId='';CharacterMode=$false;Level=54;Quiet=$true}
 Request-AccountFarm $ufRow $ufConfig
 if(!$ufA.upEnabled -or !$ufRow.Cells['up'].Value -or (Get-AccountLaunchMode $ufA) -ne 'farm'){throw 'Closed account Up not persistent or SonCap bypasses activity flow'}
 $ufSerialized=ConvertTo-Json -InputObject @($ufA) -Depth 12;$script:accounts=@($ufSerialized | ConvertFrom-Json);$script:farmUpRequests=@{};Restore-UpRequests
 if(!$script:farmUpRequests.ContainsKey($ufA.id) -or $script:farmUpRequests[$ufA.id].Config.Spots[0] -isnot [TGLL.FarmSpot] -or $script:farmUpRequests[$ufA.id].Config.Spots[0].X -ne 1490){throw 'Saved Up preference/config did not restore after manager restart'}
 $ufA.soncap=$false;$ufA.izanami=$false;$ufJob=$ufCtor.Invoke(@('C:\synthetic.exe','persistent-up-a','unused',$null,$true));$ufCreated+=,$ufJob;$null=$ufJobs.TryAdd($ufJob.Id,$ufJob);$ufJob.State='opened';$ufJob.TrainReady=$true;$ufJob.GameSettingsReady=$true;$ufJob.ReceiveKeyEnabled=$false;$script:accountJobs[$ufA.id]=$ufJob.Id
 $dailyPriority.Checked=$false;Sync-FarmUpRow $ufRow
 if(!$ufJob.PersistentUp -or !$ufJob.FarmEnabled -or !$ufJob.UpRequested){throw 'Up requires midnight checkbox or does not resume on open'}
 Set-AccountAuto $ufRow $false
 if(!$script:farmUpRequests.ContainsKey($ufA.id) -or !$ufRow.Cells['up'].Value){throw 'Auto toggle cleared Up preference'}
 $ufJob.State='closed';$ufJob.CloseRequested=$true;$ufJob.FarmEnabled=$false;Sync-FarmUpRow $ufRow
 if(!$ufRow.Cells['up'].Value -or !$ufA.upEnabled){throw 'Closing game cleared Up'}
 $ufNext=$ufCtor.Invoke(@('C:\synthetic.exe','persistent-up-a','unused',$null,$true));$ufCreated+=,$ufNext;$null=$ufJobs.TryAdd($ufNext.Id,$ufNext);$ufNext.State='opened';$ufNext.TrainReady=$true;$ufNext.GameSettingsReady=$true;$ufNext.ReceiveKeyEnabled=$false;$script:accountJobs[$ufA.id]=$ufNext.Id
 Sync-FarmUpRow $ufRow
 if(!$ufNext.PersistentUp -or !$ufNext.FarmEnabled -or $script:farmUpRequests[$ufA.id].StartedJob -ne $ufNext.Id){throw 'Reopened game did not restore Up/session ownership'}
 $ufA.izanami=$true;$izanamiGroupSize.Value=3
 foreach($n in 1,2){$idx=$grid.Rows.Add();$grid.Rows[$idx].Tag=[pscustomobject]@{id=('offline-up-'+$n);username='synthetic';izanami=$true};$script:farmUpRequests[('offline-up-'+$n)]=[pscustomobject]@{Config=$ufConfig;StartedJob='';BlockedJob=''}}
 Sync-DailyPriorityPreferences
 $ufTeam=[TGLL.NativeJob].GetField('DailyTeamIds',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($ufNext)
 if($ufTeam.Length -ne 3 -or $ufTeam[0] -ne $ufNext.Id -or $ufTeam[1] -ne '' -or $ufNext.DailyActive -or !$ufNext.FarmEnabled){throw 'PowerShell/native group binding drops offline selected members or interrupts Up'}
 $ufRow.Cells['up'].Value=$false
 if($ufA.upEnabled -or $script:farmUpRequests.ContainsKey($ufA.id) -or $ufNext.PersistentUp){throw 'Explicit untick did not cancel persisted Up'}
 Write-Output 'PASS: Up selection/config JSON restoration, Up launch overrides SonCap queue, independent midnight option, Auto toggle/closed game retain Up, replacement session resumes, explicit untick persists cancellation.'
}finally {
 foreach($j in $ufCreated){$ufRemoved=$null;$null=$ufJobs.TryRemove($j.Id,[ref]$ufRemoved)}
 while($grid.Rows.Count -gt $ufCount){$grid.Rows.RemoveAt($grid.Rows.Count-1)}
 $script:accounts=$ufAccounts;$script:accountJobs=$ufMappings;$script:farmUpRequests=$ufRequests;$dailyPriority.Checked=$ufRepeat;$izanamiGroupSize.Value=$ufSize
}
