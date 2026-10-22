# Synthetic jobs and the test process only; never launches or closes a game.
$openTestRow=$grid.Rows[0];$openTestAccount=$openTestRow.Tag;$openTestKey=[string]$openTestAccount.id
$openTestJob=$ctor.Invoke([object[]]@([string](Join-Path $PSScriptRoot 'QLTK.exe'),[string]'open-fixture',[string]'unused',$null,$true))
$null=$jobs.TryAdd($openTestJob.Id,$openTestJob);$script:accountJobs[$openTestKey]=$openTestJob.Id
foreach($state in @('queued','starting','retrying')){
    $openTestJob.State=$state;$openTestJob.ProcessId=0;Update-AccountVisibility $openTestRow
    if(!(Test-AccountOpenBlocked $openTestAccount) -or $openTestRow.Cells['open'].Tag -ne $true){throw "OPEN not disabled for $state"}
    Open-Account $openTestAccount
    if($script:accountJobs[$openTestKey] -ne $openTestJob.Id){throw 'Blocked OPEN submitted another job'}
}
$testProcess=[Diagnostics.Process]::GetCurrentProcess();$testPath='';[long]$testCreated=0
try{
    $identityArgs=[object[]]@([int]$testProcess.Id,[string]'',[long]0)
    $null=[TGLL.ManagedGames].GetMethod('ReadIdentity',[Reflection.BindingFlags]'Static,NonPublic').Invoke($null,$identityArgs)
    $testPath=[string]$identityArgs[1]
    [TGLL.ManagedGames]::Register($testProcess,$testPath)
    $openTestJob.ProcessId=$testProcess.Id;$openTestJob.State='opened';Update-AccountVisibility $openTestRow
    if(!(Test-AccountOpenBlocked $openTestAccount) -or $openTestRow.Cells['open'].Tag -ne $true){throw 'Live process did not disable OPEN'}
    # Cancellation/failed cleanup can leave a live game: keep duplicate prevention.
    $openTestJob.State='cancelled'
    if(!(Test-AccountOpenBlocked $openTestAccount)){throw 'Live cancelled process would allow duplicate'}
    $script:accountJobs.Remove($openTestKey);$script:accountWindowPids[$openTestKey]=$testProcess.Id
    if(!(Test-AccountOpenBlocked $openTestAccount)){throw 'Manual/bridge process did not disable OPEN'}
    $script:accountWindowPids.Remove($openTestKey);$script:accountJobs[$openTestKey]=$openTestJob.Id
    $openTestJob.ProcessId=2147483647;$openTestJob.State='opened';Update-AccountVisibility $openTestRow
    if((Test-AccountOpenBlocked $openTestAccount) -or $openTestRow.Cells['open'].Tag -ne $false){throw 'Exited process did not re-enable OPEN'}
    $openTestJob.ProcessId=0;$openTestJob.State='failed';Update-AccountVisibility $openTestRow
    if(Test-AccountOpenBlocked $openTestAccount){throw 'Failed startup did not re-enable OPEN'}
    Update-AccountOpen $openTestRow $true
    $image=New-Object Drawing.Bitmap($grid.Width,$grid.Height)
    try{$grid.DrawToBitmap($image,(New-Object Drawing.Rectangle(0,0,$grid.Width,$grid.Height)))}finally{$image.Dispose()}
}finally{
    $testProcess.Dispose();$script:accountJobs.Remove($openTestKey);$removed=$null;$null=$jobs.TryRemove($openTestJob.Id,[ref]$removed);Update-AccountVisibility $openTestRow
}
Write-Output 'PASS: OPEN disabled for pending/retry/live/native/manual accounts, duplicate launch guard, disabled painting and re-enable after exit/failure.'
