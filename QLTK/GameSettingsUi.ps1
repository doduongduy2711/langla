$script:gameSettingPages=@();$script:gameSettingPanels=@();$script:gameSettingHeaders=@()
$script:gameSettingControls=@{};$script:gameSettingKey='';$script:loadingGameSettings=$false;$script:gameSettingsApplied=@{}
foreach($title in @('Tự động','Nhặt đồ','Cấu Hình','Auto Game')){
    $page=New-Object Windows.Forms.TabPage;$page.Text=$title;$page.BackColor=$form.BackColor;$tabs.TabPages.Add($page)
    $header=Ui-Label 'Chọn tài khoản rồi mở game để đọc các tùy chọn.' $page 12 10 630 36
    $header.Dock='Top'
    $panel=New-Object Windows.Forms.Panel;$panel.Dock='Fill';$panel.AutoScroll=$true;$page.Controls.Add($panel);$panel.BringToFront()
    $script:gameSettingPages+=@($page);$script:gameSettingHeaders+=@($header);$script:gameSettingPanels+=@($panel)
}
function Save-GameOption($control){
    if($script:loadingGameSettings -or $null -eq $control.Tag){return}
    $tag=$control.Tag;$a=@($script:accounts | Where-Object {[string]$_.id -eq $tag.Account})[0];if($null -eq $a){return}
    $value=$(if($control -is [Windows.Forms.CheckBox]){[int]$control.Checked}else{$control.SelectedIndex});if($value -lt 0){return}
    try {
        if($script:accountJobs.ContainsKey($tag.Account)){
            $j=Get-AccountJob $script:accountJobs[$tag.Account]
            if($null -ne $j -and $j.GameSettingsReady){[TGLL.NativeBackend]::SetGameOption($j.Id,$tag.Code,$value)}
        }
        $overrides=@($a.gameOverrides | Where-Object {$null -ne $_ -and $_.code -ne $tag.Code})
        $overrides+=@([pscustomobject]@{code=$tag.Code;value=$value})
        $a | Add-Member NoteProperty gameOverrides $overrides -Force
        if(!$UiTest){Save-Accounts}
        $status.Text=$a.username+': Đã lưu '+$tag.Label+'; áp dụng khi game sẵn sàng.'
    }catch{$status.Text=$a.username+': '+$_.Exception.Message}
}
function Update-GameSettingsUi([bool]$applyOnly=$false) {
    # Replay only choices explicitly changed in QLTK; preserve other native settings.
    foreach($a in $script:accounts){
        $id=[string]$a.id;if(!$script:accountJobs.ContainsKey($id)){continue}
        $j=Get-AccountJob $script:accountJobs[$id]
        if($null -eq $j -or !$j.PSObject.Properties['GameSettingsReady'] -or !$j.GameSettingsReady -or $script:gameSettingsApplied[$id] -eq $j.Id){continue}
        foreach($setting in @($a.gameOverrides)){
            if($null -eq $setting){continue}
            try{[TGLL.NativeBackend]::SetGameOption($j.Id,[int]$setting.code,[int]$setting.value)}catch{$status.Text=$a.username+': '+$_.Exception.Message}
        }
        $script:gameSettingsApplied[$id]=$j.Id
    }
    if($applyOnly){return}
    $a=$null;$j=$null;$options=@();$key='none';$live=$false
    if($grid.SelectedRows.Count){
        $a=$grid.SelectedRows[0].Tag
        if($null -ne $a){
            $key=[string]$a.id
            if($script:accountJobs.ContainsKey($key)){$j=Get-AccountJob $script:accountJobs[$key]}
            if($null -ne $j -and $j.PSObject.Properties['GameSettingsReady'] -and $j.GameSettingsReady){$options=@($j.GameOptions);$live=$true}
            elseif($a.PSObject.Properties['gameSettings']){$options=@($a.gameSettings)}
        }
    }
    $schema=$key+':'+(($options | ForEach-Object {$_.Code.ToString()+':'+$_.Label+':'+($_.Choices -join '|')}) -join ';')
    $script:loadingGameSettings=$true
    try {
        if($script:gameSettingKey -ne $schema){
            foreach($panel in $script:gameSettingPanels){foreach($c in @($panel.Controls)){$c.Dispose()};$panel.Controls.Clear()}
            $script:gameSettingControls=@{};$counts=@(0,0,0,0)
            foreach($o in $options){
                $t=[int]$o.Tab;if($t -lt 0 -or $t -gt 3){continue};$panel=$script:gameSettingPanels[$t]
                $n=$counts[$t];$counts[$t]++;$x=12+($n%2)*310;$y=12+([int][Math]::Floor($n/2))*58
                if($o.Kind -eq 0){
                    $c=New-Object Windows.Forms.CheckBox;$c.Text=$o.Label;$c.SetBounds($x,$y,295,45)
                    $c.Add_CheckedChanged({Save-GameOption $this})
                }else{
                    $null=Ui-Label $o.Label $panel $x $y 295 20
                    $c=New-Object Windows.Forms.ComboBox;$c.DropDownStyle='DropDownList';$c.SetBounds($x,($y+21),295,25)
                    $c.Items.AddRange([object[]]@($o.Choices));$c.Add_SelectionChangeCommitted({Save-GameOption $this})
                }
                $c.Tag=[pscustomobject]@{Account=$key;Code=[int]$o.Code;Label=[string]$o.Label}
                $panel.Controls.Add($c);$script:gameSettingControls[[string]$o.Code]=$c
            }
            $script:gameSettingKey=$schema
        }
        foreach($o in $options){
            $c=$script:gameSettingControls[[string]$o.Code];if($null -eq $c -or ($c -is [Windows.Forms.ComboBox] -and $c.DroppedDown)){continue}
            $value=[int]$o.Value
            if(!$live){$saved=@($a.gameOverrides | Where-Object {$_.code -eq $o.Code});if($saved.Count){$value=[int]$saved[-1].value}}
            if($c -is [Windows.Forms.CheckBox]){$c.Checked=$value -eq 1}else{if($value -ge 0 -and $value -lt $c.Items.Count){$c.SelectedIndex=$value}}
        }
        foreach($h in $script:gameSettingHeaders){
            $h.Text=$(if($null -eq $a){'Chọn tài khoản trong bảng.'}elseif($live){$a.username+' • Cài đặt trong game; thay đổi tại đây được áp dụng trực tiếp.'}elseif($options.Count){$a.username+' • Cài đặt đã lưu; áp dụng lần mở game kế tiếp.'}else{$a.username+' • Mở game để đọc các tùy chọn của tài khoản.'})
            if($null -ne $j -and $j.PSObject.Properties['SettingsError'] -and $j.SettingsError){$h.Text=$a.username+': '+$j.SettingsError}
        }
        if($live -and $options.Count){
            $saved=@($options | ForEach-Object {[pscustomobject]@{Tab=$_.Tab;Code=$_.Code;Kind=$_.Kind;Label=$_.Label;Choices=@($_.Choices);Value=$_.Value}})
            $a | Add-Member NoteProperty gameSettings $saved -Force
        }
    }finally{$script:loadingGameSettings=$false}
}
$grid.Add_SelectionChanged({if($script:gameSettingPages -contains $tabs.SelectedTab){Update-GameSettingsUi}})
$tabs.Multiline=$true;$tabs.Appearance='Buttons' # Buttons style keeps multiline rows in place when selecting.
Update-GameSettingsUi
