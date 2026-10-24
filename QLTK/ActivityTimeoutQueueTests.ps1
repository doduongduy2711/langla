# Exercise all four real schedulers with simulated process exit. Never open/close a real game.
$timeoutSavedAccounts=$script:accounts
$timeoutFunctions=@{}
foreach($kind in @('Quest','Dungeon','Izanami','SonCap')){foreach($prefix in @('Get-','Test-')){$name=if($prefix -eq 'Get-'){"Get-${kind}MemberJob"}else{"Test-${kind}MemberRunning"};$timeoutFunctions[$name]=(Get-Item ("Function:\"+$name)).ScriptBlock}}
foreach($kind in @('Quest','Dungeon','Izanami','SonCap')){$name="Begin-${kind}TeamClose";$timeoutFunctions[$name]=(Get-Item ("Function:\"+$name)).ScriptBlock}
$timeoutFunctions['Test-AccountOpenBlocked']=(Get-Item Function:\Test-AccountOpenBlocked).ScriptBlock
try {
    $script:timeoutFakeRunning=$true
    function Test-AccountOpenBlocked($a){return $script:timeoutFakeRunning}
    foreach($kind in @('Quest','Dungeon','Izanami','SonCap')){
        $collection=$kind.ToLowerInvariant();$batchName=$collection+'Batches';$pauseName=$collection+'QueuePaused'
        $oldBatches=Get-Variable -Scope Script -Name $batchName -ValueOnly;$oldPaused=Get-Variable -Scope Script -Name $pauseName -ValueOnly
        $timeoutCtor=[TGLL.NativeJob].GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')[0]
        $script:timeoutJob=$timeoutCtor.Invoke(@('C:\synthetic.exe','fixture','unused',$null,$true))
        $script:timeoutJob.ProcessId=123456;$script:timeoutJob.State='closing';$script:timeoutJob.ActivityTimeoutClosed=$true;$script:timeoutJob.CloseRequested=$true
        $member=[pscustomobject]@{id='timeout-fixture';job=$script:timeoutJob.Id}
        $batch=[pscustomobject]@{members=@($member);state='blocked';error='fault';closeOperation=$null;limitClose=$false}
        $script:accounts=@([pscustomobject]@{id='timeout-fixture';username='fixture'})
        Set-Variable -Scope Script -Name $batchName -Value @($batch);Set-Variable -Scope Script -Name $pauseName -Value $false
        Set-Item ("Function:\Begin-${kind}TeamClose") {param($b)return [pscustomobject]@{Completed=$false;Failed=0;Error=''}}
        Set-Item ("Function:\Get-${kind}MemberJob") {param($m)return $script:timeoutJob}
        Set-Item ("Function:\Test-${kind}MemberRunning") {param($m)return $script:timeoutFakeRunning}
        try {
            $script:timeoutFakeRunning=$true;& ("Pump-${kind}Queue")
            if($batch.state -eq 'done'){throw "$kind released a failed-close process"}
            $script:timeoutFakeRunning=$false;$script:timeoutJob.State='closed';& ("Pump-${kind}Queue")
            if($batch.state -ne 'done'){throw "$kind held a confirmed watchdog exit"}
        }finally{Set-Variable -Scope Script -Name $batchName -Value $oldBatches;Set-Variable -Scope Script -Name $pauseName -Value $oldPaused}
    }
    Write-Output 'PASS: all four activity queues hold a timeout account until actual process exit, then release it without marking the activity successful.'
}finally{
    $script:accounts=$timeoutSavedAccounts
    foreach($name in $timeoutFunctions.Keys){Set-Item ("Function:\"+$name) $timeoutFunctions[$name]}
}

. (Join-Path $PSScriptRoot 'ActivityCapacityTests.ps1')
