$scOldMax=$soncapMax.Value;$scOldClose=$soncapCloseAfter.Checked
$scOldAccounts=$script:accounts;$scOldJobs=$script:accountJobs;$scOldClip=$script:copiedAccountConfig;$scOldGroup=$soncapGroupSize.Value;$scOldPortals=@($script:settings.soncapPortals)
try{
 $scSource=[pscustomobject]@{id='sc-source';username='sc-source';soncap=$true};$scDest=[pscustomobject]@{id='sc-dest';username='sc-dest';soncap=$false}
 $script:accounts=@($scSource,$scDest);$script:accountJobs=@{};Refresh-Grid;$grid.ClearSelection();$grid.Rows[0].Selected=$true
 $soncapGroupSize.Value=3;for($scI=0;$scI -lt 4;$scI++){$soncapRouteGrid.Rows[$scI].Cells['x'].Value=1000+$scI;$soncapRouteGrid.Rows[$scI].Cells['y'].Value=200+$scI}
 $soncapMax.Value=24;$soncapCloseAfter.Checked=$true;Copy-AccountConfig;$scSnapshot=$script:copiedAccountConfig | ConvertFrom-Json
 if(!$scSnapshot.soncap -or $scSnapshot.soncapGroupSize -ne 6 -or @($scSnapshot.soncapPortals).Count -ne 4){throw 'SonCap copy incomplete'}
 $soncapMax.Value=6;$soncapCloseAfter.Checked=$false;$soncapGroupSize.Value=1;$soncapRouteGrid.Rows[2].Cells['x'].Value=9;Paste-AccountConfigAll
 if(!$scDest.soncap -or $soncapGroupSize.Value -ne 6 -or $soncapRouteGrid.Rows[2].Cells['x'].Value -ne 1002 -or [TGLL.NativeBackend]::SonCapTeamSize -ne 6){throw 'SonCap paste failed'}
 if($soncapMax.Value -ne 24 -or !$soncapCloseAfter.Checked){throw 'SonCap max/auto-close not pasted'}
 $grid.ClearSelection();$grid.Rows[1].Selected=$true;Update-SonCapUi;if(!$soncapCheck.Checked){throw 'SonCap checkbox not refreshed'}
 foreach($scField in @('soncap','soncapGroupSize','soncapPortals')){$scSnapshot.PSObject.Properties.Remove($scField)}
 $script:copiedAccountConfig=$scSnapshot | ConvertTo-Json -Depth 12 -Compress;$scDest.soncap=$false;$soncapGroupSize.Value=2;Paste-AccountConfigAll
 if($scDest.soncap -or $soncapGroupSize.Value -ne 6){throw 'Legacy copy overwrote SonCap preferences'}
 Write-Output 'PASS: SonCap enabled flag, group size, all four portal coordinates, native configuration, checkbox refresh and legacy clipboard preservation.'
}finally{
 $soncapMax.Value=$scOldMax;$soncapCloseAfter.Checked=$scOldClose
 $script:loadingSonCap=$true
 try{$soncapGroupSize.Value=$scOldGroup;for($scI=0;$scI -lt 4;$scI++){$soncapRouteGrid.Rows[$scI].Cells['x'].Value=$scOldPortals[$scI].x;$soncapRouteGrid.Rows[$scI].Cells['y'].Value=$scOldPortals[$scI].y};Apply-SonCapConfig}finally{$script:loadingSonCap=$false}
 $script:accounts=$scOldAccounts;$script:accountJobs=$scOldJobs;$script:copiedAccountConfig=$scOldClip;Refresh-Grid
}
