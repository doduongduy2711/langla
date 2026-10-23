# Config clipboard stays in QLTK and contains only per-account automation preferences.
$script:copiedAccountConfig=$null
function Copy-AccountConfig {
    $a=Selected-Account;$id=[string]$a.id;$j=$null;$options=@()
    if($script:accountJobs.ContainsKey($id)){$j=Get-AccountJob $script:accountJobs[$id]}
    if($null -ne $j -and $j.GameSettingsReady){$options=@($j.GameOptions)}
    elseif($a.PSObject.Properties['gameSettings']){$options=@($a.gameSettings)}
    $values=@{};$schema=@()
    foreach($o in $options){
        $values[[string]$o.Code]=[int]$o.Value
        $schema+=@([pscustomobject]@{Tab=[int]$o.Tab;Code=[int]$o.Code;Kind=[int]$o.Kind;Label=[string]$o.Label;Choices=@($o.Choices);Value=[int]$o.Value})
    }
    # Explicit choices win over a live snapshot that has not caught up with the queue.
    foreach($o in @($a.gameOverrides)){if($null -ne $o){$values[[string]$o.code]=[int]$o.value}}
    foreach($o in $schema){$o.Value=$values[[string]$o.Code]}
    $choices=@(Get-AccountAutoSkills $a $j)
    $itemPreference=Get-AutoItemPreference $a
    $chestPreference=Get-StoneChestPreference $a;$cloakPreference=Get-CloakPreference $a;$cloakClose=(!$a.PSObject.Properties['cloakEquipClose']) -or [bool]$a.cloakEquipClose
    Apply-SonCapConfig
    $payload=[pscustomobject]@{beastMaxOnline=[int]$beastMax.Value;beastCloseAfter=$beastCloseAfter.Checked;marketConfig=(Get-MarketPreference $a);izanamiRounds=[int]$izanamiRounds.Value;soncapMaxOnline=[int]$soncapMax.Value;soncapCloseAfter=$soncapCloseAfter.Checked;soncap=[bool]$a.soncap;soncapGroupSize=[int]$soncapGroupSize.Value;soncapPortals=@($script:settings.soncapPortals);cloakMaxOnline=[int]$cloakMax.Value;stoneChestQuantity=$chestPreference.quantity;stoneChestOpenAfter=$chestPreference.openAfter;cloakId=$cloakPreference.id;cloakQuantity=$cloakPreference.quantity;cloakEquipClose=$cloakClose;mailNamedEnabled=$mailNamedCheck.Checked;mailNamedRecipient=$mailNamedRecipient.Text;mailItems=$mailItems.Text;mailSeconds=[int]$mailDelay.Value;launchConcurrency=[int]$launchConcurrency.Value;launchDelaySeconds=[double]$launchDelay.Value;dungeonMaxOnline=[int]$dungeonMax.Value;dungeonCloseAfter=$dungeonCloseAfter.Checked;questMaxOnline=[int]$questMax.Value;questCloseAfter=$questCloseAfter.Checked;izanamiMaxOnline=[int]$izanamiMax.Value;izanamiCloseAfter=$izanamiCloseAfter.Checked;closeAfterKey=[bool]$a.closeAfterKey;autoQuest=[bool]$a.autoQuest;giftEnabled=[bool]$a.giftEnabled;giftCodes=@($a.giftCodes);autoItemEnabled=$itemPreference.enabled;autoItemId=$itemPreference.id;autoItemIds=@($itemPreference.ids);autoItemOnceIds=@($itemPreference.once);autoItemSeconds=$itemPreference.seconds;receiveKey=[bool]$a.receiveKey;dungeonHigh=[bool]$a.dungeonHigh;izanami=[bool]$a.izanami;trainSkills=@($choices);trainPriorityThiQuy=[bool]$a.trainPriorityThiQuy;gameSettings=@($schema);gameOverrides=@($values.Keys | Sort-Object | ForEach-Object {[pscustomobject]@{code=[int]$_;value=$values[$_]}})}
    $script:copiedAccountConfig=$payload | ConvertTo-Json -Depth 12 -Compress
    $pasteConfigButton.Enabled=$true
    $status.Text='Đã copy config của '+$a.username+' • '+$values.Count+' tùy chọn game và cấu hình auto, gồm áo choàng/rương tinh thạch.'
    if(!$schema.Count){$status.Text+=' Mở game nguồn để đọc đủ tùy chọn của 4 tab.'}
}
function Paste-AccountConfigAll {
    if(!$script:copiedAccountConfig){throw 'Bấm Copy config từ tài khoản nguồn trước.'}
    $legacy=$script:copiedAccountConfig | ConvertFrom-Json
    if(!$legacy.PSObject.Properties['autoItemIds'] -and $legacy.PSObject.Properties['autoItemId']){$legacy | Add-Member NoteProperty autoItemIds @([int]$legacy.autoItemId);$script:copiedAccountConfig=$legacy | ConvertTo-Json -Depth 30}
    $backup=@();$prepared=@()
    foreach($a in $script:accounts){
        $old=@{}
        foreach($field in @('closeAfterKey','receiveKey','dungeonHigh','izanami','trainSkills','trainPriorityThiQuy','gameSettings','gameOverrides','autoItemEnabled','autoItemId','autoItemIds','autoItemOnceIds','autoItemSeconds','autoQuest','giftEnabled','giftCodes','stoneChestQuantity','stoneChestOpenAfter','cloakId','cloakQuantity','cloakEquipClose','soncap','marketConfig','marketConfig','marketConfig','marketConfig','marketConfig')){
            $prop=$a.PSObject.Properties[$field];$old[$field]=[pscustomobject]@{exists=($null -ne $prop);value=$(if($null -ne $prop){$prop.Value}else{$null})}
        }
        $backup+=@([pscustomobject]@{account=$a;fields=$old})
        # Decode once per destination to avoid shared arrays and option objects.
        $prepared+=@([pscustomobject]@{account=$a;config=($script:copiedAccountConfig | ConvertFrom-Json)})
    }
    try {
        foreach($item in $prepared){foreach($field in @('closeAfterKey','receiveKey','dungeonHigh','izanami','trainSkills','trainPriorityThiQuy','gameSettings','gameOverrides','autoItemEnabled','autoItemId','autoItemIds','autoItemOnceIds','autoItemSeconds','autoQuest','giftEnabled','giftCodes','stoneChestQuantity','stoneChestOpenAfter','cloakId','cloakQuantity','cloakEquipClose','soncap','marketConfig','marketConfig','marketConfig','marketConfig','marketConfig')){if($item.config.PSObject.Properties[$field]){$item.account | Add-Member NoteProperty $field $item.config.$field -Force}}}
        if(!$UiTest){Save-Accounts}
    }catch{
        foreach($item in $backup){foreach($field in $item.fields.Keys){if($item.fields[$field].exists){$item.account | Add-Member NoteProperty $field $item.fields[$field].value -Force}else{$item.account.PSObject.Properties.Remove($field)}}}
        throw
    }
    $copiedQueue=$script:copiedAccountConfig | ConvertFrom-Json
    if($copiedQueue.PSObject.Properties['beastMaxOnline']){$beastMax.Value=[Math]::Max(6,[Math]::Min(9996,[int]$copiedQueue.beastMaxOnline));$beastCloseAfter.Checked=[bool]$copiedQueue.beastCloseAfter;Save-BeastSettings}
    if($copiedQueue.PSObject.Properties['soncapGroupSize'] -or $copiedQueue.PSObject.Properties['soncapPortals']){
        $script:loadingSonCap=$true
        try{
            $soncapGroupSize.Value=6
            if($copiedQueue.PSObject.Properties['soncapPortals']){
                $portals=@($copiedQueue.soncapPortals)
                if($portals.Count -ne 4){throw 'Cấu hình Sơn Cáp cần đủ 4 cổng.'}
                for($i=0;$i -lt 4;$i++){$soncapRouteGrid.Rows[$i].Cells['x'].Value=[int]$portals[$i].x;$soncapRouteGrid.Rows[$i].Cells['y'].Value=[int]$portals[$i].y}
            }
            Save-SonCapConfig
        }finally{$script:loadingSonCap=$false}
    }
    if($copiedQueue.PSObject.Properties['soncapMaxOnline']){$soncapMax.Value=[Math]::Max(6,[Math]::Min(10000,[int]$copiedQueue.soncapMaxOnline))};if($copiedQueue.PSObject.Properties['soncapCloseAfter']){$soncapCloseAfter.Checked=[bool]$copiedQueue.soncapCloseAfter};Save-SonCapConfig
    if($copiedQueue.PSObject.Properties['cloakMaxOnline']){$cloakMax.Value=[Math]::Max(1,[Math]::Min(10000,[int]$copiedQueue.cloakMaxOnline));Save-CloakQueueSettings}
    if($copiedQueue.PSObject.Properties['mailItems']){$script:mailLoading=$true;try{$mailItems.Text=[string]$copiedQueue.mailItems;if($copiedQueue.PSObject.Properties['mailNamedEnabled']){$mailNamedCheck.Checked=[bool]$copiedQueue.mailNamedEnabled;$mailNamedRecipient.Text=[string]$copiedQueue.mailNamedRecipient};$mailDelay.Value=[Math]::Max(1,[Math]::Min(3600,[int]$copiedQueue.mailSeconds))}finally{$script:mailLoading=$false};Save-MailSettings}
    if($copiedQueue.PSObject.Properties['izanamiRounds']){$izanamiRounds.Value=[Math]::Max(1,[Math]::Min(1000,[int]$copiedQueue.izanamiRounds));Save-IzanamiQueueSettings}
    if($copiedQueue.PSObject.Properties['izanamiMaxOnline']){$izanamiMax.Value=[Math]::Max(6,[Math]::Min(10000,[int]$copiedQueue.izanamiMaxOnline));$izanamiCloseAfter.Checked=[bool]$copiedQueue.izanamiCloseAfter;Save-IzanamiQueueSettings}
    if($copiedQueue.PSObject.Properties['dungeonMaxOnline']){$dungeonMax.Value=[Math]::Max(1,[Math]::Min(10000,[int]$copiedQueue.dungeonMaxOnline));$dungeonCloseAfter.Checked=[bool]$copiedQueue.dungeonCloseAfter;Save-DungeonQueueSettings}
    if($copiedQueue.PSObject.Properties['questMaxOnline']){$questMax.Value=[Math]::Max(1,[Math]::Min(10000,[int]$copiedQueue.questMaxOnline));$questCloseAfter.Checked=[bool]$copiedQueue.questCloseAfter;Save-QuestQueueSettings}
    if($copiedQueue.PSObject.Properties['launchConcurrency']){$launchConcurrency.Value=[Math]::Max(1,[Math]::Min(100,[int]$copiedQueue.launchConcurrency))}
    if($copiedQueue.PSObject.Properties['launchDelaySeconds']){$launchDelay.Value=[Math]::Max([decimal]0,[Math]::Min([decimal]3600,[decimal]$copiedQueue.launchDelaySeconds))}
    $pending=0
    foreach($item in $prepared){
        $a=$item.account;$id=[string]$a.id;$script:trainSelections[$id]=@($a.trainSkills);$script:gameSettingsApplied.Remove($id)
        if(!$script:accountJobs.ContainsKey($id)){continue}
        $j=Get-AccountJob $script:accountJobs[$id]
        if($null -eq $j -or $j.CloseRequested -or $j.State -in @('failed','closed','cancelled')){continue}
        if($j.GameSettingsReady){
            $settingsFailed=$false
            foreach($setting in @($a.gameOverrides)){try{[TGLL.NativeBackend]::SetGameOption($j.Id,[int]$setting.code,[int]$setting.value)}catch{$settingsFailed=$true}}
            $script:gameSettingsApplied[$id]=$j.Id
            if($settingsFailed){$pending++}
        }
        try {
            Sync-TrainPriority $a $j
            [TGLL.NativeBackend]::SetCloseAfterKey($j.Id,[bool]$a.closeAfterKey)
            [TGLL.NativeBackend]::SetReceiveKey($j.Id,[bool]$a.receiveKey)
            [TGLL.NativeBackend]::SetDungeonHigh($j.Id,[bool]$a.dungeonHigh)
            [TGLL.NativeBackend]::SetIzanami($j.Id,[bool]$a.izanami)
            if($j.TrainEnabled){
                [TGLL.NativeBackend]::StopTraining($j.Id)
                if(@($a.trainSkills).Count -and !$j.ReceiveKeyRequested -and !$j.IzanamiRequested -and !$j.ReplayRequested -and $j.State -notin @('waiting_map','waiting_popup','confirming_popup','finding_npc','choosing_key','waiting_dungeon','choosing_dungeon','creating_party','checking_dungeon_keys','finding_onoki','choosing_izanami','waiting_dungeon','choosing_dungeon','creating_party','checking_dungeon_keys','finding_onoki','choosing_izanami')){[TGLL.NativeBackend]::StartTraining($j.Id,[int[]]@($a.trainSkills))}
            }
        }catch{$pending++}
    }
    $script:skillViewKey='';$script:gameSettingKey=''
    Update-TrainUi;Update-DungeonUi;Update-GameSettingsUi;Update-IzanamiUi;Sync-AutoItemPreferences;Update-AutoItemUi;Sync-QuestPreferences;Update-QuestUi;Update-StoneChestUi;Update-CloakUi;Sync-SonCapPreferences;Update-SonCapUi
    $status.Text='Đã paste và lưu config cho '+$prepared.Count+' tài khoản. Game đang mở áp dụng khi sẵn sàng; tài khoản chưa mở áp dụng lần mở tới.'
    if($pending){$status.Text+=' '+$pending+' lượt áp dụng chưa hoàn tất; kiểm tra tùy chọn và chiêu đã học trong game.'}
}
$copyConfigButton=Ui-NewButton 'Copy config' {try{Copy-AccountConfig}catch{Show-Error $_.Exception.Message}}
$pasteConfigButton=Ui-NewButton 'Paste config tất cả' {try{Paste-AccountConfigAll}catch{Show-Error $_.Exception.Message}}
$pasteConfigButton.Enabled=$false
$bottomButtons+=@($copyConfigButton,$pasteConfigButton)
Resize-UiLayout
