$giftSource=$script:accounts[0];$giftKey=[string]$giftSource.id
$giftJobs=[TGLL.NativeBackend].GetField('jobs',[Reflection.BindingFlags]'NonPublic,Static').GetValue($null)
$giftCtor=[TGLL.NativeJob].GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')[0]
$giftFixture=$giftCtor.Invoke(@('C:\synthetic.exe','gift-test','secret',$null,$true));$giftFixture.State='opened'
$null=$giftJobs.TryAdd($giftFixture.Id,$giftFixture);$script:accountJobs[$giftKey]=$giftFixture.Id
$grid.Rows[0].Cells['pick'].Value=$true;Update-GiftUi;$giftCodes.Text=" TEST1 `r`nTEST2`r`nTEST1";Set-GiftSelected $true
if(!$giftSource.giftEnabled -or @($giftSource.giftCodes).Count -ne 2 -or !$giftFixture.GiftEnabled){throw 'Gift tab did not save/apply selected codes'}
Copy-AccountConfig
$giftSecond=[pscustomobject]@{id='gift-second';username='gift-second';secret='unused'};$script:accounts+=@($giftSecond);Refresh-Grid;$grid.ClearSelection();$grid.Rows[1].Selected=$true;Update-GiftUi
if($giftEnabled.Checked -or $giftCodes.Text){throw 'Gift configuration leaked to another account'}
Paste-AccountConfigAll;Update-GiftUi
if(!$giftSecond.giftEnabled -or @($giftSecond.giftCodes).Count -ne 2 -or !$giftEnabled.Checked){throw 'Gift config copy/paste failed'}
$grid.ClearSelection();$grid.Rows[0].Selected=$true;Update-GiftUi;Set-GiftSelected $false
if($giftFixture.GiftEnabled -or $giftSource.giftEnabled){throw 'Gift stop selected failed'}
$script:accountJobs.Remove($giftKey);$removed=$null;$null=$giftJobs.TryRemove($giftFixture.Id,[ref]$removed);$script:accounts=@($giftSource);Refresh-Grid
Write-Output 'PASS: Gift Code tab, multiline/deduplicate, apply selected, stop, account isolation and copy/paste.'
