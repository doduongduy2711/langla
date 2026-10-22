$itemPage=New-Object Windows.Forms.TabPage;$itemPage.Text='Auto vật phẩm';$itemPage.BackColor=$form.BackColor;$tabs.TabPages.Add($itemPage)
$script:loadingAutoItem=$false;$script:autoItemApplied=@{};$script:autoItemRaw=@{};$script:autoItemDrafts=@{};$script:autoItemUiAccount=$null
$itemAccount=Ui-Label 'Chọn tài khoản trong bảng.' $itemPage 16 15 630 25
$itemEnabled=New-Object Windows.Forms.CheckBox;$itemEnabled.Text='Tự sử dụng vật phẩm theo ID';$itemEnabled.SetBounds(16,50,300,27);$itemPage.Controls.Add($itemEnabled)
$null=Ui-Label 'ID vật phẩm' $itemPage 16 92 130 25
$itemId=New-Object Windows.Forms.TextBox;$itemId.Multiline=$true;$itemId.ScrollBars='Vertical';$itemId.SetBounds(155,90,150,90);$itemPage.Controls.Add($itemId)
$null=Ui-Label 'Chỉ sử dụng 1 lần / phiên' $itemPage 330 62 260 25
$itemOnce=New-Object Windows.Forms.TextBox;$itemOnce.Multiline=$true;$itemOnce.ScrollBars='Vertical';$itemOnce.SetBounds(330,90,180,90);$itemPage.Controls.Add($itemOnce)
$null=Ui-Label 'Lặp mỗi (giây)' $itemPage 16 192 130 25
$itemInterval=New-Object Windows.Forms.NumericUpDown;$itemInterval.Minimum=1;$itemInterval.Maximum=3600;$itemInterval.Value=5;$itemInterval.SetBounds(155,190,150,27);$itemPage.Controls.Add($itemInterval)
$null=Ui-Label 'Lấy ID ở tab Trang bị. Mỗi dòng một ID hoặc cách bằng dấu phẩy. Ô bên phải: dùng 1 lần / phiên.' $itemPage 16 230 630 25
$itemHint=Ui-Label '' $itemPage 16 265 630 65;$itemHint.Anchor='Top,Left,Right'
function Get-AutoItemIds([string]$text){
    $ids=@();foreach($line in ($text -split '[\s,;]+')){if(!$line.Trim()){continue};$n=0;if(![int]::TryParse($line.Trim(),[ref]$n) -or $n -lt 0 -or $n -gt 65535){throw 'ID phải là số từ 0 đến 65535; ngăn cách bằng dòng mới hoặc dấu phẩy.'};if($ids -notcontains $n){$ids+= $n}}
    if(!$ids.Count){throw 'Nhập ít nhất một ID vật phẩm.'};return $ids
}
function Get-AutoItemPreference($a) {
    if($null -eq $a){return [pscustomobject]@{enabled=$false;id=0;ids=@(0);once=@();seconds=5}}
    $id=0;$seconds=5
    if($a.PSObject.Properties['autoItemId']){$id=[int]$a.autoItemId}
    if($a.PSObject.Properties['autoItemSeconds'] -and [int]$a.autoItemSeconds -ge 1){$seconds=[int]$a.autoItemSeconds}
    $ids=@($id);if($a.PSObject.Properties['autoItemIds']){$ids=@($a.autoItemIds | ForEach-Object {[int]$_})};[pscustomobject]@{enabled=[bool]$a.autoItemEnabled;id=$(if($ids.Count){$ids[0]}else{0});ids=$ids;once=@($a.autoItemOnceIds | Where-Object {$null -ne $_} | ForEach-Object {[int]$_});seconds=$seconds}
}
function Sync-AutoItemPreferences {
    foreach($a in $script:accounts){
        $key=[string]$a.id;if(!$script:accountJobs.ContainsKey($key)){continue}
        $j=Get-AccountJob $script:accountJobs[$key];if($null -eq $j -or $j.CloseRequested -or $j.State -in @('failed','closed','cancelled','reconnecting')){continue}
        $raw=$j.Id+':'+[bool]$a.autoItemEnabled+':'+$a.autoItemId+':'+$a.autoItemSeconds+':'+($a.autoItemIds -join ',')+':'+($a.autoItemOnceIds -join ',')
        if($script:autoItemApplied.ContainsKey($key) -and $script:autoItemRaw[$key] -eq $raw){continue}
        $p=Get-AutoItemPreference $a;$signature=$j.Id+':'+$p.enabled+':'+($p.ids -join ',')+':'+($p.once -join ',')+':'+$p.seconds
        if($script:autoItemApplied[$key] -eq $signature){continue}
        try{[TGLL.NativeBackend]::ConfigureAutoItemPlan($j.Id,$p.enabled,[int[]]$p.ids,[int[]]$p.once,($p.seconds*1000));$script:autoItemApplied[$key]=$signature;$script:autoItemRaw[$key]=$raw}catch{$status.Text=$a.username+': Không áp dụng được auto vật phẩm: '+$_.Exception.Message}
    }
}
function Remember-AutoItemDraft {
    if($script:loadingAutoItem -or $null -eq $script:autoItemUiAccount){return}
    $script:autoItemDrafts[[string]$script:autoItemUiAccount.id]=@{ids=$itemId.Text;once=$itemOnce.Text;error=''}
}
function Update-AutoItemUi {
    $script:loadingAutoItem=$true
    try{
        $has=$grid.SelectedRows.Count -gt 0;$itemEnabled.Enabled=$has;$itemId.Enabled=$has;$itemOnce.Enabled=$has;$itemInterval.Enabled=$has
        if(!$has){$script:autoItemUiAccount=$null;$itemAccount.Text='Chọn tài khoản trong bảng.';$itemHint.Text='';return}
        $a=$grid.SelectedRows[0].Tag;$changed=$null -eq $script:autoItemUiAccount -or [string]$script:autoItemUiAccount.id -ne [string]$a.id
        $script:autoItemUiAccount=$a;$p=Get-AutoItemPreference $a;$itemAccount.Text='Tài khoản: '+$a.username
        $draft=$script:autoItemDrafts[[string]$a.id]
        $idText=$p.ids -join "`r`n";$onceText=$p.once -join "`r`n"
        if($null -ne $draft){$idText=$draft.ids;$onceText=$draft.once}
        $itemEnabled.Checked=$p.enabled
        if(($changed -or !$itemId.ContainsFocus) -and $itemId.Text -ne $idText){$itemId.Text=$idText}
        if(($changed -or !$itemOnce.ContainsFocus) -and $itemOnce.Text -ne $onceText){$itemOnce.Text=$onceText}
        $itemInterval.Value=$p.seconds
        $itemHint.Text=$(if($p.enabled){'Đã bật • ID '+($p.ids -join ', ')+' • Mỗi '+$p.seconds+' giây.'}else{'Auto vật phẩm: Tắt'})
        if($script:accountJobs.ContainsKey([string]$a.id)){$j=Get-AccountJob $script:accountJobs[[string]$a.id];if($null -ne $j){$itemHint.Text+="`r`n"+$j.AutoItemStatus+' • Lượt gửi: '+$j.AutoItemRequests}}
        if($null -ne $draft -and $draft.error){$itemHint.Text=$draft.error+' Nội dung được giữ lại, chưa áp dụng.'}
    }finally{$script:loadingAutoItem=$false}
}
function Save-AutoItemUi {
    if($script:loadingAutoItem -or $null -eq $script:autoItemUiAccount){return}
    $a=$script:autoItemUiAccount
    Remember-AutoItemDraft
    try{$ids=@();$once=@();if($itemId.Text.Trim()){$ids=@(Get-AutoItemIds $itemId.Text)};if($itemOnce.Text.Trim()){$once=@(Get-AutoItemIds $itemOnce.Text)};if(!$ids.Count -and !$once.Count){throw 'Nhập ít nhất một ID ở một trong hai ô.'}}catch{
        $script:autoItemDrafts[[string]$a.id].error=$_.Exception.Message
        $itemHint.Text=$_.Exception.Message+' Nội dung được giữ lại, chưa áp dụng.';return
    }
    $a | Add-Member NoteProperty autoItemEnabled $itemEnabled.Checked -Force
    $a | Add-Member NoteProperty autoItemId $(if($ids.Count){[int]$ids[0]}else{0}) -Force
    $a | Add-Member NoteProperty autoItemIds ([int[]]$ids) -Force
    $a | Add-Member NoteProperty autoItemOnceIds ([int[]]$once) -Force
    $a | Add-Member NoteProperty autoItemSeconds ([int]$itemInterval.Value) -Force
    $script:autoItemDrafts.Remove([string]$a.id)
    if(!$UiTest){Save-Accounts};Sync-AutoItemPreferences;Update-AutoItemUi
}
$itemId.Add_TextChanged({Remember-AutoItemDraft});$itemOnce.Add_TextChanged({Remember-AutoItemDraft})
$itemEnabled.Add_CheckedChanged({Save-AutoItemUi});$itemId.Add_Leave({Save-AutoItemUi});$itemOnce.Add_Leave({Save-AutoItemUi});$itemInterval.Add_ValueChanged({Save-AutoItemUi})
$grid.Add_SelectionChanged({Update-AutoItemUi});Update-AutoItemUi
