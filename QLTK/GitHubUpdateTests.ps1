$ghRoot=Join-Path (Split-Path $PSScriptRoot -Parent) ('github-update-fixture-'+[guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory -Path $ghRoot
function Assert-GitHubRejected($action,[string]$label){$rejected=$false;try{& $action | Out-Null}catch{$rejected=$true};if(!$rejected){throw ('Accepted invalid update: '+$label)}}
function New-GitHubZipFixture([string]$path,[string]$name,[string]$text,[bool]$badHash=$false){
 Add-Type -AssemblyName System.IO.Compression,System.IO.Compression.FileSystem
 $bytes=[Text.Encoding]::UTF8.GetBytes($text);$sha=[Security.Cryptography.SHA256]::Create();try{$hash=[BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-','')}finally{$sha.Dispose()}
 if($badHash){$hash='0'*64}
 $manifest=@{schema=1;version=210;files=@(@{name=$name;size=$bytes.Length;sha256=$hash})} | ConvertTo-Json -Depth 5 -Compress
 $stream=[IO.File]::Create($path);$zip=New-Object IO.Compression.ZipArchive($stream,[IO.Compression.ZipArchiveMode]::Create)
 try{
  foreach($entry in @(@('manifest.json',[Text.Encoding]::UTF8.GetBytes($manifest)),@(('QLTK/'+$name),$bytes))){$writer=$zip.CreateEntry([string]$entry[0]).Open();try{$writer.Write([byte[]]$entry[1],0,$entry[1].Length)}finally{$writer.Dispose()}}
 }finally{$zip.Dispose();$stream.Dispose()}
 return [pscustomobject]@{Version=210;Size=(Get-Item -LiteralPath $path).Length;Hash=(Get-LiveUpdateHash $path)}
}
$ghOldWriter=(Get-Item Function:Write-UpdateFile).ScriptBlock
try{
 if((Normalize-UpdateRepository 'https://github.com/demo/QLTK.git') -ne 'demo/QLTK'){throw 'Repository normalization failed'}
 Assert-GitHubRejected {Normalize-UpdateRepository 'https://evil.example/demo/QLTK'} 'repository origin'
 foreach($name in @('../accounts.json','Data/accounts.json','accounts.json','settings.json','api-token.json','NativeBackend.cs:stream','C:\evil.ps1','x/evil.ps1','QLTK.exe','AutoItemUiTests.ps1')){if(Test-UpdateFileName $name){throw ('Unsafe update filename accepted: '+$name)}}
 $release=[pscustomobject]@{tag_name='v210';draft=$false;prerelease=$false;assets=@([pscustomobject]@{name='QLTK-update.zip';state='uploaded';size=100;digest=('sha256:'+('a'*64));browser_download_url='https://github.com/demo/QLTK/releases/download/v210/QLTK-update.zip'})}
 $parsed=Read-GitHubRelease $release 'demo/QLTK';if($parsed.Version -ne 210 -or $parsed.Hash -cne ('A'*64)){throw 'Release metadata parsing failed'}
 $release.assets[0].browser_download_url='https://github.com/other/repo/releases/download/v210/QLTK-update.zip'
 Assert-GitHubRejected {Read-GitHubRelease $release 'demo/QLTK'} 'foreign release asset'
 $zipPath=Join-Path $ghRoot 'good.zip';$fixture=New-GitHubZipFixture $zipPath 'AutoItemUi.ps1' 'function Fixture-Update { 210 }'
 $stage=Join-Path $ghRoot 'stage';$files=@(Expand-VerifiedUpdate $zipPath $stage $fixture)
 $target=Join-Path $ghRoot 'target';$null=New-Item -ItemType Directory -Path $target
 [IO.File]::WriteAllText((Join-Path $target 'AutoItemUi.ps1'),'old')
 [IO.File]::WriteAllText((Join-Path $target 'accounts.json'),'personal data fixture')
 $backup=Join-Path $ghRoot 'backup';$count=Install-VerifiedUpdate $stage $target $backup $files
 if($count -ne 1 -or [IO.File]::ReadAllText((Join-Path $backup 'AutoItemUi.ps1')) -ne 'old' -or [IO.File]::ReadAllText((Join-Path $target 'accounts.json')) -ne 'personal data fixture'){throw 'Update backup/data isolation failed'}
 if((Install-VerifiedUpdate $stage $target (Join-Path $ghRoot 'backup2') $files) -ne 0){throw 'Unchanged update rewrote files'}
 $badZip=Join-Path $ghRoot 'bad.zip';$bad=New-GitHubZipFixture $badZip '../accounts.json' 'evil'
 Assert-GitHubRejected {Expand-VerifiedUpdate $badZip (Join-Path $ghRoot 'badstage') $bad} 'zip traversal'
 $hashZip=Join-Path $ghRoot 'hash.zip';$bad=New-GitHubZipFixture $hashZip 'AutoItemUi.ps1' 'valid' $true
 Assert-GitHubRejected {Expand-VerifiedUpdate $hashZip (Join-Path $ghRoot 'hashstage') $bad} 'file hash mismatch'
 $syntaxZip=Join-Path $ghRoot 'syntax.zip';$bad=New-GitHubZipFixture $syntaxZip 'AutoItemUi.ps1' 'function Broken {'
 Assert-GitHubRejected {Expand-VerifiedUpdate $syntaxZip (Join-Path $ghRoot 'syntaxstage') $bad} 'PowerShell syntax'
 # Inject a failed write after one changed file and verify all original files are restored.
 [IO.File]::WriteAllText((Join-Path $stage 'RetryUi.ps1'),'new retry')
 [IO.File]::WriteAllText((Join-Path $target 'AutoItemUi.ps1'),'original item')
 [IO.File]::WriteAllText((Join-Path $target 'RetryUi.ps1'),'original retry')
 $files+=@([pscustomobject]@{name='RetryUi.ps1';sha256=(Get-LiveUpdateHash (Join-Path $stage 'RetryUi.ps1'))})
 $script:ghWriteCount=0
 function Write-UpdateFile($source,$destination){$script:ghWriteCount++;if($script:ghWriteCount -eq 2){throw 'synthetic write failure'};[IO.File]::Copy($source,$destination,$true)}
 Assert-GitHubRejected {Install-VerifiedUpdate $stage $target (Join-Path $ghRoot 'rollback') $files} 'failed disk transaction'
 if([IO.File]::ReadAllText((Join-Path $target 'AutoItemUi.ps1')) -ne 'original item' -or [IO.File]::ReadAllText((Join-Path $target 'RetryUi.ps1')) -ne 'original retry'){throw 'Failed install did not roll back'}
 # Exercise the completed background-download branch without network/game/file installation.
 $ghSavedApply=(Get-Item Function:Apply-LiveUpdate).ScriptBlock;$ghSavedInstall=(Get-Item Function:Install-VerifiedUpdate).ScriptBlock
 $ghSavedTimer=$socketTimer;$ghSavedBinaryMap=$script:liveUpdateBinaries.Clone()
 try{
  function Install-VerifiedUpdate($stagePath,$targetRoot,$backupPath,$files){$script:ghInstalled=$files.Count;return $files.Count}
  function Apply-LiveUpdate {return [pscustomobject]@{RestartRequired=$false;Applied=1}}
  $socketTimer=New-Object Windows.Forms.Timer;$socketTimer.Interval=60000;$socketTimer.Start()
  $downloadFolder=Join-Path $ghRoot 'completed-download';$null=New-Item -ItemType Directory -Path $downloadFolder
  $completion=New-Object 'System.Threading.Tasks.TaskCompletionSource[object]';$completion.SetResult($null)
  $script:githubUpdateWork=@{client=(New-Object Net.WebClient);task=$completion.Task;phase='package';at=[datetime]::UtcNow;folder=$downloadFolder;zip=$zipPath;release=$fixture}
  $script:liveUpdateBusy=$true;$refreshButton.Enabled=$false;Pump-GitHubUpdate
  if(!$socketTimer.Enabled -or !$refreshButton.Enabled -or $script:liveUpdateBusy -or $script:githubUpdateWork -or $script:ghInstalled -ne 1 -or $script:liveUpdateMessage -notlike '*Đã cập nhật v210*'){throw 'Background update completion failed to retain manager/restore controls'}
  $completion=New-Object 'System.Threading.Tasks.TaskCompletionSource[object]';$completion.SetCanceled()
  $script:githubUpdateWork=@{client=(New-Object Net.WebClient);task=$completion.Task;phase='release';at=[datetime]::UtcNow}
  $script:liveUpdateBusy=$true;$refreshButton.Enabled=$false;Pump-GitHubUpdate
  if(!$socketTimer.Enabled -or !$refreshButton.Enabled -or $script:liveUpdateBusy -or $script:githubUpdateWork){throw 'Failed background download left updater/manager blocked'}
 }finally{
  if($socketTimer -and ![object]::ReferenceEquals($socketTimer,$ghSavedTimer)){$socketTimer.Stop();$socketTimer.Dispose()};$socketTimer=$ghSavedTimer
  Set-Item Function:Apply-LiveUpdate $ghSavedApply;Set-Item Function:Install-VerifiedUpdate $ghSavedInstall;$script:liveUpdateBinaries=$ghSavedBinaryMap
 }
 Write-Output 'PASS: GitHub repository/release provenance, SHA-256, zip traversal/credentials/data exclusions, full syntax validation, backup, unchanged-file skip and rollback on partial write failure. No external upload or network request.'
}finally{Set-Item Function:Write-UpdateFile $ghOldWriter}
