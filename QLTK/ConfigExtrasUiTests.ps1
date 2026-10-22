$ceOldMax=$cloakMax.Value;$ceOldAccounts=$script:accounts;$ceOldJobs=$script:accountJobs;$ceOldClip=$script:copiedAccountConfig;$ceSource=$script:accounts[0];$ceFields=@('stoneChestQuantity','stoneChestOpenAfter','cloakId','cloakQuantity','cloakEquipClose');$ceBackup=@{}
foreach($f in $ceFields){$p=$ceSource.PSObject.Properties[$f];$ceBackup[$f]=[pscustomobject]@{exists=($null -ne $p);value=$(if($p){$p.Value}else{$null})}}
$ceSavedSave=(Get-Item Function:Save-Accounts).ScriptBlock;$ceSavedUiTest=$UiTest
try{
 $script:accountJobs=@{};$ceDest=[pscustomobject]@{id='copy-extra';username='extra-demo';secret='keep-private';proxy='keep-route';stoneChestQuantity=3;stoneChestOpenAfter=$true;cloakId=128;cloakQuantity=1;cloakEquipClose=$true}
 $script:accounts=@($ceSource,$ceDest);Refresh-Grid;$grid.ClearSelection();$grid.Rows[0].Selected=$true
 $ceSource | Add-Member NoteProperty stoneChestQuantity 100 -Force;$ceSource | Add-Member NoteProperty stoneChestOpenAfter $false -Force;$ceSource | Add-Member NoteProperty cloakId 131 -Force;$ceSource | Add-Member NoteProperty cloakQuantity 2 -Force;$ceSource | Add-Member NoteProperty cloakEquipClose $false -Force
 $cloakMax.Value=12;Copy-AccountConfig;$ceCopy=$script:copiedAccountConfig | ConvertFrom-Json
 if($ceCopy.stoneChestQuantity -ne 100 -or $ceCopy.stoneChestOpenAfter -or $ceCopy.cloakId -ne 131 -or $ceCopy.cloakQuantity -ne 2 -or $ceCopy.cloakEquipClose){throw 'Chest/cloak copy fields missing or wrong'}
 $ceSource.stoneChestQuantity=9;$ceSource.cloakQuantity=9;$cloakMax.Value=3;Paste-AccountConfigAll
 if($cloakMax.Value -ne 12){throw 'Parallel cloak max not copied/pasted'}
 if($ceDest.stoneChestQuantity -ne 100 -or $ceDest.stoneChestOpenAfter -or $ceDest.cloakId -ne 131 -or $ceDest.cloakQuantity -ne 2 -or $ceDest.cloakEquipClose){throw 'Chest/cloak paste fields wrong'}
 if($ceDest.secret -ne 'keep-private' -or $ceDest.proxy -ne 'keep-route'){throw 'Copy/paste must preserve credentials and proxy'}
 $grid.ClearSelection();$grid.Rows[1].Selected=$true;Update-StoneChestUi;Update-CloakUi
 if($chestQuantity.Value -ne 100 -or $chestOpen.Checked -or $cloakChoice.SelectedItem.TemplateId -ne 131 -or $cloakQuantity.Value -ne 2 -or $cloakEquipClose.Checked){throw 'Pasted values must appear on both tabs'}
 if($script:cloakRun -and $script:cloakRun.active){throw 'Paste started purchase queue'}
 # Clipboard cũ thiếu trường mới phải giữ nguyên cấu hình đã có.
 foreach($f in $ceFields){$ceCopy.PSObject.Properties.Remove($f)};$script:copiedAccountConfig=$ceCopy | ConvertTo-Json -Depth 12 -Compress
 $ceDest.stoneChestQuantity=77;$ceDest.cloakId=130;Paste-AccountConfigAll
 if($ceDest.stoneChestQuantity -ne 77 -or $ceDest.cloakId -ne 130){throw 'Legacy clipboard cleared new preferences'}
 # Lưu lỗi phải hoàn tác cả những trường mới; không ghi tệp trong kiểm thử.
 $ceCopy | Add-Member NoteProperty stoneChestQuantity 11 -Force;$ceCopy | Add-Member NoteProperty cloakId 129 -Force;$script:copiedAccountConfig=$ceCopy | ConvertTo-Json -Depth 12 -Compress
 function Save-Accounts {throw 'synthetic_save_failure'}
 $UiTest=$false;$ceThrew=$false;try{Paste-AccountConfigAll}catch{$ceThrew=$true}finally{$UiTest=$ceSavedUiTest}
 if(!$ceThrew -or $ceDest.stoneChestQuantity -ne 77 -or $ceDest.cloakId -ne 130){throw 'New preference rollback failed on save error'}
 Write-Output 'PASS: cloak/chest config copy, independent snapshot, paste + both tab values, credentials preserved, no purchase start, legacy clipboard preservation and save-failure rollback.'
}finally{
 $UiTest=$ceSavedUiTest;Set-Item Function:Save-Accounts $ceSavedSave
 foreach($f in $ceFields){if($ceBackup[$f].exists){$ceSource | Add-Member NoteProperty $f $ceBackup[$f].value -Force}else{$ceSource.PSObject.Properties.Remove($f)}}
 $script:accounts=$ceOldAccounts;$script:accountJobs=$ceOldJobs;$script:copiedAccountConfig=$ceOldClip;$cloakMax.Value=$ceOldMax;Refresh-Grid
}
