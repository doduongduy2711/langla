# Synthetic session; no credentials, game process or network login is used.
$ctor=[TGLL.NativeJob].GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')[0]
$fake=$ctor.Invoke([object[]]@([string](Join-Path $PSScriptRoot 'QLTK.exe'),[string]'synthetic',[string]'unused',$null,$true))
$fake.State='opened';$fake.ReceiveKeyEnabled=$false
$jobsField=[TGLL.NativeBackend].GetField('jobs',[Reflection.BindingFlags]'Static,NonPublic')
$jobs=$jobsField.GetValue($null);$null=$jobs.TryAdd($fake.Id,$fake)
$testRow=$grid.Rows[0];$testRow.Cells['auto'].Value=$true
if(!$script:autoTrainWanted['test'] -or $fake.TrainEnabled){throw 'Auto pending before open failed'}
$script:accountJobs['test']=$fake.Id;Sync-AutoRows
if(!$fake.GameAutoWanted -or $fake.TrainEnabled){throw 'Auto did not request native idle mode'}
$testRow.Cells['auto'].Value=$false
if($fake.GameAutoWanted -or $script:autoTrainWanted['test']){throw 'Auto untick did not request idle off'}
$testRow.Cells['auto'].Value=$true;$fake.ReceiveKeyRequested=$true;Sync-AutoRows
if(!$fake.GameAutoWanted -or !$grid.Rows[0].Cells['auto'].Value){throw 'NPC flow lost idle intent'}
$fake.ReceiveKeyRequested=$false;$fake.GameSettingsReady=$true
$fake.TrainReady=$true;$skill=New-Object TGLL.SkillInfo;$skill.Id=295;$skill.Type=0
$skill2=New-Object TGLL.SkillInfo;$skill2.Id=368;$skill2.Type=1
$fake.Skills=[TGLL.SkillInfo[]]@($skill,$skill2);$script:trainSelections['test']=@(295,368)
Sync-AutoRows
if(!$fake.TrainEnabled -or ($fake.TrainSkillIds -join ',') -ne '295,368'){throw 'Auto failed to start account selected skills'}
$testRow.Cells['auto'].Value=$false
if($fake.TrainEnabled -or $fake.GameAutoWanted){throw 'Untick did not stop both modes'}
$testRow.Cells['auto'].Value=$true
if(!$fake.TrainEnabled -or !$fake.GameAutoWanted){throw 'Tick did not start both modes'}
$fake.ReceiveKeyRequested=$true;$fake.TrainEnabled=$false;Sync-AutoRows
if($fake.TrainEnabled){throw 'Train interrupted NPC flow'}
$fake.ReceiveKeyRequested=$false;$fake.State='key_submitted';Sync-AutoRows
if(!$fake.TrainEnabled){throw 'Train did not resume after NPC'}
$script:trainSelections['test']=@();$fake.TrainEnabled=$false;Sync-AutoRows
if(!$fake.TrainEnabled -or ($fake.TrainSkillIds -join ',') -ne '295'){throw 'Default Phi missing for empty selection'}
$script:trainSelections['test']=@(368);Sync-AutoRows
if(!$fake.TrainEnabled -or ($fake.TrainSkillIds -join ',') -ne '295,368'){throw 'Updated account selection not used'}
$check=New-Object TGLL.GameOption;$check.Tab=1;$check.Code=201;$check.Kind=0;$check.Label='Tự động nhặt vật phẩm'
$drop=New-Object TGLL.GameOption;$drop.Tab=0;$drop.Code=400;$drop.Kind=1;$drop.Label='Phạm vi đánh';$drop.Choices=[string[]]@('Không giới hạn','Gần')
$keyboard=New-Object TGLL.GameOption;$keyboard.Tab=2;$keyboard.Code=1000;$keyboard.Kind=2;$keyboard.Label='Bàn phím';$keyboard.Choices=[string[]]@('Bàn phím ảo','Bàn phím hệ thống','Gõ phím trực tiếp')
$benefit=New-Object TGLL.GameOption;$benefit.Tab=3;$benefit.Code=2907;$benefit.Kind=0;$benefit.Label='Nhập phúc lợi'
$fake.GameOptions=[TGLL.GameOption[]]@($check,$drop,$keyboard,$benefit)
Update-GameSettingsUi
if($script:gameSettingControls.Count -ne 4 -or $script:gameSettingPages.Count -ne 4){throw 'Four native setting tabs failed'}
$script:gameSettingControls['201'].Checked=$true
if($script:accounts[0].gameOverrides[0].code -ne 201 -or $script:accounts[0].gameOverrides[0].value -ne 1){throw 'Checkbox choice not saved'}
$script:gameSettingControls['400'].SelectedIndex=1;Save-GameOption $script:gameSettingControls['400']
if(@($script:accounts[0].gameOverrides | Where-Object {$_.code -eq 400 -and $_.value -eq 1}).Count -ne 1){throw 'Dropdown choice not saved'}
$queueField=[TGLL.NativeJob].GetField('SettingsQueue',[Reflection.BindingFlags]'Instance,NonPublic')
if($queueField.GetValue($fake).Count -ne 2){throw 'Setting commands not queued'}
$rejected=$false;try{[TGLL.NativeBackend]::SetGameOption($fake.Id,400,100)}catch{$rejected=$true}
if(!$rejected){throw 'Invalid choice accepted'}
$fake.GameAutoEnabled=$true;Sync-AutoRows;Refresh-Grid;Sync-AutoRows
if(!$grid.Rows[0].Cells['auto'].Value -or !$fake.TrainEnabled){throw 'Refresh lost combined auto intent'}
$receiveKey.Checked=$true
if(!$fake.ReceiveKeyEnabled -or !$fake.ReceiveKeyRequested -or !$script:accounts[0].receiveKey){throw 'Dungeon key request failed'}
$receiveKey.Checked=$false
if($fake.ReceiveKeyEnabled -or $fake.ReceiveKeyRequested){throw 'Dungeon untick failed'}
Set-AccountAuto $grid.Rows[0] $false
$fake.EntryAutoActivations++;$fake.GameAutoWanted=$true;Sync-AutoRows
if(!$grid.Rows[0].Cells['auto'].Value -or !$script:autoTrainWanted[[string]$grid.Rows[0].Tag.id]){throw 'Entry auto did not synchronize checkbox'}
Set-AccountAuto $grid.Rows[0] $false;Sync-AutoRows
if($grid.Rows[0].Cells['auto'].Value -or $fake.GameAutoWanted){throw 'Manual untick after entry did not stay off'}
$removed=$null;$null=$jobs.TryRemove($fake.Id,[ref]$removed);$script:accountJobs.Clear()
$second=[pscustomobject]@{id='test2';username='demo2';secret='unused-in-masked-view'}
$script:accounts+=@($second);Refresh-Grid;Enable-AllAuto
if(@($grid.Rows | Where-Object {$_.Cells['auto'].Value -eq $true}).Count -ne 2){throw 'Auto all failed'}
$grid.ClearSelection();$grid.Rows[1].Selected=$true;Update-GameSettingsUi
if($script:gameSettingControls.Count){throw 'Account selection leaked setting controls'}
if($autoAllButton.Text -ne 'Bật auto tất cả' -or $bottomButtons.Count -ne 12 -or $autoAllButton.Bounds.Right -gt $bottom.Width){throw 'Auto all layout failed'}
$script:accounts=@($script:accounts[0]);Refresh-Grid
Write-Output 'PASS: combined idle/train Auto, selected skills, pending/NPC/resume/untick, all-account toggle, four setting tabs, account isolation, saved choices, native queue and validation.'
