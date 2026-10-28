$btBatches=$script:beastBatches;$btPause=$script:beastQueuePaused;$btAccounts=$script:accounts;$btMax=$beastMax.Value;$btClose=$beastCloseAfter.Checked;$btQueues=@($script:soncapBatches,$script:izanamiBatches,$script:questBatches,$script:dungeonBatches);$btSavedJobs=@{};foreach($key in $script:accountJobs.Keys){$btSavedJobs[$key]=$script:accountJobs[$key]};$btFns=@{}
foreach($n in @('Start-BeastMember','Get-BeastMemberJob','Test-BeastMemberRunning','Begin-BeastTeamClose','Sync-BeastTeams','Test-AccountOpenBlocked','Get-AccountJob')){$btFns[$n]=(Get-Item ('Function:'+ $n)).ScriptBlock}
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
 if($tabs.TabPages.Count -ne 26 -or $beastPage.Text -ne 'Auto Linh thú'){throw 'Beast UI missing'}
 # Max50 must fill with healthy accounts when the first 40 launch attempts fail.
 function Test-AccountOpenBlocked($a){$j=$script:btJobs[[string]$a.id];return ($j -and $j.State -in @('opened','queued','starting','retrying','reconnecting'))}
 function Get-AccountJob($id){return $script:btJobs[[string]$id]}
 function Start-BeastMember($m){
  if([int]($m.id -replace '^cap-','') -le 40){$script:btJobs[$m.id]=[pscustomobject]@{QuestFinished=$false;State='failed';CloseRequested=$false;CancelRequested=$false;BeastQuestEnabled=$true};throw 'fixture launch failed'}
  $m.job=$m.id;$script:btStarts+=@($m.id);$script:btJobs[$m.id]=[pscustomobject]@{QuestFinished=$false;State='opened';CloseRequested=$false;CancelRequested=$false;BeastQuestEnabled=$true}
 }
 $script:beastBatches=@();$script:btJobs=@{};$script:btStarts=@();$script:accounts=@(1..120 | ForEach-Object {[pscustomobject]@{id="cap-$_";username="cap-$_"}});$beastMax.Value=50
 Add-BeastQueue $script:accounts
 for($n=0;$n -lt 40;$n++){Pump-BeastQueue;if(@($script:btJobs.Values | Where-Object {$_.State -eq 'opened'}).Count -gt 50){throw 'Beast retry exceeded max50'}}
 if($script:btStarts.Count -ne 50 -or $script:btStarts[0] -ne 'cap-41' -or $script:btStarts[-1] -ne 'cap-90'){throw 'Forty launch failures held ghost slots instead of filling 50 healthy accounts'}
 $retryJob=$script:btJobs['cap-41'];$retryJob.State='failed';Pump-BeastQueue
 if(!$retryJob.CancelRequested -or $script:btStarts.Count -ne 51 -or $script:btStarts[-1] -ne 'cap-91'){throw 'Failed game did not yield one slot to the queue before bounded retry'}
 $script:btJobs['cap-42'].State='closed';$script:btJobs['cap-42'].CloseRequested=$true;Pump-BeastQueue
 if($script:btStarts.Count -ne 52 -or $script:btStarts[-1] -ne 'cap-92'){throw 'Stopped account held a ghost Beast slot'}
 # A close worker can finish while the account is already queued to reopen: hold that slot.
 $pending=$script:beastBatches | Where-Object {$_.members[0].id -eq 'cap-43'} | Select-Object -First 1
 $pending.state='closing';$pending.closeOperation=[pscustomobject]@{Completed=$true;Failed=1;Error=''};$script:btJobs['cap-43'].State='queued';Pump-BeastQueue
 if($pending.state -eq 'done' -or $script:btStarts.Count -ne 52){throw 'Pending login released a Beast slot'}
 $script:btJobs['cap-43'].State='closed';$script:btJobs['cap-43'].CloseRequested=$true;Pump-BeastQueue
 if($script:btStarts.Count -ne 53){throw 'Verified exit did not release failed-close reservation'}
 # An old completed account reopened separately also consumes the limit.
 $script:btJobs['cap-42'].State='opened';$script:accountJobs['cap-42']='cap-42'
 $script:btJobs['cap-44'].State='closed';$script:btJobs['cap-44'].CloseRequested=$true;Pump-BeastQueue
 if($script:btStarts.Count -ne 53){throw 'Reopened completed Beast account was omitted from max50'}
 Update-BeastUi;if($beastHint.Text -notmatch 'Game đang mở:' -or $beastHint.Text -notmatch 'Đang mở/đăng nhập:'){throw 'Beast counters hide actual game count'}
 # The real lookup must not rebind a queued retry to its canceled old job.
 $script:accountJobs['retired-fixture']='retired-job';$script:btJobs['retired-job']=[pscustomobject]@{State='failed'}
 $retiredMember=[pscustomobject]@{id='retired-fixture';job='';retiredJob='retired-job'}
 if($null -ne (& $btFns['Get-BeastMemberJob'] $retiredMember) -or $retiredMember.job){throw 'Retired failed job rebound to retry and prevented launch'}
 $script:accountJobs['retired-fixture']='fresh-job';$script:btJobs['fresh-job']=[pscustomobject]@{State='queued'}
 if($null -eq (& $btFns['Get-BeastMemberJob'] $retiredMember) -or $retiredMember.job -ne 'fresh-job'){throw 'Fresh retry job failed to rebind'}
 # Run actual native solo sync: one busy account cannot stop configuring the next account.
 $btNative=[TGLL.NativeBackend].GetField('jobs',[Reflection.BindingFlags]'NonPublic,Static').GetValue($null)
 $btCtor=[TGLL.NativeJob].GetConstructors([Reflection.BindingFlags]'NonPublic,Instance')[0]
 $busy=$btCtor.Invoke(@('C:\synthetic.exe','busy','unused',$null,$true));$good=$btCtor.Invoke(@('C:\synthetic.exe','good','unused',$null,$true))
 $busy.State='opened';$good.State='opened';[TGLL.NativeJob].GetField('ReceiveFlowActive',[Reflection.BindingFlags]'NonPublic,Instance').SetValue($busy,$true)
 try{
  foreach($job in @($busy,$good)){$null=$btNative.TryAdd($job.Id,$job);$script:btJobs[$job.Id]=$job}
  $script:beastBatches=@(foreach($job in @($busy,$good)){[pscustomobject]@{state='blocked';members=@([pscustomobject]@{id=$job.Id;job=$job.Id});closeOperation=$null;error=''}})
  & $btFns['Sync-BeastTeams']
  if($script:beastBatches[0].state -ne 'blocked' -or !$good.BeastQuestEnabled -or $script:beastBatches[1].state -ne 'running'){throw 'Busy solo sync prevented healthy account from progressing'}
 }finally{foreach($job in @($busy,$good)){$removed=$null;$null=$btNative.TryRemove($job.Id,[ref]$removed)}}
 # A real native snapshot can remain opened after its process exits; retry through the capped solo queue.
 function Test-BeastMemberRunning($m){return $false}
 function Test-AccountOpenBlocked($a){return $false}
 function Start-BeastMember($m){$script:btStarts+=@($m.id);$m.job='new-'+$m.id;$script:btJobs[$m.id]=[pscustomobject]@{State='queued';QuestFinished=$false;CloseRequested=$false;CancelRequested=$false;BeastQuestEnabled=$true}}
 $crash=$btCtor.Invoke(@('C:\synthetic.exe','crash','unused',$null,$true));$crash.ProcessId=123456;$crash.State='opened'
 $script:accounts=@([pscustomobject]@{id='crash';username='crash'},[pscustomobject]@{id='healthy';username='healthy'})
 $script:btJobs=@{'crash'=$crash};$script:btStarts=@();$beastMax.Value=2
 $lost=[pscustomobject]@{state='running';members=@([pscustomobject]@{id='crash';job=$crash.Id});closeOperation=$null;error=''}
 $healthy=[pscustomobject]@{state='waiting';members=@([pscustomobject]@{id='healthy';job=''});closeOperation=$null;error=''}
 $script:beastBatches=@($lost,$healthy);Pump-BeastQueue
 if($lost.state -ne 'waiting' -or $lost.members[0].job -or !$crash.CancelRequested -or $script:btStarts.Count -ne 1){throw 'Crashed Beast session held capacity or retried without cooldown'}
 $lost.retryAt=[datetime]::UtcNow.AddSeconds(-1);Pump-BeastQueue;Pump-BeastQueue
 if($script:btStarts.Count -ne 2 -or @($script:btStarts | Where-Object {$_ -eq 'crash'}).Count -ne 1){throw 'Beast crash retry did not use exactly one capped slot'}
 $gone=$btCtor.Invoke(@('C:\synthetic.exe','gone','unused',$null,$true));$gone.ProcessId=123456;$gone.State='opened'
 $script:accounts=@([pscustomobject]@{id='gone';username='gone'});$script:btJobs=@{'gone'=$gone}
 $drained=[pscustomobject]@{state='closing';members=@([pscustomobject]@{id='gone';job=$gone.Id});closeOperation=[pscustomobject]@{Completed=$true;Failed=1;Error=''};error=''}
 $script:beastBatches=@($drained);Pump-BeastQueue
 if($drained.state -ne 'done'){throw 'Beast finished close worker held verified exited opened snapshot'}
 Write-Output 'PASS: Beast unexpected process exit yields to healthy accounts, retries after cooldown once within capacity, and completed close drains stale opened snapshot.'
 Write-Output 'PASS: Beast max50 fills past forty failed launch attempts, failed/manual exits yield slots, pending relogin and reopened-account capacity guards; beast solo FIFO queue, per-account cap including max1, independent completion/close and one-slot refill only after process exit. No real game opened.'
}finally{
 foreach($n in $btFns.Keys){Set-Item ('Function:'+ $n) $btFns[$n]};$script:accountJobs.Clear();foreach($key in $btSavedJobs.Keys){$script:accountJobs[$key]=$btSavedJobs[$key]};$script:beastBatches=$btBatches;$script:beastQueuePaused=$btPause;$script:accounts=$btAccounts;$beastMax.Value=$btMax;$beastCloseAfter.Checked=$btClose;$script:soncapBatches=$btQueues[0];$script:izanamiBatches=$btQueues[1];$script:questBatches=$btQueues[2];$script:dungeonBatches=$btQueues[3]
}
