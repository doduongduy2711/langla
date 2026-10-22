$qs=$script:accounts[0];$qkey=[string]$qs.id
$qjobs=[TGLL.NativeBackend].GetField('jobs',[Reflection.BindingFlags]'NonPublic,Static').GetValue($null)
$qctor=[TGLL.NativeJob].GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')[0]
$qfixture=$qctor.Invoke(@('C:\synthetic.exe','quest-test','unused',$null,$true));$qfixture.State='opened';$qfixture.GameAutoWanted=$true
$qfixture.ReceiveKeyEnabled=$false
foreach($field in @('receiveKey','dungeonHigh','izanami')){$qs | Add-Member NoteProperty $field $false -Force}
$null=$qjobs.TryAdd($qfixture.Id,$qfixture);$script:accountJobs[$qkey]=$qfixture.Id
Sync-QuestPreferences
if(!$qfixture.GameAutoWanted){throw 'Default quest-off disabled unrelated Auto'}
Update-QuestUi;$questCheck.Checked=$true
if(!$qs.autoQuest -or !$qfixture.QuestEnabled -or !$qfixture.QuestPauseAuto){throw 'Quest checkbox not saved/applied'}
$phaseField=[TGLL.NativeJob].GetField('QuestPhase',[Reflection.BindingFlags]'NonPublic,Instance');$phaseField.SetValue($qfixture,6);Sync-QuestPreferences
if($phaseField.GetValue($qfixture) -ne 6){throw 'Refresh resets quest cycle'}
$qs.receiveKey=$true;[TGLL.NativeBackend]::SetReceiveKey($qfixture.Id,$true);Sync-QuestPreferences
if($qfixture.QuestEnabled -or !$qfixture.ReceiveKeyEnabled -or (Get-AccountLaunchMode $qs) -ne 'dungeon'){throw 'Key receipt must take priority over saved weekly preference'}
[TGLL.NativeBackend]::SetAutoQuest($qfixture.Id,$true)
if($qfixture.QuestEnabled){throw 'Native quest setter bypasses key priority'}
$qs.receiveKey=$false;[TGLL.NativeBackend]::SetReceiveKey($qfixture.Id,$false);Sync-QuestPreferences
if(!$qfixture.QuestEnabled -or (Get-AccountLaunchMode $qs) -ne 'quest'){throw 'Weekly preference cannot resume after key mode disabled'}
$savedTaskMax=$questMax.Value;$savedTaskClose=$questCloseAfter.Checked
$questMax.Value=7;$questCloseAfter.Checked=$true;Copy-AccountConfig
$questMax.Value=12;$questCloseAfter.Checked=$false
$qsecond=[pscustomobject]@{id='q-second';username='q-second';secret='unused'};$script:accounts+=@($qsecond);Refresh-Grid;$grid.ClearSelection();$grid.Rows[1].Selected=$true;Update-QuestUi
if($questCheck.Checked){throw 'Quest preference leaked across accounts'}
Paste-AccountConfigAll
if($questMax.Value -ne 7 -or !$questCloseAfter.Checked){throw 'Quest queue controls omitted from config paste'}
if(!$qsecond.autoQuest -or !$questCheck.Checked){throw 'Quest omitted from config paste'}
$grid.ClearSelection();$grid.Rows[0].Selected=$true;Update-QuestUi;$questCheck.Checked=$false
if($qfixture.QuestEnabled -or $qfixture.GameAutoWanted -or $qs.autoQuest){throw 'Quest untick failed'}
$script:accountJobs.Remove($qkey);$removed=$null;$null=$qjobs.TryRemove($qfixture.Id,[ref]$removed);$script:accounts=@($qs);Refresh-Grid
Write-Output 'PASS: Auto Nhiem vu tab, per-account enable, no refresh rearm, unrelated Auto preserved, untick and copy/paste all.'
$questMax.Value=$savedTaskMax;$questCloseAfter.Checked=$savedTaskClose
