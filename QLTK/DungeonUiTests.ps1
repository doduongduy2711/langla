$dungeonSource=$script:accounts[0];$dungeonKey=[string]$dungeonSource.id
$dungeonFixture=$ctor.Invoke([object[]]@([string](Join-Path $PSScriptRoot 'QLTK.exe'),[string]'dungeon-fixture',[string]'unused',$null,$true))
$dungeonFixture.State='opened';$dungeonFixture.ReceiveKeyEnabled=$false
$null=$jobs.TryAdd($dungeonFixture.Id,$dungeonFixture);$script:accountJobs[$dungeonKey]=$dungeonFixture.Id
$dungeonSource | Add-Member NoteProperty receiveKey $false -Force
$dungeonSource | Add-Member NoteProperty dungeonHigh $false -Force
Update-DungeonUi
$dungeonHigh.Checked=$true
if(!$dungeonSource.dungeonHigh -or !$dungeonFixture.DungeonHighEnabled -or !$dungeonFixture.ReceiveKeyRequested){throw 'Dungeon checkbox did not save and request entry'}
$dungeonHigh.Checked=$false
if($dungeonSource.dungeonHigh -or $dungeonFixture.DungeonHighEnabled -or $dungeonFixture.ReceiveKeyRequested){throw 'Dungeon untick did not cancel pending entry'}
$closeAfterKey.Checked=$true
if(!$dungeonSource.closeAfterKey -or !$dungeonFixture.CloseAfterKey){throw 'Close after key checkbox did not save and apply live'}
$dungeonHigh.Checked=$true;Copy-AccountConfig
if(!(($script:copiedAccountConfig | ConvertFrom-Json).closeAfterKey)){throw 'Copy omitted close after key preference'}
if(!(($script:copiedAccountConfig | ConvertFrom-Json).dungeonHigh)){throw 'Copy omitted high dungeon preference'}
$dungeonSecond=[pscustomobject]@{id='dungeon-second';username='dungeon-second';secret='unused';proxy=$null;receiveKey=$false;dungeonHigh=$false}
$script:accounts+=@($dungeonSecond);Refresh-Grid;$grid.ClearSelection();$grid.Rows[1].Selected=$true;Update-DungeonUi
if($dungeonHigh.Checked){throw 'Dungeon selection leaked to another account'}
if($closeAfterKey.Checked){throw 'Close after key selection leaked to another account'}
Paste-AccountConfigAll
if(!$dungeonSecond.dungeonHigh -or !$dungeonHigh.Checked){throw 'Paste omitted high dungeon preference'}
if(!$dungeonSecond.closeAfterKey -or !$closeAfterKey.Checked){throw 'Paste omitted close after key preference'}
$grid.ClearSelection();$grid.Rows[0].Selected=$true;Update-DungeonUi;$closeAfterKey.Checked=$false
if($dungeonSource.closeAfterKey -or $dungeonFixture.CloseAfterKey){throw 'Close after key untick did not apply live'}
$script:accountJobs.Remove($dungeonKey);$removed=$null;$null=$jobs.TryRemove($dungeonFixture.Id,[ref]$removed)
$script:accounts=@($dungeonSource);$dungeonSource | Add-Member NoteProperty dungeonHigh $false -Force;Refresh-Grid
Write-Output 'PASS: dungeon checkbox, saved preference, pending request/untick, account isolation and copy/paste config.'
