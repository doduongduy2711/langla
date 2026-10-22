$script:closeAllOperation=$null;$script:closingAll=$false
$closeAllTimer=New-Object Windows.Forms.Timer;$closeAllTimer.Interval=100
function Begin-CloseAllOperation {return [TGLL.NativeBackend]::BeginCloseAllGames()}
function Start-CloseAllUi {
 if($script:closingAll){return}
 $script:closingAll=$true;$closeAllButton.Enabled=$false;$closeAllButton.Text='Đang tắt...'
 try{
  Reset-SonCapQueue;Reset-IzanamiQueue;Reset-QuestQueue;Reset-DungeonQueue
  if($script:cloakRun){$script:cloakRun.active=$false}
  [TGLL.AutoLogin]::CancelAll();Stop-MailCollection;if(Get-Command Stop-Distribution -ErrorAction SilentlyContinue){Stop-Distribution}
  $script:closeAllOperation=Begin-CloseAllOperation
  $script:deferAccountSave=$true;$script:accountSavePending=$false;$grid.SuspendLayout()
  try{foreach($row in $grid.Rows){Set-AccountAuto $row $false}}
  finally{$grid.ResumeLayout();$script:deferAccountSave=$false}
  if($script:accountSavePending -and !$UiTest){Save-Accounts};$script:accountSavePending=$false
  $script:lastLoginJob='';$status.Text='Đang đóng các game; QLTK vẫn có thể thao tác.';$closeAllTimer.Start()
 }catch{
  $status.Text='Lỗi khi bắt đầu tắt tất cả: '+$_.Exception.Message
  if($null -ne $script:closeAllOperation){$closeAllTimer.Start()}else{$script:closingAll=$false;$closeAllButton.Enabled=$true;$closeAllButton.Text='Tắt tất cả'}
 }
}
function Complete-CloseAllUi {
 if($null -eq $script:closeAllOperation -or !$script:closeAllOperation.Completed){return}
 try{
  if($script:closeAllOperation.Error){$status.Text='Chưa đóng xong tất cả game: '+$script:closeAllOperation.Error}
  else{$closed=$script:closeAllOperation.Result;$status.Text='Đã hủy hàng đợi và đóng '+$closed.Closed+' game do QLTK mở.';if($closed.Failed){$status.Text+=' '+$closed.Failed+' game chưa đóng được.'}}
  Update-UiSummary
 }finally{$closeAllTimer.Stop();$script:closeAllOperation=$null;$script:closingAll=$false;$closeAllButton.Enabled=$true;$closeAllButton.Text='Tắt tất cả'}
}
$closeAllTimer.Add_Tick({Complete-CloseAllUi})
$form.Add_Disposed({$closeAllTimer.Stop();$closeAllTimer.Dispose()})
