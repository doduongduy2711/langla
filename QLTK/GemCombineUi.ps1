$gemPage=New-Object Windows.Forms.TabPage;$gemPage.Text='Auto ghép đá 12';$gemPage.BackColor=$form.BackColor;$gemPage.AutoScroll=$true;$tabs.TabPages.Add($gemPage)
$null=Ui-Label 'Ghép 100%: 16 đá 6 → đá 8 • 16 đá 8 → đá 10 • 16 đá 10 → đá 12.' $gemPage 16 16 680 30
$null=Ui-Label 'Chỉ dùng 16 đá cùng cấp và bạc khóa. Đọc lại tỷ lệ, phí và ô túi trước mỗi lần ghép. Thiếu đá thì chờ; thiếu bạc khóa thì bỏ qua và dừng ghép đá. Chưa xác nhận kết quả thì dừng.' $gemPage 16 55 680 55
$null=Ui-Label 'Có thể tích cả 3 cấp. Ưu tiên cấp cao nhất đủ nguyên liệu; mỗi lần chỉ ghép 1 lượt. Xác nhận kết quả rồi bật lại ghép trên 50%.' $gemPage 16 115 680 50
$script:gemLoading=$false;$script:gemApplied=@{}
$gemCheck=New-Object Windows.Forms.CheckBox;$gemCheck.Text='Tự ghép đá 12 cho tài khoản đang chọn';$gemCheck.SetBounds(16,165,650,27);$gemPage.Controls.Add($gemCheck)
$gem8Check=New-Object Windows.Forms.CheckBox;$gem8Check.Text='Tự ghép đá 8 (16 đá cấp 6)';$gem8Check.SetBounds(16,195,650,27);$gemPage.Controls.Add($gem8Check)
$gem10Check=New-Object Windows.Forms.CheckBox;$gem10Check.Text='Tự ghép đá 10 (16 đá cấp 8)';$gem10Check.SetBounds(16,225,650,27);$gemPage.Controls.Add($gem10Check)
$null=Ui-Label 'Tích ô ghép đá là tự chạy cho acc đang xem; bỏ tích để dừng cấp đó.' $gemPage 16 265 480 35
$gemStop=New-Object Windows.Forms.Button;$gemStop.Text='Dừng đã chọn';$gemStop.SetBounds(516,265,180,30);$gemPage.Controls.Add($gemStop)
$gemReport=New-Object Windows.Forms.TextBox;$gemReport.Multiline=$true;$gemReport.ReadOnly=$true;$gemReport.ScrollBars='Vertical';$gemReport.SetBounds(16,310,680,180);$gemPage.Controls.Add($gemReport)
function Get-GemCombineTargets($a){$mask=0;if([bool]$a.gemCombineEnabled){$mask+=4};if([bool]$a.gemCombine8Enabled){$mask+=1};if([bool]$a.gemCombine10Enabled){$mask+=2};return $mask}
function Start-GemCombineJob($j,[int]$targets=4){[TGLL.NativeBackend]::ConfigureGemCombine($j.Id,$targets)}
function Stop-GemCombineJob($j){[TGLL.NativeBackend]::StopGemCombine($j.Id)}
function Sync-GemCombineAccount($a,$j){
 if($j -isnot [TGLL.NativeJob] -or $j.CloseRequested -or $j.CancelRequested -or $j.State -in @('failed','closed','cancelled')){return}
 $key=[string]$a.id;$targets=Get-GemCombineTargets $a;$wanted=$targets -ne 0
 if($wanted -and $j.GemSkipped){foreach($field in @('gemCombineEnabled','gemCombine8Enabled','gemCombine10Enabled')){$a | Add-Member NoteProperty $field $false -Force};$wanted=$false;$targets=0;if(!$UiTest){Save-Accounts}}
 $signature=$j.Id+':'+$targets;if($script:gemApplied[$key] -eq $signature){return}
 if($wanted){if(!$j.TrainReady -or !$j.GameSettingsReady){return};try{Start-GemCombineJob $j $targets}catch{$status.Text=$a.username+': '+$_.Exception.Message;return}}
 elseif($j.GemEnabled){Stop-GemCombineJob $j}
 $script:gemApplied[$key]=$signature
}
function Set-GemCombinePreference($row,[bool]$enabled,[int]$level=12){
 $mask=Get-GemCombineTargets $row.Tag;$bit=switch($level){8{1};10{2};12{4};default{throw 'Cấp đá không hợp lệ'}};if($enabled){$mask=$mask -bor $bit}else{$mask=$mask -band (7 -bxor $bit)};Set-GemCombineTargets $row $mask
}
function Set-GemCombineTargets($row,[int]$targets){
 $a=$row.Tag;$key=[string]$a.id;$enabled=$targets -ne 0;$a | Add-Member NoteProperty gemCombineEnabled ([bool]($targets -band 4)) -Force;$a | Add-Member NoteProperty gemCombine8Enabled ([bool]($targets -band 1)) -Force;$a | Add-Member NoteProperty gemCombine10Enabled ([bool]($targets -band 2)) -Force;$script:gemApplied.Remove($key)
 if($enabled){$overrides=@($a.gameOverrides | Where-Object {$null -ne $_ -and [int]$_.code -ne 307});$overrides+=([pscustomobject]@{code=307;value=1});$a | Add-Member NoteProperty gameOverrides $overrides -Force}
 if(!$UiTest){Save-Accounts}
 if($script:accountJobs.ContainsKey($key)){$j=Get-AccountJob $script:accountJobs[$key];if($j -is [TGLL.NativeJob]){$j.GemSkipped=$false};Sync-GemCombineAccount $a $j}
}
function Update-GemCombineUi {
 $script:gemLoading=$true;try{$gem8Check.Enabled=$grid.SelectedRows.Count -gt 0;$gem10Check.Enabled=$grid.SelectedRows.Count -gt 0;$gem8Check.Checked=$grid.SelectedRows.Count -gt 0 -and [bool]$grid.SelectedRows[0].Tag.gemCombine8Enabled;$gem10Check.Checked=$grid.SelectedRows.Count -gt 0 -and [bool]$grid.SelectedRows[0].Tag.gemCombine10Enabled;$gemCheck.Enabled=$grid.SelectedRows.Count -gt 0;$gemCheck.Checked=$grid.SelectedRows.Count -gt 0 -and [bool]$grid.SelectedRows[0].Tag.gemCombineEnabled;$gemCheck.Text=if($grid.SelectedRows.Count){'Tự ghép đá 12: '+$grid.SelectedRows[0].Tag.username}else{'Tự ghép đá 12 cho tài khoản đang chọn'}}finally{$script:gemLoading=$false}
 $lines=@(foreach($row in $grid.Rows){$key=[string]$row.Tag.id;if(!$script:accountJobs.ContainsKey($key)){continue};$j=Get-AccountJob $script:accountJobs[$key];if($j -is [TGLL.NativeJob] -and ($j.GemEnabled -or $j.GemStatus -ne 'Tắt')){$row.Tag.username+': '+$j.GemStatus+' • Đã ghép: '+$j.GemCompleted}})
 $text=if($lines.Count){$lines -join "`r`n"}else{'Tích ô ghép đá 8, 10 hoặc 12 cho acc đang xem. Tự chạy khi vào game và đủ nguyên liệu; không cần bấm nút bật.'};if($gemReport.Text -ne $text){$gemReport.Text=$text}
}
$gemStop.Add_Click({foreach($row in @(Get-LaunchRows)){Set-GemCombineTargets $row 0};Update-GemCombineUi})
$gemCheck.Add_CheckedChanged({if($script:gemLoading -or !$grid.SelectedRows.Count){return};Set-GemCombinePreference $grid.SelectedRows[0] $gemCheck.Checked;Update-GemCombineUi})
$grid.Add_SelectionChanged({if($tabs.SelectedTab -eq $gemPage){Update-GemCombineUi}});Update-GemCombineUi

$gem8Check.Add_CheckedChanged({if($script:gemLoading -or !$grid.SelectedRows.Count){return};Set-GemCombinePreference $grid.SelectedRows[0] $gem8Check.Checked 8;Update-GemCombineUi})
$gem10Check.Add_CheckedChanged({if($script:gemLoading -or !$grid.SelectedRows.Count){return};Set-GemCombinePreference $grid.SelectedRows[0] $gem10Check.Checked 10;Update-GemCombineUi})
