$inventoryPage=New-Object Windows.Forms.TabPage;$inventoryPage.Text='Trang bị';$inventoryPage.BackColor=$form.BackColor;$tabs.TabPages.Add($inventoryPage)
$inventoryRefresh=Ui-NewButton 'Đọc hành trang' {Request-InventoryUi};Ui-Place $inventoryRefresh $inventoryPage 12 10 130 27
$inventoryFilter=New-Object Windows.Forms.ComboBox;$inventoryFilter.DropDownStyle='DropDownList';$inventoryFilter.SetBounds(151,10,155,27);$inventoryFilter.Items.AddRange(@('Chỉ trang bị','Toàn bộ vật phẩm'));$inventoryFilter.SelectedIndex=0;$inventoryPage.Controls.Add($inventoryFilter)
$inventorySearch=New-Object Windows.Forms.TextBox;$inventorySearch.SetBounds(316,11,170,25);$inventoryPage.Controls.Add($inventorySearch)
$inventoryAuto=New-Object Windows.Forms.CheckBox;$inventoryAuto.Text='Tự cập nhật (3 giây)';$inventoryAuto.SetBounds(496,10,160,27);$inventoryPage.Controls.Add($inventoryAuto)
$inventoryHint=Ui-Label 'Chọn tài khoản đã vào game để đọc hành trang. Tìm theo tên hoặc ID.' $inventoryPage 12 43 645 36;$inventoryHint.Anchor='Top,Left,Right'
$inventoryGrid=New-Object TGLL.ResponsiveGrid;$inventoryGrid.SetBounds(12,82,645,165);$inventoryGrid.Anchor='Top,Left';$inventoryGrid.ScrollBars='Both'
$inventoryGrid.AllowUserToResizeColumns=$false;$inventoryGrid.AllowUserToResizeRows=$false;$inventoryGrid.AllowUserToOrderColumns=$false
$inventoryGrid.ReadOnly=$true;$inventoryGrid.AllowUserToAddRows=$false;$inventoryGrid.AllowUserToDeleteRows=$false;$inventoryGrid.RowHeadersVisible=$false;$inventoryGrid.AutoGenerateColumns=$false;$inventoryGrid.SelectionMode='FullRowSelect';$inventoryGrid.MultiSelect=$false;$inventoryGrid.BackgroundColor=[Drawing.Color]::White;$inventoryGrid.AutoSizeColumnsMode='None'
foreach($c in @(@('slot','Ô túi',55),@('template','ID',65),@('name','Tên trang bị / vật phẩm',260),@('quantity','Số lượng',70),@('type','Loại ID',65))){$col=New-Object Windows.Forms.DataGridViewTextBoxColumn;$col.Name=$c[0];$col.HeaderText=$c[1];$col.Width=[int]$c[2];$col.AutoSizeMode='None';$col.Resizable='False';$null=$inventoryGrid.Columns.Add($col)}
$quantityColumn=$inventoryGrid.Columns['quantity'];$quantityColumn.AutoSizeMode='None';$quantityColumn.Width=70;$quantityColumn.MinimumWidth=65;$quantityColumn.ValueType=[int]
$quantityColumn.DefaultCellStyle.Alignment='MiddleCenter';$quantityColumn.HeaderCell.Style.Alignment='MiddleCenter';$quantityColumn.HeaderCell.Style.WrapMode='False'
$inventoryPage.Controls.Add($inventoryGrid)
function Resize-InventoryUi {
    # Calculate from the visible page, not the default TabPage size used by Anchor.
    $inventoryGrid.SetBounds(12,82,[Math]::Max(1,$inventoryPage.ClientSize.Width-24),[Math]::Max(1,$inventoryPage.ClientSize.Height-90))
}
$inventoryPage.Add_SizeChanged({Resize-InventoryUi});Resize-InventoryUi
$script:inventoryViewKey='';$script:inventoryRequestAt=[datetime]::MinValue
function Request-InventoryUi {
    if(!$grid.SelectedRows.Count){return}
    $a=$grid.SelectedRows[0].Tag;$key=[string]$a.id
    if(!$script:accountJobs.ContainsKey($key)){$inventoryHint.Text='Tài khoản chưa mở trong phiên QLTK này.';return}
    try {[TGLL.NativeBackend]::ScanInventory($script:accountJobs[$key]);$script:inventoryRequestAt=[datetime]::UtcNow;Update-InventoryUi}
    catch {$inventoryHint.Text='Mở tài khoản bằng QLTK và đợi vào game để đọc hành trang.'}
}
function Update-InventoryUi {
    if(!$grid.SelectedRows.Count){$inventoryGrid.Rows.Clear();$script:inventoryViewKey='';$inventoryHint.Text='Chọn tài khoản để xem hành trang.';return}
    $a=$grid.SelectedRows[0].Tag;$key=[string]$a.id;$j=$null
    if($script:accountJobs.ContainsKey($key)){$j=Get-AccountJob $script:accountJobs[$key]}
    if($null -eq $j -or !$j.PSObject.Properties['Inventory']){$inventoryGrid.Rows.Clear();$script:inventoryViewKey='';$inventoryHint.Text=$a.username+' • Chưa mở bằng bộ nối trực tiếp trong phiên QLTK này.';return}
    $snapshot=$j.Inventory;$all=@($snapshot.Items);$query=$inventorySearch.Text.Trim()
    $items=@($all | Where-Object {($inventoryFilter.SelectedIndex -eq 1 -or $_.Equipment) -and (!$query.Length -or $_.Name.IndexOf($query,[StringComparison]::OrdinalIgnoreCase) -ge 0 -or ([string]$_.TemplateId).Contains($query))})
    $signature=$key+':'+$snapshot.ReadAt.Ticks+':'+$snapshot.Ready+':'+$inventoryFilter.SelectedIndex+':'+$query
    if($signature -ne $script:inventoryViewKey){
        $inventoryGrid.Rows.Clear();foreach($item in $items){$i=$inventoryGrid.Rows.Add([object[]]@($item.Slot,$item.TemplateId,$item.Name,$item.Quantity,$item.Type));$inventoryGrid.Rows[$i].Tag=$item};$script:inventoryViewKey=$signature
    }
    $equipmentCount=@($all | Where-Object Equipment).Count
    $inventoryHint.Text=$a.username+' • '+$equipmentCount+' trang bị / '+$all.Count+' ô có vật phẩm / '+$snapshot.Capacity+' ô túi • Hiển thị '+$items.Count
    if($snapshot.ReadAt -ne [datetime]::MinValue){$inventoryHint.Text+=' • '+$snapshot.ReadAt.ToLocalTime().ToString('HH:mm:ss')}
    if($j.InventoryScanning){$inventoryHint.Text+="`r`nĐang đọc hành trang..."}
    elseif(!$snapshot.Ready){
        $message=switch($snapshot.Error){'game_exited' {'Game đã thoát.'};'inventory_changed' {'Hành trang đang thay đổi, bấm Đọc hành trang để thử lại.'};'inventory_template_missing' {'Chưa tải đủ tên vật phẩm, thử đọc lại.'};default {'Chưa đọc được hành trang, đợi vào game rồi thử lại.'}}
        $inventoryHint.Text+="`r`n"+$message;if($all.Count){$inventoryHint.Text+=' Danh sách đang hiển thị là lần đọc trước.'}
    }
    if($tabs.SelectedTab -eq $inventoryPage -and $inventoryAuto.Checked -and !$j.InventoryScanning -and ([datetime]::UtcNow-$script:inventoryRequestAt).TotalSeconds -ge 3){
        try {[TGLL.NativeBackend]::ScanInventory($j.Id);$script:inventoryRequestAt=[datetime]::UtcNow}catch{}
    }
}
$inventoryFilter.Add_SelectedIndexChanged({$script:inventoryViewKey='';Update-InventoryUi})
$inventorySearch.Add_TextChanged({$script:inventoryViewKey='';Update-InventoryUi})
$grid.Add_SelectionChanged({Update-InventoryUi;if($tabs.SelectedTab -eq $inventoryPage){Request-InventoryUi}})
$tabs.Add_SelectedIndexChanged({if($tabs.SelectedTab -eq $inventoryPage){Resize-InventoryUi;Update-InventoryUi;Request-InventoryUi}})
