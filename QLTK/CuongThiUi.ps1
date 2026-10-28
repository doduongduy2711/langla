$cuongThiPage=New-Object Windows.Forms.TabPage;$cuongThiPage.Text='Auto Cương Thi';$cuongThiPage.BackColor=$form.BackColor;$cuongThiPage.AutoScroll=$true;$tabs.TabPages.Add($cuongThiPage)
$null=Ui-Label 'Có Cương Thi sống trong map và HP từ 10 triệu trở lên: bật treo. HP dưới 10 triệu, quái chết hoặc chưa đọc được HP: tắt treo.' $cuongThiPage 16 16 650 50
$null=Ui-Label 'Chọn map/khu riêng bên dưới. QLTK đến đúng vị trí và khu trước khi săn; không cần bật Up ở Auto bãi quái. Nếu có nhiều Cương Thi, lấy HP thấp nhất.' $cuongThiPage 16 76 650 55
$script:cuongThiLoading=$false;$script:cuongThiShownAccount=''
$cuongThiCheck=New-Object Windows.Forms.CheckBox;$cuongThiCheck.Text='Bật Auto Cương Thi cho tài khoản đang chọn';$cuongThiCheck.SetBounds(16,140,650,27);$cuongThiPage.Controls.Add($cuongThiCheck)
$cuongThiMove=New-Object Windows.Forms.CheckBox;$cuongThiMove.Text='Tự đến map/khu và tọa độ trước khi săn Cương Thi';$cuongThiMove.SetBounds(16,177,650,27);$cuongThiPage.Controls.Add($cuongThiMove)
$script:cuongThiMapChoices=@($script:farmMapCatalogue | Where-Object {[TGLL.NativeBackend]::IsFarmDestinationMap([int]$_.Id,[int]$_.Type,[string]$_.Name)})
$null=Ui-Label 'Map / làng' $cuongThiPage 16 211 280 20
$cuongThiMap=New-Object Windows.Forms.ComboBox;$cuongThiMap.DropDownStyle='DropDownList';$cuongThiMap.SetBounds(16,235,295,26);foreach($map in $script:cuongThiMapChoices){$null=$cuongThiMap.Items.Add(($map.Id.ToString()+' • '+$map.Name))};$cuongThiMap.SelectedIndex=0;$cuongThiPage.Controls.Add($cuongThiMap)
$null=Ui-Label 'Khu (-1: giữ khu)' $cuongThiPage 326 211 150 20
$cuongThiZone=New-Object Windows.Forms.NumericUpDown;$cuongThiZone.Minimum=-1;$cuongThiZone.Maximum=255;$cuongThiZone.Value=-1;$cuongThiZone.SetBounds(326,235,80,26);$cuongThiPage.Controls.Add($cuongThiZone)
$null=Ui-Label 'X' $cuongThiPage 427 211 80 20
$cuongThiX=New-Object Windows.Forms.NumericUpDown;$cuongThiX.Minimum=0;$cuongThiX.Maximum=32767;$cuongThiX.SetBounds(427,235,95,26);$cuongThiPage.Controls.Add($cuongThiX)
$null=Ui-Label 'Y' $cuongThiPage 540 211 80 20
$cuongThiY=New-Object Windows.Forms.NumericUpDown;$cuongThiY.Minimum=0;$cuongThiY.Maximum=32767;$cuongThiY.SetBounds(540,235,95,26);$cuongThiPage.Controls.Add($cuongThiY)
$cuongThiCapture=Ui-NewButton 'Lấy vị trí acc đang xem' {Capture-CuongThiPosition};Ui-Place $cuongThiCapture $cuongThiPage 16 275 215 30
$cuongThiSave=Ui-NewButton 'Lưu map/khu cho acc đang xem' {try{if($grid.SelectedRows.Count){Save-CuongThiConfigForRow $grid.SelectedRows[0];$cuongThiInputHint.Text='Đã lưu map/khu riêng cho acc đang xem.'}}catch{$cuongThiInputHint.Text=$_.Exception.GetBaseException().Message}};Ui-Place $cuongThiSave $cuongThiPage 242 275 285 30
$cuongThiInputHint=Ui-Label 'Nhập X/Y hoặc bấm Lấy vị trí acc. Bật di chuyển Cương Thi sẽ dừng Up/bãi quái của chính acc đó.' $cuongThiPage 16 315 650 40
$cuongThiStart=Ui-NewButton 'Bật đã chọn' {foreach($row in @(Get-LaunchRows)){try{Set-CuongThiPreference $row $true}catch{$status.Text=$row.Tag.username+': '+$_.Exception.GetBaseException().Message}};Update-CuongThiUi};Ui-Place $cuongThiStart $cuongThiPage 16 366 185 30
$cuongThiStop=Ui-NewButton 'Dừng đã chọn' {foreach($row in @(Get-LaunchRows)){Set-CuongThiPreference $row $false};Update-CuongThiUi};Ui-Place $cuongThiStop $cuongThiPage 212 366 185 30
$cuongThiReport=New-Object Windows.Forms.TextBox;$cuongThiReport.Multiline=$true;$cuongThiReport.ReadOnly=$true;$cuongThiReport.ScrollBars='Vertical';$cuongThiReport.SetBounds(16,410,650,155);$cuongThiPage.Controls.Add($cuongThiReport)
function Enable-CuongThiDestinationControls {foreach($control in @($cuongThiMap,$cuongThiZone,$cuongThiX,$cuongThiY)){$control.Enabled=$cuongThiMove.Checked}}
function Get-CuongThiInputConfig {
 if($cuongThiMap.SelectedIndex -lt 0){throw 'Chọn map Cương Thi.'};$map=$script:cuongThiMapChoices[$cuongThiMap.SelectedIndex]
 if($cuongThiMove.Checked -and $cuongThiX.Value -eq 0 -and $cuongThiY.Value -eq 0){throw 'Nhập X/Y nơi muốn đứng hoặc bấm Lấy vị trí acc.'}
 return [pscustomobject]@{Move=[bool]$cuongThiMove.Checked;MapId=[int]$map.Id;MapName=[string]$map.Name;X=[int]$cuongThiX.Value;Y=[int]$cuongThiY.Value;Zone=[int]$cuongThiZone.Value}
}
function Get-CuongThiDestination($config){
 if(!$config -or !$config.Move){return $null};$map=@($script:cuongThiMapChoices | Where-Object {$_.Id -eq [int]$config.MapId});if($map.Count -ne 1){throw 'Map Cương Thi đã lưu không hợp lệ.'}
 $target=New-Object TGLL.FarmSpot;$target.MapId=[int]$map[0].Id;$target.MapName=[string]$map[0].Name;$target.MobId=-1;$target.MobName='Cương Thi';$target.Level=1;$target.X=[int]$config.X;$target.Y=[int]$config.Y;$target.Zone=if($config.PSObject.Properties['Zone']){[int]$config.Zone}else{-1};return $target
}
function Start-CuongThiJob($j,$target=$null){[TGLL.NativeBackend]::StartCuongThi($j.Id,$target)}
function Stop-CuongThiJob($j){[TGLL.NativeBackend]::StopCuongThi($j.Id)}
function Clear-CuongThiUpPreference($a,$j){
 $key=[string]$a.id;$changed=[bool]$a.upEnabled -or $script:farmUpRequests.ContainsKey($key)
 if($changed){$a | Add-Member NoteProperty upEnabled $false -Force;$script:farmUpRequests.Remove($key);foreach($row in $grid.Rows){if($row.Tag -and [string]$row.Tag.id -eq $key){Set-UpCell $row $false;break}};if(!$UiTest){Save-Accounts}}
 if($j -is [TGLL.NativeJob] -and ($j.FarmEnabled -or $j.DailyManaged)){[TGLL.NativeBackend]::StopMapFarm($j.Id)}
 if($changed){$script:autoTrainWanted[$key]=$false;if($j){$script:autoTrainApplied[$key]=$j.Id}}
}
function Sync-CuongThiAccount($a,$j,[bool]$force=$false){
 if($j -isnot [TGLL.NativeJob] -or $j.CloseRequested -or $j.CancelRequested -or $j.State -in @('closed','failed','cancelled')){return}
 try{
  if([bool]$a.cuongThiEnabled){$target=Get-CuongThiDestination $a.cuongThiConfig;$signature=[TGLL.NativeBackend]::CuongThiTargetKey($target)
   if(($force -or !$j.CuongThiEnabled -or $j.CuongThiDestinationKey -ne $signature) -and $j.AutoLoginEnabled -and [TGLL.NativeBackend]::UpSessionReady($j)){if($target){Clear-CuongThiUpPreference $a $j};Start-CuongThiJob $j $target}
  }elseif($j.CuongThiEnabled){Stop-CuongThiJob $j}
 }catch{$status.Text=$a.username+': '+$_.Exception.GetBaseException().Message}
}
function Save-CuongThiConfigForRow($row){$config=Get-CuongThiInputConfig;$a=$row.Tag;$a | Add-Member NoteProperty cuongThiConfig $config -Force;if($a.cuongThiEnabled -and $config.Move){Clear-CuongThiUpPreference $a $null};if(!$UiTest){Save-Accounts};if($script:accountJobs.ContainsKey([string]$a.id)){Sync-CuongThiAccount $a (Get-AccountJob $script:accountJobs[[string]$a.id]) $true}}
function Set-CuongThiPreference($row,[bool]$enabled){
 $a=$row.Tag;if($enabled){$config=Get-CuongThiInputConfig;$a | Add-Member NoteProperty cuongThiConfig $config -Force;if($config.Move){Clear-CuongThiUpPreference $a $null}}
 $a | Add-Member NoteProperty cuongThiEnabled $enabled -Force;if(!$UiTest){Save-Accounts};if($script:accountJobs.ContainsKey([string]$a.id)){Sync-CuongThiAccount $a (Get-AccountJob $script:accountJobs[[string]$a.id]) $enabled}
}
function Disable-CuongThiForUp($row){if($row.Tag.cuongThiEnabled -and $row.Tag.cuongThiConfig -and $row.Tag.cuongThiConfig.Move){Set-CuongThiPreference $row $false}}
function Capture-CuongThiPosition {
 try{if(!$grid.SelectedRows.Count){throw 'Chọn một acc đang mở.'};$a=$grid.SelectedRows[0].Tag;if(!$script:accountJobs.ContainsKey([string]$a.id)){throw 'Acc chưa mở.'};$spot=[TGLL.NativeBackend]::GetReferenceFarmSpot($script:accountJobs[[string]$a.id]);for($n=0;$n -lt $script:cuongThiMapChoices.Count;$n++){if($script:cuongThiMapChoices[$n].Id -eq $spot.MapId){$cuongThiMap.SelectedIndex=$n;break}};$cuongThiX.Value=$spot.X;$cuongThiY.Value=$spot.Y;$cuongThiZone.Value=$spot.Zone;$cuongThiMove.Checked=$true;$cuongThiInputHint.Text='Đã lấy vị trí '+$a.username+'. Bấm Lưu map/khu hoặc Bật đã chọn.'}catch{$cuongThiInputHint.Text=$_.Exception.GetBaseException().Message}
}
function Update-CuongThiUi {
 $a=if($grid.SelectedRows.Count){$grid.SelectedRows[0].Tag}else{$null};$key=if($a){[string]$a.id}else{''}
 $script:cuongThiLoading=$true;try{
  if($script:cuongThiShownAccount -ne $key){$script:cuongThiShownAccount=$key;$config=if($a){$a.cuongThiConfig}else{$null};$cuongThiMove.Checked=[bool]($config -and $config.Move);$mapId=if($config){[int]$config.MapId}else{69};for($n=0;$n -lt $script:cuongThiMapChoices.Count;$n++){if($script:cuongThiMapChoices[$n].Id -eq $mapId){$cuongThiMap.SelectedIndex=$n;break}};$cuongThiX.Value=if($config){[Math]::Max(0,[Math]::Min(32767,[int]$config.X))}else{0};$cuongThiY.Value=if($config){[Math]::Max(0,[Math]::Min(32767,[int]$config.Y))}else{0};$cuongThiZone.Value=if($config -and $config.PSObject.Properties['Zone']){[Math]::Max(-1,[Math]::Min(255,[int]$config.Zone))}else{-1}}
  $cuongThiCheck.Enabled=$null -ne $a;$cuongThiCheck.Checked=$null -ne $a -and [bool]$a.cuongThiEnabled;$cuongThiCheck.Text=if($a){'Auto Cương Thi: '+$a.username}else{'Bật Auto Cương Thi cho tài khoản đang chọn'}
 }finally{$script:cuongThiLoading=$false};Enable-CuongThiDestinationControls
 $text='Chọn một acc để xem Cương Thi và HP trong map.'
 if($a){$j=if($script:accountJobs.ContainsKey($key)){Get-AccountJob $script:accountJobs[$key]}else{$null};$text=$a.username+': '+$(if($j -is [TGLL.NativeJob]){$j.CuongThiStatus}else{if($a.cuongThiEnabled){'Đã lưu lựa chọn; chờ acc vào game.'}else{'Tắt'}})
  if($j -is [TGLL.NativeJob]){if($j.CuongThiRouteStatus){$text+="`r`n"+$j.CuongThiRouteStatus};if($j.CuongThi){$snap=$j.CuongThi;$text+="`r`nMap: "+$snap.MapId;if($snap.Found){$text+=' • ID: '+$snap.TemplateId+' • '+$snap.Name+"`r`nHP thực: "+$snap.Hp.ToString('N0',[Globalization.CultureInfo]::GetCultureInfo('vi-VN'))}};$text+="`r`n"+$j.GameAutoStatus}
 }
 if($cuongThiReport.Text -ne $text){$cuongThiReport.Text=$text}
}
$cuongThiMove.Add_CheckedChanged({Enable-CuongThiDestinationControls})
$cuongThiCheck.Add_CheckedChanged({if($script:cuongThiLoading -or !$grid.SelectedRows.Count){return};try{Set-CuongThiPreference $grid.SelectedRows[0] $cuongThiCheck.Checked}catch{$status.Text=$_.Exception.GetBaseException().Message};Update-CuongThiUi})
$grid.Add_SelectionChanged({Update-CuongThiUi});Update-CuongThiUi
