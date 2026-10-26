# Run the real queue with 239 accounts and no launchable member. No game action.
$costAccounts=$script:accounts;$costBatches=$script:izanamiBatches;$costPaused=$script:izanamiQueuePaused;$costMax=$izanamiMax.Value;$costConcurrency=$launchConcurrency.Value
$costFns=@{};foreach($n in @('Sync-IzanamiCompletedClose','Sync-ActivityCapacity','Get-IzanamiMemberJob','Test-IzanamiMemberRunning','Test-AccountOpenBlocked','Get-AccountLaunchMode','Get-ActivityLaunchBatches','Update-IzanamiQueueStatus')){$costFns[$n]=(Get-Item ('Function:\'+$n)).ScriptBlock}
try{
 $script:costLaunchScans=0;$script:costModeReads=0;$script:izanamiQueuePaused=$false;$launchConcurrency.Value=6;$izanamiMax.Value=6
 $script:accounts=@(1..239 | ForEach-Object {[pscustomobject]@{id=('cost-'+$_);username='synthetic';izanami=$false}})
 $members=@(1..6 | ForEach-Object {[pscustomobject]@{id=('cost-'+$_);job=('cost-job-'+$_)}})
 $script:izanamiBatches=@([pscustomobject]@{state='running';members=$members;closeOperation=$null;error='';limitClose=$false})
 function Sync-IzanamiCompletedClose {}
 function Sync-ActivityCapacity {}
 function Get-IzanamiMemberJob($m){return [pscustomobject]@{State='opened';ProcessId=1;IzanamiNoAttempts=$false;IzanamiRoundComplete=$false;CloseRequested=$false;CancelRequested=$false}}
 function Test-IzanamiMemberRunning($m){return $true}
 function Test-AccountOpenBlocked($a){return $a.id -in @('cost-1','cost-2','cost-3','cost-4','cost-5','cost-6')}
 function Get-AccountLaunchMode($a){$script:costModeReads++;return 'normal'}
 function Get-ActivityLaunchBatches($batches){$script:costLaunchScans++;return}
 function Update-IzanamiQueueStatus {}
 Pump-IzanamiQueue
 if($script:costLaunchScans -ne 1 -or $script:costModeReads -ne 0 -or $script:izanamiBatches[0].state -ne 'running'){throw 'Idle startup pass rescans six times or resolves launch modes for closed accounts'}
 Write-Output 'PASS: 239-account queue resolves no closed-account launch modes, scans startup candidates once when none are eligible and retains the running team.'
}finally{
 foreach($n in $costFns.Keys){Set-Item ('Function:\'+$n) $costFns[$n]};$script:accounts=$costAccounts;$script:izanamiBatches=$costBatches;$script:izanamiQueuePaused=$costPaused;$izanamiMax.Value=$costMax;$launchConcurrency.Value=$costConcurrency
}
