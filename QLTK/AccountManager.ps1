param([switch]$SelfTest,[switch]$UiTest,[switch]$IntegrationTest)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing,System.Security
if(!('TGLL.UiPerformance' -as [type])){Add-Type -Path (Join-Path $PSScriptRoot 'UiPerformance.cs') -ReferencedAssemblies System.dll,System.Drawing.dll,System.Windows.Forms.dll}
$script:legacyDataDir = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'TGLLAccountManager'
$script:dataDir = Join-Path (Split-Path $PSScriptRoot -Parent) 'Data'
$script:storePath = Join-Path $script:dataDir 'accounts.json'
$script:configPath = Join-Path $script:dataDir 'settings.json'
$script:accounts = @()
$script:accountJobs=@{}
$script:autoTrainWanted=@{};$script:autoTrainApplied=@{};$script:updatingAuto=$false
function Get-LoginErrorText([string]$code) {
    $messages=@{
        game_disconnected='Mất kết nối; đợi đóng cửa sổ lỗi và tự đăng nhập lại.'
        key_game_close_failed='Đã nhận chìa nhưng chưa tắt được game. Hãy bấm Tắt game để thử lại.'
        key_receipt_unverified='Chưa xác nhận được chìa trong túi tăng sau khi nhận. Giữ game mở để kiểm tra.'
        focus_lost='Cửa sổ game mất tiêu điểm. Bấm Open lại và không chuyển cửa sổ khi đang điền.'
        focus_not_acquired='Không đưa được game lên phía trước. Chạy QLTK và game với cùng quyền Windows, rồi bấm Open lại.'
        window_not_found='Không tìm thấy cửa sổ game trong 30 giây. Kiểm tra đường dẫn game và game có mở được không.'
        login_screen_not_found='Không nhận diện được màn hình đăng nhập. Đưa game về màn hình đăng nhập và kiểm tra kích thước cửa sổ.'
        game_exited='Game đã thoát khi đang mở hoặc nhập tài khoản.'
        wrong_process='Cửa sổ không thuộc đúng bản game đã chọn. Kiểm tra đường dẫn trong Game settings.'
        input_failed='Windows không nhận thao tác nhập. Chạy game và QLTK với cùng quyền Windows.'
        launch_or_input_failed='Không mở hoặc nhập được tài khoản. Kiểm tra đường dẫn và quyền chạy game.'
        cancelled='Đã hủy thao tác đăng nhập.'
        key_baseline_unavailable='Chưa đọc được số chìa trước khi nhận; chưa gọi NPC';travel_item_missing='Không có phù ID 168 trong túi; chưa gọi Raikage';travel_slot_changed='Ô phù đã thay đổi; hãy bật lại auto';travel_item_not_ready='Chưa dùng được phù; hãy bật lại auto khi game sẵn sàng';travel_village_menu_not_ready='Chưa thấy menu Làng';travel_sand_menu_not_ready='Chưa thấy menu Làng Cát';travel_arrival_not_confirmed='Chưa xác nhận đến Làng Cát; chưa gọi Raikage';startup_popup_not_ready='Bước kiểm tra sau đăng nhập chưa hoàn tất: bản đồ chưa sẵn sàng hoặc popup chưa đóng. Không có nghĩa là đã gọi nhận chìa.'
        native_build_unsupported='Bản game khác bản TGLL_minCPU_v2 đã phân tích. Chọn đúng EXE gốc trong Cài đặt game.'
        game_invalid='Đường dẫn game không tồn tại hoặc không trỏ đúng TheGioiLangLa.exe. Chọn lại trong Cài đặt game.'
        login_length_invalid='Tên đăng nhập hoặc mật khẩu trống, có ký tự không hợp lệ hoặc dài hơn 128 ký tự. Sửa tài khoản này.'
        login_secret_unavailable='Không đọc được mật khẩu cũ trên máy này. Nhập lại mật khẩu trong Sửa tài khoản.'
        login_queue_full='Hàng chờ mở game đã đầy. Đợi hoặc hủy hàng chờ rồi thử lại.'
        login_queue_access_denied='Windows từ chối quyền truy cập khi xếp hàng mở game.'
        login_queue_io_failed='Không truy cập được tệp cần thiết khi xếp hàng mở game.'
        login_queue_failed='Chưa xếp hàng mở game được. Xem logs/launch-errors.jsonl để biết loại lỗi; không phải kết luận Windows sai bản.'
        native_x64_required='Mở QLTK.exe bản 64-bit đi kèm gói này.'
        native_access_denied='Windows không cho bộ nối truy cập game. Chạy QLTK và game bằng cùng tài khoản và cùng quyền.'
        character_focus_lost='Cửa sổ game mất tiêu điểm; hãy bấm bệ dưới chân nhân vật để tiếp tục.'
        character_screen_not_found='Chưa nhận diện được màn hình nhân vật trong 60 giây. Kiểm tra phản hồi đăng nhập của game.'
        character_entry_not_confirmed='Đã thử chọn nhân vật nhưng chưa xác nhận được màn chơi. Kiểm tra kết nối hoặc thông báo của game.'
        character_list_empty='Tài khoản chưa có nhân vật. QLTK giữ game mở và không tự tạo nhân vật.'
        character_screen_not_ready='Chưa có màn hình nhân vật sau 60 giây. QLTK đóng game lỗi rồi tự mở lại; nếu tiếp tục lỗi, kiểm tra kết nối hoặc thông báo game.'
        npc_not_found='Chưa tìm thấy Raikage Đệ Tam trên bản đồ hiện tại trong 60 giây. Game vẫn mở.'
        dungeon_no_keys='Đã dừng auto địa cung: hết Chìa khóa địa cung (cao cấp). Bỏ tích rồi tích lại sau khi có thêm chìa.'
        dungeon_inventory_unavailable='Đã dừng auto địa cung vì chưa đọc được hành trang. Chưa gửi vào; đọc lại túi và bật lại để thử.'
        dungeon_entry_not_confirmed='Game chưa xác nhận chuyển bản đồ sau 30 giây. Đã dừng để tránh gửi vào địa cung lặp lại.'
        dungeon_map_unavailable='Chưa xác định được bản đồ hiện tại. Đã dừng auto địa cung; kiểm tra game rồi bật lại.'
        onoki_not_found='Không tìm thấy Onoki (template 32) trên bản đồ hiện tại. Chưa gửi vào cấm thuật.'
        npc_izanami_menu_not_ready='Menu Onoki chưa có mục Cấm thuật Izanami đúng tên/đúng NPC. Chưa gửi vào.'
        party_create_not_confirmed='Đã yêu cầu tạo nhóm nhưng game chưa xác nhận có nhóm sau 30 giây. Chưa gửi lệnh vào hoạt động.'
        npc_dungeon_menu_not_ready='Đã gọi Raikage nhưng chưa thấy mục Địa cung (cao cấp). Game vẫn mở.'
        npc_key_menu_not_ready='Đã gọi NPC nhưng chưa thấy mục Nhận chìa khóa trong 60 giây. Game vẫn mở.'
        native_login_not_ready='Game chưa sẵn sàng đăng nhập trong 60 giây. Kiểm tra dữ liệu game và thử lại.'
        native_module_not_found='Không tìm thấy thư viện game sau khi mở. Kiểm tra bộ game đầy đủ.'
        native_process_not_ready='Windows chưa cung cấp thông tin tiến trình game sau 5 giây. Kiểm tra quyền chạy game.'
        native_cleanup_failed='Chưa đóng được game lỗi nên chưa mở lại. Bấm Tắt tất cả rồi thử lại.'
        native_wrong_process='Tiến trình vừa mở không thuộc đúng EXE game đã chọn.'
        native_operation_failed='Bộ nối chưa hoàn tất. Kiểm tra bản game, dữ liệu game và quyền Windows.'
        proxy_auth_failed='Proxy từ chối phương thức xác thực hoặc username/password proxy.'
        proxy_timeout='Không kết nối được proxy trong thời gian chờ.'
        proxy_unreachable='Không truy cập được địa chỉ proxy. Kiểm tra host, port và mạng.'
    }
    if ($messages.ContainsKey($code)) {return $messages[$code]}
    return 'Không hoàn tất đăng nhập. Kiểm tra cấu hình game.'
}
$script:defaultGame = ''
foreach ($candidate in @((Join-Path $PSScriptRoot 'TheGioiLangLa.exe'),(Join-Path (Split-Path $PSScriptRoot -Parent) 'TheGioiLangLa.exe'),'C:\Users\Admin\Downloads\TGLL_minCPU_v2\TheGioiLangLa.exe')) {
    if (Test-Path -LiteralPath $candidate -PathType Leaf) { $script:defaultGame=$candidate; break }
}
$script:settings = @{ jar = $script:defaultGame; java = 'javaw.exe'; mode = 'manual'; arguments = @(); socketPort=47651; socketEnabled=$true; proxyBridge='' }
. (Join-Path $PSScriptRoot 'JsonData.ps1')
. (Join-Path $PSScriptRoot 'PortableData.ps1')
function Write-JsonAtomic($path,$value) {
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null
    $temp = "$path.tmp"
    try {
        [IO.File]::WriteAllText($temp,(ConvertTo-Json -InputObject $value -Depth 8),(New-Object System.Text.UTF8Encoding -ArgumentList $false))
        if ([IO.File]::Exists($path)) { [IO.File]::Replace($temp,$path,[System.Management.Automation.Language.NullString]::Value) }
        else { [IO.File]::Move($temp,$path) }
    } finally { if ([IO.File]::Exists($temp)) { [IO.File]::Delete($temp) } }
}
function Save-Accounts { if($script:deferAccountSave){$script:accountSavePending=$true;return};Write-JsonAtomic $script:storePath @($script:accounts) }
function Quote-Argument([string]$value) {
    # Windows CommandLineToArgvW quoting, including quotes and trailing backslashes.
    '"' + [regex]::Replace([regex]::Replace($value,'(\\*)"','$1$1\"'),'(\\+)$','$1$1') + '"'
}
function Build-Arguments($account,$settings) {
    $result = @()
    if ([IO.Path]::GetExtension([string]$settings.jar) -eq '.jar') { $result = @('-jar',[string]$settings.jar) }
    if ($settings.mode -eq 'arguments') {
        $password = Unprotect-Password $account.secret
        foreach ($arg in @($settings.arguments)) {
            $result += ([string]$arg).Replace('{username}',[string]$account.username).Replace('{password}',$password)
        }
    }
    ($result | ForEach-Object { Quote-Argument $_ }) -join ' '
}
function Get-LaunchInfo($account,$settings) {
    if ([string]::IsNullOrWhiteSpace([string]$settings.jar) -or !(Test-Path -LiteralPath $settings.jar -PathType Leaf)) { throw 'Chọn TheGioiLangLa.exe trong Cài đặt trước.' }
    $extension = [IO.Path]::GetExtension([string]$settings.jar)
    if ($extension -notin @('.exe','.jar')) { throw 'Chỉ hỗ trợ file .exe hoặc .jar.' }
    $target = [IO.Path]::GetFullPath([string]$settings.jar)
    $binary = $target
    if ($extension -eq '.jar') {
        $binary=$settings.java
        if (!(Test-Path -LiteralPath $binary -PathType Leaf)) { $binary=(Get-Command $binary -ErrorAction Stop).Source }
    }
    $psi=New-Object Diagnostics.ProcessStartInfo
    $psi.FileName=$binary; $psi.Arguments=Build-Arguments $account $settings
    $psi.WorkingDirectory=[IO.Path]::GetDirectoryName($target); $psi.UseShellExecute=$false
    return $psi
}
. (Join-Path $PSScriptRoot 'SocketApi.ps1')
if ($IntegrationTest) {
    if ([string]::IsNullOrEmpty($script:defaultGame)) { throw 'Game executable not found' }
    $testSettings=@{jar=$script:defaultGame;java='missing-java.exe';mode='manual';arguments=@('--user','{username}','--pass','{password}')}
    $info=Get-LaunchInfo @{username='demo';secret='must-not-decrypt'} $testSettings
    if ($info.FileName -cne $script:defaultGame -or $info.Arguments -ne '' -or $info.WorkingDirectory -cne (Split-Path $script:defaultGame -Parent) -or $info.UseShellExecute) { throw 'EXE launch configuration failed' }
    $testSettings.jar=Join-Path $PSScriptRoot 'missing.exe'
    $rejected=$false
    try { $null=Get-LaunchInfo $null $testSettings } catch { $rejected=$true }
    if (!$rejected) { throw 'Missing executable accepted' }
    Write-Output 'PASS: game discovery, EXE launch path, working directory, no Java or credentials in manual mode, missing file rejection.'
    exit 0
}
if ($SelfTest) {
    $secret = 'p a"ss\漢字'
    if ((Unprotect-Password (Protect-Password $secret)) -cne $secret) { throw 'Plain storage round trip failed' }
    if ((Quote-Argument 'a b') -cne '"a b"') { throw 'Space quoting failed' }
    if ((Quote-Argument 'a"b') -cne '"a\"b"') { throw 'Quote escaping failed' }
    if ((Quote-Argument 'a\') -cne '"a\\"') { throw 'Trailing slash escaping failed' }
    $a = @{username='demo';secret=(Protect-Password 'secret')}
    $s = @{jar='C:\test app\demo.jar';mode='arguments';arguments=@('--user','{username}','--pass','{password}')}
    $line = Build-Arguments $a $s
    if ($line -cne '"-jar" "C:\test app\demo.jar" "--user" "demo" "--pass" "secret"') { throw 'Argument mapping failed' }
    $s.mode = 'manual'
    if ((Build-Arguments $a $s) -match 'secret|demo"') { throw 'Manual mode passed credentials' }
    $testPath = Join-Path $env:TEMP ([guid]::NewGuid().ToString()+'.json')
    try {
        Write-JsonAtomic $testPath @($a)
        Write-JsonAtomic $testPath @($a,$a)
        $decoded = Get-Content -Raw $testPath | ConvertFrom-Json; $saved = @($decoded)
        if ($saved.Count -ne 2 -or (Unprotect-Password $saved[0].secret) -cne 'secret') { throw 'Persistence failed' }
    } finally { if (Test-Path $testPath) { Remove-Item -LiteralPath $testPath } }
    $s.jar='C:\test app\TheGioiLangLa.exe'; $s.mode='arguments'
    if ((Build-Arguments $a $s) -cne '"--user" "demo" "--pass" "secret"') { throw 'EXE argument mapping failed' }
    $s.mode='manual'
    if ((Build-Arguments $a $s) -ne '') { throw 'EXE manual mode passed arguments' }
    Write-Output 'PASS: plaintext storage, quoting, JAR/EXE credential mapping, manual mode, atomic persistence.'
    exit 0
}
function Show-Error($e) { if ($UiTest) { throw [string]$e }; [Windows.Forms.MessageBox]::Show([string]$e,'TGLL - Lỗi','OK','Error') | Out-Null }
try {
    if(!$UiTest){Initialize-PortableData}
    if (!$UiTest -and (Test-Path $script:storePath)) { $decoded = Read-JsonData $script:storePath; $script:accounts = @($decoded) }
    if (!$UiTest -and (Test-Path $script:configPath)) {
        $loaded = Read-JsonData $script:configPath
        foreach ($key in @('jar','java','mode','arguments','socketPort','socketEnabled','proxyBridge','dungeonMaxOnline','dungeonCloseAfter','izanamiMaxOnline','izanamiGroupSize','izanamiCloseAfter','dailyPriority','soncapGroupSize','soncapPortals','soncapMaxOnline','soncapCloseAfter','questMaxOnline','questCloseAfter','beastMaxOnline','beastCloseAfter','launchDelaySeconds','launchConcurrency','autoReconnect')) { if ($null -ne $loaded.$key) { $script:settings[$key] = $loaded.$key } }
    }
} catch { Show-Error "Không đọc được dữ liệu. Ứng dụng sẽ dừng để tránh ghi đè: $($_.Exception.Message)"; exit 1 }
if(!$UiTest -and $script:defaultGame -and ([string]::IsNullOrWhiteSpace([string]$script:settings.jar) -or !(Test-Path -LiteralPath ([string]$script:settings.jar) -PathType Leaf))){$script:settings.jar=$script:defaultGame}
# HTTP is the selected default; migrate existing proxy records without touching endpoints/secrets.
if(!$UiTest){$httpChanged=$false;foreach($a in $script:accounts){if($a.proxy -and $a.proxy.type -ne 'http'){$a.proxy.type='http';$httpChanged=$true}};if($httpChanged){try{if(!(Test-Path -LiteralPath ($script:storePath+'.before-http.bak'))){Copy-Item -LiteralPath $script:storePath -Destination ($script:storePath+'.before-http.bak')};Save-Accounts}catch{Show-Error ('Không lưu được chuyển proxy HTTP: '+$_.Exception.Message);exit 1}}}
if ($UiTest) { $script:accounts=@([pscustomobject]@{id='test';username='demo';secret='unused-in-masked-view'}) }
$form = New-Object Windows.Forms.Form
$form.Text = 'TGLL_minCPU_v2 • Quản lý tài khoản'
$form.Size = New-Object Drawing.Size(940,640)
$form.MinimumSize = New-Object Drawing.Size(940,540)
$form.StartPosition = 'CenterScreen'
$form.Font = New-Object Drawing.Font('Segoe UI',10)
$form.BackColor = [Drawing.Color]::FromArgb(245,247,250)
function Add-Label($text,$x,$y,$width) {
    $c = New-Object Windows.Forms.Label; $c.Text=$text; $c.SetBounds($x,$y,$width,26); $form.Controls.Add($c); return $c
}
function Add-Button($text,$x,$y,$width,$handler) {
    $c=New-Object Windows.Forms.Button; $c.Text=$text; $c.SetBounds($x,$y,$width,34); $c.Add_Click($handler); $form.Controls.Add($c); return $c
}
$title = Add-Label 'TÀI KHOẢN TGLL' 22 18 500
$title.Font = New-Object Drawing.Font('Segoe UI',16,[Drawing.FontStyle]::Bold)
$title.Height=36
$null = Add-Label 'Lưu cục bộ • mật khẩu được mã hóa bằng tài khoản Windows hiện tại' 24 60 850
$grid = New-Object TGLL.ResponsiveGrid
$grid.SetBounds(24,104,875,300); $grid.Anchor='Top,Bottom,Left,Right'
$grid.ReadOnly=$true; $grid.AllowUserToAddRows=$false; $grid.AllowUserToDeleteRows=$false
$grid.SelectionMode='FullRowSelect'; $grid.MultiSelect=$true; $grid.RowHeadersVisible=$false
$grid.AutoSizeColumnsMode='Fill'; $grid.BackgroundColor=[Drawing.Color]::White
$grid.Columns.Add('username','Username') | Out-Null
$grid.Columns.Add('password','Password') | Out-Null
$grid.Columns.Add('proxy','HTTP') | Out-Null
$grid.Columns.Add('status','Trạng thái') | Out-Null
$openCol = New-Object Windows.Forms.DataGridViewButtonColumn
$openCol.Name='open'; $openCol.HeaderText='Chạy'; $openCol.Text='Open'; $openCol.UseColumnTextForButtonValue=$true
$openCol.FillWeight=40; $grid.Columns.Add($openCol)|Out-Null; $form.Controls.Add($grid)
$script:showPasswords=$false
$grid.Add_CellDoubleClick({param($sender,$e)
    if ($e.RowIndex -lt 0 -or $e.ColumnIndex -lt 0 -or $grid.Columns[$e.ColumnIndex].Name -ne 'status') {return}
    $id=[string]$grid.Rows[$e.RowIndex].Tag.id
    if ($script:accountJobs.ContainsKey($id)) {
        $detail=(Get-AccountJob $script:accountJobs[$id])
        if ($null -ne $detail -and $detail.Error) {Show-Error ((Get-LoginErrorText $detail.Error)+' ('+$detail.Error+')')}
    }
})
function Refresh-Grid {
    $grid.Rows.Clear()
    foreach($a in $script:accounts) {
        $pass='••••••••'
        if ($script:showPasswords) { try { $pass=Unprotect-Password $a.secret } catch { $pass='[Không giải mã được]' } }
        $proxyText='Không dùng'; if ($null -ne $a.proxy) { $proxyText=('{0}:{1}' -f $a.proxy.host,$a.proxy.port) }; $idx=$grid.Rows.Add([string]$a.username,$pass,$proxyText,'Chưa mở'); $grid.Rows[$idx].Tag=$a;if($grid.Columns.Contains('number')){$grid.Rows[$idx].Cells['number'].Value=$idx+1}
        if($grid.Columns.Contains('auto')){Set-AutoCell $grid.Rows[$idx] ([bool]$script:autoTrainWanted[[string]$a.id])}
        if((Get-Command Sync-FarmUpRow -ErrorAction SilentlyContinue) -and $grid.Columns.Contains('up')){Sync-FarmUpRow $grid.Rows[$idx]}
        if($grid.Columns.Contains('visibility')){Update-AccountVisibility $grid.Rows[$idx]}
        if($grid.Columns.Contains('bima')){Update-AccountBiMa $grid.Rows[$idx] $null}
    }
}
function Selected-Account {
    if ($grid.SelectedRows.Count -eq 0) { throw 'Hãy chọn một tài khoản.' }
    $grid.SelectedRows[0].Tag
}
function Edit-Account($existing) {
    $dialog=New-Object Windows.Forms.Form; $dialog.Text='Tài khoản'; $dialog.Size=New-Object Drawing.Size(440,495)
    $dialog.StartPosition='CenterParent'; $dialog.FormBorderStyle='FixedDialog'; $dialog.MaximizeBox=$false; $dialog.MinimizeBox=$false
    $dialog.Font=$form.Font
    $u=New-Object Windows.Forms.TextBox; $u.SetBounds(125,25,275,28)
    $p=New-Object Windows.Forms.TextBox; $p.SetBounds(125,72,275,28); $p.UseSystemPasswordChar=$true
    foreach($item in @(@('Username',25),@('Password',72))) {
        $label=New-Object Windows.Forms.Label; $label.Text=$item[0]; $label.SetBounds(15,$item[1],105,28); $dialog.Controls.Add($label)
    }
    $ph=New-Object Windows.Forms.TextBox; $ph.SetBounds(125,118,190,28)
    $pp=New-Object Windows.Forms.TextBox; $pp.SetBounds(325,118,75,28); $pp.Text='1080'
    $pu=New-Object Windows.Forms.TextBox; $pu.SetBounds(125,164,275,28)
    $ps=New-Object Windows.Forms.TextBox; $ps.SetBounds(125,210,275,28); $ps.UseSystemPasswordChar=$true
    foreach($item in @(@('HTTP host:port',118),@('Proxy username',164),@('Proxy password',210))) {
        $label=New-Object Windows.Forms.Label; $label.Text=$item[0]; $label.SetBounds(15,$item[1],110,36); $dialog.Controls.Add($label)
    }
    $quickLabel=New-Object Windows.Forms.Label; $quickLabel.Text='Dán proxy'; $quickLabel.SetBounds(15,260,105,28)
    $quickProxy=New-Object Windows.Forms.TextBox; $quickProxy.SetBounds(125,260,275,28); $quickProxy.UseSystemPasswordChar=$true
    $note=New-Object Windows.Forms.Label; $note.SetBounds(15,305,390,75)
    $note.Text='Dán host:port:username:password vào Dán proxy. QLTK tự tách host, port, username và password. Để trống cả host và Dán proxy để không dùng.'
    $quickProxy.Add_TextChanged({
        if ([string]::IsNullOrWhiteSpace($quickProxy.Text)) {$note.ForeColor=[Drawing.Color]::Black; $note.Text='Dán host:port:username:password để tự tách proxy.';return}
        try {
            $parsedProxy=Normalize-Proxy $quickProxy.Text
            $ph.Text=$parsedProxy.host; $pp.Text=[string]$parsedProxy.port; $pu.Text=$parsedProxy.username; $ps.Text=$parsedProxy.password
            $note.ForeColor=[Drawing.Color]::DarkGreen; $note.Text='Đã tách proxy HTTP vào các ô bên trên.'
        } catch {$note.ForeColor=[Drawing.Color]::DarkRed; $note.Text='Nhập host:port:username:password, hoặc host:port nếu không xác thực.'}
    })
    $ph.Add_TextChanged({
        # Also accept the full proxy line in the existing host box.
        if ($ph.Text -notmatch '^[^:]+:[0-9]+(?::|$)') {return}
        try {
            $parsedProxy=Normalize-Proxy $ph.Text
            $pp.Text=[string]$parsedProxy.port; $pu.Text=$parsedProxy.username; $ps.Text=$parsedProxy.password; $ph.Text=$parsedProxy.host
            $quickProxy.Clear(); $note.ForeColor=[Drawing.Color]::DarkGreen; $note.Text='Đã tách proxy HTTP vào host, port, username và password.'
        } catch {$note.ForeColor=[Drawing.Color]::DarkRed; $note.Text='Chuỗi proxy chưa hợp lệ. Dùng host:port:username:password.'}
    })
    $dialog.Controls.AddRange(@($u,$p,$ph,$pp,$pu,$ps,$quickLabel,$quickProxy,$note))
    if ($existing) {
        $u.Text=$existing.username; $p.Text=Unprotect-Password $existing.secret
        $storedProxy=Read-Proxy $existing
        if ($null -ne $storedProxy) { $ph.Text=$storedProxy.host; $pp.Text=[string]$storedProxy.port; $pu.Text=$storedProxy.username; $ps.Text=$storedProxy.password }
    }
    $ok=New-Object Windows.Forms.Button; $ok.Text='Lưu'; $ok.SetBounds(300,400,100,35)
    $ok.Add_Click({
        if ([string]::IsNullOrWhiteSpace($u.Text) -or $p.Text.Length -eq 0) { Show-Error 'Nhập đủ username và password.'; return }
        try {
            if ($quickProxy.Text.Trim().Length) {$null=Normalize-Proxy $quickProxy.Text}
            elseif ($ph.Text.Trim().Length) {$null=Normalize-Proxy ([pscustomobject]@{type='http';host=$ph.Text;port=$pp.Text;username=$pu.Text;password=$ps.Text})}
            elseif ($pu.Text.Length -or $ps.Text.Length) {throw 'Nhập host proxy hoặc xóa username/password proxy.'}
        } catch {Show-Error $_.Exception.Message;return}
        $dialog.DialogResult='OK'
    }); $dialog.Controls.Add($ok); $dialog.AcceptButton=$ok
    try {
        if ($UiTest) {
            $quickProxy.Text='127.0.0.1:1080:demo_proxy:demo:password'
            if ($ph.Text -ne '127.0.0.1' -or $pp.Text -ne '1080' -or $pu.Text -ne 'demo_proxy' -or $ps.Text -ne 'demo:password') {throw 'Proxy paste auto split failed'}
            $quickProxy.Text='invalid:port'
            if ($note.ForeColor -ne [Drawing.Color]::DarkRed) {throw 'Invalid proxy indicator failed'}
            $ph.Text='localhost:19000:other_proxy:other_password'
            if ($ph.Text -ne 'localhost' -or $pp.Text -ne '19000' -or $pu.Text -ne 'other_proxy' -or $ps.Text -ne 'other_password' -or $quickProxy.Text.Length) {throw 'Host box proxy auto split failed'}
            return
        }
        if ($dialog.ShowDialog($form) -eq 'OK') {
            $name=$u.Text.Trim()
            $duplicates=@($script:accounts | Where-Object { $_.username -eq $name -and (!$existing -or $_.id -ne $existing.id) })
            if ($duplicates.Count) { throw 'Username này đã có trong danh sách.' }
            $proxy=$null
            if ($quickProxy.Text.Trim().Length) { $proxy=Normalize-Proxy $quickProxy.Text }
            elseif ($ph.Text.Trim().Length) { $proxy=Normalize-Proxy ([pscustomobject]@{type='http';host=$ph.Text;port=$pp.Text;username=$pu.Text;password=$ps.Text}) }
            elseif ($pu.Text.Length -or $ps.Text.Length) { throw 'Nhập host proxy, hoặc xóa thông tin xác thực proxy để không dùng.' }
            $entry=[pscustomobject]@{id=([guid]::NewGuid().ToString());username=$name;secret=(Protect-Password $p.Text);proxy=(Protect-Proxy $proxy)}
            $old=$script:accounts
            if ($existing) { foreach($field in @('trainSkills','receiveKey','dungeonHigh','izanami','gameOverrides','gameSettings')){if($existing.PSObject.Properties[$field]){$entry | Add-Member NoteProperty $field $existing.$field}}; $entry.id=$existing.id; $script:accounts=@($script:accounts | ForEach-Object { if ($_.id -eq $existing.id) { $entry } else { $_ } }) }
            else { $script:accounts=@($script:accounts)+@($entry) }
            try { Save-Accounts } catch { $script:accounts=$old; throw }
            Refresh-Grid
        }
    } finally { $dialog.Dispose() }
}
function Open-Account($a,[bool]$reconnect=$false) {
    if((Get-Command Get-CloakQueuedMember -ErrorAction SilentlyContinue) -and $null -ne (Get-CloakQueuedMember ([string]$a.id))){$status.Text="Tài khoản đang chờ mua/mặc áo: "+$a.username;return}
    if((Get-Command Get-DistributionQueuedMember -ErrorAction SilentlyContinue) -and $null -ne (Get-DistributionQueuedMember ([string]$a.id))){$status.Text='Tài khoản đang chờ chia đồ.';return}
    if((Get-Command Get-MailQueuedMember -ErrorAction SilentlyContinue) -and $null -ne (Get-MailQueuedMember ([string]$a.id))){$status.Text="Tài khoản đang chờ lượt gom đồ: "+$a.username;return}
    if((Get-Command Get-QuestQueuedMember -ErrorAction SilentlyContinue) -and $null -ne (Get-QuestQueuedMember ([string]$a.id))){$status.Text='Tài khoản đang chờ lượt nhiệm vụ: '+$a.username;return}
    if((Get-Command Get-BeastQueuedMember -ErrorAction SilentlyContinue) -and $null -ne (Get-BeastQueuedMember ([string]$a.id))){$status.Text='Tài khoản đang chờ linh thú';return}
    if($script:closingAll){$status.Text='Đang tắt tất cả game; đợi đóng xong rồi mở lại.';return}
    if((Get-AccountLaunchMode $a) -eq 'beast' -and [string]$script:beastLaunchingAccountId -ne [string]$a.id -and (Get-Command Add-BeastQueue -ErrorAction SilentlyContinue)){Add-BeastQueue @($a);return}
    if((Get-AccountLaunchMode $a) -eq 'soncap' -and [string]$script:beastLaunchingAccountId -ne [string]$a.id -and [string]$script:soncapLaunchingAccountId -ne [string]$a.id -and (Get-Command Add-SonCapQueue -ErrorAction SilentlyContinue)){Add-SonCapQueue @($a);return}
    if((Get-Command Get-SonCapQueuedMember -ErrorAction SilentlyContinue) -and $null -ne (Get-SonCapQueuedMember ([string]$a.id))){$status.Text='Tài khoản đang chờ lượt Sơn Cáp: '+$a.username;return}
    if((Get-Command Get-IzanamiQueuedMember -ErrorAction SilentlyContinue) -and $null -ne (Get-IzanamiQueuedMember ([string]$a.id))){$status.Text='Tài khoản đang chờ lượt cấm thuật: '+$a.username;return}
    if((Get-Command Get-DungeonQueuedMember -ErrorAction SilentlyContinue) -and $null -ne (Get-DungeonQueuedMember ([string]$a.id))){$status.Text='Tài khoản đang chờ lượt địa cung: '+$a.username;return}
    if(Test-AccountOpenBlocked $a){$status.Text='Tài khoản đã có game đang chạy hoặc đang mở: '+$a.username;return}
    $proxy=Read-Proxy $a
    $launched=Start-ManagedAccount $a $proxy ($autoLoginCheck.Checked -or $reconnect)
    $status.Text="Đã yêu cầu mở game cho $($a.username)."
    if (!$autoLoginCheck.Checked -and $script:settings.mode -eq 'manual') { $status.Text+=' Chọn tài khoản, sao chép username/password để điền vào cửa sổ đăng nhập.' }
    if ($launched.job_id) {$script:accountJobs[$a.id]=$launched.job_id; $script:lastLoginJob=$launched.job_id; $status.Text='Đã xếp hàng đăng nhập: '+$a.username}
    if ($launched.pid -gt 0) {$script:accountWindowPids[[string]$a.id]=[int]$launched.pid}
    if ($null -ne $proxy) { $status.Text='Bộ chuyển báo đã mở game qua HTTP. PID: '+$launched.pid }
    foreach($row in $grid.Rows){if([string]$row.Tag.id -eq [string]$a.id){Update-AccountVisibility $row;break}}
}
$grid.Add_CellContentClick({param($sender,$e) if ($e.RowIndex -ge 0 -and $grid.Columns[$e.ColumnIndex].Name -eq 'open') { try { Open-Account $grid.Rows[$e.RowIndex].Tag } catch { Show-Error $_.Exception.Message } }})
$buttons=@()
$buttons+=Add-Button 'Add' 24 422 80 {try { Edit-Account $null } catch { Show-Error $_.Exception.Message }}
$buttons+=Add-Button 'Edit' 114 422 80 {try { Edit-Account (Selected-Account) } catch { Show-Error $_.Exception.Message }}
$buttons+=Add-Button 'Delete' 204 422 80 {try {
    $a=Selected-Account
    if ([Windows.Forms.MessageBox]::Show("Xóa tài khoản $($a.username)?",'Xác nhận','YesNo','Question') -eq 'Yes') {
        $old=$script:accounts; $script:accounts=@($script:accounts | Where-Object id -ne $a.id)
        try { Save-Accounts } catch { $script:accounts=$old; throw }; Refresh-Grid
    }
} catch { Show-Error $_.Exception.Message }}
$buttons+=Add-Button 'Copy username' 300 422 145 {try { [Windows.Forms.Clipboard]::SetText([string](Selected-Account).username); $status.Text='Đã sao chép username.' } catch { Show-Error $_.Exception.Message }}
$buttons+=Add-Button 'Copy password' 455 422 145 {try { [Windows.Forms.Clipboard]::SetText((Unprotect-Password (Selected-Account).secret)); $status.Text='Đã sao chép password. Dùng Xóa clipboard sau khi điền.' } catch { Show-Error $_.Exception.Message }}
$buttons+=Add-Button 'Xóa clipboard' 610 422 145 {[Windows.Forms.Clipboard]::Clear(); $status.Text='Đã xóa clipboard.'}
$buttons+=Add-Button 'Nhập CSV' 770 422 129 {try {
    $file=New-Object Windows.Forms.OpenFileDialog; $file.Filter='Tài khoản CSV (*.csv)|*.csv'
    try {
        if ($file.ShowDialog($form) -ne 'OK') {return}
        $rows=@(Import-Csv -LiteralPath $file.FileName -Encoding UTF8)
        if ($rows.Count -lt 1 -or $rows.Count -gt 10000) {throw 'CSV phải có 1-10000 tài khoản và cột username,password.'}
        $items=@($rows | ForEach-Object {
            $item=[pscustomobject]@{username=$_.username;password=$_.password}
            if ($_.PSObject.Properties['proxy']) {
                $proxyLine=([string]$_.proxy).Trim(); $proxyValue=$null; if ($proxyLine.Length) {$proxyValue=$proxyLine}
                $item | Add-Member -NotePropertyName proxy -NotePropertyValue $proxyValue
            }
            elseif ($_.PSObject.Properties['proxy_host']) {
                $proxy=$null
                if (![string]::IsNullOrWhiteSpace($_.proxy_host)) {$proxy=[pscustomobject]@{type='http';host=$_.proxy_host;port=$_.proxy_port;username=$_.proxy_username;password=$_.proxy_password}}
                $item | Add-Member -NotePropertyName proxy -NotePropertyValue $proxy
            }
            $item
        })
        $plan=Prepare-AccountBatch $items
        if ([Windows.Forms.MessageBox]::Show('Nhập '+$plan.changed.Count+' tài khoản? Username đã có sẽ được cập nhật mật khẩu.','Nhập CSV','YesNo','Question') -ne 'Yes') {return}
        Commit-AccountBatch $plan
        $status.Text='Đã nhập '+$plan.changed.Count+' tài khoản từ CSV.'
    } finally {$file.Dispose()}
} catch {Show-Error $_.Exception.Message}}
$show=New-Object Windows.Forms.CheckBox; $show.Text='Hiện mật khẩu'; $show.SetBounds(24,474,170,30); $show.Anchor='Bottom,Left'
$show.Add_CheckedChanged({$script:showPasswords=$show.Checked; Refresh-Grid}); $form.Controls.Add($show)
$settingsButton=Add-Button 'Cài đặt game' 204 474 190 {
    $d=New-Object Windows.Forms.Form; $d.Text='Cài đặt tích hợp'; $d.Size=New-Object Drawing.Size(680,430)
    $d.StartPosition='CenterParent'; $d.Font=$form.Font; $d.FormBorderStyle='FixedDialog'; $d.MaximizeBox=$false
    $controls=@()
    foreach($row in @(@('File game',20),@('Java (chỉ JAR)',65),@('Chế độ',110),@('Đối số JSON',155))) {
        $l=New-Object Windows.Forms.Label; $l.Text=$row[0]; $l.SetBounds(15,$row[1],120,28); $d.Controls.Add($l)
    }
    $jar=New-Object Windows.Forms.TextBox; $jar.SetBounds(140,20,420,28); $jar.Text=$script:settings.jar
    $browse=New-Object Windows.Forms.Button; $browse.Text='Chọn'; $browse.SetBounds(570,20,75,28)
    $browse.Add_Click({$f=New-Object Windows.Forms.OpenFileDialog; $f.Filter='Game (*.exe;*.jar)|*.exe;*.jar'; if ($f.ShowDialog() -eq 'OK') {$jar.Text=$f.FileName}; $f.Dispose()})
    $java=New-Object Windows.Forms.TextBox; $java.SetBounds(140,65,505,28); $java.Text=$script:settings.java; $java.Enabled=([IO.Path]::GetExtension($jar.Text) -eq '.jar')
    $jar.Add_TextChanged({$java.Enabled=([IO.Path]::GetExtension($jar.Text) -eq '.jar')})
    $mode=New-Object Windows.Forms.ComboBox; $mode.SetBounds(140,110,505,28); $mode.DropDownStyle='DropDownList'
    $mode.Items.AddRange(@('Mở game + sao chép thủ công','Truyền đối số đã xác minh')); $mode.SelectedIndex=0
    if ($script:settings.mode -eq 'arguments') {$mode.SelectedIndex=1}
    $argsBox=New-Object Windows.Forms.TextBox; $argsBox.SetBounds(140,155,505,70); $argsBox.Multiline=$true
    $argsBox.Text=ConvertTo-Json -InputObject @($script:settings.arguments) -Compress
    $hint=New-Object Windows.Forms.Label; $hint.SetBounds(15,238,630,90)
    $hint.Text='Chỉ bật truyền đối số khi đã biết game hỗ trợ. TGLL này chưa xác minh được giao thức đối số. Mỗi phần tử JSON là một đối số; dùng {username} và {password}. Ví dụ minh họa: ["--user","{username}","--pass","{password}"]. Mật khẩu truyền qua đối số có thể bị công cụ xem tiến trình đọc được.'
    $save=New-Object Windows.Forms.Button; $save.Text='Lưu cài đặt'; $save.SetBounds(480,340,165,34)
    $save.Add_Click({try {
        if (!(Test-Path -LiteralPath $jar.Text -PathType Leaf) -or [IO.Path]::GetExtension($jar.Text) -notin @('.exe','.jar')) {throw 'Chọn file .exe hoặc .jar hợp lệ.'}
        if ([IO.Path]::GetExtension($jar.Text) -eq '.jar' -and [string]::IsNullOrWhiteSpace($java.Text)) {throw 'Nhập javaw.exe hoặc đường dẫn Java.'}
        if (!$argsBox.Text.Trim().StartsWith('[')) {throw 'Đối số phải là một mảng JSON.'}
        $decodedArgs=ConvertFrom-Json -InputObject $argsBox.Text; $parsed=@($decodedArgs)
        foreach($arg in $parsed) {if ($arg -isnot [string]) {throw 'Mỗi đối số phải là chuỗi.'}}
        $new=@{jar=[IO.Path]::GetFullPath($jar.Text);java=$java.Text.Trim();mode='manual';arguments=$parsed;socketPort=$script:settings.socketPort;socketEnabled=$script:settings.socketEnabled;proxyBridge=$script:settings.proxyBridge;dungeonMaxOnline=$script:settings.dungeonMaxOnline;dungeonCloseAfter=$script:settings.dungeonCloseAfter;izanamiMaxOnline=$script:settings.izanamiMaxOnline;izanamiGroupSize=$script:settings.izanamiGroupSize;izanamiCloseAfter=$script:settings.izanamiCloseAfter;beastMaxOnline=$script:settings.beastMaxOnline;beastCloseAfter=$script:settings.beastCloseAfter;questMaxOnline=$script:settings.questMaxOnline;questCloseAfter=$script:settings.questCloseAfter;launchDelaySeconds=$script:settings.launchDelaySeconds;launchConcurrency=$script:settings.launchConcurrency;dailyPriority=$dailyPriority.Checked;soncapMaxOnline=$script:settings.soncapMaxOnline;soncapCloseAfter=$script:settings.soncapCloseAfter;soncapGroupSize=6;soncapPortals=$script:settings.soncapPortals}
        if ($mode.SelectedIndex -eq 1) {$new.mode='arguments'}
        Write-JsonAtomic $script:configPath $new; $script:settings=$new; $d.DialogResult='OK'
    } catch {Show-Error $_.Exception.Message}})
    $d.Controls.AddRange(@($jar,$browse,$java,$mode,$argsBox,$hint,$save))
    $null=$d.ShowDialog($form); $d.Dispose()
}
$settingsButton.Anchor='Bottom,Left'
foreach($b in $buttons) {$b.Anchor='Bottom,Left'}
$status=Add-Label 'Nhập CSV, chọn tài khoản rồi Open. Không chuyển cửa sổ khi QLTK đang điền.' 24 550 870
$status.Height=35; $status.Anchor='Bottom,Left,Right'
$socketButton=Add-Button 'Socket / Proxy' 410 474 160 {
    $d=New-Object Windows.Forms.Form; $d.Text='Socket TCP / HTTP'; $d.Size=New-Object Drawing.Size(690,430)
    $d.StartPosition='CenterParent'; $d.Font=$form.Font; $d.FormBorderStyle='FixedDialog'; $d.MaximizeBox=$false
    $head=New-Object Windows.Forms.Label; $head.SetBounds(15,15,650,32)
    $head.Text='API chỉ lắng nghe 127.0.0.1. Mỗi lệnh phải có token.'
    $enable=New-Object Windows.Forms.CheckBox; $enable.Text='Bật socket'; $enable.SetBounds(15,55,140,28); $enable.Checked=[bool]$script:settings.socketEnabled
    $portLabel=New-Object Windows.Forms.Label; $portLabel.Text='Port'; $portLabel.SetBounds(170,55,50,28)
    $port=New-Object Windows.Forms.NumericUpDown; $port.SetBounds(230,55,115,28); $port.Minimum=1024; $port.Maximum=65535; $port.Value=[int]$script:settings.socketPort
    $token=New-Object Windows.Forms.TextBox; $token.SetBounds(15,100,480,28); $token.ReadOnly=$true; $token.UseSystemPasswordChar=$true; $token.Text=$script:socketToken
    $copy=New-Object Windows.Forms.Button; $copy.Text='Copy token'; $copy.SetBounds(510,100,140,30); $copy.Add_Click({if ($token.Text.Length) {[Windows.Forms.Clipboard]::SetText($token.Text)}})
    $label=New-Object Windows.Forms.Label; $label.Text='EXE bộ chuyển mạng (giao thức stdin tgll-launch-v1)'; $label.SetBounds(15,152,640,28)
    $bridge=New-Object Windows.Forms.TextBox; $bridge.SetBounds(15,185,480,28); $bridge.Text=[string]$script:settings.proxyBridge
    $browse=New-Object Windows.Forms.Button; $browse.Text='Chọn EXE'; $browse.SetBounds(510,185,140,30)
    $browse.Add_Click({$f=New-Object Windows.Forms.OpenFileDialog; $f.Filter='Bộ chuyển (*.exe)|*.exe'; if ($f.ShowDialog() -eq 'OK') {$bridge.Text=$f.FileName}; $f.Dispose()})
    $hint=New-Object Windows.Forms.Label; $hint.SetBounds(15,238,640,78)
    $hint.Text='Open dùng bộ nối trực tiếp trong game để đăng nhập, chọn nhân vật số 1 và nhận chìa khóa từ Raikage. Proxy HTTP được xử lý sẵn; bạn không cần chọn EXE bộ chuyển. Để trống ô phía trên khi dùng chế độ mặc định.'
    $save=New-Object Windows.Forms.Button; $save.Text='Lưu / áp dụng'; $save.SetBounds(480,335,170,34)
    $save.Add_Click({try {
        if ($bridge.Text.Length -and (!(Test-Path -LiteralPath $bridge.Text -PathType Leaf) -or [IO.Path]::GetExtension($bridge.Text) -ne '.exe')) { throw 'Chọn EXE bộ chuyển hợp lệ.' }
        $updated=@{}; foreach($key in $script:settings.Keys) {$updated[$key]=$script:settings[$key]}
        $updated.socketPort=[int]$port.Value; $updated.socketEnabled=$enable.Checked; $updated.proxyBridge=$bridge.Text.Trim()
        Stop-SocketApi
        try {
            if ($updated.socketEnabled) { Start-SocketApi $updated.socketPort }
            Write-JsonAtomic $script:configPath $updated
        } catch {
            Stop-SocketApi
            if ($script:settings.socketEnabled) { try { Start-SocketApi ([int]$script:settings.socketPort) } catch {} }
            throw
        }
        $script:settings=$updated
        $status.Text='Đã áp dụng Socket / Proxy.'; $d.DialogResult='OK'
    } catch {Show-Error $_.Exception.Message}})
    $d.Controls.AddRange(@($head,$enable,$portLabel,$port,$token,$copy,$label,$bridge,$browse,$hint,$save))
    $null=$d.ShowDialog($form); $d.Dispose()
}
$socketButton.Anchor='Bottom,Left'
$autoLoginCheck=New-Object Windows.Forms.CheckBox
$autoLoginCheck.Text='Đăng nhập trực tiếp'; $autoLoginCheck.SetBounds(580,474,165,30); $autoLoginCheck.Checked=$true; $autoLoginCheck.Anchor='Bottom,Left'; $form.Controls.Add($autoLoginCheck)
$bulkOpen=Add-Button 'Open đã chọn' 750 474 149 {
    $launchRows=@(Get-LaunchRows)
    if ($launchRows.Count -eq 0) {Show-Error 'Đánh dấu cột Chọn hoặc chọn các dòng cần mở.';return}
    $beastRows=@($launchRows | Where-Object {(Get-AccountLaunchMode $_.Tag) -eq 'beast'});if($beastRows.Count){try{Add-BeastQueue @($beastRows | ForEach-Object {$_.Tag})}catch{Show-Error $_.Exception.Message;return}}
    $soncapRows=@($launchRows | Where-Object {(Get-AccountLaunchMode $_.Tag) -eq 'soncap'});if($soncapRows.Count){try{Add-SonCapQueue @($soncapRows | ForEach-Object {$_.Tag})}catch{Show-Error $_.Exception.Message;return}}
    $izanamiRows=@($launchRows | Where-Object {(Get-AccountLaunchMode $_.Tag) -eq 'izanami'})
    if($izanamiRows.Count){try{Add-IzanamiQueue @($izanamiRows | ForEach-Object {$_.Tag})}catch{Show-Error $_.Exception.Message;return}}
    $questRows=@($launchRows | Where-Object {(Get-AccountLaunchMode $_.Tag) -eq 'quest'})
    if($questRows.Count){try{Add-QuestQueue @($questRows | ForEach-Object {$_.Tag})}catch{Show-Error $_.Exception.Message;return}}
    $dungeonRows=@($launchRows | Where-Object {(Get-AccountLaunchMode $_.Tag) -eq 'dungeon' -and $_.Tag.dungeonHigh})
    if($dungeonRows.Count){try{Add-DungeonQueue @($dungeonRows | ForEach-Object {$_.Tag})}catch{Show-Error $_.Exception.Message;return}}
    $errors=0
    foreach($row in @($launchRows | Where-Object {(Get-AccountLaunchMode $_.Tag) -in @('farm') -or ((Get-AccountLaunchMode $_.Tag) -in @('normal','dungeon') -and !$_.Tag.dungeonHigh)})) {try {Open-Account $row.Tag} catch {$errors++}}
    if ($errors) {Show-Error ($errors.ToString()+' tài khoản chưa xếp hàng được. Kiểm tra proxy và đường dẫn game; không có bộ chuyển thì tài khoản có proxy sẽ không mở.')}
}
$bulkOpen.Anchor='Bottom,Left'
$cancelButton=Add-Button 'Hủy hàng đợi' 24 515 155 {if(Get-Command Stop-AutoAll -ErrorAction SilentlyContinue){Stop-AutoAll};if(Get-Command Reset-DungeonQueue -ErrorAction SilentlyContinue){Reset-DungeonQueue;Update-DungeonQueueStatus};if(Get-Command Stop-MailCollection -ErrorAction SilentlyContinue){Stop-MailCollection};if(Get-Command Reset-SonCapQueue -ErrorAction SilentlyContinue){Reset-SonCapQueue;Update-SonCapQueueStatus};if(Get-Command Reset-IzanamiQueue -ErrorAction SilentlyContinue){Reset-IzanamiQueue;Update-IzanamiQueueStatus};if(Get-Command Reset-QuestQueue -ErrorAction SilentlyContinue){Reset-QuestQueue;Update-QuestQueueStatus};[TGLL.AutoLogin]::CancelAll(); [TGLL.NativeBackend]::CancelAll(); $status.Text='Đã yêu cầu hủy các tác vụ đăng nhập chưa hoàn tất.'}; $cancelButton.Anchor='Bottom,Left'
$npcIdsButton=Add-Button 'Xem ID NPC' 364 515 155 {
    if ($grid.SelectedRows.Count -eq 0) {Show-Error 'Chọn tài khoản cần xem.';return}
    $id=[string]$grid.SelectedRows[0].Tag.id
    if (!$script:accountJobs.ContainsKey($id)) {Show-Error 'Tài khoản chưa được mở trong phiên QLTK này.';return}
    $npcJob=Get-AccountJob $script:accountJobs[$id]
    $message='Chưa đọc được danh sách NPC.'
    if ($npcJob.PSObject.Properties['NpcDetails']) {
        $message='Trạng thái: '+$npcJob.State+' / '+$npcJob.Stage+' / '+$npcJob.Error
        $message+="`r`nRaikage: ID "+$npcJob.NpcId+', ID mẫu '+$npcJob.NpcTemplateId
        if ($npcJob.PSObject.Properties['CapturedNpcId']) {$message+="`r`nNPC đã chọn trong game: "+$npcJob.CapturedNpcName+' — ID '+$npcJob.CapturedNpcId+'/'+$npcJob.CapturedTemplateId}
        $message+="`r`nDanh sách (ID trên bản đồ / ID mẫu: tên):`r`n"+$npcJob.NpcDetails
        if (!$npcJob.NpcDetails) {$message+="`r`nChưa đọc được NPC; kiểm tra lỗi ở bước trước. ID -1 nghĩa là chưa xác định."}
    }
    $null=[Windows.Forms.MessageBox]::Show($message,'ID NPC')
}; $npcIdsButton.Anchor='Bottom,Left'
$replayNpcButton=Add-Button 'Gọi lại Raikage' 534 515 155 {
    try {
        if ($grid.SelectedRows.Count -eq 0) {throw 'Chọn tài khoản cần gọi NPC.'}
        $id=[string]$grid.SelectedRows[0].Tag.id
        if (!$script:accountJobs.ContainsKey($id)) {throw 'Tài khoản chưa mở trong phiên này.'}
        [TGLL.NativeBackend]::ReplayCapturedNpc($script:accountJobs[$id])
        $script:lastLoginJob=$script:accountJobs[$id];$status.Text='Đã xếp lệnh gọi Raikage bằng ID vừa bắt; chờ menu từ máy chủ.'
    } catch {Show-Error $_.Exception.Message}
}; $replayNpcButton.Anchor='Bottom,Left'
$menuLogButton=Add-Button 'Log menu' 704 515 155 {
    if ($grid.SelectedRows.Count -eq 0) {Show-Error 'Chọn tài khoản cần xem menu.';return}
    $id=[string]$grid.SelectedRows[0].Tag.id
    if (!$script:accountJobs.ContainsKey($id)) {Show-Error 'Tài khoản chưa mở trong phiên này.';return}
    $menuJob=Get-AccountJob $script:accountJobs[$id]
    if (!$menuJob.PSObject.Properties['DynamicMenuJson']) {Show-Error 'Tài khoản chưa dùng bộ nối trực tiếp.';return}
    $dialog=New-Object Windows.Forms.Form;$dialog.Text='DynamicMenu / ID NPC';$dialog.ClientSize=New-Object Drawing.Size(710,440);$dialog.StartPosition='CenterParent'
    $box=New-Object Windows.Forms.TextBox;$box.Multiline=$true;$box.ReadOnly=$true;$box.WordWrap=$false;$box.ScrollBars='Both';$box.Dock='Fill'
    $box.Text='NPC đã chọn: '+$menuJob.CapturedNpcName+' — ID '+$menuJob.CapturedNpcId+'/'+$menuJob.CapturedTemplateId+"`r`n"
    $box.Text+='Menu tag: '+$menuJob.MenuTag+' / chỉ số đã chọn: '+$menuJob.MenuSelectedIndex+"`r`n"
    for($i=0;$i -lt $menuJob.MenuOptions.Length;$i++) {$box.Text+='['+$i+'] '+$menuJob.MenuOptions[$i]+"`r`n"}
    $box.Text+="`r`nJSON: "+$menuJob.DynamicMenuJson+"`r`nFile log: "+$menuJob.LogFile+"`r`nLỗi ghi log: "+$menuJob.LogError
    $dialog.Controls.Add($box);$null=$dialog.ShowDialog($form);$dialog.Dispose()
}; $menuLogButton.Anchor='Bottom,Left'
. (Join-Path $PSScriptRoot 'CloseAllUi.ps1')
$closeAllButton=Add-Button 'Tắt toàn bộ' 194 515 155 {Start-CloseAllUi};$closeAllButton.Anchor='Bottom,Left'
$script:lastLoginJob=''
Refresh-Grid
. (Join-Path $PSScriptRoot 'BulkAccountUi.ps1')
. (Join-Path $PSScriptRoot 'UiLayout.ps1')
. (Join-Path $PSScriptRoot 'RetryUi.ps1')
. (Join-Path $PSScriptRoot 'UiPerformance.ps1')
. (Join-Path $PSScriptRoot 'LiveUpdate.ps1')
function Invoke-ManagerTick {
    Poll-SocketRequests
    if($script:closingAll){return}
    Initialize-GitHubUpdate
    Sync-CaptchaRuntime
    Invoke-AutoHideAfterLogin
    Update-VisibleCharacterNames
    $script:uiTicks++
    if(($script:uiTicks % 10) -eq 0){Retry-FailedAccounts}
    Invoke-ActivityQueuePhase
    if($grid.Rows.Count -gt 32){Invoke-LargeManagerRefresh;return}
    if(($script:uiTicks % 10) -ne 0 -and !$script:uiRefreshPending){return}
    # Native combat and queue checks above keep running; coalesce UI-wide preference/paint passes during wheel/scrollbar input.
    if([TGLL.UiPerformance]::IsScrolling){$script:uiRefreshPending=$true;return}
    $script:uiRefreshPending=$false
    Sync-IzanamiTeams;Sync-DailyPriorityPreferences;Sync-SonCapPreferences;Sync-CloakQueue;Sync-AutoRows;Sync-AutoItemPreferences;Sync-QuestPreferences;Sync-GiftPreferences;Sync-MailCollection;Sync-Distribution
    # Keep queue/preference work above running while moving/minimized; defer only rendering.
    if($script:uiDragging -or $form.WindowState -eq [Windows.Forms.FormWindowState]::Minimized){return}
    Update-ActiveTabUi;Update-UiSummary
    Update-ManagerStatus
}
function Update-ManagerStatusRows($rows) {
    if(!$script:managerStatusLabels){$script:managerStatusLabels=@{reconnecting='Mất kết nối: chuẩn bị đăng nhập lại';queued='Đang chờ';retrying='Chờ mở lại';starting='Đang mở';typing='Đang truyền';submitted='Đã gửi đăng nhập';selecting_character='Đang chọn NV 1';character_submitted='Đã gửi chọn NV 1';waiting_map='Đợi bản đồ';waiting_popup='Đợi 5 giây';confirming_popup='Đang xác nhận';finding_onoki='Đang tìm Onoki';choosing_izanami='Đang chọn Izanami';izanami_submitted='Đã gửi vào cấm thuật';izanami_running='Cấm thuật: chờ thông báo ải';izanami_unavailable='Chưa vào cấm thuật';using_travel_item='Đang dùng phù';choosing_village='Đang chọn Làng';choosing_sand_village='Đang chọn Làng Cát';waiting_sand_village='Đợi đến Làng Cát';finding_npc='Đang tìm Raikage';choosing_key='Đang nhận chìa khóa';waiting_key_baseline='Đợi đọc số chìa trước khi nhận';waiting_key_receipt='Đợi xác nhận nhận chìa';key_submitted='Đã gửi nhận chìa khóa';creating_party='Đang tạo/đợi nhóm';waiting_dungeon='Chuẩn bị vào địa cung';choosing_dungeon='Đang chọn địa cung cao cấp';checking_dungeon_keys='Kiểm tra chìa cao cấp';dungeon_no_keys='Dừng: hết chìa cao cấp';dungeon_stopped='Địa cung đã dừng';dungeon_running='Địa cung: đợi quay lại';dungeon_submitted='Đã gửi vào địa cung cao cấp';startup_unavailable='Cần kiểm tra sau đăng nhập';npc_unavailable='Chưa nhận chìa khóa';manual_entry='Cần kiểm tra game';opened='Đã mở';failed='Lỗi';cancelled='Đã hủy';closed='Đã tắt'}}
    $states=$script:managerStatusLabels
    foreach($row in $rows) {
        if ($script:accountJobs.ContainsKey([string]$row.Tag.id)) {
            $rowJob=(Get-AccountJob $script:accountJobs[[string]$row.Tag.id])
            if ($null -ne $rowJob) {
                $text=$states[$rowJob.State]
                if($rowJob -is [TGLL.NativeJob] -and $rowJob.State -eq 'izanami_running'){$text=[TGLL.NativeBackend]::IzanamiStageStatus($rowJob)}
                if($rowJob.IzanamiEnabled -and $rowJob.IzanamiRequested -and $rowJob.TeamStatus){$text+=' • '+$rowJob.TeamStatus}
                if($rowJob.StartAttempts -gt 1){$text+=' (lần '+$rowJob.StartAttempts+'/3)'}
                if ($rowJob.Error) {
                    $text+=': '+$rowJob.Error
                    $detail=(Get-LoginErrorText $rowJob.Error)+' ('+$rowJob.Error+').'
                    if ($rowJob.State -eq 'npc_unavailable' -and $rowJob.PSObject.Properties['NpcCount']) {$detail+=' Bước '+$rowJob.ReadyPhase+', danh sách có '+$rowJob.NpcCount+' NPC.'}
                    $row.Cells['status'].ToolTipText=$detail+' Nhấp đúp để xem chi tiết.'
                }
                else {$row.Cells['status'].ToolTipText=''}
                if($script:farmUpRequests -and $script:farmUpRequests.ContainsKey([string]$row.Tag.id) -and $script:farmUpRequests[[string]$row.Tag.id].BlockedJob -eq $rowJob.Id){$text=$row.Cells['up'].ToolTipText}
                if($rowJob -is [TGLL.NativeJob] -and $rowJob.SonCapEnabled -and !$rowJob.CloseRequested -and $rowJob.State -notin @('closed','cancelled','failed')){$text=[TGLL.NativeBackend]::SonCapDisplayStatus($rowJob)}
                if($rowJob -is [TGLL.NativeJob] -and $rowJob.DailyActive){$text=$rowJob.DailyStatus}
                if($rowJob -is [TGLL.NativeJob]){$questText=[TGLL.NativeBackend]::QuestDisplayStatus($rowJob);if($questText){$text=$questText}}
                $curseText='';if($rowJob -is [TGLL.NativeJob]){$curseText=[TGLL.NativeBackend]::CurseDisplayStatus($rowJob)};if($curseText){$text=$curseText;$row.Cells['status'].ToolTipText='Nhân vật bị yểm bùa; mở game và nhập mã thủ công để giải bùa.'}
                if($rowJob -is [TGLL.NativeJob] -and $rowJob.ActivityWatchStatus){$text=$rowJob.ActivityWatchStatus};if ($row.Cells['status'].Value -ne $text) {$row.Cells['status'].Value=$text}
            }
        }
    }
 }
function Update-ManagerStatus {
    $rows=$grid.Rows
    if($grid.Rows.Count -gt 32 -and $grid.FirstDisplayedScrollingRowIndex -ge 0){
        $first=$grid.FirstDisplayedScrollingRowIndex;$last=[Math]::Min($grid.Rows.Count-1,$first+$grid.DisplayedRowCount($false))
        $rows=@(for($n=$first;$n -le $last;$n++){$grid.Rows[$n]})
        if($grid.SelectedRows.Count -and $rows -notcontains $grid.SelectedRows[0]){$rows+=,$grid.SelectedRows[0]}
    }
    Update-ManagerStatusRows $rows
    Update-SonCapQueueStatus;Update-IzanamiQueueStatus;Update-QuestQueueStatus
    if ($script:lastLoginJob) {
        $job=(Get-AccountJob $script:lastLoginJob)
        if ($null -ne $job) {
            $stateText=@{queued='đang chờ';retrying='đang chờ mở lại';starting='đang mở game';typing='đang truyền tài khoản';submitted='đã gọi đăng nhập trực tiếp';selecting_character='đang chờ danh sách nhân vật';character_submitted='đã gọi vào nhân vật số 1 trực tiếp';waiting_map='đang chờ vào bản đồ';waiting_popup='đang đợi 5 giây trước khi xác nhận';confirming_popup='đang xác nhận popup trước khi tìm NPC';finding_onoki='đang tìm Onoki';choosing_izanami='đang chọn Cấm thuật Izanami';izanami_submitted='đã gửi vào cấm thuật';izanami_running='Cấm thuật: chờ thông báo ải';izanami_unavailable='chưa vào cấm thuật';using_travel_item='Đang dùng phù';choosing_village='Đang chọn Làng';choosing_sand_village='Đang chọn Làng Cát';waiting_sand_village='Đợi đến Làng Cát';finding_npc='đang tìm Raikage Đệ Tam';choosing_key='đang chọn Nhận chìa khóa';waiting_key_baseline='Đợi đọc số chìa trước khi nhận';waiting_key_receipt='Đợi chìa trong túi tăng để tắt game';key_submitted='đã gửi lệnh Nhận chìa khóa một lần';creating_party='Đang tạo/đợi nhóm';waiting_dungeon='chuẩn bị vào địa cung';choosing_dungeon='đang chọn địa cung cao cấp';checking_dungeon_keys='kiểm tra chìa cao cấp';dungeon_no_keys='đã dừng: hết chìa cao cấp';dungeon_stopped='auto địa cung đã dừng';dungeon_running='đang ở lượt địa cung, đợi quay lại';dungeon_submitted='đã gửi vào địa cung cao cấp';startup_unavailable='chưa hoàn tất kiểm tra sau đăng nhập';npc_unavailable='chưa hoàn tất nhận chìa khóa';manual_entry='cần kiểm tra màn hình game';opened='đã mở game';failed='chưa hoàn tất';cancelled='đã hủy';closed='đã tắt'}[$job.State]
            if($job -is [TGLL.NativeJob] -and $job.State -eq 'izanami_running'){$stateText=[TGLL.NativeBackend]::IzanamiStageStatus($job)}
            if($job -is [TGLL.NativeJob]){$questInfo=[TGLL.NativeBackend]::QuestDisplayStatus($job);if($questInfo){$stateText=$questInfo}}
            $status.Text=$job.Username+': '+$stateText
            if($job.StartAttempts -gt 1){$status.Text+=' (lần '+$job.StartAttempts+'/3)'}
            if ($job.State -in @('finding_npc','choosing_key','npc_unavailable')) {
                if ($job.PSObject.Properties['ReadyPhase']) {$status.Text+=' [bước '+$job.ReadyPhase}
                if ($job.PSObject.Properties['NpcCount']) {$status.Text+=', NPC '+$job.NpcCount}
                $status.Text+=']'
            }
            if ($job.Error) {$status.Text+=' ('+$job.Error+'). '+(Get-LoginErrorText $job.Error)}
            if ($job.PSObject.Properties['NpcId'] -and $job.NpcId -ge 0) {$status.Text+=' ID Raikage '+$job.NpcId+'/'+$job.NpcTemplateId}
        }
    }
    if($script:autoAllRun -and $script:autoAllRun.active){$status.Text=$script:autoAllRun.message}
    if($script:liveUpdateMessageUntil -and [datetime]::UtcNow -lt $script:liveUpdateMessageUntil){$status.Text=$script:liveUpdateMessage}
}
Initialize-LiveUpdate
if ($UiTest) { if ($grid.Columns.Count -ne 14 -or $buttons.Count -ne 7 -or $grid.Rows.Count -ne 1 -or $closeAllButton.Text -ne 'Tắt tất cả') { throw 'UI construction failed' }; if($tabs.TabPages.Count -ne 26 -or $grid.Bounds.Bottom -gt $tabs.Top -or $detailsGroup.Bounds.Right -gt $form.ClientSize.Width){throw 'Compact layout invalid'}; $grid.Rows[0].Cells['pick'].Value=$true; if(@(Get-LaunchRows).Count -ne 1){throw 'Checkbox launch selection failed'}; . (Join-Path $PSScriptRoot 'AutoUiTests.ps1'); . (Join-Path $PSScriptRoot 'ConfigUiTests.ps1'); . (Join-Path $PSScriptRoot 'DungeonUiTests.ps1'); . (Join-Path $PSScriptRoot 'OpenButtonTests.ps1'); . (Join-Path $PSScriptRoot 'InventoryUiTests.ps1'); . (Join-Path $PSScriptRoot 'IzanamiUiTests.ps1'); . (Join-Path $PSScriptRoot 'ArrangeUiTests.ps1'); . (Join-Path $PSScriptRoot 'AutoItemUiTests.ps1'); . (Join-Path $PSScriptRoot 'GiftCodeUiTests.ps1'); . (Join-Path $PSScriptRoot 'MailUiTests.ps1'); . (Join-Path $PSScriptRoot 'QuestUiTests.ps1'); . (Join-Path $PSScriptRoot 'TabPositionUiTests.ps1'); . (Join-Path $PSScriptRoot 'IzanamiQueueTests.ps1'); . (Join-Path $PSScriptRoot 'QuestQueueTests.ps1'); . (Join-Path $PSScriptRoot 'DungeonQueueTests.ps1'); . (Join-Path $PSScriptRoot 'RetryUiTests.ps1'); . (Join-Path $PSScriptRoot 'UiPollingTests.ps1'); . (Join-Path $PSScriptRoot 'LaunchDelayUiTests.ps1'); . (Join-Path $PSScriptRoot 'LiveCountUiTests.ps1'); . (Join-Path $PSScriptRoot 'FarmUiTests.ps1'); . (Join-Path $PSScriptRoot 'LaunchErrorUiTests.ps1'); . (Join-Path $PSScriptRoot 'DailyUiTests.ps1'); . (Join-Path $PSScriptRoot 'SonCapUiTests.ps1'); . (Join-Path $PSScriptRoot 'IzanamiMapUiTests.ps1'); . (Join-Path $PSScriptRoot 'StoneChestUiTests.ps1'); . (Join-Path $PSScriptRoot 'CloakUiTests.ps1'); . (Join-Path $PSScriptRoot 'CloakQueueUiTests.ps1'); . (Join-Path $PSScriptRoot 'CloakParallelUiTests.ps1'); . (Join-Path $PSScriptRoot 'ConfigExtrasUiTests.ps1'); . (Join-Path $PSScriptRoot 'SonCapConfigUiTests.ps1'); . (Join-Path $PSScriptRoot 'SonCapQueueTests.ps1'); . (Join-Path $PSScriptRoot 'SonCapReopenUiTests.ps1'); . (Join-Path $PSScriptRoot 'CloseAllUiTests.ps1'); . (Join-Path $PSScriptRoot 'VitalsUiTests.ps1'); . (Join-Path $PSScriptRoot 'BulkAccountUiTests.ps1'); . (Join-Path $PSScriptRoot 'DistributionTests.ps1'); . (Join-Path $PSScriptRoot 'UpStoppedUiTests.ps1'); . (Join-Path $PSScriptRoot 'ClearUpAllTests.ps1'); . (Join-Path $PSScriptRoot 'MarketUiTests.ps1'); . (Join-Path $PSScriptRoot 'GemCombineUiTests.ps1'); . (Join-Path $PSScriptRoot 'UiPerformanceTests.ps1'); . (Join-Path $PSScriptRoot 'ScrollCompactTests.ps1'); . (Join-Path $PSScriptRoot 'AutoSkillDefaultsTests.ps1'); . (Join-Path $PSScriptRoot 'LargeRefreshTests.ps1'); . (Join-Path $PSScriptRoot 'CharacterNamesUiTests.ps1'); . (Join-Path $PSScriptRoot 'UpFlowUiTests.ps1'); . (Join-Path $PSScriptRoot 'LiveUpdateTests.ps1'); . (Join-Path $PSScriptRoot 'BeastUiTests.ps1'); . (Join-Path $PSScriptRoot 'ActivityTimeoutQueueTests.ps1'); . (Join-Path $PSScriptRoot 'BeastPreferenceUiTests.ps1'); . (Join-Path $PSScriptRoot 'AutoAllUiTests.ps1'); . (Join-Path $PSScriptRoot 'CaptchaUiTests.ps1'); . (Join-Path $PSScriptRoot 'CuongThiUiTests.ps1'); . (Join-Path $PSScriptRoot 'FarmDestinationUiTests.ps1'); Edit-Account $null; $form.Dispose(); Write-Output 'PASS: Windows Forms UI and proxy auto split from paste/host fields, invalid input indicator.'; exit 0 }
$socketTimer=New-Object Windows.Forms.Timer; $socketTimer.Interval=100
$socketTimer.Add_Tick({Invoke-ManagerTick})
$socketTimer.Start()
try {
    $tokenPath=Join-Path $script:dataDir 'api-token.json'
    if (Test-Path -LiteralPath $tokenPath) {
        $savedToken=Get-Content -LiteralPath $tokenPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $script:socketToken=Unprotect-Password $savedToken.secret
    } else {
        $random=New-Object byte[] 32; $rng=[Security.Cryptography.RandomNumberGenerator]::Create()
        try {$rng.GetBytes($random)} finally {$rng.Dispose()}
        $script:socketToken=[Convert]::ToBase64String($random)
        Write-JsonAtomic $tokenPath @{secret=(Protect-Password $script:socketToken)}
    }
    if ($script:socketToken.Length -lt 32) {throw 'Token không hợp lệ.'}
    if ($script:settings.socketEnabled) { Start-SocketApi ([int]$script:settings.socketPort); $status.Text='Socket: 127.0.0.1:'+$script:socketServer.Port+'. Mở Socket / Proxy để copy token.' }

} catch { $status.Text='Socket chưa bật: kiểm tra Socket / Proxy. '+$_.Exception.Message }
[Windows.Forms.Application]::EnableVisualStyles()
try {$null=$form.ShowDialog()} finally { $socketTimer.Stop(); $socketTimer.Dispose(); [TGLL.AutoLogin]::CancelAll(); [TGLL.NativeBackend]::CancelAll(); Stop-SocketApi; $form.Dispose() }
