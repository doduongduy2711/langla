function ConvertFrom-BulkAccountText([string]$text) {
    $items=New-Object 'System.Collections.Generic.List[object]';$seen=@{};$lineNumber=0
    foreach($line in ($text -split '\r\n|\n|\r')){
        $lineNumber++;if([string]::IsNullOrWhiteSpace($line)){continue}
        if($items.Count -ge 10000){throw 'Mỗi lần nhập tối đa 10000 tài khoản.'}
        $parts=$line.Split([char[]]',',3)
        if($parts.Count -lt 2 -or [string]::IsNullOrWhiteSpace($parts[0]) -or !$parts[1].Length){throw "Dòng ${lineNumber}: cần tk,mk,proxy, không để trống tài khoản hoặc mật khẩu."}
        $name=$parts[0].Trim()
        if($seen.ContainsKey($name)){throw "Dòng ${lineNumber}: tài khoản bị trùng trong danh sách nhập."};$seen[$name]=$true
        $item=[pscustomobject]@{username=$name;password=$parts[1]}
        if($parts.Count -eq 3){
            $proxyText=$parts[2].Trim();$proxy=$null
            if($proxyText.Length){try{$proxy=Normalize-Proxy $proxyText}catch{throw "Dòng ${lineNumber}: proxy không hợp lệ. Dùng host:port hoặc host:port:user:pass."}}
            $item | Add-Member NoteProperty proxy $proxy
        }
        $items.Add($item)
    }
    if(!$items.Count){throw 'Nhập ít nhất một tài khoản.'}
    return $items.ToArray()
}
function ConvertFrom-BulkSplitText([string]$accountText,[string]$proxyText) {
    $items=New-Object 'System.Collections.Generic.List[object]';$seen=@{};$lineNumber=0
    foreach($line in ($accountText -split '\r\n|\n|\r')){
        $lineNumber++;if([string]::IsNullOrWhiteSpace($line)){continue}
        $colon=$line.IndexOf(':')
        if($colon -le 0 -or $colon -eq $line.Length-1){throw "Tài khoản dòng ${lineNumber}: cần user:pass, không để trống user hoặc pass."}
        $name=$line.Substring(0,$colon).Trim();$pass=$line.Substring($colon+1)
        if(!$name.Length -or $seen.ContainsKey($name)){throw "Tài khoản dòng ${lineNumber}: tên trống hoặc bị trùng."}
        if($items.Count -ge 10000){throw 'Mỗi lần nhập tối đa 10000 tài khoản.'}
        $seen[$name]=$true;$items.Add([pscustomobject]@{username=$name;password=$pass;proxy=$null})
    }
    if(!$items.Count){throw 'Nhập ít nhất một tài khoản user:pass.'}
    $pool=New-Object 'System.Collections.Generic.List[object]';$proxyKeys=@{};$lineNumber=0
    foreach($line in ($proxyText -split '\r\n|\n|\r')){
        $lineNumber++;if([string]::IsNullOrWhiteSpace($line)){continue}
        try{$p=Normalize-Proxy $line.Trim()}catch{throw "Proxy dòng ${lineNumber}: dùng host:port hoặc host:port:user:pass hợp lệ."}
        $key=$p.host.ToLowerInvariant()+':'+$p.port
        if($proxyKeys.ContainsKey($key)){continue}
        $entry=[pscustomobject]@{key=$key;proxy=$p;count=0};$proxyKeys[$key]=$entry;$pool.Add($entry)
    }
    if(!$pool.Count){return $items.ToArray()}
    # Imported existing accounts are reassigned, so release their current slots in this plan.
    foreach($a in $script:accounts){
        if($seen.ContainsKey([string]$a.username) -or $null -eq $a.proxy){continue}
        $key=([string]$a.proxy.host).ToLowerInvariant()+':'+$a.proxy.port
        if($proxyKeys.ContainsKey($key)){$proxyKeys[$key].count++}
    }
    foreach($item in $items){
        $minimum=[int]::MaxValue;foreach($p in $pool){if($p.count -lt $minimum){$minimum=$p.count}}
        $candidates=@($pool | Where-Object {$_.count -eq $minimum})
        $chosen=$candidates[(Get-Random -Minimum 0 -Maximum $candidates.Count)]
        $item.proxy=$chosen.proxy;$chosen.count++
    }
    return $items.ToArray()
}

function Save-BulkAccountDialog($button) {
    $dialog=$button.FindForm();$state=$dialog.Tag
    try {
        $items=@(ConvertFrom-BulkSplitText $state.Input.Text $state.Proxies.Text)
        $plan=Prepare-AccountBatch $items
        Commit-AccountBatch $plan
        $status.Text='Đã thêm/cập nhật '+$plan.changed.Count+' tài khoản.'
        $state.Input.Clear();$state.Proxies.Clear();$dialog.DialogResult=[Windows.Forms.DialogResult]::OK;$dialog.Close()
    }catch{$state.Note.ForeColor=[Drawing.Color]::DarkRed;$state.Note.Text=$_.Exception.Message}
}
function New-BulkAccountDialog {
    $dialog=New-Object Windows.Forms.Form;$dialog.Text='Thêm nhiều tài khoản';$dialog.ClientSize=New-Object Drawing.Size(820,500);$dialog.MinimumSize=New-Object Drawing.Size(660,420);$dialog.StartPosition='CenterParent'
    $layout=New-Object Windows.Forms.TableLayoutPanel;$layout.SetBounds(12,12,796,350);$layout.Anchor='Top,Bottom,Left,Right';$layout.ColumnCount=2;$layout.RowCount=2
    $null=$layout.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::Percent,50)));$null=$layout.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::Percent,50)))
    $null=$layout.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::Absolute,25)));$null=$layout.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::Percent,100)));$dialog.Controls.Add($layout)
    $accountLabel=New-Object Windows.Forms.Label;$accountLabel.Text='Tài khoản — mỗi dòng user:pass';$accountLabel.Dock='Fill';$layout.Controls.Add($accountLabel,0,0)
    $proxyLabel=New-Object Windows.Forms.Label;$proxyLabel.Text='Proxy — mỗi dòng host:port[:user:pass]';$proxyLabel.Dock='Fill';$layout.Controls.Add($proxyLabel,1,0)
    $inputBox=New-Object Windows.Forms.TextBox;$inputBox.Multiline=$true;$inputBox.AcceptsReturn=$true;$inputBox.ScrollBars='Both';$inputBox.WordWrap=$false;$inputBox.MaxLength=8388608;$inputBox.Dock='Fill';$layout.Controls.Add($inputBox,0,1)
    $proxyBox=New-Object Windows.Forms.TextBox;$proxyBox.Multiline=$true;$proxyBox.AcceptsReturn=$true;$proxyBox.ScrollBars='Both';$proxyBox.WordWrap=$false;$proxyBox.MaxLength=8388608;$proxyBox.Dock='Fill';$layout.Controls.Add($proxyBox,1,1)
    $hint=New-Object Windows.Forms.Label;$hint.Text="Chia proxy ngẫu nhiên, cân đều theo tổng số acc đã gán; không giới hạn acc/proxy.`r`nProxy trùng chỉ tính một lần. Tự chia hết tài khoản cho danh sách proxy.`r`nBỏ trống ô proxy: không dùng proxy. TK trùng: cập nhật và giữ cấu hình auto.";$hint.SetBounds(12,370,796,66);$hint.Anchor='Bottom,Left,Right';$dialog.Controls.Add($hint)
    $note=New-Object Windows.Forms.Label;$note.SetBounds(12,437,796,24);$note.Anchor='Bottom,Left,Right';$dialog.Controls.Add($note)
    $save=New-Object Windows.Forms.Button;$save.Text='Thêm / cập nhật';$save.SetBounds(12,463,155,28);$save.Anchor='Bottom,Left';$save.Add_Click({param($sender,$e) Save-BulkAccountDialog $sender});$dialog.Controls.Add($save)
    $cancel=New-Object Windows.Forms.Button;$cancel.Text='Đóng';$cancel.SetBounds(710,463,98,28);$cancel.Anchor='Bottom,Right';$cancel.DialogResult='Cancel';$dialog.Controls.Add($cancel);$dialog.CancelButton=$cancel
    $dialog.Tag=[pscustomobject]@{Input=$inputBox;Proxies=$proxyBox;Note=$note;Save=$save}
    return $dialog
}

function Show-BulkAccountDialog {
    $dialog=New-BulkAccountDialog
    try{$null=$dialog.ShowDialog($form)}finally{$dialog.Tag.Input.Clear();$dialog.Tag.Proxies.Clear();$dialog.Dispose()}
}
$bulkAccountButton=Add-Button 'Thêm nhiều acc' 0 0 95 {Show-BulkAccountDialog}
