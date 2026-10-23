$btBatches=$script:beastBatches;$btPause=$script:beastQueuePaused;$btAccounts=$script:accounts;$btMax=$beastMax.Value;$btClose=$beastCloseAfter.Checked;$btQueues=@($script:soncapBatches,$script:izanamiBatches,$script:questBatches,$script:dungeonBatches);$btFns=@{}
foreach($n in @('Start-BeastMember','Get-BeastMemberJob','Test-BeastMemberRunning','Begin-BeastTeamClose','Sync-BeastTeams')){$btFns[$n]=(Get-Item ('Function:'+ $n)).ScriptBlock}
try{
 $script:beastBatches=@();$script:beastQueuePaused=$false;$script:soncapBatches=@();$script:izanamiBatches=@();$script:questBatches=@();$script:dungeonBatches=@();$script:btJobs=@{};$script:btStarts=@()
 function Start-BeastMember($m){$m.job=$m.id;$script:btStarts+=@($m.id);$script:btJobs[$m.id]=[pscustomobject]@{QuestFinished=$false;State='opened';CloseRequested=$false;CancelRequested=$false}}
 function Get-BeastMemberJob($m){return $script:btJobs[$m.id]}
 function Test-BeastMemberRunning($m){return $script:btJobs[$m.id].State -eq 'opened'}
 function Begin-BeastTeamClose($b){return [pscustomobject]@{Completed=$false;Failed=0;Error=''}}
 function Sync-BeastTeams {}
 $script:accounts=@(1..7 | ForEach-Object {[pscustomobject]@{id="beast-$_";username="beast-$_"}})
 $beastMax.Value=3;$beastCloseAfter.Checked=$true;Add-BeastQueue $script:accounts;for($n=0;$n -lt 5;$n++){Pump-BeastQueue}
 if($script:btStarts.Count -ne 3 -or ($script:btStarts -join ',') -ne 'beast-1,beast-2,beast-3' -or @($script:beastBatches | Where-Object {$_.members.Count -ne 1}).Count){throw 'Solo FIFO/cap/single-account queue failed'}
 $first=$script:beastBatches[0];$script:btJobs[$first.members[0].id].QuestFinished=$true;Pump-BeastQueue
 if($first.state -ne 'closing' -or $script:beastBatches[1].state -ne 'running'){throw 'Finished solo account waited for others or closed another account'}
 $first.closeOperation.Completed=$true;$first.closeOperation.Failed=1;Pump-BeastQueue;if($script:btStarts.Count -ne 3){throw 'Still-live solo close released a slot'}
 $script:btJobs[$first.members[0].id].State='closed';Pump-BeastQueue
 if($first.state -ne 'done' -or $script:btStarts.Count -ne 4 -or $script:btStarts[3] -ne 'beast-4'){throw 'Solo exit did not refill exactly one FIFO slot'}
 # A limit of one works; no minimum or increments of six remain.
 $script:beastBatches=@();$script:btJobs=@{};$script:btStarts=@();$script:accounts=@(1..3 | ForEach-Object {[pscustomobject]@{id="one-$_";username="one-$_"}});$beastMax.Value=1
 Add-BeastQueue $script:accounts;for($n=0;$n -lt 3;$n++){Pump-BeastQueue}
 if($script:btStarts.Count -ne 1 -or $beastMax.Minimum -ne 1 -or $beastMax.Increment -ne 1){throw 'Single-account limit unsupported'}
 Update-BeastUi;if($beastHint.Text -notmatch '1 / 1' -or $beastCloseAfter.Text -notmatch 'Tắt từng game'){throw 'Solo status/close option not updated'}
 if($tabs.TabPages.Count -ne 23 -or $beastPage.Text -ne 'Auto Linh thú'){throw 'Beast UI missing'}
 Write-Output 'PASS: beast solo FIFO queue, per-account cap including max1, independent completion/close and one-slot refill only after process exit. No real game opened.'
}finally{
 foreach($n in $btFns.Keys){Set-Item ('Function:'+ $n) $btFns[$n]};$script:beastBatches=$btBatches;$script:beastQueuePaused=$btPause;$script:accounts=$btAccounts;$beastMax.Value=$btMax;$beastCloseAfter.Checked=$btClose;$script:soncapBatches=$btQueues[0];$script:izanamiBatches=$btQueues[1];$script:questBatches=$btQueues[2];$script:dungeonBatches=$btQueues[3]
}
