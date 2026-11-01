# Hiding a page changes navigation only; its controls and running activities stay alive.
$script:managerTabPages=@($tabs.TabPages | ForEach-Object {$_})
$script:managerTabItems=@{}
$script:tabVisibilityPath=Join-Path $script:dataDir 'tab-visibility.json'
$script:tabMenuUpdating=$false
function Save-ManagerTabVisibility {
    if($UiTest){return}
    $hidden=@($script:managerTabPages | Where-Object {!$tabs.TabPages.Contains($_)} | ForEach-Object {$_.Text})
    Write-JsonAtomic $script:tabVisibilityPath @{hidden=$hidden}
}
function Set-ManagerTabVisible([Windows.Forms.TabPage]$page,[bool]$visible,[bool]$save=$true) {
    $tabs.SuspendLayout()
    try {
        if(!$visible -and $tabs.TabPages.Contains($page)){$tabs.TabPages.Remove($page)}
        elseif($visible -and !$tabs.TabPages.Contains($page)){
            $index=0
            foreach($other in $script:managerTabPages){if($other -eq $page){break};if($tabs.TabPages.Contains($other)){$index++}}
            $tabs.TabPages.Insert($index,$page)
        }
        $script:tabMenuUpdating=$true
        try{$script:managerTabItems[$page.Text].Checked=$visible}finally{$script:tabMenuUpdating=$false}
    }finally{$tabs.ResumeLayout()}
    if($save){Save-ManagerTabVisibility}
}
$tabMenu=New-Object Windows.Forms.ContextMenuStrip
$tabMenu.AutoClose=$true
$tabMenu.Add_Closing({param($sender,$eventArgs)if($eventArgs.CloseReason -eq [Windows.Forms.ToolStripDropDownCloseReason]::ItemClicked){$eventArgs.Cancel=$true}})
$tabMenu.ShowCheckMargin=$true
$tabMenu.ShowImageMargin=$false
foreach($page in $script:managerTabPages){
    $entry=New-Object Windows.Forms.ToolStripMenuItem
    $entry.Text=$page.Text;$entry.Tag=$page;$entry.CheckOnClick=$true;$entry.Checked=$true
    $entry.Add_CheckedChanged({param($sender,$eventArgs)
        if($script:tabMenuUpdating){return}
        try{Set-ManagerTabVisible $sender.Tag $sender.Checked}
        catch{$message=$_.Exception.Message;Set-ManagerTabVisible $sender.Tag (!$sender.Checked) $false;Show-Error ('Không lưu được lựa chọn tab: '+$message)}
    })
    $script:managerTabItems[$page.Text]=$entry
    $null=$tabMenu.Items.Add($entry)
}
$tabMenu.Items.Add((New-Object Windows.Forms.ToolStripSeparator)) | Out-Null
$closeTabMenu=New-Object Windows.Forms.ToolStripMenuItem
$closeTabMenu.Text='Đóng menu';$closeTabMenu.Add_Click({$tabMenu.Close()});$null=$tabMenu.Items.Add($closeTabMenu)
$tabMenuButton=Ui-NewButton 'Menu tab' {$tabMenu.Show($tabMenuButton,(New-Object Drawing.Point(0,0)),[Windows.Forms.ToolStripDropDownDirection]::AboveLeft)}
$form.Controls.Add($tabMenuButton)
$tabMenuButton.Anchor='Bottom,Right'
$tabMenuButton.BringToFront()
if(!$UiTest -and (Test-Path -LiteralPath $script:tabVisibilityPath)){
    try{
        $preferences=Read-JsonData $script:tabVisibilityPath
        foreach($page in $script:managerTabPages){if(@($preferences.hidden) -contains $page.Text){Set-ManagerTabVisible $page $false $false}}
    }catch{$status.Text='Không đọc được lựa chọn tab; đang hiển thị các tab có sẵn.'}
}
$form.Add_FormClosed({$tabMenu.Dispose();foreach($page in $script:managerTabPages){if(!$page.IsDisposed){$page.Dispose()}}})
Resize-UiLayout
