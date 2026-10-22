# Real UI handlers with synthetic jobs; no game, stored accounts or credentials are used.
if($copyConfigButton.Text -ne 'Copy config' -or $pasteConfigButton.Text -ne 'Paste config tất cả' -or $pasteConfigButton.Enabled){throw ('Config buttons initial state failed: copy='+$copyConfigButton.Text+' paste='+$pasteConfigButton.Text+' enabled='+$pasteConfigButton.Enabled)}
$source=$script:accounts[0]
$source | Add-Member NoteProperty receiveKey $true -Force
$script:trainSelections['test']=@(295,368)
$fake.State='opened';$fake.ReceiveKeyEnabled=$false;$fake.ReceiveKeyRequested=$false;$fake.TrainEnabled=$false
$null=$jobs.TryAdd($fake.Id,$fake);$script:accountJobs['test']=$fake.Id
$second=[pscustomobject]@{id='cfg-open';username='open-demo';secret='secret-kept';proxy='proxy-kept';receiveKey=$false;trainSkills=@(1)}
$third=[pscustomobject]@{id='cfg-closed';username='closed-demo';secret='closed-secret';proxy='closed-proxy';receiveKey=$false;trainSkills=@()}
$script:accounts+=@($second,$third);Refresh-Grid;$grid.ClearSelection();$grid.Rows[0].Selected=$true
$other=$ctor.Invoke([object[]]@([string](Join-Path $PSScriptRoot 'QLTK.exe'),[string]'synthetic2',[string]'unused',$null,$true))
$other.State='opened';$other.ReceiveKeyEnabled=$false;$other.GameSettingsReady=$true;$other.TrainReady=$true;$other.Skills=$fake.Skills;$other.GameOptions=$fake.GameOptions
$null=$jobs.TryAdd($other.Id,$other);$script:accountJobs['cfg-open']=$other.Id
$clickMethod=[Windows.Forms.Control].GetMethod('OnClick',[Reflection.BindingFlags]'Instance,NonPublic');$null=$clickMethod.Invoke($copyConfigButton,[object[]]@([EventArgs]::Empty))
if(!$pasteConfigButton.Enabled -or !$script:copiedAccountConfig){throw 'Copy config button failed'}
$snapshot=$script:copiedAccountConfig | ConvertFrom-Json
if(@($snapshot.gameOverrides).Count -ne 4 -or @($snapshot.gameSettings).Count -ne 4){throw 'Copy must include unchanged native defaults in all four tabs'}
if(@($snapshot.gameOverrides | Where-Object {$_.code -eq 201 -and $_.value -eq 1}).Count -ne 1){throw 'Copy did not preserve pending checkbox choice'}
if($script:copiedAccountConfig -match 'secret|username|proxy|job'){throw 'Config snapshot contains unrelated account data'}
# Editing the source after copying must not mutate the copied snapshot.
$source.receiveKey=$false;$script:trainSelections['test']=@(1)
$source.gameOverrides[0].value=0
$queueBefore=$queueField.GetValue($other).Count
$null=$clickMethod.Invoke($pasteConfigButton,[object[]]@([EventArgs]::Empty))
foreach($a in $script:accounts){
    if(!$a.receiveKey -or (@($a.trainSkills) -join ',') -ne '295,368' -or @($a.gameOverrides).Count -ne 4){throw 'Paste config all did not replace automation preferences'}
}
if($second.secret -ne 'secret-kept' -or $second.proxy -ne 'proxy-kept' -or $third.secret -ne 'closed-secret' -or $third.proxy -ne 'closed-proxy'){throw 'Paste overwrote credentials or proxy'}
if(!$other.ReceiveKeyRequested -or $queueField.GetValue($other).Count -ne $queueBefore+4){throw 'Live account did not queue copied settings and key request'}
if($other.GameAutoWanted -or $other.TrainEnabled){throw 'Paste unexpectedly enabled runtime Auto or train'}
if(($script:trainSelections['cfg-closed'] -join ',') -ne '295,368'){throw 'Closed account did not retain skill preferences'}
$second.gameOverrides[0].value=999;$second.gameSettings[0].Label='changed';$second.trainSkills[0]=1
if($third.gameOverrides[0].value -eq 999 -or $third.gameSettings[0].Label -eq 'changed' -or $third.trainSkills[0] -eq 1){throw 'Destination config objects share references'}
if($script:copiedAccountConfig -ne ($snapshot | ConvertTo-Json -Depth 12 -Compress)){throw 'Copied snapshot changed after paste'}
# Applying the same receiveKey config twice must not enqueue another key request.
$other.ReceiveKeyRequested=$false;Paste-AccountConfigAll
if($other.ReceiveKeyRequested){throw 'Repeated paste duplicated key request'}
foreach($b in @($copyConfigButton,$pasteConfigButton)){if($b.Bounds.Right -gt $bottom.Width -or $b.Bounds.Bottom -gt $bottom.Height){throw 'Config button outside layout'}}
$removed=$null;$null=$jobs.TryRemove($fake.Id,[ref]$removed);$null=$jobs.TryRemove($other.Id,[ref]$removed);$script:accountJobs.Clear()
$script:accounts=@($source);Refresh-Grid
Write-Output 'PASS: config button handlers, full native defaults + pending choices, snapshot isolation, all-account paste, open/closed accounts, credential/proxy preservation, live queue and no duplicate receive-key request.'
