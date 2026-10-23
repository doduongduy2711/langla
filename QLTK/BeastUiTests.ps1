$btBatches=$script:beastBatches;$btPause=$script:beastQueuePaused;$btAccounts=$script:accounts;$btMax=$beastMax.Value;$btClose=$beastCloseAfter.Checked;$btQueues=@($script:soncapBatches,$script:izanamiBatches,$script:questBatches,$script:dungeonBatches);$btFns=@{}
foreach($n in @('Start-BeastMember','Get-BeastMemberJob','Test-BeastMemberRunning','Begin-BeastTeamClose','Sync-BeastTeams')){$btFns[$n]=(Get-Item ('Function:'+ $n)).ScriptBlock}
try{
 $script:beastBatches=@();$script:beastQueuePaused=$false;$script:soncapBatches=@();$script:izanamiBatches=@();$script:questBatches=@();$script:dungeonBatches=@();$script:btJobs=@{};$script:btStarts=@()
 function Start-BeastMember($m){$m.job=$m.id;$script:btStarts+=@($m.id);$script:btJobs[$m.id]=[pscustomobject]@{QuestFinished=$false;State='opened';CloseRequested=$false;CancelRequested=$false}}
 function Get-BeastMemberJob($m){return $script:btJobs[$m.id]}
 function Test-BeastMemberRunning($m){return $script:btJobs[$m.id].State -eq 'opened'}
 function Begin-BeastTeamClose($b){return [pscustomobject]@{Completed=$false;Failed=0;Error=''}}
 function Sync-BeastTeams {}
 $script:accounts=@(1..18 | ForEach-Object {[pscustomobject]@{id="beast-$_";username="beast-$_"}})
 $beastMax.Value=12;$beastCloseAfter.Checked=$true;Add-BeastQueue $script:accounts;for($n=0;$n -lt 5;$n++){Pump-BeastQueue}
 if($script:btStarts.Count -ne 12 -or ($script:beastBatches[0].members.id -join ',') -ne 'beast-1,beast-2,beast-3,beast-4,beast-5,beast-6'){throw 'Beast queue cap/order/group size failed'}
 $first=$script:beastBatches[0];foreach($m in $first.members[0..4]){$script:btJobs[$m.id].QuestFinished=$true};Pump-BeastQueue;if($first.state -ne 'running'){throw 'Partial completion closed the group'}
 $script:btJobs[$first.members[5].id].QuestFinished=$true;Pump-BeastQueue;$first.closeOperation.Completed=$true;$first.closeOperation.Failed=1;Pump-BeastQueue
 if($script:btStarts.Count -ne 12){throw 'Live group released slots'}
 foreach($m in $first.members){$script:btJobs[$m.id].State='closed'};for($n=0;$n -lt 4;$n++){Pump-BeastQueue}
 if($script:btStarts.Count -ne 18 -or $first.state -ne 'done'){throw 'Delayed exit did not refill six slots'}
 # A new final partial group uses its actual size for admission and status.
 $script:beastBatches=@();$script:btJobs=@{};$script:btStarts=@();$script:accounts=@(1..14 | ForEach-Object {[pscustomobject]@{id="tail-$_";username="tail-$_"}});$beastMax.Value=18
 Add-BeastQueue $script:accounts;for($n=0;$n -lt 5;$n++){Pump-BeastQueue}
 if(($script:beastBatches.members.Count | Measure-Object -Sum).Sum -ne 14 -or ((@($script:beastBatches | ForEach-Object {$_.members.Count})) -join ',') -ne '6,6,2' -or $script:btStarts.Count -ne 14){throw 'Final partial group did not launch 6+6+2'}
 Update-BeastUi;if($beastHint.Text -notmatch '14 / 18'){throw 'Partial group counted as six slots'}
 if($tabs.TabPages.Count -ne 23 -or $beastPage.Text -ne 'Auto Linh thú'){throw 'Beast UI missing'}
 Write-Output 'PASS: beast fixed six-person queue, final partial group accepted, FIFO leader order, max12, all-member finish and delayed-close refill. No real game opened.'
}finally{
 foreach($n in $btFns.Keys){Set-Item ('Function:'+ $n) $btFns[$n]};$script:beastBatches=$btBatches;$script:beastQueuePaused=$btPause;$script:accounts=$btAccounts;$beastMax.Value=$btMax;$beastCloseAfter.Checked=$btClose;$script:soncapBatches=$btQueues[0];$script:izanamiBatches=$btQueues[1];$script:questBatches=$btQueues[2];$script:dungeonBatches=$btQueues[3]
}
