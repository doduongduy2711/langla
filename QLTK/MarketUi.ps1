$marketPage=New-Object Windows.Forms.TabPage;$marketPage.Text='Auto chợ';$marketPage.BackColor=$form.BackColor;$marketPage.AutoScroll=$true;$tabs.TabPages.Add($marketPage)
$script:marketLoading=$false
$null=Ui-Label 'Treo chợ: ID,số lượng,giá cả lần bán' $marketPage 12 8 330 24
$null=Ui-Label 'Quét chợ: ID,giá tối đa cả lần mua' $marketPage 355 8 330 24
$marketSellRules=New-Object Windows.Forms.TextBox;$marketSellRules.Multiline=$true;$marketSellRules.ScrollBars='Vertical';$marketSellRules.SetBounds(12,35,330,85);$marketPage.Controls.Add($marketSellRules)
$marketScanRules=New-Object Windows.Forms.TextBox;$marketScanRules.Multiline=$true;$marketScanRules.ScrollBars='Vertical';$marketScanRules.SetBounds(355,35,330,85);$marketPage.Controls.Add($marketScanRules)
$null=Ui-Label 'Tự tách đúng số lượng; treo 8 giờ, phí theo game.' $marketPage 12 124 330 34
$marketAutoBuy=New-Object Windows.Forms.CheckBox;$marketAutoBuy.Text='Tự mua khi đúng ID và giá';$marketAutoBuy.SetBounds(355,125,320,25);$marketPage.Controls.Add($marketAutoBuy)
$null=Ui-Label 'Tối đa lần mua' $marketPage 355 155 130 25
$marketMaxBuys=New-Object Windows.Forms.NumericUpDown;$marketMaxBuys.Minimum=1;$marketMaxBuys.Maximum=100000;$marketMaxBuys.Value=1;$marketMaxBuys.SetBounds(490,154,150,25);$marketPage.Controls.Add($marketMaxBuys)
$null=Ui-Label 'Tổng bạc tối đa' $marketPage 355 185 130 25
$marketBudget=New-Object Windows.Forms.NumericUpDown;$marketBudget.Minimum=1;$marketBudget.Maximum=2147483647;$marketBudget.Value=1000000;$marketBudget.ThousandsSeparator=$true;$marketBudget.SetBounds(490,184,150,25);$marketPage.Controls.Add($marketBudget)
$marketSellButton=New-Object Windows.Forms.Button;$marketSellButton.Text='Rao bán đã chọn';$marketSellButton.SetBounds(12,166,160,30);$marketPage.Controls.Add($marketSellButton)
$marketScanButton=New-Object Windows.Forms.Button;$marketScanButton.Text='Quét đã chọn';$marketScanButton.SetBounds(355,220,150,30);$marketPage.Controls.Add($marketScanButton)
$marketStopButton=New-Object Windows.Forms.Button;$marketStopButton.Text='Dừng đã chọn';$marketStopButton.SetBounds(515,220,150,30);$marketPage.Controls.Add($marketStopButton)
$null=Ui-Label 'Quét không cần mở Chợ; khi mở dùng bộ lọc hiện tại. Rao bán cần mở Chợ. Quét tối thiểu 0,5 giây/trang và đợi dữ liệu mới. Giá cả chồng; giới hạn mua riêng từng acc.' $marketPage 12 260 680 42
$marketReport=New-Object Windows.Forms.TextBox;$marketReport.Multiline=$true;$marketReport.ReadOnly=$true;$marketReport.ScrollBars='Both';$marketReport.WordWrap=$false;$marketReport.SetBounds(12,309,680,52);$marketPage.Controls.Add($marketReport)
$script:marketResultAccountIds=@();$script:marketResultKey=''
$marketResultsGrid=New-Object TGLL.ResponsiveGrid
$marketResultsGrid.SetBounds(12,370,680,220);$marketResultsGrid.ReadOnly=$true;$marketResultsGrid.AllowUserToAddRows=$false;$marketResultsGrid.AllowUserToDeleteRows=$false;$marketResultsGrid.RowHeadersVisible=$false;$marketResultsGrid.SelectionMode='FullRowSelect';$marketResultsGrid.MultiSelect=$false;$marketResultsGrid.BackgroundColor=[Drawing.Color]::White
$marketPage.Controls.Add($marketResultsGrid)
foreach($c in @(@('account','Tài khoản',110),@('itemId','ID vật phẩm',85),@('itemName','Tên vật phẩm',190),@('quantity','Số lượng',75),@('price','Giá cả chồng (bạc)',140),@('seller','Người bán',110),@('listingId','ID tin',90))){
 $column=New-Object Windows.Forms.DataGridViewTextBoxColumn;$column.Name=$c[0];$column.HeaderText=$c[1];$column.Width=[int]$c[2];$null=$marketResultsGrid.Columns.Add($column)
}
$marketResultsGrid.Columns['itemId'].ValueType=[int];$marketResultsGrid.Columns['quantity'].ValueType=[int];$marketResultsGrid.Columns['price'].ValueType=[long];$marketResultsGrid.Columns['listingId'].ValueType=[long];$marketResultsGrid.Columns['price'].DefaultCellStyle.Format='N0'
function Set-MarketResultTable($records){
 $rows=@($records);$key=($rows | ForEach-Object {$_.account+'|'+$_.itemId+'|'+$_.name+'|'+$_.quantity+'|'+$_.price+'|'+$_.seller+'|'+$_.listingId}) -join "`n"
 if($script:marketResultKey -eq $key){return};$script:marketResultKey=$key
 $marketResultsGrid.SuspendLayout()
 try{$marketResultsGrid.Rows.Clear();foreach($r in $rows){$null=$marketResultsGrid.Rows.Add([object[]]@([string]$r.account,[int]$r.itemId,[string]$r.name,[int]$r.quantity,[long]$r.price,[string]$r.seller,[long]$r.listingId))}}finally{$marketResultsGrid.ResumeLayout()}
}
function Update-MarketResultTable {
 $records=New-Object Collections.Generic.List[object]
 foreach($r in $grid.Rows){
  if($script:marketResultAccountIds.Count){if([string]$r.Tag.id -notin $script:marketResultAccountIds){continue}}elseif(!$r.Selected){continue}
  $key=[string]$r.Tag.id;if(!$script:accountJobs.ContainsKey($key)){continue};$job=Get-AccountJob $script:accountJobs[$key];if($job -isnot [TGLL.NativeJob]){continue}
  foreach($x in $job.MarketResults){$records.Add([pscustomobject]@{account=$r.Tag.username;itemId=$x.ItemId;name=$x.Name;quantity=$x.Quantity;price=$x.Price;seller=$x.Seller;listingId=$x.Id})}
 }
 Set-MarketResultTable @($records.ToArray())
}
function Get-MarketPreference($a){
 $p=[pscustomobject]@{sell='';scan='';buy=$false;maxBuys=1;budget=1000000}
 if($null -ne $a -and $a.PSObject.Properties['marketConfig'] -and $null -ne $a.marketConfig){$c=$a.marketConfig;foreach($f in @('sell','scan','buy','maxBuys','budget')){if($c.PSObject.Properties[$f]){$p.$f=$c.$f}}}
 $p.maxBuys=[Math]::Max(1,[Math]::Min(100000,[int]$p.maxBuys));$p.budget=[Math]::Max(1,[Math]::Min(2147483647,[long]$p.budget));return $p
}
function Save-MarketUi {
 if($script:marketLoading -or !$grid.SelectedRows.Count){return}
 $p=[pscustomobject]@{sell=$marketSellRules.Text;scan=$marketScanRules.Text;buy=$marketAutoBuy.Checked;maxBuys=[int]$marketMaxBuys.Value;budget=[long]$marketBudget.Value}
 $grid.SelectedRows[0].Tag | Add-Member NoteProperty marketConfig $p -Force
 if(!$UiTest){Save-Accounts}
}
function Update-MarketUi {
 Update-MarketResultTable;if(!$grid.SelectedRows.Count){return};$a=$grid.SelectedRows[0].Tag
 if(!$marketSellRules.Focused -and !$marketScanRules.Focused -and !$marketMaxBuys.Focused -and !$marketBudget.Focused -and !$marketAutoBuy.Focused){
  $script:marketLoading=$true;try{$p=Get-MarketPreference $a;$marketSellRules.Text=[string]$p.sell;$marketScanRules.Text=[string]$p.scan;$marketAutoBuy.Checked=[bool]$p.buy;$marketMaxBuys.Value=[decimal]$p.maxBuys;$marketBudget.Value=[decimal]$p.budget}finally{$script:marketLoading=$false}
 }
 if($script:accountJobs.ContainsKey([string]$a.id)){$j=Get-AccountJob $script:accountJobs[[string]$a.id];if($j -is [TGLL.NativeJob]){
  $lines=@(($a.username+': '+$j.MarketStatus),('Đã mua: '+$j.MarketBought+' lần • '+$j.MarketSpent+' bạc | Đã rao: '+$j.MarketSold+' chồng'))

  $value=$lines -join "`r`n";if($marketReport.Text -ne $value){$marketReport.Text=$value};return
 }};$marketReport.Text=$a.username+': mở game để quét; rao bán cần mở bảng Chợ.'
}
function Start-MarketSelected([bool]$sell){
 try{$rules=if($sell){$marketSellRules.Text}else{$marketScanRules.Text};$null=[TGLL.NativeBackend]::ParseMarketRules($rules,$sell)}catch{$status.Text=$_.Exception.Message;return}
 $rows=@(Get-LaunchRows);if(!$rows.Count){$status.Text='Tích Chọn các acc cần chạy Auto chợ.';return};Save-MarketUi;$p=Get-MarketPreference $grid.SelectedRows[0].Tag;$ok=0;$errors=@();if(!$sell){$script:marketResultAccountIds=@()}
 foreach($row in $rows){try{$id=[string]$row.Tag.id;if(!$script:accountJobs.ContainsKey($id)){throw 'Acc chưa mở.'};[TGLL.NativeBackend]::StartMarket($script:accountJobs[$id],$rules,$sell,$marketAutoBuy.Checked,[int]$marketMaxBuys.Value,[long]$marketBudget.Value);$row.Tag | Add-Member NoteProperty marketConfig ($p | ConvertTo-Json | ConvertFrom-Json) -Force;$ok++;if(!$sell){$script:marketResultAccountIds+=([string]$row.Tag.id)}}catch{$errors+=($row.Tag.username+': '+$_.Exception.Message)}}
 if(!$UiTest){Save-Accounts};$status.Text='Auto chợ: bắt đầu '+$ok+' acc. '+($errors -join ' | ');Update-MarketUi
}
$marketSellRules.Add_TextChanged({Save-MarketUi});$marketScanRules.Add_TextChanged({Save-MarketUi});$marketAutoBuy.Add_CheckedChanged({Save-MarketUi});$marketMaxBuys.Add_ValueChanged({Save-MarketUi});$marketBudget.Add_ValueChanged({Save-MarketUi})
$marketSellButton.Add_Click({Start-MarketSelected $true});$marketScanButton.Add_Click({Start-MarketSelected $false});$marketStopButton.Add_Click({foreach($row in @(Get-LaunchRows)){$id=[string]$row.Tag.id;if($script:accountJobs.ContainsKey($id)){[TGLL.NativeBackend]::StopMarket($script:accountJobs[$id])}};Update-MarketUi})
$grid.Add_SelectionChanged({Update-MarketUi});Update-MarketUi
