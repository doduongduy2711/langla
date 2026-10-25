if($gemPage.Text -ne 'Auto ghép đá 12' -or !$gemPage.AutoScroll -or !$gemReport.ReadOnly -or $tabs.TabPages.Count -ne 24){throw 'Gem combine tab missing'}
if($gemStart.Text -ne 'Bật đã chọn' -or $gemStop.Text -ne 'Dừng đã chọn'){throw 'Gem combine actions missing'}
Update-GemCombineUi
if(!$gemReport.Text){throw 'Gem combine guidance missing'}
Write-Output 'PASS: Auto gem level 12 tab, selected-account controls and status guidance.'
