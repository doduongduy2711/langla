$chestPage=New-Object Windows.Forms.TabPage;$chestPage.Text='Auto rương tinh thạch';$chestPage.BackColor=$form.BackColor;$chestPage.AutoScroll=$true;$tabs.TabPages.Add($chestPage)
$script:loadingChest=$false
$chestAccount=Ui-Label 'Chọn tài khoản trong bảng.' $chestPage 16 12 630 24
$null=Ui-Label 'Số rương mua / tài khoản' $chestPage 16 48 190 26
$chestQuantity=New-Object Windows.Forms.NumericUpDown;$chestQuantity.Minimum=1;$chestQuantity.Maximum=100000;$chestQuantity.Value=1;$chestQuantity.SetBounds(215,46,140,27);$chestPage.Controls.Add($chestQuantity)
$chestOpen=New-Object Windows.Forms.CheckBox;$chestOpen.Text='Mở rương sau khi mua';$chestOpen.Checked=$true;$chestOpen.SetBounds(16,85,300,27);$chestPage.Controls.Add($chestOpen)
$null=Ui-Label 'Chỉ mua bằng vàng khóa. Đọc giá cửa hàng trước khi mua; chờ rương vào túi rồi mở theo số lượng.' $chestPage 16 120 650 45
$chestBuy=New-Object Windows.Forms.Button;$chestBuy.Text='Mua đã chọn';$chestBuy.SetBounds(16,174,145,29);$chestPage.Controls.Add($chestBuy)
$chestUse=New-Object Windows.Forms.Button;$chestUse.Text='Mở tất cả rương đang có';$chestUse.SetBounds(169,174,215,29);$chestPage.Controls.Add($chestUse)
$chestStop=New-Object Windows.Forms.Button;$chestStop.Text='Dừng đã chọn';$chestStop.SetBounds(392,174,145,29);$chestPage.Controls.Add($chestStop)
$chestHint=Ui-Label '' $chestPage 16 216 650 90;$chestHint.Anchor='Top,Left,Right'
function Get-StoneChestPreference($a){
    $quantity=1;$openAfter=$true
    if($null -ne $a){
        if($a.PSObject.Properties['stoneChestQuantity']){$quantity=[Math]::Max(1,[Math]::Min(100000,[int]$a.stoneChestQuantity))}
        if($a.PSObject.Properties['stoneChestOpenAfter']){$openAfter=[bool]$a.stoneChestOpenAfter}
    }
    [pscustomobject]@{quantity=$quantity;openAfter=$openAfter}
}
function Update-StoneChestUi {
    $script:loadingChest=$true
    try{
        $has=$grid.SelectedRows.Count -gt 0;$chestQuantity.Enabled=$has;$chestOpen.Enabled=$has
        if(!$has){$chestAccount.Text='Chọn tài khoản trong bảng.';$chestHint.Text='';return}
        $a=$grid.SelectedRows[0].Tag;$p=Get-StoneChestPreference $a;$chestAccount.Text='Tài khoản: '+$a.username
        if(!$chestQuantity.Focused){$chestQuantity.Value=$p.quantity};$chestOpen.Checked=$p.openAfter
        $chestHint.Text='Tích Chọn các acc đã mở rồi bấm Mua. Mỗi lần bấm chạy một đợt; không tự mua lại khi mở game.'
        if($script:accountJobs.ContainsKey([string]$a.id)){
            $j=Get-AccountJob $script:accountJobs[[string]$a.id]
            if($null -ne $j -and $j -is [TGLL.NativeJob]){
                $chestHint.Text=$j.ChestStatus+"`r`n"+'Đã mua: '+$j.ChestBought+' • Đã mở: '+$j.ChestUsed+' • Còn chờ mở: '+$j.ChestRemaining
                if($j.ChestUnitPrice -gt 0){$chestHint.Text+="`r`n"+'Giá: '+$j.ChestUnitPrice+' vàng khóa/rương • Tổng đợt mua: '+([long]$j.ChestUnitPrice*$j.ChestQuantity)}
            }
        }
    }finally{$script:loadingChest=$false}
}
function Save-StoneChestUi {
    if($script:loadingChest -or !$grid.SelectedRows.Count){return}
    $a=$grid.SelectedRows[0].Tag;$a | Add-Member NoteProperty stoneChestQuantity ([int]$chestQuantity.Value) -Force;$a | Add-Member NoteProperty stoneChestOpenAfter $chestOpen.Checked -Force
    if(!$UiTest){Save-Accounts}
}
function Start-StoneChestSelected([bool]$useOnly){
    $rows=@(Get-LaunchRows);if(!$rows.Count){$status.Text='Tích Chọn các tài khoản cần mua/mở rương.';return}
    $quantity=[int]$chestQuantity.Value;$openAfter=$chestOpen.Checked;$started=0;$errors=New-Object Collections.Generic.List[string]
    foreach($row in $rows){
        $a=$row.Tag;$a | Add-Member NoteProperty stoneChestQuantity $quantity -Force;$a | Add-Member NoteProperty stoneChestOpenAfter $openAfter -Force
        try{
            if(!$script:accountJobs.ContainsKey([string]$a.id)){throw 'Acc chưa mở.'}
            $j=Get-AccountJob $script:accountJobs[[string]$a.id];if($null -eq $j -or $j -isnot [TGLL.NativeJob]){throw 'Acc chưa sẵn sàng.'}
            [TGLL.NativeBackend]::StartStoneChest($j.Id,$quantity,$openAfter,$useOnly);$started++
        }catch{$errors.Add($a.username+': '+$_.Exception.Message)}
    }
    if(!$UiTest){Save-Accounts};Update-StoneChestUi
    $status.Text='Đã bắt đầu '+$started+' acc.';if($errors.Count){$status.Text+=' '+($errors -join ' | ')}
}
function Stop-StoneChestSelected {
    foreach($row in @(Get-LaunchRows)){$key=[string]$row.Tag.id;if($script:accountJobs.ContainsKey($key)){[TGLL.NativeBackend]::StopStoneChest($script:accountJobs[$key])}}
    Update-StoneChestUi
}
$chestQuantity.Add_ValueChanged({Save-StoneChestUi});$chestOpen.Add_CheckedChanged({Save-StoneChestUi})
$chestBuy.Add_Click({Start-StoneChestSelected $false});$chestUse.Add_Click({Start-StoneChestSelected $true});$chestStop.Add_Click({Stop-StoneChestSelected})
$grid.Add_SelectionChanged({Update-StoneChestUi});Update-StoneChestUi
