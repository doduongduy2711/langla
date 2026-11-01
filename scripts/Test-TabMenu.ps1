$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
$UiTest=$false
$script:dataDir=Join-Path $PSScriptRoot '../dist/tab-menu-test'
$null=[IO.Directory]::CreateDirectory($script:dataDir)
function Write-JsonAtomic($path,$value){[IO.File]::WriteAllText($path,($value|ConvertTo-Json -Depth 5),(New-Object Text.UTF8Encoding($false)))}
function Read-JsonData($path){Get-Content -LiteralPath $path -Raw -Encoding UTF8|ConvertFrom-Json}
function Show-Error($message){throw $message}
function Ui-NewButton($text,$action){$button=New-Object Windows.Forms.Button;$button.Text=$text;$button.Add_Click($action);return $button}
function Resize-UiLayout {}
$form=New-Object Windows.Forms.Form
$tabs=New-Object Windows.Forms.TabControl;$tabs.Dock='Fill';$form.Controls.Add($tabs)
$null=$form.Handle;$null=$tabs.Handle
$status=New-Object Windows.Forms.Label
$titles=@('Tài khoản','Đăng nhập / NPC','Proxy / Socket','Nhật ký','Train quái','Auto địa cung','Auto Sơn Cáp','Auto Cương Thi')
foreach($title in $titles){$page=New-Object Windows.Forms.TabPage;$page.Text=$title;$tabs.TabPages.Add($page)}
[IO.File]::WriteAllText((Join-Path $script:dataDir 'tab-visibility.json'),'{"hidden":["Train quái","Auto Cương Thi","Tab cũ"]}')
. (Join-Path $PSScriptRoot '../QLTK/TabMenuUi.ps1')
if($tabs.TabPages.Count -ne 6 -or $script:managerTabItems['Train quái'].Checked){throw 'Saved preferences not restored'}
$train=$script:managerTabPages[4];$child=New-Object Windows.Forms.CheckBox;$child.Checked=$true;$train.Controls.Add($child)
$script:managerTabItems['Train quái'].PerformClick()
if($tabs.TabPages[4] -ne $train -or !$child.Checked){throw ('Original order/control state not retained: '+$tabs.TabPages.Count+' '+$script:managerTabItems['Train quái'].Checked+' '+(($tabs.TabPages|ForEach-Object{$_.Text}) -join ',')+' child='+$child.Checked)}
$tabs.SelectedTab=$train;$script:managerTabItems['Train quái'].PerformClick()
if($tabs.TabPages.Contains($train) -or $train.IsDisposed -or !$child.Checked -or !$tabs.SelectedTab){throw 'Hiding selected page destroyed state or selection'}
$saved=Read-JsonData $script:tabVisibilityPath
if(@($saved.hidden) -notcontains 'Train quái' -or @($saved.hidden) -notcontains 'Auto Cương Thi'){throw 'Changes not persisted'}
foreach($page in $script:managerTabPages){Set-ManagerTabVisible $page $false}
if($tabs.TabPages.Count -ne 0 -or !$tabMenuButton.Enabled){throw 'Menu inaccessible after hiding every tab'}
foreach($page in $script:managerTabPages){Set-ManagerTabVisible $page $true}
if(($tabs.TabPages|ForEach-Object {$_.Text}) -join '|' -ne ($titles -join '|')){throw 'Tab ordering changed'}
if($script:managerTabItems.Count -ne $titles.Count){throw 'Menu missing tabs'}
$closing=[Windows.Forms.ToolStripDropDown].GetMethod('OnClosing',[Reflection.BindingFlags]'Instance,NonPublic')
foreach($reason in @('AppClicked','AppFocusChange','Keyboard')){
    $event=New-Object Windows.Forms.ToolStripDropDownClosingEventArgs ([Windows.Forms.ToolStripDropDownCloseReason]::$reason)
    $arguments=New-Object object[] 1;$arguments[0]=$event.PSObject.BaseObject;$null=$closing.Invoke($tabMenu,$arguments)
    if($event.Cancel -or !$tabMenu.AutoClose){throw ('Menu will not close: '+$reason)}
}
$event=New-Object Windows.Forms.ToolStripDropDownClosingEventArgs ([Windows.Forms.ToolStripDropDownCloseReason]::ItemClicked)
$arguments=New-Object object[] 1;$arguments[0]=$event.PSObject.BaseObject;$null=$closing.Invoke($tabMenu,$arguments);if(!$event.Cancel){throw 'Checkbox click should allow more selections'}
$form.Dispose();$tabMenu.Dispose()
Write-Output 'PASS: stored visibility, checkbox clicks, selected-page fallback, preserved controls, all-hidden recovery and original ordering.'

