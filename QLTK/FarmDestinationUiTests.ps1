$fdMode=$farmMode.SelectedIndex;$fdMap=[int]$farmManualMap.Value;$fdZone=[int]$farmZone.Value;$fdSpots=$script:farmSpots;$fdAccounts=$script:accounts;$fdRequests=$script:farmUpRequests
try{
 $script:farmSpots=@();$farmMaps.SelectedIndex=69
 if([int]$farmManualMap.Value -ne 69 -or $farmMode.SelectedIndex -ne 3){throw 'Village dropdown did not drive manual destination'}
 $farmManualMap.Value=59;if($farmMaps.SelectedIndex -ne 59){throw 'Map ID did not update dropdown'}
 $farmManualMap.Value=69;$farmZone.Value=14;$config=Get-FarmRunConfig
 if($config.Spots[0].MapId -ne 69 -or $config.Spots[0].Zone -ne 14 -or $config.Quiet -or $farmQuiet.Enabled){throw 'Village/fixed-zone configuration invalid'}
 $serialized=$config | ConvertTo-Json -Depth 8 | ConvertFrom-Json
 $a=[pscustomobject]@{id='farm-destination-fixture';username='fixture';upEnabled=$true;upConfig=$serialized};$script:accounts=@($a);$script:farmUpRequests=@{};Restore-UpRequests
 if($script:farmUpRequests[$a.id].Config.Spots[0].Zone -ne 14){throw 'Saved zone lost after restart'}
 $a.upConfig.Spots[0].PSObject.Properties.Remove('Zone');$script:farmUpRequests=@{};Restore-UpRequests
 if($script:farmUpRequests[$a.id].Config.Spots[0].Zone -ne -1){throw 'Legacy config forced zone zero'}
 $farmZone.Value=-1;if(!$farmQuiet.Enabled -or (Get-FarmRunConfig).Spots[0].Zone -ne -1){throw 'Automatic zone mode failed to restore'}
 if($farmGrid.Columns['zone'].HeaderText -ne 'Khu'){throw 'Saved spots missing zone column'}
}finally{$script:accounts=$fdAccounts;$script:farmUpRequests=$fdRequests;$script:farmSpots=$fdSpots;$farmManualMap.Value=$fdMap;$farmZone.Value=$fdZone;$farmMode.SelectedIndex=$fdMode;Show-FarmSpots}
Write-Output 'PASS: village/map dropdown synchronization, fixed-zone priority, zone column, config restart persistence and legacy -1 default.'
