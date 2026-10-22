$liveSavedSources=$script:liveUpdateSources;$liveSavedBinaries=$script:liveUpdateBinaries
$liveSavedMessage=$script:liveUpdateMessage;$liveSavedUntil=$script:liveUpdateMessageUntil
$liveSavedStatus=$status.Text;$liveSavedTimer=$socketTimer
$liveDir=Join-Path (Split-Path $PSScriptRoot -Parent) ('live-update-fixture-'+[guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory -Path $liveDir
$livePath=Join-Path $liveDir 'Fixture.ps1';$liveNativePath=Join-Path $liveDir 'Fixture.cs'
$liveFile={param($text)[IO.File]::WriteAllText($livePath,$text,(New-Object Text.UTF8Encoding($true)))}
try{
 & $liveFile 'function Live-Fixture($value) { return "old:"+$value }; $script:fixtureStartup=1'
 . $livePath
 $script:liveUpdateSources=@{};$script:liveUpdateSources[$livePath]=Read-LiveUpdateSource $livePath
 [IO.File]::WriteAllText($liveNativePath,'native1');$script:liveUpdateBinaries=@{};$script:liveUpdateBinaries[$liveNativePath]=Get-LiveUpdateHash $liveNativePath
 $liveAccounts=$script:accounts;$liveJobs=$script:accountJobs;$liveUp=$script:farmUpRequests;$liveDrafts=$script:autoItemDrafts;$liveRows=$grid.Rows.Count;$liveTabs=$tabs.TabPages.Count
 $script:fixtureStartup=9
 $liveClick=New-Object Windows.Forms.Button;$liveClick.Add_Click({$script:liveClickResult=Live-Fixture 'event'})
 & $liveFile 'function Live-Fixture($value) { return "new:"+$value+":"+$PSScriptRoot }; $script:fixtureStartup=1'
 $liveResult=Apply-LiveUpdate
 if($liveResult.Applied -ne 1 -or $liveResult.RestartRequired -or (Live-Fixture 'x') -ne ('new:x:'+$liveDir) -or $script:fixtureStartup -ne 9){throw 'Reload lost inline parameters/file context or reran startup'}
 $liveClick.PerformClick()
 if($script:liveClickResult -ne ('new:event:'+$liveDir)){throw 'Existing handler did not invoke new function'}
 if((Apply-LiveUpdate).Applied -ne 0){throw 'Unchanged update reapplied'}
 # Whole batch must be rejected on a syntax error, including an earlier valid file.
 $liveBadPath=Join-Path $liveDir 'Bad.ps1';[IO.File]::WriteAllText($liveBadPath,'function Live-Bad { 1 }')
 $script:liveUpdateSources[$liveBadPath]=Read-LiveUpdateSource $liveBadPath
 [IO.File]::WriteAllText($liveBadPath,'function Live-Bad {')
 & $liveFile 'function Live-Fixture($value) { return "broken-batch" }; $script:fixtureStartup=1'
 $liveRejected=$false;try{$null=Apply-LiveUpdate}catch{$liveRejected=$true}
 if(!$liveRejected -or (Live-Fixture 'x') -ne ('new:x:'+$liveDir)){throw 'Syntax error caused partial update'}
 $script:liveUpdateSources.Remove($liveBadPath)
 # Top-level controls/events and native types are deliberately not reexecuted/recompiled.
 & $liveFile 'function Live-Fixture($value) { return "unsafe" }; $script:fixtureStartup=2'
 $liveResult=Apply-LiveUpdate
 if(!$liveResult.RestartRequired -or $script:fixtureStartup -ne 9 -or (Live-Fixture 'x') -ne ('new:x:'+$liveDir)){throw 'Top-level update was falsely reported/applied'}
 & $liveFile 'function Live-Fixture($value) { return "native-changed" }; $script:fixtureStartup=1'
 [IO.File]::WriteAllText($liveNativePath,'native2')
 if(!(Apply-LiveUpdate).RestartRequired -or (Live-Fixture 'x') -ne ('new:x:'+$liveDir)){throw 'Native update applied partly'}
 [IO.File]::WriteAllText($liveNativePath,'native1')
 $socketTimer=New-Object Windows.Forms.Timer;$socketTimer.Interval=60000;$socketTimer.Start()
 & $liveFile 'function Live-Fixture($value) { return "button:"+$value }; $script:fixtureStartup=1'
 $null=[Windows.Forms.Button].GetMethod('OnClick',[Reflection.BindingFlags]'NonPublic,Instance').Invoke($refreshButton,@([EventArgs]::Empty))
 if((Live-Fixture 'ok') -ne 'button:ok' -or !$socketTimer.Enabled -or !$refreshButton.Enabled -or $script:liveUpdateBusy -or $script:liveUpdateMessage -notlike '*Đã cập nhật QLTK*'){throw ('Update button failure: result='+(Live-Fixture 'ok')+' timer='+$socketTimer.Enabled+' enabled='+$refreshButton.Enabled+' busy='+$script:liveUpdateBusy+' message='+$script:liveUpdateMessage)}
 $script:liveUpdateBusy=$true;$null=[Windows.Forms.Button].GetMethod('OnClick',[Reflection.BindingFlags]'NonPublic,Instance').Invoke($refreshButton,@([EventArgs]::Empty));$script:liveUpdateBusy=$false
 if(!$socketTimer.Enabled){throw 'Reentrant update stopped timer'}
 if(![object]::ReferenceEquals($liveAccounts,$script:accounts) -or ![object]::ReferenceEquals($liveJobs,$script:accountJobs) -or ![object]::ReferenceEquals($liveUp,$script:farmUpRequests) -or ![object]::ReferenceEquals($liveDrafts,$script:autoItemDrafts) -or $grid.Rows.Count -ne $liveRows -or $tabs.TabPages.Count -ne $liveTabs){throw 'Live update reset accounts/jobs/Up/drafts/UI'}
 Write-Output 'PASS: live button updates functions in place, preserves inline parameters/file context/existing handlers/jobs/Up/queues/drafts; no duplicate reload/startup; syntax/native/UI changes reject whole batch; timer resumes.'
}finally{
 if($socketTimer -and ![object]::ReferenceEquals($socketTimer,$liveSavedTimer)){$socketTimer.Stop();$socketTimer.Dispose()}
 if($liveClick){$liveClick.Dispose()}
 $socketTimer=$liveSavedTimer;$script:liveUpdateSources=$liveSavedSources;$script:liveUpdateBinaries=$liveSavedBinaries;$script:liveUpdateBusy=$false
 $script:liveUpdateMessage=$liveSavedMessage;$script:liveUpdateMessageUntil=$liveSavedUntil;$status.Text=$liveSavedStatus
 Remove-Item Function:Live-Fixture -ErrorAction SilentlyContinue
}

. (Join-Path $PSScriptRoot 'AutoHideUiTests.ps1')

. (Join-Path $PSScriptRoot 'GitHubUpdateTests.ps1')

. (Join-Path $PSScriptRoot 'IzanamiCompletedCloseTests.ps1')

. (Join-Path $PSScriptRoot 'ThiQuyPriorityUiTests.ps1')
