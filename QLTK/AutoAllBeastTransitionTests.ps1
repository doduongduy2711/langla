# Real Beast queue/member lookup with 239 closed Quest sessions from the prior stage.
# Fake launch adapters only; no real account opens, closes or writes.
$abNames=@('accounts','accountJobs','autoAllRun','beastBatches','beastQueuePaused','beastApplied','soncapBatches','izanamiBatches','dungeonBatches','questBatches','mailRun','distributionRun','cloakRun')
$abVars=@{};foreach($n in $abNames){$abVars[$n]=Get-Variable -Scope Script -Name $n -ValueOnly}
$abFns=@{};foreach($n in @('Get-AccountJob','Test-AccountOpenBlocked','Test-BeastMemberRunning','Start-BeastMember','Sync-BeastTeams')){$abFns[$n]=(Get-Item ('Function:\'+$n)).ScriptBlock}
$abMax=$beastMax.Value;$abConcurrency=$launchConcurrency.Value
try{
 $script:accounts=@(1..239 | ForEach-Object {[pscustomobject]@{id=('ab-'+$_);username='synthetic';autoBeast=$false}})
 $script:accountJobs=@{};$script:abJobs=@{};$script:abStarts=@();$ids=@{}
 foreach($a in $script:accounts){$ids[$a.id]=$true;$script:accountJobs[$a.id]='quest-old-'+$a.id;$script:abJobs['quest-old-'+$a.id]=[pscustomobject]@{Id=('quest-old-'+$a.id);State='closed';ProcessId=0;QuestFinished=$true;BeastQuestEnabled=$false;CloseRequested=$true;CancelRequested=$true}}
 $script:autoAllRun=[pscustomobject]@{active=$true;stage=4;ids=$ids};$script:beastBatches=@();$script:beastQueuePaused=$false;$script:beastApplied=@{}
 $script:soncapBatches=@();$script:izanamiBatches=@();$script:dungeonBatches=@();$script:questBatches=@();$script:mailRun=$null;$script:distributionRun=$null;$script:cloakRun=$null
 $beastMax.Value=50;$launchConcurrency.Value=6
 function Get-AccountJob($id){return $script:abJobs[[string]$id]}
 function Test-AccountOpenBlocked($a){$j=$script:abJobs[$script:accountJobs[[string]$a.id]];return $j -and !$j.CloseRequested -and !$j.CancelRequested -and $j.State -in @('queued','opened','starting')}
 function Test-BeastMemberRunning($m){return $false}
 function Sync-BeastTeams {}
 function Start-BeastMember($m){$m.job='beast-new-'+$m.id;$script:accountJobs[$m.id]=$m.job;$script:abJobs[$m.job]=[pscustomobject]@{Id=$m.job;State='queued';ProcessId=0;QuestFinished=$false;BeastQuestEnabled=$true;CloseRequested=$false;CancelRequested=$false};$script:abStarts+=@($m.id)}
 $unsubmitted=[pscustomobject]@{id='ab-1';job=''}
 if($null -ne (Get-BeastMemberJob $unsubmitted) -or $unsubmitted.job){throw 'Unsubmitted Beast member inherited closed Quest session'}
 Submit-AutoAllStage 'beast' $script:accounts
 for($n=0;$n -lt 15;$n++){$before=$script:abStarts.Count;Pump-BeastQueue;if($script:abStarts.Count-$before -gt 6){throw 'Beast transition exceeded startup concurrency'}}
 $active=@($script:beastBatches | Where-Object {$_.state -notin @('waiting','done')}).Count
 if($script:abStarts.Count -ne 50 -or $active -ne 50 -or @($script:beastBatches | Where-Object {$_.state -eq 'done'}).Count -or @($script:beastBatches | Where-Object {$_.state -eq 'waiting'}).Count -ne 189){throw 'Auto all skipped Beast stage or violated 50-account cap after Quest close'}
 if(@($script:abStarts | Select-Object -Unique).Count -ne 50 -or $script:abStarts[0] -ne 'ab-1' -or $script:abStarts[-1] -ne 'ab-50'){throw 'Beast transition changed FIFO or submitted duplicates'}
 foreach($b in $script:beastBatches | Where-Object {$_.state -ne 'waiting'}){if($b.members[0].job -notlike 'beast-new-*'){throw 'Active Beast member still points at previous activity'}}
 Write-Output 'PASS: Auto all Quest-to-Beast transition with 239 old closed sessions launches 50 fresh Beast jobs, keeps 189 waiting, respects startup concurrency/FIFO and never inherits old Quest completion.'
}finally{
 foreach($n in $abFns.Keys){Set-Item ('Function:\'+$n) $abFns[$n]};foreach($n in $abNames){Set-Variable -Scope Script -Name $n -Value $abVars[$n]};$beastMax.Value=$abMax;$launchConcurrency.Value=$abConcurrency
}
