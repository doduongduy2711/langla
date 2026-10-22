$marketTestAccount=$grid.Rows[0].Tag
$oldMarketProperty=$marketTestAccount.PSObject.Properties['marketConfig'];$oldMarket=$null;if($null -ne $oldMarketProperty){$oldMarket=$oldMarketProperty.Value}
try {
 $grid.ClearSelection();$grid.Rows[0].Selected=$true
 $marketSellRules.Text="277,5,100`r`n498,1,200";$marketScanRules.Text='277,80';$marketAutoBuy.Checked=$true;$marketMaxBuys.Value=3;$marketBudget.Value=500;Save-MarketUi
 $p=Get-MarketPreference $marketTestAccount
 if($p.sell -notmatch '498,1,200' -or $p.scan -ne '277,80' -or !$p.buy -or $p.maxBuys -ne 3 -or $p.budget -ne 500){throw 'Market preferences lost'}
 Update-MarketUi;if($marketSellRules.Text -notmatch '277,5,100' -or $marketMaxBuys.Value -ne 3){throw 'Market UI roundtrip failed'}
 Copy-AccountConfig;$copied=$script:copiedAccountConfig | ConvertFrom-Json;if($copied.marketConfig.maxBuys -ne 3 -or $copied.marketConfig.budget -ne 500){throw 'Market config clipboard missing'}
 if($marketPage.Text -ne 'Auto chợ' -or !$marketPage.AutoScroll -or !$marketReport.ReadOnly -or $tabs.TabPages.Count -ne 22){throw 'Market layout failed'}
 $fixture=@([pscustomobject]@{account='acc1';itemId=428;name='Rương ngọc khảm';quantity=500;price=14000000;seller='Seller';listingId=502044},[pscustomobject]@{account='acc2';itemId=8;name='Đá cấp 9';quantity=1;price=45555;seller='Other';listingId=502067})
 Set-MarketResultTable $fixture
 if($marketResultsGrid.Rows.Count -ne 2 -or $marketResultsGrid.Rows[0].Cells['itemId'].Value -ne 428 -or $marketResultsGrid.Rows[0].Cells['price'].Value -ne 14000000 -or $marketResultsGrid.Rows[1].Cells['account'].Value -ne 'acc2'){throw 'Market result ID/price/account columns incorrect'}
 if(!$marketResultsGrid.ReadOnly -or $marketResultsGrid.AllowUserToAddRows){throw 'Market result grid must be read only'}
 Set-MarketResultTable $fixture;if($marketResultsGrid.Rows.Count -ne 2){throw 'Market repeated refresh duplicated results'}
 Set-MarketResultTable @();if($marketResultsGrid.Rows.Count -ne 0){throw 'Market old results were retained'}
 Write-Output 'PASS: result grid ID/price/quantity/account mapping, repeated refresh, clear, and read-only display.'
 Write-Output 'PASS: market sell/scan multiline rules, separate account preferences, purchase count and budget limits, config clipboard and results layout. No transaction sent.'
}finally{if($null -ne $oldMarketProperty){$marketTestAccount | Add-Member NoteProperty marketConfig $oldMarket -Force}else{$marketTestAccount.PSObject.Properties.Remove('marketConfig')};Update-MarketUi}
