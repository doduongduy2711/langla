$gemPage=New-Object Windows.Forms.TabPage;$gemPage.Text='Auto ghép đá 12';$gemPage.BackColor=$form.BackColor;$gemPage.AutoScroll=$true;$tabs.TabPages.Add($gemPage)
$null=Ui-Label 'Ghép thẳng 16 viên đá cấp 10 → 1 viên đá cấp 12 • tỷ lệ 100%.' $gemPage 16 16 680 30
$null=Ui-Label 'Chỉ dùng đá cấp 10 và bạc khóa. Đọc lại tỷ lệ, phí và ô túi trước mỗi lần ghép. Thiếu đá thì chờ; thiếu bạc khóa thì bỏ qua và dừng ghép đá 12. Chưa xác nhận kết quả thì dừng.' $gemPage 16 55 680 55
$null=Ui-Label 'Đủ 16 đá cấp 10 mới tạm tắt ghép trên 50%. Ghép xong xác nhận đá 12 rồi tự bật lại chế độ trên 50% để tiếp tục gom đá cấp 10.' $gemPage 16 115 680 50
$script:gemLoading=$false;$script:gemApplied=@{}
$gemCheck=New-Object Windows.Forms.CheckBox;$gemCheck.Text='Tự ghép đá 12 cho tài khoản đang chọn';$gemCheck.SetBounds(16,165,650,27);$gemPage.Controls.Add($gemCheck)
$gemStart=New-Object Windows.Forms.Button;$gemStart.Text='Bật đã chọn';$gemStart.SetBounds(16,205,180,30);$gemPage.Controls.Add($gemStart)
$gemStop=New-Object Windows.Forms.Button;$gemStop.Text='Dừng đã chọn';$gemStop.SetBounds(205,205,180,30);$gemPage.Controls.Add($gemStop)
$gemReport=New-Object Windows.Forms.TextBox;$gemReport.Multiline=$true;$gemReport.ReadOnly=$true;$gemReport.ScrollBars='Vertical';$gemReport.SetBounds(16,250,680,180);$gemPage.Controls.Add($gemReport)
function Start-GemCombineJob($j){[TGLL.NativeBackend]::StartGemCombine($j.Id)}
function Stop-GemCombineJob($j){[TGLL.NativeBackend]::StopGemCombine($j.Id)}
function Sync-GemCombineAccount($a,$j){
 if($j -isnot [TGLL.NativeJob] -or $j.CloseRequested -or $j.CancelRequested -or $j.State -in @('failed','closed','cancelled')){return}
 $key=[string]$a.id;$wanted=[bool]$a.gemCombineEnabled
 if($wanted -and $j.GemSkipped){$a | Add-Member NoteProperty gemCombineEnabled $false -Force;$wanted=$false;if(!$UiTest){Save-Accounts}}
 $signature=$j.Id+':'+$wanted;if($script:gemApplied[$key] -eq $signature){return}
 if($wanted){if(!$j.TrainReady -or !$j.GameSettingsReady){return};try{Start-GemCombineJob $j}catch{$status.Text=$a.username+': '+$_.Exception.Message;return}}
 elseif($j.GemEnabled){Stop-GemCombineJob $j}
 $script:gemApplied[$key]=$signature
}
function Set-GemCombinePreference($row,[bool]$enabled){
 $a=$row.Tag;$key=[string]$a.id;$a | Add-Member NoteProperty gemCombineEnabled $enabled -Force;$script:gemApplied.Remove($key)
 if($enabled){$overrides=@($a.gameOverrides | Where-Object {$null -ne $_ -and [int]$_.code -ne 307});$overrides+=([pscustomobject]@{code=307;value=1});$a | Add-Member NoteProperty gameOverrides $overrides -Force}
 if(!$UiTest){Save-Accounts}
 if($script:accountJobs.ContainsKey($key)){$j=Get-AccountJob $script:accountJobs[$key];if($j -is [TGLL.NativeJob]){$j.GemSkipped=$false};Sync-GemCombineAccount $a $j}
}
function Update-GemCombineUi {
 $script:gemLoading=$true;try{$gemCheck.Enabled=$grid.SelectedRows.Count -gt 0;$gemCheck.Checked=$grid.SelectedRows.Count -gt 0 -and [bool]$grid.SelectedRows[0].Tag.gemCombineEnabled;$gemCheck.Text=if($grid.SelectedRows.Count){'Tự ghép đá 12: '+$grid.SelectedRows[0].Tag.username}else{'Tự ghép đá 12 cho tài khoản đang chọn'}}finally{$script:gemLoading=$false}
 $lines=@(foreach($row in $grid.Rows){$key=[string]$row.Tag.id;if(!$script:accountJobs.ContainsKey($key)){continue};$j=Get-AccountJob $script:accountJobs[$key];if($j -is [TGLL.NativeJob] -and ($j.GemEnabled -or $j.GemStatus -ne 'Tắt')){$row.Tag.username+': '+$j.GemStatus+' • Đã ghép: '+$j.GemCompleted}})
 $text=if($lines.Count){$lines -join "`r`n"}else{'Tích Chọn các acc đã vào game rồi bấm Bật đã chọn.'};if($gemReport.Text -ne $text){$gemReport.Text=$text}
}
$gemStart.Add_Click({$rows=@(Get-LaunchRows);foreach($row in $rows){Set-GemCombinePreference $row $true};$status.Text='Đã chọn tự ghép đá 12 cho '+$rows.Count+' acc; tự chạy khi vào game.';Update-GemCombineUi})
$gemStop.Add_Click({foreach($row in @(Get-LaunchRows)){Set-GemCombinePreference $row $false};Update-GemCombineUi})
$gemCheck.Add_CheckedChanged({if($script:gemLoading -or !$grid.SelectedRows.Count){return};Set-GemCombinePreference $grid.SelectedRows[0] $gemCheck.Checked;Update-GemCombineUi})
$grid.Add_SelectionChanged({Update-GemCombineUi});Update-GemCombineUi
