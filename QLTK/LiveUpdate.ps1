# Reload function-only updates inside this manager. Do not rerun startup or recreate controls/jobs.
function Get-LiveUpdateHash([string]$path) {
    if(![IO.File]::Exists($path)){throw ('Không tìm thấy tệp cập nhật: '+[IO.Path]::GetFileName($path))}
    return (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
}
function Test-LiveUpdateNamedParser {
    return @([Management.Automation.Language.Parser].GetMethods([Reflection.BindingFlags]'Public,Static') | Where-Object {$_.Name -eq 'ParseInput' -and $_.GetParameters().Count -eq 4}).Count -gt 0
}
function Read-LiveUpdateSource([string]$path) {
    $initialHash=Get-LiveUpdateHash $path;$text=[IO.File]::ReadAllText($path);$tokens=$null;$errors=$null
    # ParseFile preserves source paths and is available on older VPS PowerShell.
    $ast=[Management.Automation.Language.Parser]::ParseFile($path,[ref]$tokens,[ref]$errors)
    if($errors.Count){throw ('Tệp '+[IO.Path]::GetFileName($path)+' lỗi cú pháp, dòng '+$errors[0].Extent.StartLineNumber+'. Chưa áp dụng cập nhật.')}
    $definitions=@{};$root=$text
    $functions=@($ast.EndBlock.Statements | Where-Object {$_ -is [Management.Automation.Language.FunctionDefinitionAst]})
    foreach($fn in $functions){
        if($fn.Name -notmatch '^[A-Za-z_][A-Za-z0-9_-]*$'){throw ('Tên hàm cập nhật không hợp lệ hoặc trùng lặp: '+$fn.Name+' trong '+[IO.Path]::GetFileName($path))}
        $definitions[$fn.Name]=$fn.Extent.Text
    }
    foreach($fn in @($functions | Sort-Object {$_.Extent.StartOffset} -Descending)){$root=$root.Remove($fn.Extent.StartOffset,$fn.Extent.EndOffset-$fn.Extent.StartOffset)}
    $rootTokens=$null;$null=[Management.Automation.Language.Parser]::ParseInput($root,[ref]$rootTokens,[ref]$errors)
    $shape=($rootTokens | Where-Object {$_.Kind -notin @('NewLine','Comment','EndOfInput')} | ForEach-Object {([string]$_.Text.Length)+':'+$_.Text}) -join '|'
    $finalHash=Get-LiveUpdateHash $path;if($initialHash -ne $finalHash){throw 'Tệp cập nhật đang được ghi. Đợi vài giây rồi bấm lại.'}
    return [pscustomobject]@{Path=$path;Hash=$finalHash;Shape=$shape;Functions=$definitions}
}
function Initialize-LiveUpdate {
    $script:liveUpdateSources=@{};$script:liveUpdateBinaries=@{};$script:liveUpdateBusy=$false
    $root=[IO.Path]::GetFullPath($PSScriptRoot)
    $paths=@((Join-Path $root 'AccountManager.ps1'))
    foreach($fn in @(Get-Command -CommandType Function)){
        $path=$fn.ScriptBlock.File
        if($path -and [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($path)) -eq $root -and $path -notlike '*Tests.ps1'){$paths+=$path}
    }
    foreach($path in @($paths | Select-Object -Unique)){$script:liveUpdateSources[$path]=Read-LiveUpdateSource $path}
    foreach($name in @('NativeBackend.cs','AutoLogin.cs','SocketServer.cs','StoredJson.cs','UiPerformance.cs','MapCatalog.json','CaptchaOcr.py')){
        $path=Join-Path $root $name;$script:liveUpdateBinaries[$path]=Get-LiveUpdateHash $path
    }
}
function Apply-LiveUpdate {
    $changed=@();$pending=@();$definitions=@{};$before=@{};$snapshots=@{}
    # Validate the entire batch first; a changed native type/startup requires restart, not partial execution.
    foreach($path in $script:liveUpdateBinaries.Keys){if((Get-LiveUpdateHash $path) -ne $script:liveUpdateBinaries[$path]){$pending+=[IO.Path]::GetFileName($path)}}
    foreach($path in $script:liveUpdateSources.Keys){
        $old=$script:liveUpdateSources[$path]
        if((Get-LiveUpdateHash $path) -eq $old.Hash){continue}
        $next=Read-LiveUpdateSource $path;$snapshots[$path]=$next;$changed+=$path
        if($next.Shape -ne $old.Shape){$pending+=[IO.Path]::GetFileName($path);continue}
        foreach($name in @($old.Functions.Keys)){
            if(!$next.Functions.ContainsKey($name)){$pending+=[IO.Path]::GetFileName($path);break}
        }
        foreach($name in $next.Functions.Keys){
            if($old.Functions.ContainsKey($name) -and $old.Functions[$name] -ceq $next.Functions[$name]){continue}
            $current=Get-Command -Name $name -CommandType Function -ErrorAction SilentlyContinue
            # A later loaded module may intentionally override an earlier function.
            if($current -and $current.ScriptBlock.File -and $current.ScriptBlock.File -ne $path){
                if($old.Functions.ContainsKey($name)){continue}
                throw ('Hàm mới bị trùng: '+$name+'. Chưa áp dụng cập nhật.')
            }
            if($definitions.ContainsKey($name)){throw ('Hai tệp cùng sửa hàm '+$name+'. Chưa áp dụng cập nhật.')}
            # Older hosts can download/install updates, but cannot preserve the
            # source context when compiling changed function text in memory.
            if(!(Test-LiveUpdateNamedParser)){$pending+=[IO.Path]::GetFileName($path);continue}
            $definition=$next.Functions[$name] -replace ('(?i)^(function|filter)\s+'+[regex]::Escape($name)+'\b'),('$1 script:'+$name)
            $tokens=$null;$errors=$null
            $body=[Management.Automation.Language.Parser]::ParseInput($definition,$path,[ref]$tokens,[ref]$errors)
            if($errors.Count){throw ('Không nạp được hàm '+$name+'.')}
            $definitions[$name]=$body.GetScriptBlock()
            $before[$name]=if($current){$current.ScriptBlock}else{$null}
        }
    }
    if($pending.Count){return [pscustomobject]@{Applied=0;RestartRequired=$true;Files=@($pending | Select-Object -Unique)}}
    # Reject files being replaced while the batch was being checked.
    foreach($path in $snapshots.Keys){if((Get-LiveUpdateHash $path) -ne $snapshots[$path].Hash){throw 'Tệp cập nhật đang được ghi. Đợi vài giây rồi bấm lại.'}}
    foreach($path in $script:liveUpdateBinaries.Keys){if((Get-LiveUpdateHash $path) -ne $script:liveUpdateBinaries[$path]){throw 'Bộ điều khiển game vừa thay đổi. Chưa áp dụng cập nhật.'}}
    $applied=@()
    try{
        foreach($name in $definitions.Keys){$applied+=$name;. $definitions[$name]}
    }catch{
        foreach($name in $applied){
            if($null -ne $before[$name]){Set-Item -Path ('Function:script:'+$name) -Value $before[$name]}
            else{Remove-Item -Path ('Function:script:'+$name) -ErrorAction SilentlyContinue}
        }
        throw 'Không nạp được bản cập nhật; đã khôi phục các hàm trước đó.'
    }
    foreach($path in $snapshots.Keys){$script:liveUpdateSources[$path]=$snapshots[$path]}
    return [pscustomobject]@{Applied=$applied.Count;RestartRequired=$false;Files=$changed}
}
function Invoke-LocalLiveUpdate {
    if($script:liveUpdateBusy){return}
    $script:liveUpdateBusy=$true;$refreshButton.Enabled=$false;$refreshButton.Text='Đang nạp...'
    $resume=$null -ne $socketTimer -and $socketTimer.Enabled
    try{
        if($resume){$socketTimer.Stop()}
        $result=Apply-LiveUpdate
        if($result.RestartRequired){
            $message='Bản cập nhật này cần mở lại QLTK: '+($result.Files -join ', ')+'. Chưa áp dụng thay đổi; phiên hiện tại vẫn chạy.'
        }elseif($result.Applied){$message='Đã cập nhật QLTK ('+$result.Applied+' hàm). Các acc và hàng chờ tiếp tục chạy.'}
        else{$message='QLTK đã dùng các tệp hiện có; chưa có bản sửa mới để nạp.'}
        $script:liveUpdateMessageUntil=[datetime]::UtcNow.AddSeconds(20);$script:liveUpdateMessage=$message;$refreshButton.AccessibleDescription=$message;$status.Text=$message
    }catch{
        $script:liveUpdateMessageUntil=[datetime]::UtcNow.AddSeconds(20);$script:liveUpdateMessage='Cập nhật QLTK thất bại: '+$_.Exception.Message;$status.Text=$script:liveUpdateMessage
    }finally{
        if($resume){$socketTimer.Start()}
        $refreshButton.Text='Cập nhật QLTK';$refreshButton.Enabled=$true;$script:liveUpdateBusy=$false
    }
}

# Functions only: this module is appended to LiveUpdate.ps1 for in-place bootstrap.
function Normalize-UpdateRepository([string]$value) {
    $repo=$value.Trim() -replace '^https://github\.com/','' -replace '/$','' -replace '\.git$',''
    if($repo -notmatch '^[A-Za-z0-9][A-Za-z0-9-]{0,38}/[A-Za-z0-9_.-]{1,100}$' -or $repo.Split('/')[1] -in @('.','..')){throw 'Nhập owner/repo hoặc link repository GitHub.'}
    return $repo
}
function Get-UpdateRepository {
    if($script:settings.ContainsKey('updateRepository')){if($script:settings.updateRepository){return Normalize-UpdateRepository ([string]$script:settings.updateRepository)};return ''}
    $path=Join-Path $PSScriptRoot 'update-channel.json'
    if(Test-Path -LiteralPath $path){$channel=Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json;if($channel.repository){return Normalize-UpdateRepository ([string]$channel.repository)}}
    return ''
}
function Initialize-GitHubUpdate {
    if($script:githubUpdateTimer){return}
    $script:githubUpdateTimer=New-Object Windows.Forms.Timer;$script:githubUpdateTimer.Interval=200
    $script:githubUpdateTimer.Add_Tick({Pump-GitHubUpdate})
    $menu=New-Object Windows.Forms.ContextMenuStrip
    $entry=$menu.Items.Add('Nguồn cập nhật GitHub...');$entry.Add_Click({Show-UpdateRepositoryDialog})
    $local=$menu.Items.Add('Chỉ nạp bản sửa trên máy này');$local.Add_Click({Invoke-LocalLiveUpdate})
    $refreshButton.ContextMenuStrip=$menu
    $form.Add_Disposed({if($script:githubUpdateWork){$script:githubUpdateWork.client.CancelAsync();$script:githubUpdateWork.client.Dispose()};$script:githubUpdateTimer.Stop();$script:githubUpdateTimer.Dispose()})
}
function Show-UpdateRepositoryDialog {
    if($script:liveUpdateBusy){return}
    $dialog=New-Object Windows.Forms.Form;$dialog.Text='Nguồn cập nhật GitHub';$dialog.ClientSize=New-Object Drawing.Size(470,160);$dialog.StartPosition='CenterParent'
    $label=New-Object Windows.Forms.Label;$label.SetBounds(12,12,445,35);$label.Text='Repository công khai (owner/repo). Để trống: chỉ cập nhật trên máy này.';$dialog.Controls.Add($label)
    $box=New-Object Windows.Forms.TextBox;$box.SetBounds(12,52,445,25);$box.Text=Get-UpdateRepository;$dialog.Controls.Add($box)
    $save=New-Object Windows.Forms.Button;$save.Text='Lưu';$save.SetBounds(350,100,105,28);$dialog.Controls.Add($save)
    $save.Add_Click({try{$repo='';if($box.Text.Trim()){$repo=Normalize-UpdateRepository $box.Text};$script:settings.updateRepository=$repo;if(!$UiTest){Write-JsonAtomic $script:configPath $script:settings};$dialog.DialogResult='OK'}catch{[Windows.Forms.MessageBox]::Show($_.Exception.Message,'QLTK') | Out-Null}})
    $dialog.AcceptButton=$save;$null=$dialog.ShowDialog($form);$dialog.Dispose()
}
function New-UpdateWebClient {
    [Net.ServicePointManager]::SecurityProtocol=[Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    $client=New-Object Net.WebClient;$client.Headers['User-Agent']='QLTK-Updater';$client.Headers['Accept']='application/vnd.github+json';return $client
}
function Read-GitHubRelease($release,[string]$repo) {
    if($release.draft -or $release.prerelease -or [string]$release.tag_name -notmatch '^v([1-9][0-9]{0,8})$'){throw 'Bản phát hành GitHub chưa hợp lệ.'}
    $version=[int]$Matches[1];$assets=@($release.assets | Where-Object {$_.name -ceq 'QLTK-update.zip' -and $_.state -eq 'uploaded'})
    if($assets.Count -ne 1){throw 'Bản phát hành chưa có đúng một file QLTK-update.zip.'}
    $asset=$assets[0];$url=[uri]$asset.browser_download_url
    $prefix='/'+$repo+'/releases/download/'+[string]$release.tag_name+'/QLTK-update.zip'
    if($url.Scheme -ne 'https' -or $url.Host -ne 'github.com' -or $url.AbsolutePath -cne $prefix -or $url.UserInfo -or $url.Query){throw 'Link gói cập nhật không thuộc repository đã chọn.'}
    if([long]$asset.size -lt 1 -or [long]$asset.size -gt 33554432 -or [string]$asset.digest -notmatch '^sha256:([a-fA-F0-9]{64})$'){throw 'Gói cập nhật thiếu mã kiểm tra SHA-256 hoặc vượt kích thước cho phép.'}
    return [pscustomobject]@{Version=$version;Url=$url;Size=[long]$asset.size;Hash=$Matches[1].ToUpperInvariant()}
}
function Test-UpdateFileName([string]$name) {
    if($name -ceq 'CaptchaOcr.py'){return $true}
    if($name -notmatch '^[A-Za-z0-9_-]+\.(ps1|cs|json)$'){return $false}
    if($name -match '(?i)(test|profilemanager|nametestmanager)'){return $false}
    if($name.EndsWith('.json',[StringComparison]::OrdinalIgnoreCase)){return $name -cin @('MapCatalog.json','update-channel.json','release-version.json')}
    return $true
}
function Expand-VerifiedUpdate([string]$zipPath,[string]$stagePath,$release) {
    if((Get-Item -LiteralPath $zipPath).Length -ne $release.Size -or (Get-LiveUpdateHash $zipPath) -cne $release.Hash){throw 'Gói tải về không khớp mã kiểm tra; chưa thay đổi QLTK.'}
    Add-Type -AssemblyName System.IO.Compression,System.IO.Compression.FileSystem
    $zip=[IO.Compression.ZipFile]::OpenRead($zipPath)
    try{
        if($zip.Entries.Count -gt 128){throw 'Gói cập nhật có quá nhiều tệp.'}
        $entries=@{};$total=0L
        foreach($entry in $zip.Entries){
            if($entries.ContainsKey($entry.FullName) -or $entry.Length -lt 1 -or $entry.Length -gt 16777216){throw 'Gói cập nhật chứa tệp trùng hoặc kích thước không hợp lệ.'}
            $entries[$entry.FullName]=$entry;$total+=$entry.Length
            if($total -gt 67108864){throw 'Gói cập nhật sau giải nén quá lớn.'}
        }
        if(!$entries.ContainsKey('manifest.json') -or $entries['manifest.json'].Length -gt 262144){throw 'Gói cập nhật thiếu manifest.json hợp lệ.'}
        $reader=New-Object IO.StreamReader($entries['manifest.json'].Open());try{$manifest=$reader.ReadToEnd() | ConvertFrom-Json}finally{$reader.Dispose()}
        if([int]$manifest.schema -ne 1 -or [int]$manifest.version -ne $release.Version){throw 'Phiên bản trong gói không khớp bản phát hành.'}
        $files=@($manifest.files);if($files.Count -lt 1 -or $files.Count -gt 100 -or $entries.Count -ne $files.Count+1){throw 'Danh sách tệp cập nhật không khớp gói.'}
        $seen=@{};$null=[IO.Directory]::CreateDirectory($stagePath)
        foreach($file in $files){
            $name=[string]$file.name
            if(!(Test-UpdateFileName $name) -or $seen.ContainsKey($name) -or [string]$file.sha256 -notmatch '^[a-fA-F0-9]{64}$'){throw 'Tên tệp hoặc mã kiểm tra trong gói không hợp lệ.'}
            $seen[$name]=$true;$entry=$entries['QLTK/'+$name]
            if($null -eq $entry -or [long]$file.size -ne $entry.Length){throw 'Gói thiếu tệp hoặc sai kích thước.'}
            $target=Join-Path $stagePath $name;[IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$target,$false)
            if((Get-LiveUpdateHash $target) -cne ([string]$file.sha256).ToUpperInvariant()){throw 'Nội dung tệp cập nhật không khớp mã kiểm tra.'}
            if($name.EndsWith('.ps1',[StringComparison]::OrdinalIgnoreCase)){
                $tokens=$null;$errors=$null;$null=[Management.Automation.Language.Parser]::ParseFile($target,[ref]$tokens,[ref]$errors)
                if($errors.Count){throw ('Tệp cập nhật lỗi cú pháp: '+$name)}
            }
        }
        return $files
    }finally{$zip.Dispose()}
}
function Write-UpdateFile([string]$source,[string]$target) {
    $temp=$target+'.update-'+[guid]::NewGuid().ToString('N')
    try{[IO.File]::Copy($source,$temp);if([IO.File]::Exists($target)){[IO.File]::Replace($temp,$target,[Management.Automation.Language.NullString]::Value)}else{[IO.File]::Move($temp,$target)}}finally{if([IO.File]::Exists($temp)){[IO.File]::Delete($temp)}}
}
function Install-VerifiedUpdate([string]$stagePath,[string]$targetRoot,[string]$backupPath,$files) {
    $root=Get-Item -LiteralPath $targetRoot
    if($root.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Không cập nhật vào thư mục liên kết.'}
    $changed=@();$completed=@();$null=[IO.Directory]::CreateDirectory($backupPath)
    foreach($file in $files){
        if(!(Test-UpdateFileName ([string]$file.name))){throw 'Tệp cập nhật nằm ngoài phạm vi chương trình.'}
        $target=Join-Path $targetRoot $file.name;$exists=Test-Path -LiteralPath $target
        if($exists){if((Get-Item -LiteralPath $target).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Tệp đích là liên kết.'};if((Get-LiveUpdateHash $target) -eq ([string]$file.sha256)){continue}}
        $changed+=@{name=[string]$file.name;exists=$exists}
    }
    foreach($file in $changed){if($file.exists){[IO.File]::Copy((Join-Path $targetRoot $file.name),(Join-Path $backupPath $file.name))}}
    try{
        foreach($file in $changed){$completed+=,$file;Write-UpdateFile (Join-Path $stagePath $file.name) (Join-Path $targetRoot $file.name)}
    }catch{
        $rollbackFailed=$false
        foreach($file in $completed){try{$target=Join-Path $targetRoot $file.name;if($file.exists){[IO.File]::Copy((Join-Path $backupPath $file.name),$target,$true)}elseif([IO.File]::Exists($target)){[IO.File]::Delete($target)}}catch{$rollbackFailed=$true}}
        if($rollbackFailed){throw ('Cập nhật lỗi; cần khôi phục từ thư mục sao lưu '+$backupPath)}
        throw 'Không ghi được bản cập nhật; đã khôi phục các tệp trước đó.'
    }
    return $changed.Count
}
function Finish-GitHubUpdate([string]$message) {
    if($script:githubUpdateWork){$script:githubUpdateWork.client.Dispose();$script:githubUpdateWork=$null}
    $script:githubUpdateTimer.Stop();$script:liveUpdateBusy=$false;$refreshButton.Enabled=$true;$refreshButton.Text='Cập nhật QLTK'
    $script:liveUpdateMessage=$message;$script:liveUpdateMessageUntil=[datetime]::UtcNow.AddSeconds(30);$status.Text=$message
}
function Start-GitHubUpdate([string]$repo) {
    Initialize-GitHubUpdate
    if($script:liveUpdateBusy){return}
    $repo=Normalize-UpdateRepository $repo;$client=New-UpdateWebClient
    try{
        $task=$client.DownloadStringTaskAsync([uri]('https://api.github.com/repos/'+$repo+'/releases/latest'))
        $script:githubUpdateWork=@{client=$client;task=$task;phase='release';repo=$repo;at=[datetime]::UtcNow}
        $script:liveUpdateBusy=$true;$refreshButton.Enabled=$false;$refreshButton.Text='Đang tải...';$script:githubUpdateTimer.Start()
        $status.Text='Đang kiểm tra bản phát hành GitHub; các acc vẫn tiếp tục chạy.'
    }catch{$client.Dispose();throw 'Không kết nối được GitHub.'}
}
function Pump-GitHubUpdate {
    $work=$script:githubUpdateWork;if(!$work){return}
    try{
        if(([datetime]::UtcNow-$work.at).TotalSeconds -gt 90){$work.client.CancelAsync();throw 'Tải cập nhật quá thời gian. Hãy thử lại.'}
        if(!$work.task.IsCompleted){return}
        if($work.task.IsFaulted -or $work.task.IsCanceled){throw 'Không tải được bản phát hành. Kiểm tra mạng và quyền truy cập repository công khai.'}
        if($work.phase -eq 'release'){
            $json=[string]$work.task.Result;if($json.Length -gt 1048576){throw 'Phản hồi GitHub quá lớn.'}
            $release=Read-GitHubRelease ($json | ConvertFrom-Json) $work.repo
            $installed=0;$versionPath=Join-Path $PSScriptRoot 'release-version.json'
            if(Test-Path -LiteralPath $versionPath){$installed=[int]((Get-Content -LiteralPath $versionPath -Raw -Encoding UTF8 | ConvertFrom-Json).version)}
            if($release.Version -le $installed){$script:githubUpdateWork.client.Dispose();$script:githubUpdateWork=$null;$script:githubUpdateTimer.Stop();$script:liveUpdateBusy=$false;Invoke-LocalLiveUpdate;return}
            $folder=Join-Path $PSScriptRoot ('.updates/'+[guid]::NewGuid().ToString('N'));$null=[IO.Directory]::CreateDirectory($folder)
            $work.folder=$folder;$work.release=$release;$work.zip=Join-Path $folder 'package.zip';$work.phase='package';$work.at=[datetime]::UtcNow
            $work.client.Headers.Remove('Accept');$work.task=$work.client.DownloadFileTaskAsync($release.Url,$work.zip);return
        }
        $stage=Join-Path $work.folder 'stage';$files=@(Expand-VerifiedUpdate $work.zip $stage $work.release)
        $catalog=Join-Path $PSScriptRoot 'MapCatalog.json';if(!$script:liveUpdateBinaries.ContainsKey($catalog)){$script:liveUpdateBinaries[$catalog]=Get-LiveUpdateHash $catalog}
        $count=Install-VerifiedUpdate $stage $PSScriptRoot (Join-Path $work.folder 'backup') $files
        $resume=$socketTimer -and $socketTimer.Enabled
        try{if($resume){$socketTimer.Stop()};$result=Apply-LiveUpdate}finally{if($resume){$socketTimer.Start()}}
        if($result.RestartRequired){Finish-GitHubUpdate ('Đã tải v'+$work.release.Version+'. Cần mở lại QLTK để áp dụng; các acc hiện tại vẫn chạy.')}
        else{Finish-GitHubUpdate ('Đã cập nhật v'+$work.release.Version+' ('+$count+' tệp). Các acc và hàng chờ tiếp tục chạy.')}
    }catch{Finish-GitHubUpdate ('Cập nhật GitHub chưa hoàn tất: '+$_.Exception.Message)}
}
function Invoke-LiveUpdate {
    if($UiTest){Invoke-LocalLiveUpdate;return}
    try{$repo=Get-UpdateRepository;if($repo){Start-GitHubUpdate $repo}else{Invoke-LocalLiveUpdate}}
    catch{$status.Text='Chưa cập nhật: '+$_.Exception.Message}
}
