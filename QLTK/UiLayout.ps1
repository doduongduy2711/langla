# Compact desktop layout. Existing handlers and account storage stay in AccountManager.ps1.
$form.Text='QLTK - TGLL_minCPU_v2'
$form.Font=New-Object Drawing.Font('Tahoma',8.25)
$form.BackColor=[Drawing.Color]::FromArgb(240,240,240)
$form.ClientSize=New-Object Drawing.Size(960,660)
$form.MinimumSize=New-Object Drawing.Size(916,679)
foreach($c in @($form.Controls)) {if($c -is [Windows.Forms.Label] -and $c -ne $status){$c.Visible=$false}}
function Ui-Group($text,$parent) {
    $g=New-Object Windows.Forms.GroupBox;$g.Text=$text;$g.ForeColor=[Drawing.Color]::FromArgb(0,45,95);$parent.Controls.Add($g);return $g
}
function Ui-Label($text,$parent,$x,$y,$w,$h) {
    $l=New-Object Windows.Forms.Label;$l.Text=$text;$l.SetBounds($x,$y,$w,$h);$parent.Controls.Add($l);return $l
}
function Ui-Place($control,$parent,$x,$y,$w,$h) {
    $parent.Controls.Add($control);$control.Anchor='Top,Left';$control.SetBounds($x,$y,$w,$h)
}
function Ui-NewButton($text,$handler) {return (Add-Button $text 0 0 95 $handler)}
function Get-LaunchRows {
    $checked=@($grid.Rows | Where-Object {$_.Cells['pick'].Value -eq $true})
    if($checked.Count){return $checked}
    return @($grid.SelectedRows | Sort-Object Index)
}
$top=New-Object Windows.Forms.Panel;$form.Controls.Add($top)
$selectAll=Ui-NewButton 'Chọn tất cả' {foreach($r in $grid.Rows){$r.Cells['pick'].Value=$true};Update-UiSummary}
$clearAll=Ui-NewButton 'Bỏ chọn' {foreach($r in $grid.Rows){$r.Cells['pick'].Value=$false};Update-UiSummary}
$refreshButton=Ui-NewButton 'Cập nhật QLTK' {Invoke-LiveUpdate}
$script:arrangeOperation=$null
$arrangeTimer=New-Object Windows.Forms.Timer;$arrangeTimer.Interval=100
$sortButton=Ui-NewButton 'Sắp xếp' {
    if($null -ne $script:arrangeOperation -and !$script:arrangeOperation.Completed){return}
    try {
        $area=[Windows.Forms.Screen]::PrimaryScreen.WorkingArea
        $script:arrangeOperation=[TGLL.ManagedGames]::BeginArrangeWindows($area.Left,$area.Top,$area.Width,$area.Height)
        $sortButton.Enabled=$false;$sortButton.Text='Đang xếp...'
        $status.Text='Đang sắp xếp cửa sổ game...';$arrangeTimer.Start()
    } catch {$sortButton.Enabled=$true;$sortButton.Text='Sắp xếp';Show-Error 'Không bắt đầu được việc sắp xếp cửa sổ game.'}
}
$arrangeTimer.Add_Tick({
    if($null -eq $script:arrangeOperation -or !$script:arrangeOperation.Completed){return}
    $arrangeTimer.Stop();$sortButton.Enabled=$true;$sortButton.Text='Sắp xếp'
    if($script:arrangeOperation.Error){$status.Text='Không sắp xếp được cửa sổ game.'}
    else {
        $arranged=$script:arrangeOperation.Result
        $status.Text='Đã yêu cầu sắp xếp '+$arranged.Arranged+' cửa sổ game trên màn hình chính cố định.'
        if($arranged.NotReady){$status.Text+=' '+$arranged.NotReady+' game chưa có cửa sổ.'}
        if($arranged.Failed){$status.Text+=' '+$arranged.Failed+' cửa sổ chưa sắp xếp được.'}
        if(!$arranged.Arranged -and !$arranged.NotReady -and !$arranged.Failed){$status.Text='Chưa có cửa sổ game do phiên QLTK này mở để sắp xếp.'}
    }
    $script:arrangeOperation=$null
})
$form.Add_Disposed({$arrangeTimer.Stop();$arrangeTimer.Dispose()})
$buttons[0].Text='Thêm';$buttons[1].Text='Sửa';$buttons[2].Text='Xóa';$buttons[6].Text='Nhập CSV';$bulkAccountButton.Text='Thêm nhiều'
$bulkOpen.Text='Mở game';$closeAllButton.Text='Tắt tất cả'
$hideGamesButton=Ui-NewButton 'Ẩn tất cả' {Set-GameWindowsVisible $false}
$showGamesButton=Ui-NewButton 'Hiện tất cả' {Set-GameWindowsVisible $true}
function Set-GameWindowsVisible([bool]$visible) {
    try {
        $result=[TGLL.ManagedGames]::SetAllVisible($visible)
        $action=if($visible){'hiện'}else{'ẩn'}
        $status.Text="Đã yêu cầu $action $($result.Changed) cửa sổ game."
        if($result.NotReady){$status.Text+=" $($result.NotReady) game chưa có cửa sổ."}
        if($result.Failed){$status.Text+=" $($result.Failed) cửa sổ chưa thực hiện được."}
    } catch {Show-Error 'Không đổi được trạng thái cửa sổ game.'}
}
$upAllButton=Ui-NewButton 'Chọn Up all' {Start-FarmAllOpen}
$topButtons=@($bulkOpen,$buttons[0],$buttons[2],$refreshButton,$closeAllButton,$buttons[1],$selectAll,$clearAll,$buttons[6],$sortButton,$settingsButton,$socketButton,$hideGamesButton,$showGamesButton,$upAllButton,$bulkAccountButton)
for($i=0;$i -lt $topButtons.Count;$i++) {Ui-Place $topButtons[$i] $top (($i%4)*85) ([int][Math]::Floor($i/4)*26) 81 23}
$basic=Ui-Group 'Thông tin cơ bản' $form
$summaryLabel=Ui-Label '' $basic 12 19 190 57
$clearUpAllButton=Ui-NewButton 'Bỏ chọn Up all' {Clear-FarmAllUp};Ui-Place $clearUpAllButton $basic 12 81 183 24
$options=Ui-Group 'Tùy chọn' $form
Ui-Place $autoLoginCheck $options 12 19 170 22
$autoLoginCheck.Checked=$true;$autoLoginCheck.Enabled=$false
Ui-Place $show $options 190 19 145 22
$optionHint=Ui-Label 'Nhận chìa: bật trong tab Auto địa cung' $options 12 42 365 20
$optionHint.ForeColor=[Drawing.Color]::FromArgb(65,65,65)
$optionHint.Visible=$false
$autoReconnect=New-Object Windows.Forms.CheckBox;$autoReconnect.Text='Tự đăng nhập lại khi mất kết nối';$autoReconnect.SetBounds(12,40,365,22)
$autoReconnect.Checked=$true;$autoReconnect.Enabled=$false;$script:settings.autoReconnect=$true
$options.Controls.Add($autoReconnect);[TGLL.NativeBackend]::AutoReconnectEnabled=$autoReconnect.Checked
$autoReconnect.Add_CheckedChanged({[TGLL.NativeBackend]::AutoReconnectEnabled=$autoReconnect.Checked;$script:settings.autoReconnect=$autoReconnect.Checked;if(!$UiTest){Write-JsonAtomic $script:configPath $script:settings}})

$null=Ui-Label 'Delay mở tab (giây):' $options 12 82 130 22
$launchDelay=New-Object Windows.Forms.NumericUpDown;$launchDelay.Minimum=0;$launchDelay.Maximum=3600;$launchDelay.DecimalPlaces=1;$launchDelay.Increment=0.1;$launchDelay.SetBounds(145,80,80,24)
if($script:settings.launchDelaySeconds){$launchDelay.Value=[Math]::Max([decimal]0,[Math]::Min([decimal]3600,[decimal]$script:settings.launchDelaySeconds))};$options.Controls.Add($launchDelay)
[TGLL.NativeBackend]::SetLaunchDelayMs([int]($launchDelay.Value*1000))
$launchDelay.Add_ValueChanged({$script:settings.launchDelaySeconds=[double]$launchDelay.Value;[TGLL.NativeBackend]::SetLaunchDelayMs([int]($launchDelay.Value*1000));if(!$UiTest){Write-JsonAtomic $script:configPath $script:settings}})
$null=Ui-Label 'Số acc mở cùng lúc:' $options 12 61 130 20
$launchConcurrency=New-Object Windows.Forms.NumericUpDown;$launchConcurrency.Minimum=1;$launchConcurrency.Maximum=100;$launchConcurrency.Value=6;$launchConcurrency.SetBounds(145,58,80,22)
if($script:settings.launchConcurrency){$launchConcurrency.Value=[Math]::Max(1,[Math]::Min(100,[int]$script:settings.launchConcurrency))};$options.Controls.Add($launchConcurrency)
[TGLL.NativeBackend]::SetLaunchConcurrency([int]$launchConcurrency.Value)
$launchConcurrency.Add_ValueChanged({$script:settings.launchConcurrency=[int]$launchConcurrency.Value;[TGLL.NativeBackend]::SetLaunchConcurrency([int]$launchConcurrency.Value);if(!$UiTest){Write-JsonAtomic $script:configPath $script:settings}})
$launchDelayTip=New-Object Windows.Forms.ToolTip;$launchDelayTip.SetToolTip($launchDelay,'Khoảng cách giữa các lần mở game. 0 = không thêm khoảng chờ.');$launchDelayTip.SetToolTip($launchConcurrency,'Số tài khoản đang mở/đăng nhập song song. Giảm giá trị không đóng game đang mở.')
$form.Add_Disposed({$launchDelayTip.Dispose()})
$grid.Anchor='Top,Left';$grid.ReadOnly=$false
foreach($col in $grid.Columns){$col.ReadOnly=$true}
$pick=New-Object Windows.Forms.DataGridViewCheckBoxColumn;$pick.Name='pick';$pick.HeaderText='Chọn';$pick.ReadOnly=$false;$pick.SortMode='NotSortable';$null=$grid.Columns.Add($pick);$pick.DisplayIndex=0;$pick.FillWeight=40
$number=New-Object Windows.Forms.DataGridViewTextBoxColumn;$number.Name='number';$number.HeaderText='STT';$number.ReadOnly=$true;$null=$grid.Columns.Add($number);$number.DisplayIndex=0;$number.FillWeight=35
$autoCol=New-Object Windows.Forms.DataGridViewCheckBoxColumn;$autoCol.Name='auto';$autoCol.HeaderText='Auto';$autoCol.ReadOnly=$false;$autoCol.SortMode='NotSortable';$autoCol.FillWeight=45;$null=$grid.Columns.Add($autoCol);$autoCol.DisplayIndex=2
$visibilityCol=New-Object Windows.Forms.DataGridViewButtonColumn;$visibilityCol.Name='visibility';$visibilityCol.HeaderText='Ẩn/Hiện';$visibilityCol.ReadOnly=$true;$visibilityCol.SortMode='NotSortable';$visibilityCol.FillWeight=65;$visibilityCol.MinimumWidth=65;$visibilityCol.UseColumnTextForButtonValue=$false;$null=$grid.Columns.Add($visibilityCol)
$closeGameCol=New-Object Windows.Forms.DataGridViewButtonColumn;$closeGameCol.Name='closegame';$closeGameCol.HeaderText='Tắt game';$closeGameCol.ReadOnly=$true;$closeGameCol.SortMode='NotSortable';$closeGameCol.UseColumnTextForButtonValue=$false;$null=$grid.Columns.Add($closeGameCol)
$characterCol=New-Object Windows.Forms.DataGridViewTextBoxColumn;$characterCol.Name='character';$characterCol.HeaderText='Tên nhân vật';$characterCol.ReadOnly=$true;$characterCol.SortMode='NotSortable';$characterCol.FillWeight=110;$null=$grid.Columns.Add($characterCol);$characterCol.DisplayIndex=4
$upCol=New-Object Windows.Forms.DataGridViewCheckBoxColumn;$upCol.Name='up';$upCol.HeaderText='Up';$upCol.ReadOnly=$false;$upCol.SortMode='NotSortable';$null=$grid.Columns.Add($upCol)
$zoneCol=New-Object Windows.Forms.DataGridViewTextBoxColumn;$zoneCol.Name='zone';$zoneCol.HeaderText='Khu';$zoneCol.ReadOnly=$true;$zoneCol.SortMode='NotSortable';$null=$grid.Columns.Add($zoneCol)
$biMaCol=New-Object Windows.Forms.DataGridViewTextBoxColumn;$biMaCol.Name='bima';$biMaCol.HeaderText='Bí ma';$biMaCol.ReadOnly=$true;$biMaCol.SortMode='NotSortable';$biMaCol.DefaultCellStyle.Alignment='MiddleRight';$null=$grid.Columns.Add($biMaCol)
function Test-AccountOpenBlocked($account) {
    if($null -eq $account){return $false}
    $key=[string]$account.id
    if((Get-Command Get-MailQueuedMember -ErrorAction SilentlyContinue) -and $null -ne (Get-MailQueuedMember $key)){return $true}
    if($script:accountJobs.ContainsKey($key)){
        $j=Get-AccountJob $script:accountJobs[$key]
        if($null -ne $j){
            if($j.ProcessId -gt 0 -and [TGLL.ManagedGames]::IsRunning([int]$j.ProcessId)){return $true}
            if($j.ProcessId -le 0 -and $j.State -in @('queued','starting','retrying')){return $true}
        }
    }
    return ($script:accountWindowPids.ContainsKey($key) -and [TGLL.ManagedGames]::IsRunning([int]$script:accountWindowPids[$key]))
}
function Update-AccountOpen($row,[bool]$knownWindow=$false) {
    $queueWaiting=$script:izanamiWaiting -and $script:izanamiWaiting.ContainsKey([string]$row.Tag.id)
    $questWaiting=$script:questWaiting -and $script:questWaiting.ContainsKey([string]$row.Tag.id)
    $soncapWaiting=$script:soncapWaiting -and $script:soncapWaiting.ContainsKey([string]$row.Tag.id)
    $blocked=$soncapWaiting -or $questWaiting -or $queueWaiting -or $knownWindow -or (Test-AccountOpenBlocked $row.Tag)
    if($row.Cells['open'].Tag -ne $blocked){$row.Cells['open'].Tag=$blocked;$grid.InvalidateCell($row.Cells['open'])}
    $row.Cells['open'].ToolTipText=if($blocked){'Game đang chạy hoặc đang mở; OPEN bị khóa để tránh mở trùng.'}else{'Mở game cho tài khoản này.'}
}
# DataGridView button cells have no Enabled property. Draw the disabled state;
# Open-Account independently enforces the same guard for mouse, keyboard and bulk.
[TGLL.UiPerformance]::InstallOpenPainting($grid)
function Get-AccountWindowPid($account) {
    $key=[string]$account.id
    if($script:accountJobs.ContainsKey($key)){
        $j=Get-AccountJob $script:accountJobs[$key]
        if($null -ne $j -and $j.State -notin @('closed','cancelled','failed') -and $j.ProcessId -gt 0){return [int]$j.ProcessId}
        return 0
    }
    if($script:accountWindowPids.ContainsKey($key)){return [int]$script:accountWindowPids[$key]}
    return 0
}
function Update-AccountVisibility($row) {
    $pidForGame=Get-AccountWindowPid $row.Tag
    $visibility=if($script:summaryWindowSnapshot -and $script:summaryWindowSnapshot.ContainsKey($pidForGame)){$script:summaryWindowSnapshot[$pidForGame]}elseif($script:summaryWindowSnapshot){-1}elseif($pidForGame -gt 0){[TGLL.ManagedGames]::WindowVisibility($pidForGame)}else{-1}
    $visibilityText=if($visibility -eq 1){'Ẩn'}elseif($visibility -eq 0){'Hiện'}else{'—'}
    if($row.Cells['visibility'].Value -ne $visibilityText){$row.Cells['visibility'].Value=$visibilityText}
    $row.Cells['visibility'].ToolTipText=if($visibility -eq -1){'Game chưa có cửa sổ trong phiên QLTK này.'}else{'Ẩn hoặc hiện riêng cửa sổ game của tài khoản này.'}
    Update-AccountOpen $row ($visibility -ge 0)
    Update-AccountClose $row ($visibility -ge 0)
}
function Toggle-AccountVisibility($row) {
    $pidForGame=Get-AccountWindowPid $row.Tag
    $visibility=if($pidForGame -gt 0){[TGLL.ManagedGames]::WindowVisibility($pidForGame)}else{-1}
    if($visibility -eq -1){$status.Text='Tài khoản chưa có cửa sổ game để ẩn/hiện.';return}
    if([TGLL.ManagedGames]::SetVisible($pidForGame,($visibility -eq 0))){$status.Text='Đã yêu cầu '+$(if($visibility -eq 0){'hiện'}else{'ẩn'})+' game: '+$row.Tag.username}
    else{$status.Text='Chưa đổi được trạng thái cửa sổ game: '+$row.Tag.username}
    Update-AccountVisibility $row
}
function Update-AccountClose($row,[bool]$knownWindow=$false) {
    $available=$knownWindow -or (Test-AccountOpenBlocked $row.Tag)
    $closeText=if($available){'Tắt'}else{'—'}
    if($row.Cells['closegame'].Value -ne $closeText){$row.Cells['closegame'].Value=$closeText}
    $row.Cells['closegame'].ToolTipText=if($available){'Tắt riêng game này hoặc hủy lượt mở đang chờ.'}else{'Tài khoản chưa mở game.'}
}
function Close-AccountGame($row) {
    if(!(Test-AccountOpenBlocked $row.Tag)){$status.Text='Tài khoản chưa mở game.';return}
    $account=$row.Tag;$key=[string]$account.id
    try {
        if($script:accountJobs.ContainsKey($key)){$closed=[TGLL.NativeBackend]::CloseGame($script:accountJobs[$key])}
        else {$closed=[TGLL.ManagedGames]::CloseOne((Get-AccountWindowPid $account))}
        if($closed.Failed){$status.Text='Chưa tắt được game: '+$account.username;return}
        Set-AccountAuto $row $false
        $script:accountWindowPids.Remove($key)
        $row.Cells['status'].Value='Đã tắt';$status.Text='Đã tắt game / hủy lượt mở: '+$account.username
        Update-AccountVisibility $row;Update-UiSummary
    }catch{$status.Text='Chưa tắt được game: '+$account.username+' • '+$_.Exception.Message}
}
$grid.Add_CellContentClick({param($sender,$e) if($e.RowIndex -ge 0 -and $e.ColumnIndex -ge 0 -and $grid.Columns[$e.ColumnIndex].Name -eq 'closegame'){Close-AccountGame $grid.Rows[$e.RowIndex]}})
$grid.Add_CellContentClick({param($sender,$e) if($e.RowIndex -ge 0 -and $e.ColumnIndex -ge 0 -and $grid.Columns[$e.ColumnIndex].Name -eq 'visibility'){Toggle-AccountVisibility $grid.Rows[$e.RowIndex]}})
$grid.Columns['username'].HeaderText='Tài khoản';$grid.Columns['username'].FillWeight=125
$grid.Columns['password'].HeaderText='Mật khẩu';$grid.Columns['password'].FillWeight=110
$grid.Columns['proxy'].HeaderText='Proxy SOCKS5';$grid.Columns['proxy'].FillWeight=160
$grid.Columns['status'].FillWeight=215;$openCol.HeaderText='Mở';$openCol.Text='OPEN';$openCol.FillWeight=65
# Keep the reference column widths and order independent of window size.
$grid.AutoSizeColumnsMode='None';$grid.AllowUserToResizeColumns=$false;$grid.AllowUserToResizeRows=$false;$grid.AllowUserToOrderColumns=$false
$fixedAccountColumns=@(@('number',38),@('pick',41),@('auto',47),@('up',41),@('username',123),@('character',109),@('zone',55),@('bima',75),@('password',109),@('proxy',157),@('status',210),@('open',66),@('visibility',68),@('closegame',68))
for($i=0;$i -lt $fixedAccountColumns.Count;$i++){
    $col=$grid.Columns[$fixedAccountColumns[$i][0]];$col.AutoSizeMode='None';$col.Width=[int]$fixedAccountColumns[$i][1];$col.Resizable='False';$col.DisplayIndex=$i;$col.SortMode='NotSortable'
}

$grid.EnableHeadersVisualStyles=$false;$grid.ColumnHeadersDefaultCellStyle.BackColor=[Drawing.Color]::FromArgb(232,235,238)
$grid.ColumnHeadersDefaultCellStyle.ForeColor=[Drawing.Color]::FromArgb(0,35,75)
$grid.ColumnHeadersHeight=24;$grid.ColumnHeadersHeightSizeMode='DisableResizing';$grid.RowTemplate.Height=22
$grid.DefaultCellStyle.ForeColor=[Drawing.Color]::Black;$grid.DefaultCellStyle.SelectionBackColor=[Drawing.Color]::FromArgb(51,153,255)
$grid.DefaultCellStyle.SelectionForeColor=[Drawing.Color]::White;$grid.BorderStyle='FixedSingle';$grid.GridColor=[Drawing.Color]::Silver
$grid.AlternatingRowsDefaultCellStyle.BackColor=[Drawing.Color]::FromArgb(250,250,250)
$grid.Add_CurrentCellDirtyStateChanged({if($grid.IsCurrentCellDirty){$grid.CommitEdit([Windows.Forms.DataGridViewDataErrorContexts]::Commit)}})
$tabs=New-Object Windows.Forms.TabControl;$form.Controls.Add($tabs)
foreach($name in @('Tài khoản','Đăng nhập / NPC','Proxy / Socket','Nhật ký')) {$page=New-Object Windows.Forms.TabPage;$page.Text=$name;$page.BackColor=$form.BackColor;$tabs.TabPages.Add($page)}
$accountHelp=Ui-Label "Chọn tài khoản trong bảng để sửa hoặc xem thông tin bên phải.`r`nĐánh dấu cột Chọn để mở nhiều tài khoản cùng lúc." $tabs.TabPages[0] 16 18 625 42
$manageGroup=Ui-Group 'Danh sách tài khoản' $tabs.TabPages[0];$manageGroup.SetBounds(14,73,650,113);$manageGroup.Anchor='Top,Left,Right'
$null=Ui-Label "Nhập CSV với cột username,password,proxy.`r`nProxy có thể dán dạng host:port:username:password." $manageGroup 14 22 615 38
$importShortcut=Ui-NewButton 'Nhập list / CSV' {$buttons[6].PerformClick()};Ui-Place $importShortcut $manageGroup 14 68 130 27
$editShortcut=Ui-NewButton 'Sửa tài khoản' {$buttons[1].PerformClick()};Ui-Place $editShortcut $manageGroup 155 68 130 27
$null=Ui-Label 'Lưu cục bộ • mật khẩu được mã hóa theo tài khoản Windows hiện tại.' $tabs.TabPages[0] 16 203 640 25
$flowGroup=Ui-Group 'Khi mở game' $tabs.TabPages[1];$flowGroup.SetBounds(14,15,650,128);$flowGroup.Anchor='Top,Left,Right'
$null=Ui-Label "1. Truyền username và password trực tiếp vào game.`r`n2. Chọn nhân vật đầu tiên trong danh sách.`r`n3. Vào bản đồ, đợi 5 giây; có popup Xác nhận thì xử lý.`r`n4. Nếu bật Nhận chìa: tìm Raikage trên bản đồ hiện tại và nhận chìa." $flowGroup 14 22 610 86
$null=Ui-Label 'Các bước tự động không cần chuột hay giữ cửa sổ phía trước.' $tabs.TabPages[1] 16 160 630 32
$proxyGroup=Ui-Group 'Proxy theo từng tài khoản' $tabs.TabPages[2];$proxyGroup.SetBounds(14,15,650,160);$proxyGroup.Anchor='Top,Left,Right'
$null=Ui-Label "Dán proxy trong cửa sổ Thêm / Sửa để tự tách host, port và thông tin xác thực.`r`nHỗ trợ SOCKS5 có hoặc không có username/password.`r`nĐể trống proxy nếu muốn dùng kết nối trực tiếp." $proxyGroup 14 23 610 64
$proxyEdit=Ui-NewButton 'Sửa proxy đã chọn' {$buttons[1].PerformClick()};Ui-Place $proxyEdit $proxyGroup 14 100 155 28
$proxySettings=Ui-NewButton 'Socket / Proxy' {$socketButton.PerformClick()};Ui-Place $proxySettings $proxyGroup 182 100 145 28
$logHelp=Ui-Label "Chọn tài khoản đã mở rồi dùng Xem ID NPC hoặc Log menu.`r`nNhấp đúp ô Trạng thái bị lỗi để xem hướng dẫn xử lý." $tabs.TabPages[3] 16 20 635 44
$logShortcut=Ui-NewButton 'Xem log menu' {$menuLogButton.PerformClick()};Ui-Place $logShortcut $tabs.TabPages[3] 16 84 150 29
$npcShortcut=Ui-NewButton 'Xem ID NPC' {$npcIdsButton.PerformClick()};Ui-Place $npcShortcut $tabs.TabPages[3] 178 84 150 29
$detailsGroup=Ui-Group 'Thông tin tài khoản' $form
$detailsLabel=Ui-Label '' $detailsGroup 12 22 260 224;$detailsLabel.Anchor='Top,Bottom,Left,Right'
$bottom=New-Object Windows.Forms.Panel;$form.Controls.Add($bottom)
$bottomOpen=Ui-NewButton 'Mở đã chọn' {$bulkOpen.PerformClick()}
$bottomEdit=Ui-NewButton 'Sửa đã chọn' {$buttons[1].PerformClick()}
$bottomButtons=@($bottomOpen,$cancelButton,$replayNpcButton,$npcIdsButton,$menuLogButton,$buttons[3],$buttons[4],$buttons[5],$bottomEdit)
$buttons[3].Text='Copy username';$buttons[4].Text='Copy password'
foreach($b in $topButtons+$bottomButtons+@($clearUpAllButton,$importShortcut,$editShortcut,$proxyEdit,$proxySettings,$logShortcut,$npcShortcut)) {$b.Font=$form.Font;$b.ForeColor=[Drawing.Color]::FromArgb(0,35,75);$b.UseVisualStyleBackColor=$true}
function Get-OpenGamePids {return [TGLL.ManagedGames]::RunningProcessIds()}
function Update-AccountBiMa($row,$j) {
    if(!$grid.Columns.Contains('bima') -or $null -eq $row.Tag){return}
    $key=[string]$row.Tag.id
    if($j -is [TGLL.NativeJob]){[TGLL.NativeBackend]::BindBiMaAccount($j.Id,$key)}
    $snapshot=[TGLL.NativeBackend]::GetBiMaSnapshot($key)
    $text=if($snapshot.Count -ge 0){[string]$snapshot.Count}else{'—'}
    if($row.Cells['bima'].Value -ne $text){$row.Cells['bima'].Value=$text}
    $row.Cells['bima'].ToolTipText=if($snapshot.Count -ge 0){'ID 1276 • Số lượng đã đọc gần nhất: '+$snapshot.ReadAt.ToLocalTime().ToString('dd/MM/yyyy HH:mm:ss')}else{'ID 1276 • Chưa có số lượng được xác nhận'}
}
function Update-AccountCharacter($row,$j) {
    Update-AccountBiMa $row $j
    $newCharacter=if($j){[string]$j.CharacterName}else{''}
    if($row.Cells['character'].Value -ne $newCharacter){$row.Cells['character'].Value=$newCharacter}
}
function Update-VisibleCharacterNames {
    if($script:uiDragging -or $form.WindowState -eq [Windows.Forms.FormWindowState]::Minimized -or $grid.Rows.Count -eq 0){return}
    $first=$grid.FirstDisplayedScrollingRowIndex;if($first -lt 0){return}
    $last=[Math]::Min($grid.Rows.Count-1,$first+$grid.DisplayedRowCount($false))
    for($n=$first;$n -le $last;$n++){
        $row=$grid.Rows[$n];if($null -eq $row.Tag){continue};$key=[string]$row.Tag.id;$j=$null
        if($script:accountJobs.ContainsKey($key)){$j=Get-AccountJob $script:accountJobs[$key]}
        Update-AccountCharacter $row $j
    }
}
$grid.Add_Scroll({Update-VisibleCharacterNames})
function Update-UiSummary {
    if([TGLL.UiPerformance]::IsScrolling){$script:uiRefreshPending=$true;return}
    $total=$grid.Rows.Count;$livePids=@(Get-OpenGamePids);$active=$livePids.Count;$waiting=0;$failed=0
    $script:summaryWindowSnapshot=[TGLL.ManagedGames]::WindowVisibilities([int[]]$livePids)
    try {
    foreach($row in $grid.Rows){
        if($script:accountJobs.ContainsKey([string]$row.Tag.id)){
            $j=Get-AccountJob $script:accountJobs[[string]$row.Tag.id]
            if($j.State -eq 'failed'){$failed++}
            if($j.State -in @('queued','starting','retrying') -and $livePids -notcontains $j.ProcessId){$waiting++}
        }
    }
    $summaryRows=$grid.Rows
    if($grid.Rows.Count -gt 32 -and $grid.FirstDisplayedScrollingRowIndex -ge 0){
        $first=$grid.FirstDisplayedScrollingRowIndex;$last=[Math]::Min($grid.Rows.Count-1,$first+$grid.DisplayedRowCount($false))
        $summaryRows=@(for($n=$first;$n -le $last;$n++){$grid.Rows[$n]})
        if($grid.SelectedRows.Count -and $summaryRows -notcontains $grid.SelectedRows[0]){$summaryRows+=,$grid.SelectedRows[0]}
    }
    foreach($row in $summaryRows){
        if($row.Cells['number'].Value -ne $row.Index+1){$row.Cells['number'].Value=$row.Index+1}
        $j=$null;if($script:accountJobs.ContainsKey([string]$row.Tag.id)){$j=Get-AccountJob $script:accountJobs[[string]$row.Tag.id]}
        Update-AccountCharacter $row $j
        $zoneText='—'
        if($j -and $j.PSObject.Properties['ZoneReadAt'] -and $j.ZoneId -ge 0 -and !$j.CloseRequested -and $j.State -notin @('closed','failed','cancelled') -and ([datetime]::UtcNow-$j.ZoneReadAt).TotalSeconds -le 5){$zoneText=[string]$j.ZoneId}
        if($row.Cells['zone'].Value -ne $zoneText){$row.Cells['zone'].Value=$zoneText}
        Update-AccountVisibility $row
    }
    $summaryLabel.Text="Tài khoản: $total  |  Đang mở: $active`r`nĐang chờ: $waiting  |  Lỗi: $failed`r`nĐã chọn: $(@($grid.Rows | Where-Object {$_.Cells['pick'].Value -eq $true}).Count)"
    if(!$grid.SelectedRows.Count){$detailsLabel.Text='Chọn một tài khoản trong bảng.';return}
    $row=$grid.SelectedRows[0];$a=$row.Tag
    $detailsLabel.Text="[QLTK] $($a.username)`r`nTrạng thái: $($row.Cells['status'].Value)`r`nProxy: $($row.Cells['proxy'].Value)`r`nĐăng nhập trực tiếp: $($autoLoginCheck.Checked)`r`nNhân vật: số 1"
    if($script:accountJobs.ContainsKey([string]$a.id)){
        $j=Get-AccountJob $script:accountJobs[[string]$a.id]
        $detailsLabel.Text+="`r`nPID: $($j.ProcessId)`r`nBước: $($j.Stage)"
        if($j -is [TGLL.NativeJob]){$detailsLabel.Text+="`r`n"+[TGLL.NativeBackend]::VitalsDisplay($j)}
        $curseDetail='';if($j -is [TGLL.NativeJob]){$curseDetail=[TGLL.NativeBackend]::CurseDisplayStatus($j)};if($curseDetail){$detailsLabel.Text+="`r`n"+$curseDetail}
        if($j.NpcId -ge 0){$detailsLabel.Text+="`r`nRaikage: ID $($j.NpcId) / $($j.NpcTemplateId)"}
        if($j.Error){$detailsLabel.Text+="`r`n`r`n$(Get-LoginErrorText $j.Error)"}
    }
    } finally {$script:summaryWindowSnapshot=$null}
}
function Resize-UiLayout {
    $w=$form.ClientSize.Width;$h=$form.ClientSize.Height
    $form.SuspendLayout()
    try {
    $top.SetBounds(6,6,340,106);$basic.SetBounds(350,3,200,109);$options.SetBounds(556,3,($w-562),109)
    $autoReconnect.Width=[Math]::Max(250,$options.Width-24)
    $gridHeight=[Math]::Max(180,[Math]::Min(320,[int](($h-380)*0.75)));$tabTop=118+$gridHeight+8
    $grid.SetBounds(6,118,($w-12),$gridHeight)
    $tabs.SetBounds(6,$tabTop,($w-268),($h-$tabTop-101));$detailsGroup.SetBounds(($w-256),$tabTop,250,($h-$tabTop-101))
    $bottom.SetBounds(6,($h-93),($w-12),63)
    $bw=[int][Math]::Floor(($bottom.Width-30)/6)
    for($i=0;$i -lt $bottomButtons.Count;$i++){Ui-Place $bottomButtons[$i] $bottom (($i%6)*($bw+6)) ([int][Math]::Floor($i/6)*31) $bw 27}
    $status.SetBounds(8,($h-27),($w-16),24);$status.Anchor='Bottom,Left,Right'
    }finally{$form.ResumeLayout()}
}
$form.Add_Resize({Resize-UiLayout});$grid.Add_SelectionChanged({Update-UiSummary})
Resize-UiLayout;Update-UiSummary

. (Join-Path $PSScriptRoot 'TrainUi.ps1')
. (Join-Path $PSScriptRoot 'AutoUi.ps1')

. (Join-Path $PSScriptRoot 'GameSettingsUi.ps1')

. (Join-Path $PSScriptRoot 'ConfigUi.ps1')

. (Join-Path $PSScriptRoot 'InventoryUi.ps1')

. (Join-Path $PSScriptRoot 'IzanamiUi.ps1')

. (Join-Path $PSScriptRoot 'AutoItemUi.ps1')

. (Join-Path $PSScriptRoot 'QuestUi.ps1')

. (Join-Path $PSScriptRoot 'GiftCodeUi.ps1')

. (Join-Path $PSScriptRoot 'MailUi.ps1')
. (Join-Path $PSScriptRoot 'DistributionUi.ps1')

. (Join-Path $PSScriptRoot 'FarmUi.ps1')
. (Join-Path $PSScriptRoot 'SonCapUi.ps1')
. (Join-Path $PSScriptRoot 'BeastUi.ps1')

. (Join-Path $PSScriptRoot 'StoneChestUi.ps1')

. (Join-Path $PSScriptRoot 'CloakUi.ps1')









. (Join-Path $PSScriptRoot 'MarketUi.ps1')
. (Join-Path $PSScriptRoot 'GemCombineUi.ps1')
. (Join-Path $PSScriptRoot 'AutoAllUi.ps1')

foreach($page in $tabs.TabPages){$page.AutoScroll=$true}
$grid.Columns['password'].Visible=$show.Checked
$show.Add_CheckedChanged({$grid.Columns['password'].Visible=$show.Checked})

function Initialize-AutoHideUi {
    if($null -ne $script:autoHideAfterLogin){return}
$script:autoHideDone=@{};$script:autoHideRetry=@{};$script:autoHideCursor=0
$script:autoHideAfterLogin=New-Object Windows.Forms.CheckBox
$autoHideAfterLogin.Text='Ẩn sau khi bật';$autoHideAfterLogin.SetBounds(235,61,115,23)
$autoHideAfterLogin.Checked=[bool]$script:settings.autoHideAfterLogin;$options.Controls.Add($autoHideAfterLogin)
$launchDelayTip.SetToolTip($autoHideAfterLogin,'Tự ẩn cửa sổ game sau khi đăng nhập thành công. Bấm Hiện để mở lại; mỗi phiên chỉ tự ẩn một lần.')

$autoHideAfterLogin.Add_CheckedChanged({Save-AutoHidePreference})
}
function Save-AutoHidePreference {
    $script:settings.autoHideAfterLogin=[bool]$autoHideAfterLogin.Checked
    if(!$UiTest){Write-JsonAtomic $script:configPath $script:settings}
}
function Set-LoggedInGameHidden([int]$gamePid) {
    if(![TGLL.ManagedGames]::SetVisible($gamePid,$false)){return $false}
    return [TGLL.ManagedGames]::WindowVisibility($gamePid) -eq 0
}
function Apply-AutoHideJob([string]$accountKey,$job,[datetime]$now) {
    if(!$autoHideAfterLogin.Checked -or $null -eq $job -or $job.ProcessId -le 0){return}
    $session=[string]$job.Id+':'+[string]$job.ProcessId
    if($script:autoHideDone[$accountKey] -eq $session){return}
    if($job -isnot [TGLL.NativeJob] -or ![TGLL.NativeBackend]::UpSessionReady($job) -or $job.ReconnectRequested -or $job.State -in @('retrying','reconnecting','manual_entry','startup_unavailable')){return}
    if($script:autoHideRetry.ContainsKey($accountKey) -and $now -lt $script:autoHideRetry[$accountKey]){return}
    $script:autoHideRetry[$accountKey]=$now.AddSeconds(2)
    try{
        if(Set-LoggedInGameHidden ([int]$job.ProcessId)){$script:autoHideDone[$accountKey]=$session;$script:autoHideRetry.Remove($accountKey)}
    }catch{}
}
function Invoke-AutoHideAfterLogin {
    Initialize-AutoHideUi
    if(!$autoHideAfterLogin.Checked -or !$script:accountJobs.Count){return}
    $keys=@($script:accountJobs.Keys);$limit=[Math]::Min(24,$keys.Count);$now=[datetime]::UtcNow
    for($n=0;$n -lt $limit;$n++){
        if($script:autoHideCursor -ge $keys.Count){$script:autoHideCursor=0}
        $key=[string]$keys[$script:autoHideCursor];$script:autoHideCursor++
        Apply-AutoHideJob $key (Get-AccountJob $script:accountJobs[$key]) $now
    }
}
