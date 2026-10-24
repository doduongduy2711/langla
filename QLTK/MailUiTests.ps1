$mailOriginalAccounts=$script:accounts;$mailOriginalJobs=@{};foreach($key in $script:accountJobs.Keys){$mailOriginalJobs[$key]=$script:accountJobs[$key]}
$mailSavedOpen=(Get-Item Function:\Open-Account).ScriptBlock
$mailFunctions=@{};foreach($n in @('Start-MailMember','Begin-MailMemberClose','Test-MailMemberRunning','Test-MailReceiverReady')){$mailFunctions[$n]=(Get-Item ('Function:\'+$n)).ScriptBlock}
$mailJobs=[TGLL.NativeBackend].GetField('jobs',[Reflection.BindingFlags]'NonPublic,Static').GetValue($null)
$mailCtor=[TGLL.NativeJob].GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')[0];$mailFixtures=@();$script:mailTestOpen=@();$script:mailTestClose=@();$script:mailReceiverOnline=$true;$script:mailRunning=$true;$script:mailCloseOp=$null
try {
    $mailTestReceiver=$mailCtor.Invoke(@('C:\synthetic.exe','receiver-login','unused',$null,$true));$mailTestReceiver.CharacterName='ReceiverOne';$mailTestReceiver.GameSettingsReady=$true;$mailTestReceiver.State='opened'
    $mailTestSender=$mailCtor.Invoke(@('C:\synthetic.exe','sender-login','unused',$null,$true));$mailTestSender.CharacterName='SenderTwo';$mailTestSender.GameSettingsReady=$true;$mailTestSender.State='opened'
    $mailLater=$mailCtor.Invoke(@('C:\synthetic.exe','later','unused',$null,$true));$mailLater.CharacterName='SenderThree';$mailLater.GameSettingsReady=$true;$mailLater.State='opened'
    $mailFixtures=@($mailTestReceiver,$mailTestSender,$mailLater);foreach($j in $mailFixtures){$null=$mailJobs.TryAdd($j.Id,$j)}
    $script:accounts=@([pscustomobject]@{id='mail-one';username='login-one';secret='unused'},[pscustomobject]@{id='mail-two';username='login-two';secret='unused'},[pscustomobject]@{id='mail-three';username='login-three';secret='unused'})
    $script:accountJobs.Clear();$script:accountJobs['mail-one']=$mailTestReceiver.Id;$script:accountJobs['mail-two']=$mailTestSender.Id;$script:accountJobs['mail-three']=$mailLater.Id;Refresh-Grid
    function Start-MailMember($m){$script:mailTestOpen+=@($m.id);$m.job=$script:accountJobs[$m.id]}
    function Begin-MailMemberClose($m){$script:mailTestClose+=@($m.id);$script:mailCloseOp=[pscustomobject]@{Completed=$false;Failed=0;Error=''};return $script:mailCloseOp}
    function Test-MailMemberRunning($m){return $script:mailRunning}
    function Test-MailReceiverReady {return $script:mailReceiverOnline}
    $parsed=@(Get-MailItems "434,-1`r`n168,3");if($parsed.Count -ne 2 -or $parsed[0].quantity -ne -1){throw 'Multi-ID parsing failed'}
    foreach($invalid in @('434,0','434,-2','65536,-1',"434,-1`n434,5",'garbage')){$rejected=$false;try{$null=Get-MailItems $invalid}catch{$rejected=$true};if(!$rejected){throw 'Invalid mail input accepted'}}
    $autoLoginCheck.Checked=$true;$mailItems.Text="434,-1`r`n168,3";$mailDelay.Value=7
    # Like distribution: empty selection and receiver-only selection cannot send anything.
    foreach($row in $grid.Rows){$row.Cells['pick'].Value=$false}
    $rejected=$false;try{Start-MailCollection}catch{$rejected=$true};if(!$rejected -or $script:mailTestOpen.Count){throw 'Default collection accepted empty selection'}
    $grid.Rows[0].Cells['pick'].Value=$true
    $rejected=$false;try{Start-MailCollection}catch{$rejected=$true};if(!$rejected -or $script:mailTestOpen.Count){throw 'Receiver-only selection became a sender'}
    # Non-contiguous selection skips row 2 and keeps the first account as recipient even if unchecked.
    $grid.Rows[0].Cells['pick'].Value=$false;$grid.Rows[2].Cells['pick'].Value=$true;$script:mailReceiverOnline=$false
    Start-MailCollection
    if($script:mailRun.senders.Count -ne 1 -or $script:mailRun.senders[0].id -ne 'mail-three' -or $script:mailRun.receiverId -ne 'mail-one' -or $script:mailTestOpen.Count){throw 'Default collection included unchecked sender or changed recipient'}
    $grid.Rows[1].Cells['pick'].Value=$true;Sync-MailCollection
    if($script:mailRun.senders.Count -ne 1){throw 'Changing selection modified active default collection'}
    Stop-MailCollection;$script:mailRun=$null;$script:mailReceiverOnline=$true
    foreach($row in $grid.Rows){$row.Cells['pick'].Value=$true}
    Start-MailCollection;Sync-MailCollection
    if($script:mailTestOpen.Count -ne 1 -or !$mailTestSender.MailEnabled -or $mailLater.MailEnabled -or $mailTestReceiver.MailEnabled){throw 'Queue must configure only first sender, never receiver'}
    if($mailStart.Enabled -or $mailItems.Enabled){throw 'Queue input not frozen'}
    if($null -eq (Get-MailQueuedMember 'mail-three') -or $null -ne (Get-MailQueuedMember 'mail-two')){throw 'Waiting membership incorrect'}
    $mailIndex=[TGLL.NativeJob].GetField('MailIndex',[Reflection.BindingFlags]'NonPublic,Instance');$mailIndex.SetValue($mailTestSender,1);Sync-MailCollection;if($mailIndex.GetValue($mailTestSender) -ne 1){throw 'Polling rearmed mail'}
    $script:accounts=@($script:accounts[2],$script:accounts[0],$script:accounts[1]);Refresh-Grid;Sync-MailCollection;if($script:mailRun.recipient -ne 'ReceiverOne' -or $script:mailRun.receiverId -ne 'mail-one'){throw 'Sorting changed receiver'}
    $mailTestSender.MailDone=$true;$mailTestSender.MailBusy=$true;Sync-MailCollection;if($script:mailTestClose.Count){throw 'Closed while mail still busy'}
    $mailTestSender.MailBusy=$false;$mailTestSender.MailEnabled=$false;Sync-MailCollection;Sync-MailCollection;if($script:mailTestClose.Count -ne 1 -or $script:mailTestOpen.Count -ne 1){throw 'Close submitted twice or next opened early'}
    $script:mailCloseOp.Completed=$true;Sync-MailCollection;if($script:mailTestOpen.Count -ne 1 -or $script:mailRun.senders[0].state -ne 'closing'){throw 'Must wait for process exit'}
    $script:mailRunning=$false;Sync-MailCollection;Sync-MailCollection;Sync-MailCollection;if($script:mailTestOpen.Count -ne 2 -or !$mailLater.MailEnabled){throw 'Next sender not started after verified exit'}
    $mailLater.MailDone=$true;$mailLater.MailEnabled=$false;Sync-MailCollection;$script:mailCloseOp.Completed=$true;Sync-MailCollection;Sync-MailCollection;Update-MailUi
    if($script:mailRun.active -or $script:mailTestClose.Count -ne 2 -or $mailTestReceiver.CloseRequested){throw 'Final queue completion / receiver preservation failed'}
    $mailTestSender.MailDone=$false;$mailTestSender.MailEnabled=$false;$mailLater.MailDone=$false;$mailLater.MailEnabled=$false;$script:mailTestOpen=@();$script:mailTestClose=@();$script:mailReceiverOnline=$false;$script:accounts=@($script:accounts[1],$script:accounts[2],$script:accounts[0]);Refresh-Grid;foreach($row in $grid.Rows){$row.Cells['pick'].Value=($row.Index -gt 0)};Start-MailCollection;Sync-MailCollection
    if($script:mailTestOpen.Count){throw 'Opened sender while receiver offline'}
    $script:mailReceiverOnline=$true;Sync-MailCollection;Sync-MailCollection;$mailTestSender.MailEnabled=$false;$mailTestSender.MailStatus='Dừng: server chưa xác nhận';Sync-MailCollection
    if($script:mailRun.senders[0].state -ne 'blocked' -or $script:mailTestClose.Count -or $script:mailTestOpen.Count -ne 1){throw 'Unknown send result must hold queue without close or advance'}
    Stop-MailCollection;Sync-MailCollection;if($script:mailRun.active -or $script:mailTestOpen.Count -ne 1 -or !$mailItems.Enabled){throw 'Stop starts waiting account'}
    Copy-AccountConfig;$copy=$script:copiedAccountConfig | ConvertFrom-Json;if($copy.mailItems -ne $mailItems.Text -or $copy.mailSeconds -ne 7){throw 'Config copy missing mail settings'}
    # Kiểm tra hàm mở thật với bộ mở giả: bắt buộc đúng marker và đăng nhập trực tiếp.
    function Open-Account($a,[bool]$reconnect=$false){if(!$reconnect -or $script:mailLaunchingAccountId -ne [string]$a.id){throw 'Mail launch did not set dedicated marker or force login'}}
    $launchFixture=[pscustomobject]@{id='mail-two';job=''};& $mailFunctions['Start-MailMember'] $launchFixture
    if($launchFixture.job -ne $mailTestSender.Id -or !$mailTestSender.MailQueueOnly -or $script:mailLaunchingAccountId){throw 'Actual mail launcher lost job / mode / leaked marker'}
    Set-Item Function:\Open-Account $mailSavedOpen
    # Gửi tới tên chỉ định: không phụ thuộc acc nhận trong QLTK, chỉ gửi acc tích Chọn.
    $script:accounts=@([pscustomobject]@{id='mail-one';username='login-one';secret='unused'},[pscustomobject]@{id='mail-two';username='login-two';secret='unused'},[pscustomobject]@{id='mail-three';username='login-three';secret='unused'});Refresh-Grid
    foreach($row in $grid.Rows){$row.Cells['pick'].Value=$false}
    $mailNamedCheck.Checked=$true;$mailNamedRecipient.Text='ExternalReceiver';$script:mailTestOpen=@();$script:mailTestClose=@();$script:mailReceiverOnline=$true
    $rejected=$false;try{Start-MailCollection}catch{$rejected=$true};if(!$rejected -or $script:mailTestOpen.Count){throw 'Named send accepted empty selection'}
    $grid.Rows[0].Cells['pick'].Value=$true;$grid.Rows[2].Cells['pick'].Value=$true
    $mailNamedRecipient.Text=' ';$rejected=$false;try{Start-MailCollection}catch{$rejected=$true};if(!$rejected -or $script:mailTestOpen.Count){throw 'Named send accepted blank recipient'}
    $mailNamedRecipient.Text=' ExternalReceiver ';Start-MailCollection;Sync-MailCollection
    if(!$script:mailRun.namedRecipient -or $script:mailRun.receiverId -or $script:mailRun.recipient -ne 'ExternalReceiver' -or $script:mailRun.senders.Count -ne 2 -or $script:mailRun.senders[0].id -ne 'mail-one' -or $script:mailRun.senders[1].id -ne 'mail-three'){throw 'Named receiver / selected sender snapshot incorrect'}
    if($mailTestReceiver.MailRecipient -ne 'ExternalReceiver' -or !$mailTestReceiver.MailEnabled -or $mailNamedCheck.Enabled -or $mailNamedRecipient.Enabled -or $script:inboxWanted){throw 'Named sender config/frozen inputs/local inbox failed'}
    if(!(& $mailFunctions['Test-MailReceiverReady'])){throw 'External receiver incorrectly requires local account online'}
    $grid.Rows[1].Cells['pick'].Value=$true;Sync-MailCollection;if($script:mailRun.senders.Count -ne 2){throw 'Selection change changed running named queue'}
    Copy-AccountConfig;$namedCopy=$script:copiedAccountConfig | ConvertFrom-Json;if(!$namedCopy.mailNamedEnabled -or $namedCopy.mailNamedRecipient.Trim() -ne 'ExternalReceiver'){throw 'Named mail config copy missing'}
    Stop-MailCollection;$mailNamedCheck.Checked=$false;$mailNamedRecipient.Text='';$script:mailRun=$null;Update-MailUi
    # Không giới hạn danh sách 100 acc; chỉ mở một lượt, không submit cả danh sách.
    $script:accounts=@([pscustomobject]@{id='mail-one';username='login-one';secret='unused'});for($i=2;$i -le 153;$i++){$script:accounts+=@([pscustomobject]@{id=('bulk-'+$i);username=('sender-'+$i);secret='unused'})};Refresh-Grid;foreach($row in $grid.Rows){$row.Cells['pick'].Value=($row.Index -gt 0)};$script:mailReceiverOnline=$false;Start-MailCollection
    if($script:mailRun.senders.Count -ne 152){throw 'Large queue truncated'};Stop-MailCollection
    $mailTestSender.MailQueueOnly=$true;[TGLL.NativeBackend]::SetReceiveKey($mailTestSender.Id,$true);[TGLL.NativeBackend]::SetDungeonHigh($mailTestSender.Id,$true);[TGLL.NativeBackend]::SetIzanami($mailTestSender.Id,$true);[TGLL.NativeBackend]::SetAutoQuest($mailTestSender.Id,$true);[TGLL.NativeBackend]::SetCloseAfterKey($mailTestSender.Id,$true)
    if($mailTestSender.ReceiveKeyEnabled -or $mailTestSender.DungeonHighEnabled -or $mailTestSender.IzanamiEnabled -or $mailTestSender.QuestEnabled -or $mailTestSender.CloseAfterKey){throw 'Saved automation conflicts with mail-only session'}
    Write-Output 'PASS: selected-only default collection, empty/receiver-only rejection, non-contiguous selection and immutable sender snapshot; sequential mail queue, 152 selected senders, all-ID completion, close once, verified process exit before next launch, receiver retained/offline wait, sorting, uncertain transfer hold, Stop, config copy and mail-only automation guards.'
} finally {
    Stop-MailCollection;$mailNamedCheck.Checked=$false;$script:mailRun=$null;Set-Item Function:\Open-Account $mailSavedOpen;foreach($n in $mailFunctions.Keys){Set-Item ('Function:\'+$n) $mailFunctions[$n]}
    foreach($j in $mailFixtures){$removed=$null;$null=$mailJobs.TryRemove($j.Id,[ref]$removed)}
    $script:accounts=$mailOriginalAccounts;$script:accountJobs.Clear();foreach($key in $mailOriginalJobs.Keys){$script:accountJobs[$key]=$mailOriginalJobs[$key]};Refresh-Grid;Update-MailUi
}
