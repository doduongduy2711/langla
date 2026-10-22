$itemSource=$script:accounts[0];$itemKey=[string]$itemSource.id
$itemJobs=[TGLL.NativeBackend].GetField('jobs',[Reflection.BindingFlags]'NonPublic,Static').GetValue($null)
$itemCtor=[TGLL.NativeJob].GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')[0]
$itemFixture=$itemCtor.Invoke(@('C:\synthetic.exe','item-test','secret',$null,$true));$itemFixture.State='opened'
$null=$itemJobs.TryAdd($itemFixture.Id,$itemFixture);$script:accountJobs[$itemKey]=$itemFixture.Id
Update-AutoItemUi;$itemId.Text="168`r`n434`r`n168";Save-AutoItemUi;$itemInterval.Value=7;$itemEnabled.Checked=$true
if(!$itemSource.autoItemEnabled -or $itemSource.autoItemId -ne 168 -or $itemSource.autoItemSeconds -ne 7 -or !$itemFixture.AutoItemEnabled -or $itemFixture.AutoItemIntervalMs -ne 7000){throw 'Auto item UI did not save/apply preference'}
$nextField=[TGLL.NativeJob].GetField('AutoItemNext',[Reflection.BindingFlags]'NonPublic,Instance');$next=[datetime]::UtcNow.AddSeconds(10);$nextField.SetValue($itemFixture,$next);Sync-AutoItemPreferences
if($nextField.GetValue($itemFixture) -ne $next){throw 'UI refresh resets item timer'}
if(($itemFixture.AutoItemIds -join ',') -ne '168,434'){throw 'Multi item list not applied'}
$invalid=$false;try{$null=Get-AutoItemIds 'abc'}catch{$invalid=$true};if(!$invalid){throw 'Invalid item accepted'}
$itemOnce.Text="498`r`n568";Save-AutoItemUi;
Copy-AccountConfig
$itemSecond=[pscustomobject]@{id='item-second';username='item-second';secret='unused'};$script:accounts+=@($itemSecond);Refresh-Grid;$grid.ClearSelection();$grid.Rows[1].Selected=$true;Update-AutoItemUi
if($itemEnabled.Checked){throw 'Item preference leaked between accounts'}
Paste-AccountConfigAll
if(!$itemSecond.autoItemEnabled -or $itemSecond.autoItemId -ne 168 -or $itemSecond.autoItemSeconds -ne 7 -or !$itemEnabled.Checked){throw 'Item config not copied to closed account'}
if(($itemSecond.autoItemIds -join ',') -ne '168,434'){throw 'Multi item list copy failed'}
if(($itemSecond.autoItemOnceIds -join ',') -ne '498,568'){throw 'Once item copy failed'}
$grid.ClearSelection();$grid.Rows[0].Selected=$true;Update-AutoItemUi;$itemEnabled.Checked=$false
if($itemFixture.AutoItemEnabled -or $itemSource.autoItemEnabled){throw 'Item untick failed'}
# Simulate leaving the one-shot editor followed by periodic refresh, without a running game.
$itemLeave=[Windows.Forms.Control].GetMethod('OnLeave',[Reflection.BindingFlags]'NonPublic,Instance')
$itemOnce.Text='498,568';$null=$itemLeave.Invoke($itemOnce,@([EventArgs]::Empty));Update-AutoItemUi
if(($itemSource.autoItemOnceIds -join ',') -ne '498,568' -or ($itemFixture.AutoItemOnceIds -join ',') -ne '498,568'){throw 'Comma separated once IDs lost on leave'}
$itemOnce.Text='498,wrong';$null=$itemLeave.Invoke($itemOnce,@([EventArgs]::Empty));Update-AutoItemUi;Update-AutoItemUi
if($itemOnce.Text -ne '498,wrong' -or ($itemSource.autoItemOnceIds -join ',') -ne '498,568' -or $itemHint.Text -notlike '*chưa áp dụng*'){throw 'Invalid once draft discarded or applied on periodic refresh'}
$itemOther=[pscustomobject]@{id='draft-other';username='draft-other';secret='unused';autoItemOnceIds=@(123)}
$script:accounts+=@($itemOther);Refresh-Grid;$grid.ClearSelection();$grid.Rows[2].Selected=$true;Update-AutoItemUi
if($itemOnce.Text -ne '123'){throw 'Draft leaked into another account'}
$grid.ClearSelection();$grid.Rows[0].Selected=$true;Update-AutoItemUi
if($itemOnce.Text -ne '498,wrong'){throw 'Account switch discarded unfinished once draft'}
$itemOnce.Text="498`r`n568";$null=$itemLeave.Invoke($itemOnce,@([EventArgs]::Empty));Update-AutoItemUi
if($script:autoItemDrafts.ContainsKey($itemKey) -or ($itemSource.autoItemOnceIds -join ',') -ne '498,568'){throw 'Corrected once draft not committed'}
if((@(Get-AutoItemIds "498,568;498 `r`n 777") -join ',') -ne '498,568,777'){throw 'Mixed ID separators/deduplication failed'}
Write-Output 'PASS: once item editor retains invalid draft on leave/refresh/account switch; comma/newline IDs commit to correct account and native plan.'
$script:accountJobs.Remove($itemKey);$removed=$null;$null=$itemJobs.TryRemove($itemFixture.Id,[ref]$removed);$script:accounts=@($itemSource);Refresh-Grid
Write-Output 'PASS: Auto item tab, per-account ID/interval, native config, no repeated timer reset, untick, account isolation and copy/paste all.'
