$izanamiSource=$script:accounts[0];$izanamiKey=[string]$izanamiSource.id
$izanamiJobs=[TGLL.NativeBackend].GetField('jobs',[Reflection.BindingFlags]'NonPublic,Static').GetValue($null)
$izanamiCtor=[TGLL.NativeJob].GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')[0]
$izanamiFixture=$izanamiCtor.Invoke(@('C:\synthetic.exe','izanami-test','secret',$null,$true));$izanamiFixture.State='opened';$izanamiFixture.ReceiveKeyEnabled=$false;$izanamiFixture.CharacterName='phongtoichoi'
$null=$izanamiJobs.TryAdd($izanamiFixture.Id,$izanamiFixture);$script:accountJobs[$izanamiKey]=$izanamiFixture.Id
$izanamiSource | Add-Member NoteProperty izanami $false -Force;Update-IzanamiUi;$izanamiCheck.Checked=$true
if(!$izanamiSource.izanami -or !$izanamiFixture.IzanamiEnabled -or !$izanamiFixture.IzanamiRequested){throw 'Izanami checkbox failed to save/queue native request'}
Update-UiSummary
if($grid.Columns['character'].ReadOnly -ne $true -or $grid.Rows[0].Cells['character'].Value -ne 'phongtoichoi'){throw 'Live character-name column failed'}
$izanamiFixture.IzanamiRequested=$false;Update-IzanamiUi
if($izanamiFixture.IzanamiRequested){throw 'UI refresh repeated Izanami request'}
$izanamiSavedMax=$izanamiMax.Value;$izanamiSavedClose=$izanamiCloseAfter.Checked
$izanamiMax.Value=18;$izanamiCloseAfter.Checked=$true;Copy-AccountConfig
$izanamiMax.Value=12;$izanamiCloseAfter.Checked=$false
if(!(($script:copiedAccountConfig | ConvertFrom-Json).izanami)){throw 'Copy omitted Izanami config'}
$izanamiSecond=[pscustomobject]@{id='izanami-second';username='izanami-second';secret='unused';proxy=$null;receiveKey=$false;dungeonHigh=$false;izanami=$false}
$script:accounts+=@($izanamiSecond);Refresh-Grid;$grid.ClearSelection();$grid.Rows[1].Selected=$true;Update-IzanamiUi
if($izanamiCheck.Checked){throw 'Izanami preference leaked across accounts'}
Paste-AccountConfigAll
if($izanamiMax.Value -ne 18 -or !$izanamiCloseAfter.Checked){throw 'Copy/paste omitted global Izanami queue options'}
if(!$izanamiSecond.izanami -or !$izanamiCheck.Checked){throw 'Paste omitted Izanami for closed account'}
$grid.ClearSelection();$grid.Rows[0].Selected=$true;Update-IzanamiUi;$izanamiCheck.Checked=$false
if($izanamiFixture.IzanamiEnabled -or $izanamiFixture.IzanamiRequested -or $izanamiSource.izanami){throw 'Izanami untick failed to cancel/save'}
$script:accountJobs.Remove($izanamiKey);$removed=$null;$null=$izanamiJobs.TryRemove($izanamiFixture.Id,[ref]$removed)
$script:accounts=@($izanamiSource);Refresh-Grid
$izanamiMax.Value=$izanamiSavedMax;$izanamiCloseAfter.Checked=$izanamiSavedClose
Write-Output 'PASS: Auto cam thuat tab, per-account Izanami preference, queued request/untick, no duplicate refresh, copy/paste and account isolation.'
