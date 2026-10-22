$inventoryOriginalSize=$inventoryPage.Size
foreach($inventoryTestSize in @(@(690,228),@(980,370))){
    $inventoryPage.Size=New-Object Drawing.Size($inventoryTestSize[0],$inventoryTestSize[1]);Resize-InventoryUi
    if($inventoryGrid.Right -gt $inventoryPage.ClientSize.Width -or $inventoryGrid.Bottom -gt $inventoryPage.ClientSize.Height){throw 'Inventory grid or scrollbar clipped outside tab page'}
    if($inventoryGrid.ScrollBars -ne [Windows.Forms.ScrollBars]::Both){throw 'Inventory scrollbars disabled'}
}
$inventoryPage.Size=$inventoryOriginalSize;Resize-InventoryUi
$inventorySource=$script:accounts[0];$inventoryKey=[string]$inventorySource.id
$inventoryJobs=[TGLL.NativeBackend].GetField('jobs',[Reflection.BindingFlags]'NonPublic,Static').GetValue($null)
$inventoryCtor=[TGLL.NativeJob].GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')[0]
$inventoryFixture=$inventoryCtor.Invoke(@('C:\synthetic.exe','inventory-test','secret',$null,$true))
$inventorySnapshotCtor=[TGLL.InventorySnapshot].GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')[0]
$equip=New-Object TGLL.InventoryItem;$equip.Name='Kiếm cấp 50';$equip.TemplateId=123;$equip.Slot=9;$equip.Quantity=1;$equip.Equipment=$true;$equip.Type=3
$consumable=New-Object TGLL.InventoryItem;$consumable.Name='Vô hạn Di Chuyển phù';$consumable.TemplateId=168;$consumable.Slot=20;$consumable.Quantity=2;$consumable.Type=20
$inventoryFixture.Inventory=$inventorySnapshotCtor.Invoke(@([TGLL.InventoryItem[]]@($equip,$consumable),144,$true,'',[datetime]::UtcNow))
$null=$inventoryJobs.TryAdd($inventoryFixture.Id,$inventoryFixture);$script:accountJobs[$inventoryKey]=$inventoryFixture.Id
$inventoryAuto.Checked=$false;$inventorySearch.Text='';$inventoryFilter.SelectedIndex=0;Update-InventoryUi
if($inventoryGrid.Rows.Count -ne 1 -or $inventoryGrid.Rows[0].Cells['name'].Value -ne 'Kiếm cấp 50'){throw 'Equipment-only inventory view failed'}
$inventoryFilter.SelectedIndex=1
if($inventoryGrid.Rows.Count -ne 2){throw 'All items view omitted consumables'}
$inventorySearch.Text='168'
if($inventoryGrid.Rows.Count -ne 1 -or $inventoryGrid.Rows[0].Cells['template'].Value -ne 168){throw 'Inventory ID filter failed'}
$inventorySearch.Text='kiếm'
if($inventoryGrid.Rows.Count -ne 1 -or $inventoryGrid.Rows[0].Cells['template'].Value -ne 123){throw 'Inventory Unicode name filter failed'}
$inventoryFixture.Inventory=$inventorySnapshotCtor.Invoke(@([TGLL.InventoryItem[]]@($equip),144,$false,'inventory_changed',[datetime]::UtcNow));Update-InventoryUi
if($inventoryHint.Text -notmatch 'lần đọc trước'){throw 'Stale inventory was presented as live'}
$inventorySecond=[pscustomobject]@{id='inventory-second';username='inventory-second';secret='unused';proxy=$null;receiveKey=$false;dungeonHigh=$false}
$script:accounts+=@($inventorySecond);Refresh-Grid;$grid.ClearSelection();$grid.Rows[1].Selected=$true;Update-InventoryUi
if($inventoryGrid.Rows.Count -ne 0 -or $inventoryHint.Text -notmatch 'inventory-second'){throw 'Inventory leaked across accounts'}
$script:accountJobs.Remove($inventoryKey);$removed=$null;$null=$inventoryJobs.TryRemove($inventoryFixture.Id,[ref]$removed)
$script:accounts=@($inventorySource);$inventorySearch.Text='';$inventoryFilter.SelectedIndex=0;Refresh-Grid;Update-InventoryUi
Write-Output 'PASS: inventory tab, equipment/all-items view, ID/Unicode filters, stale-data notice and account isolation.'
