$retryAccounts=$script:accounts;$retryMappings=$script:accountJobs.Clone();$retryBatches=$script:questBatches
$retrySavedOpen=(Get-Item Function:\Open-Account).ScriptBlock;$retrySavedBlocked=(Get-Item Function:\Test-AccountOpenBlocked).ScriptBlock;$retrySavedGet=(Get-Item Function:\Get-AccountJob).ScriptBlock
try {
    $a=[pscustomobject]@{id='retry-fixture';username='retry-fixture'};$script:accounts=@($a);$script:accountJobs=@{'retry-fixture'='failed-job'};$script:failedRetryTimes=@{};$script:failedRetryCounts=@{};$script:retryOpens=0
    $script:retryFixture=[pscustomobject]@{Id='failed-job';State='failed';Error='native_login_not_ready';CancelRequested=$false;CloseRequested=$false}
    function Get-AccountJob($id){return $script:retryFixture}
    function Test-AccountOpenBlocked($a){return $false}
    function Open-Account($a){$script:retryOpens++;$script:accountJobs[[string]$a.id]='replacement-job';if($script:questLaunchingAccountId -ne 'retry-fixture'){throw 'Retry lost task queue mode'}}
    $m=[pscustomobject]@{id='retry-fixture';job='failed-job'};$b=[pscustomobject]@{members=@($m);state='running';error=''};$script:questBatches=@($b)
    Retry-FailedAccounts;if($script:retryOpens){throw 'Failure retried without backoff'}
    $script:failedRetryTimes['retry-fixture']=[DateTime]::UtcNow.AddSeconds(-1);Retry-FailedAccounts
    if($script:retryOpens -ne 1 -or $m.job -ne 'replacement-job' -or $script:questLaunchingAccountId){throw 'Failed member not replaced in existing slot or marker leaked'}
    $script:retryFixture.State='failed';$script:retryFixture.CloseRequested=$true;$script:failedRetryTimes['retry-fixture']=[DateTime]::UtcNow.AddSeconds(-1);Retry-FailedAccounts
    if($script:retryOpens -ne 1){throw 'Deliberately closed account reopened'}
    $script:retryFixture.CloseRequested=$false;$script:retryFixture.CancelRequested=$true;Retry-FailedAccounts
    if($script:retryOpens -ne 1){throw 'Cancelled account reopened'}
    $script:retryFixture.CancelRequested=$false;$script:retryFixture.Error='game_disconnected'
    $script:failedRetryTimes['retry-fixture']=[DateTime]::UtcNow.AddSeconds(-1)
    [TGLL.NativeBackend]::AutoReconnectEnabled=$false;Retry-FailedAccounts
    if($script:retryOpens -ne 1){throw 'Disabled disconnect recovery reopened account'}
    function Open-Account($a,[bool]$reconnect=$false){if(!$reconnect){throw 'Recovery did not force login'};$script:retryOpens++;$script:accountJobs[[string]$a.id]='reconnected-job'}
    [TGLL.NativeBackend]::AutoReconnectEnabled=$true;Retry-FailedAccounts
    if($script:retryOpens -ne 2 -or $m.job -ne 'reconnected-job'){throw 'Reconnect lost queue slot'}
    Write-Output 'PASS: disconnected recovery respects toggle, forces auto login, and replaces existing queue slot.'
    Write-Output 'PASS: native_login_not_ready schedules backoff, reopens once, retains quest slot/mode, clears marker, closed/cancelled sessions never reopen.'
}finally {
    Set-Item Function:\Open-Account $retrySavedOpen;Set-Item Function:\Test-AccountOpenBlocked $retrySavedBlocked;Set-Item Function:\Get-AccountJob $retrySavedGet
    $script:accounts=$retryAccounts;$script:accountJobs=$retryMappings;$script:questBatches=$retryBatches;$script:failedRetryTimes=@{};$script:failedRetryCounts=@{}
}
