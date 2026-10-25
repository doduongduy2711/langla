$gemPage=New-Object Windows.Forms.TabPage;$gemPage.Text='Auto ghép đá 12';$gemPage.BackColor=$form.BackColor;$gemPage.AutoScroll=$true;$tabs.TabPages.Add($gemPage)
$null=Ui-Label 'Ghép thẳng 16 viên đá cấp 10 → 1 viên đá cấp 12 • tỷ lệ 100%.' $gemPage 16 16 680 30
$null=Ui-Label 'Chỉ dùng đá cấp 10 và bạc khóa. Đọc lại tỷ lệ, phí và ô túi trước mỗi lần ghép. Thiếu đá hoặc bạc khóa thì chờ; chưa xác nhận kết quả thì dừng.' $gemPage 16 55 680 55
$null=Ui-Label 'Khi bật tab này, Auto ghép đá trên 50% của game sẽ tắt để giữ nguyên liệu. Dừng tab này sẽ không tự bật lại chế độ trên 50%.' $gemPage 16 115 680 50
$gemStart=New-Object Windows.Forms.Button;$gemStart.Text='Bật đã chọn';$gemStart.SetBounds(16,180,180,30);$gemPage.Controls.Add($gemStart)
$gemStop=New-Object Windows.Forms.Button;$gemStop.Text='Dừng đã chọn';$gemStop.SetBounds(205,180,180,30);$gemPage.Controls.Add($gemStop)
$gemReport=New-Object Windows.Forms.TextBox;$gemReport.Multiline=$true;$gemReport.ReadOnly=$true;$gemReport.ScrollBars='Vertical';$gemReport.SetBounds(16,225,680,180);$gemPage.Controls.Add($gemReport)
function Update-GemCombineUi {
 $lines=@(foreach($row in $grid.Rows){$key=[string]$row.Tag.id;if(!$script:accountJobs.ContainsKey($key)){continue};$j=Get-AccountJob $script:accountJobs[$key];if($j -is [TGLL.NativeJob] -and ($j.GemEnabled -or $j.GemStatus -ne 'Tắt')){$row.Tag.username+': '+$j.GemStatus+' • Đã ghép: '+$j.GemCompleted}})
 $text=if($lines.Count){$lines -join "`r`n"}else{'Tích Chọn các acc đã vào game rồi bấm Bật đã chọn.'};if($gemReport.Text -ne $text){$gemReport.Text=$text}
}
$gemStart.Add_Click({
 $ok=0;$errors=@();foreach($row in @(Get-LaunchRows)){try{$key=[string]$row.Tag.id;if(!$script:accountJobs.ContainsKey($key)){throw 'Acc chưa mở.'};[TGLL.NativeBackend]::StartGemCombine($script:accountJobs[$key]);$overrides=@($row.Tag.gameOverrides | Where-Object {$null -ne $_ -and [int]$_.code -ne 307});$overrides+=([pscustomobject]@{code=307;value=0});$row.Tag | Add-Member NoteProperty gameOverrides $overrides -Force;$ok++}catch{$errors+=($row.Tag.username+': '+$_.Exception.Message)}}
 if(!$UiTest -and $ok){Save-Accounts};$status.Text='Ghép đá 12: đã bật '+$ok+' acc. '+($errors -join ' | ');Update-GemCombineUi
})
$gemStop.Add_Click({foreach($row in @(Get-LaunchRows)){$key=[string]$row.Tag.id;if($script:accountJobs.ContainsKey($key)){[TGLL.NativeBackend]::StopGemCombine($script:accountJobs[$key])}};Update-GemCombineUi})
