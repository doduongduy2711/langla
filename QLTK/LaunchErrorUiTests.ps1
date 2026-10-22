$nativeError=New-Object Exception 'game_invalid'
$wrappedError=New-Object Exception -ArgumentList @('Wrapper error',$nativeError)
$record=New-Object Management.Automation.ErrorRecord -ArgumentList @($wrappedError,'test',[Management.Automation.ErrorCategory]::InvalidOperation,$null)
if((Get-LaunchQueueErrorCode $record) -ne 'game_invalid'){throw 'Underlying game path error was hidden'}
foreach($code in @('native_x64_required','login_length_invalid','login_queue_full','login_secret_unavailable')){
    $e=New-Object Exception $code;$r=New-Object Management.Automation.ErrorRecord -ArgumentList @($e,'test',[Management.Automation.ErrorCategory]::InvalidOperation,$null)
    if((Get-LaunchQueueErrorCode $r) -ne $code){throw 'Known launch error hidden'}
}
$invalidPathCaught=$false
try{$null=Get-NativeLaunchCredentials ([pscustomobject]@{username='test';secret='unused'}) 'C:\missing-test\TheGioiLangLa.exe'}catch{$invalidPathCaught=($_.Exception.Message -eq 'game_invalid')}
if(!$invalidPathCaught){throw 'Missing game path not caught before Enqueue'}
# Tệp giả chỉ kiểm tra dữ liệu đầu vào; không xếp hàng hoặc mở game.
$testDirectory=Join-Path ([IO.Path]::GetTempPath()) ('QLTK-launch-test-'+[guid]::NewGuid().ToString())
try{
    $null=[IO.Directory]::CreateDirectory($testDirectory);$testExe=Join-Path $testDirectory 'TheGioiLangLa.exe';[IO.File]::WriteAllBytes($testExe,[byte[]]@(0))
    foreach($invalid in @([pscustomobject]@{username='';secret='test'},[pscustomobject]@{username='test';secret=''},[pscustomobject]@{username='test';secret=('x'*129)})){
        $caught=$false;try{$null=Get-NativeLaunchCredentials $invalid $testExe}catch{$caught=$_.Exception.Message -eq 'login_length_invalid'};if(!$caught){throw 'Invalid credentials not rejected'}
    }
    $credentials=Get-NativeLaunchCredentials ([pscustomobject]@{username='test';secret='test-only'}) $testExe
    if($credentials.Username -ne 'test' -or $credentials.Password -ne 'test-only'){throw 'Valid credentials corrupted'}
}finally{if($testExe -and [IO.File]::Exists($testExe)){[IO.File]::Delete($testExe)};if([IO.Directory]::Exists($testDirectory)){[IO.Directory]::Delete($testDirectory)}}
Write-Output 'PASS: precise wrapped launch errors, missing game path, credential validation and valid input; no game launched.'
