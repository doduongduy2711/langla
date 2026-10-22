$distSavedAccounts=$script:accounts;$distSavedRun=$script:distributionRun;$distSavedItems=$distributionItems.Text;$distSavedAuto=$autoLoginCheck.Checked
$distFunctions=@{};foreach($name in @('Get-DistributionJob','Open-DistributionAccount','Prepare-DistributionJob','Request-DistributionInventory','Set-DistributionInbox','Send-DistributionItems','Begin-DistributionClose','Test-DistributionRunning','Test-DistributionSessionLive','Get-DistributionNow')){$distFunctions[$name]=(Get-Item ('Function:\'+$name)).ScriptBlock}
$distSavedQueues=@($script:questBatches,$script:izanamiBatches,$script:dungeonBatches,$script:soncapBatches);$distSavedMail=$script:mailRun;$distSavedCloak=$script:cloakRun
try {
    $script:distClock=[datetime]::UtcNow;$script:distFakeJobs=@{};$script:distOpens=@();$script:distSends=@();$script:distCloses=@()
    function New-DistFake($id){return [pscustomobject]@{Id=($id+'-job');CharacterName=('char-'+$id);GameSettingsReady=$true;CloseRequested=$false;CancelRequested=$false;ReconnectRequested=$false;State='opened';ProcessId=12345;TrainReady=$true;MapId=59;ZoneId=1;ZoneReadAt=[datetime]::UtcNow;MailDone=$false;MailEnabled=$false;MailBusy=$false;MailQuantity=[long]0;MailLetters=0;MailStatus='';InboxEnabled=$false;InboxBusy=$false;InboxPendingAttachments=0;InboxObservedAt=[datetime]::UtcNow;InboxReceived=0;InboxDeleted=0;InboxStatus='';ProcessRunning=$true;Inventory=[pscustomobject]@{Ready=$true;ReadAt=[datetime]::UtcNow;Items=@([pscustomobject]@{TemplateId=277;Quantity=100;Locked=$false;Equipment=$false;Slot=0;BagIndex=0},[pscustomobject]@{TemplateId=434;Quantity=100;Locked=$false;Equipment=$false;Slot=1;BagIndex=1})}}}
    function Test-DistributionSessionLive($j){return ($j.ProcessId -gt 0 -and $j.ProcessRunning)}
    function Get-DistributionNow {$script:distClock=$script:distClock.AddSeconds(1);return $script:distClock}
    function Get-DistributionJob($id){return $script:distFakeJobs[[string]$id]}
    function Open-DistributionAccount($id){$script:distOpens+=@($id);$script:distFakeJobs[$id]=New-DistFake $id}
    function Prepare-DistributionJob($j){}
    function Request-DistributionInventory($j){$j.Inventory.ReadAt=[datetime]::UtcNow}
    function Set-DistributionInbox($j,[bool]$enabled){$j.InboxEnabled=$enabled;$j.InboxObservedAt=[datetime]::UtcNow}
    function Send-DistributionItems($j,$name,$items,$seconds){$script:distSends+=@($name);$j.MailEnabled=$true;$j.MailDone=$false;$j.MailQuantity=0;$j.MailLetters=0}
    function Begin-DistributionClose($m){$script:distCloses+=@($m.id);return [pscustomobject]@{Completed=$false;Failed=0;Error=''}}
    function Test-DistributionRunning($m){return $script:distFakeJobs[$m.id].ProcessRunning}
    function Drive-Dist($state){for($n=0;$n -lt 30;$n++){foreach($job in $script:distFakeJobs.Values){if($job.InboxEnabled){$job.InboxObservedAt=[datetime]::UtcNow}};Sync-Distribution;if($script:distributionRun.members[$script:distributionRun.cursor].state -eq $state){return};if($script:distributionRun.error){throw $script:distributionRun.error}};throw ('Distribution never reached '+$state)}
    $script:questBatches=@();$script:izanamiBatches=@();$script:dungeonBatches=@();$script:soncapBatches=@();$script:mailRun=$null;$script:cloakRun=$null
    $script:accounts=@('dist-source','dist-two','dist-skip','dist-three' | ForEach-Object {[pscustomobject]@{id=$_;username=$_;secret='unused';proxy=$null}});Refresh-Grid;$autoLoginCheck.Checked=$true
    $distributionItems.Text="277,5`r`n434,2"
    $rejected=$false;try{$null=Get-DistributionItems '277,-1'}catch{$rejected=$true};if(!$rejected){throw 'Distribution accepted all-items instead of per-account quantity'}
    foreach($row in $grid.Rows){$row.Cells['pick'].Value=$false}
    foreach($sourceSelected in @($false,$true)){
        $grid.Rows[0].Cells['pick'].Value=$sourceSelected
        $rejected=$false;try{Start-Distribution}catch{$rejected=$true}
        if(!$rejected -or $script:distOpens.Count -ne 0 -or $script:distributionRun){throw 'No selected recipients must reject before opening any game'}
    }
    foreach($row in $grid.Rows){$row.Cells['pick'].Value=([string]$row.Tag.id -in @('dist-two','dist-three'))}
    Start-Distribution;$script:distFakeJobs['dist-source'].State='waiting_popup';for($n=0;$n -lt 3;$n++){Sync-Distribution};if($script:distOpens.Count -ne 1 -or $script:distSends.Count){throw 'Sender login not confirmed before receiver launch/send'};$script:distFakeJobs['dist-source'].State='opened';Drive-Dist 'opening'
    $loginReceiver=$script:distFakeJobs['dist-two'];$loginMember=$script:distributionRun.members[0]
    foreach($phase in @('queued','starting','selecting_character','character_submitted','waiting_map','waiting_popup','confirming_popup','manual_entry')){$loginReceiver.State=$phase;Sync-Distribution;if($script:distSends.Count){throw ('Sent during login phase '+$phase)}}
    $loginReceiver.State='opened';$loginReceiver.TrainReady=$false;Sync-Distribution;if($script:distSends.Count){throw 'Sent before training/world readiness'};$loginReceiver.TrainReady=$true
    $loginReceiver.ProcessRunning=$false;Sync-Distribution;if($script:distSends.Count){throw 'Sent to exited receiver'};$loginReceiver.ProcessRunning=$true
    $loginReceiver.MapId=-1;Sync-Distribution;if($script:distSends.Count){throw 'Sent without confirmed map'};$loginReceiver.MapId=59
    $loginReceiver.ZoneReadAt=[datetime]::UtcNow.AddSeconds(-10);Sync-Distribution;if($script:distSends.Count){throw 'Sent with stale world data'};$loginReceiver.ZoneReadAt=[datetime]::UtcNow
    Sync-Distribution;if($script:distSends.Count -or $loginMember.state -ne 'opening'){throw 'Receiver bypassed stable login confirmation'}
    $loginReceiver.State='waiting_popup';Sync-Distribution;$loginReceiver.State='opened';Sync-Distribution;if($script:distSends.Count -or $loginMember.state -ne 'opening'){throw 'Readiness flap did not reset confirmation'}
    Drive-Dist 'sending'
    if(($script:distributionRun.members.id -join ',') -ne 'dist-two,dist-three'){throw 'Distribution did not snapshot selected recipients in row order'}
    foreach($row in $grid.Rows){$row.Cells['pick'].Value=([string]$row.Tag.id -eq 'dist-skip')}

    if(($script:distOpens -join ',') -ne 'dist-source,dist-two' -or $script:distSends[0] -ne 'char-dist-two'){throw 'Distribution did not use first account / actual recipient character'}
    for($r=0;$r -lt 2;$r++){
        $run=$script:distributionRun;$m=$run.members[$run.cursor];$source=$script:distFakeJobs['dist-source'];$receiver=$script:distFakeJobs[$m.id]
        Sync-Distribution;if($script:distCloses.Count -ne $r){throw 'Unconfirmed send closed receiver'}
        $source.MailDone=$true;$source.MailEnabled=$false;$source.MailQuantity=7;$source.MailLetters=2
        Sync-Distribution;if($m.state -ne 'receiving'){throw 'Send completion did not wait for receipt'}
        $m.sentAt=[datetime]::UtcNow.AddSeconds(-4);$receiver.InboxObservedAt=[datetime]::UtcNow
        $receiver.Inventory.Items[0].Quantity+=5;$receiver.InboxReceived+=1;$receiver.InboxDeleted+=2
        Sync-Distribution;if($m.state -ne 'receiving'){throw 'Missing second item was treated as received'}
        $receiver.Inventory.Items[1].Quantity+=2
        # Đủ đồ cũ trước lượt gửi không phải thành công; snapshot phải mới hơn lúc gửi xong.
        $receiver.InboxEnabled=$false;$receiver.InboxBusy=$true;$receiver.InboxPendingAttachments=9
        $receiver.InboxReceived=0;$receiver.InboxDeleted=0;$receiver.InboxObservedAt=[datetime]::MinValue
        $receiver.Inventory.ReadAt=$m.sentAt.AddSeconds(-1);$m.scanAt=[datetime]::UtcNow;Sync-Distribution
        if($m.state -ne 'receiving'){throw 'Stale inventory allowed receiver close'}
        Request-DistributionInventory $receiver;Sync-Distribution
        if($m.state -ne 'closing' -or $script:distCloses.Count -ne ($r+1)){throw 'Verified receipt did not close receiver exactly once'}
        $m.closeOperation.Completed=$true;Sync-Distribution;if($run.cursor -ne $r){throw 'Live receiver released queue slot'}
        $receiver.ProcessRunning=$false;$receiver.State='closed';Sync-Distribution
        if($run.cursor -ne ($r+1)){throw 'Exited receiver did not release slot'}
        if($r -eq 0){Drive-Dist 'sending'}
    }
    Sync-Distribution;if($script:distributionRun.active -or !$script:distFakeJobs['dist-source'].ProcessRunning -or ($script:distSends -join ',') -ne 'char-dist-two,char-dist-three'){throw 'Distribution completion/order/source preservation failed'}
    if($script:distOpens -contains 'dist-skip'){throw 'Unchecked recipient was opened'}
    foreach($row in $grid.Rows){$row.Cells['pick'].Value=([string]$row.Tag.id -in @('dist-two','dist-three'))}
    Start-Distribution;Drive-Dist 'sending';$script:distFakeJobs['dist-source'].Id='replaced-job';Sync-Distribution
    if($script:distributionRun.members[0].state -ne 'blocked' -or $script:distSends.Count -ne 3){throw 'Session replacement caused an automatic resend'}
    $script:distributionRun=$null;$script:distFakeJobs=@{};Start-Distribution
    $source=New-DistFake 'dist-source';$source.Inventory.Items[0].Locked=$true;$script:distFakeJobs['dist-source']=$source
    Drive-Dist 'stock';Sync-Distribution
    if($script:distributionRun.members[0].state -ne 'blocked' -or $script:distSends.Count -ne 3){throw 'Locked-only inventory was distributed'}
    Write-Output 'PASS: login/world/process/stale snapshot gates, stable confirmation and flap reset before sending; selected recipients only, empty/source-only selection rejected before launch, selection snapshot and row order, fresh inventory delta alone completes despite inbox stopped/busy, stale inventory and missing ID hold, distribution first sender, multiple item IDs/quantities, actual character names, sequential open/send/verified receipt/delete/close, wait for process exit, source kept online, missing-item hold, locked stock rejection and no resend on session replacement. No game launched.'
}finally {
    $script:distributionRun=$distSavedRun;$script:accounts=$distSavedAccounts;$distributionItems.Text=$distSavedItems;$autoLoginCheck.Checked=$distSavedAuto;$script:mailRun=$distSavedMail;$script:cloakRun=$distSavedCloak
    $script:questBatches=$distSavedQueues[0];$script:izanamiBatches=$distSavedQueues[1];$script:dungeonBatches=$distSavedQueues[2];$script:soncapBatches=$distSavedQueues[3]
    foreach($name in $distFunctions.Keys){Set-Item ('Function:\'+$name) $distFunctions[$name]};Refresh-Grid;Update-DistributionUi
}
