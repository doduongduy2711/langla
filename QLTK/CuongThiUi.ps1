$cuongThiPage=New-Object Windows.Forms.TabPage;$cuongThiPage.Text='Auto Cương Thi';$cuongThiPage.BackColor=$form.BackColor;$cuongThiPage.AutoScroll=$true;$tabs.TabPages.Add($cuongThiPage)
$null=Ui-Label 'Có Cương Thi sống trong map và HP từ 10 triệu trở lên: bật treo. HP dưới 10 triệu, không còn Cương Thi hoặc chưa đọc được HP: tắt treo.' $cuongThiPage 16 16 650 55
$null=Ui-Label 'Dùng HP thực của quái. Hỗ trợ Cương thi Nagato và các quái có tên bắt đầu bằng Cương thi. Nếu có nhiều con, lấy HP thấp nhất. Tạm nhường nhiệm vụ/địa cung/cấm thuật/Sơn Cáp.' $cuongThiPage 16 80 650 60
$script:cuongThiLoading=$false
$cuongThiCheck=New-Object Windows.Forms.CheckBox;$cuongThiCheck.Text='Bật Auto Cương Thi cho tài khoản đang chọn';$cuongThiCheck.SetBounds(16,150,650,27);$cuongThiPage.Controls.Add($cuongThiCheck)
$cuongThiStart=Ui-NewButton 'Bật đã chọn' {foreach($row in @(Get-LaunchRows)){Set-CuongThiPreference $row $true};Update-CuongThiUi};Ui-Place $cuongThiStart $cuongThiPage 16 192 185 30
$cuongThiStop=Ui-NewButton 'Dừng đã chọn' {foreach($row in @(Get-LaunchRows)){Set-CuongThiPreference $row $false};Update-CuongThiUi};Ui-Place $cuongThiStop $cuongThiPage 212 192 185 30
$cuongThiReport=New-Object Windows.Forms.TextBox;$cuongThiReport.Multiline=$true;$cuongThiReport.ReadOnly=$true;$cuongThiReport.ScrollBars='Vertical';$cuongThiReport.SetBounds(16,240,650,135);$cuongThiPage.Controls.Add($cuongThiReport)
function Start-CuongThiJob($j){[TGLL.NativeBackend]::StartCuongThi($j.Id)}
function Stop-CuongThiJob($j){[TGLL.NativeBackend]::StopCuongThi($j.Id)}
function Sync-CuongThiAccount($a,$j){
 if($j -isnot [TGLL.NativeJob] -or $j.CloseRequested -or $j.CancelRequested -or $j.State -in @('closed','failed','cancelled')){return}
 try{
  if([bool]$a.cuongThiEnabled){if(!$j.CuongThiEnabled -and $j.AutoLoginEnabled -and [TGLL.NativeBackend]::UpSessionReady($j)){Start-CuongThiJob $j}}
  elseif($j.CuongThiEnabled){Stop-CuongThiJob $j}
 }catch{$status.Text=$a.username+': '+$_.Exception.Message}
}
function Set-CuongThiPreference($row,[bool]$enabled){
 $a=$row.Tag;$a | Add-Member NoteProperty cuongThiEnabled $enabled -Force
 if(!$UiTest){Save-Accounts}
 if($script:accountJobs.ContainsKey([string]$a.id)){Sync-CuongThiAccount $a (Get-AccountJob $script:accountJobs[[string]$a.id])}
}
function Update-CuongThiUi {
 $a=if($grid.SelectedRows.Count){$grid.SelectedRows[0].Tag}else{$null}
 $script:cuongThiLoading=$true;try{$cuongThiCheck.Enabled=$null -ne $a;$cuongThiCheck.Checked=$null -ne $a -and [bool]$a.cuongThiEnabled;$cuongThiCheck.Text=if($a){'Auto Cương Thi: '+$a.username}else{'Bật Auto Cương Thi cho tài khoản đang chọn'}}finally{$script:cuongThiLoading=$false}
 $text='Chọn một acc để xem Cương Thi và HP trong map.'
 if($a){$j=if($script:accountJobs.ContainsKey([string]$a.id)){Get-AccountJob $script:accountJobs[[string]$a.id]}else{$null};$text=$a.username+': '+$(if($j -is [TGLL.NativeJob]){$j.CuongThiStatus}else{if($a.cuongThiEnabled){'Đã lưu lựa chọn; chờ acc vào game.'}else{'Tắt'}})
  if($j -is [TGLL.NativeJob] -and $j.CuongThi){$snapshot=$j.CuongThi;$text+="`r`nMap: "+$snapshot.MapId;if($snapshot.Found){$text+=' • ID: '+$snapshot.TemplateId+' • '+$snapshot.Name+"`r`nHP thực: "+$snapshot.Hp.ToString('N0',[Globalization.CultureInfo]::GetCultureInfo('vi-VN'))};$text+="`r`n"+$j.GameAutoStatus}
 }
 if($cuongThiReport.Text -ne $text){$cuongThiReport.Text=$text}
}
$cuongThiCheck.Add_CheckedChanged({if($script:cuongThiLoading -or !$grid.SelectedRows.Count){return};Set-CuongThiPreference $grid.SelectedRows[0] $cuongThiCheck.Checked;Update-CuongThiUi})
$grid.Add_SelectionChanged({Update-CuongThiUi});Update-CuongThiUi

$null=Ui-Label 'Bỏ tích Auto Cương Thi: trở lại tùy chọn Auto/Up của acc.' $cuongThiPage 16 390 650 30
