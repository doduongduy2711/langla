$script:privacyPath=Join-Path $script:dataDir 'privacy.json'
$script:accountPrivacy=New-Object TGLL.AccountPrivacy($grid)
$hideAccountInfo=New-Object Windows.Forms.CheckBox
$hideAccountInfo.Text='Ẩn thông tin';$hideAccountInfo.SetBounds(154,105,120,22)
$options.Controls.Add($hideAccountInfo)
if(!$UiTest -and (Test-Path -LiteralPath $script:privacyPath)){
    try{$hideAccountInfo.Checked=[bool](Read-JsonData $script:privacyPath).hideAccountInfo}catch{$status.Text='Không đọc được tùy chọn ẩn thông tin.'}
}
$script:accountPrivacy.Enabled=$hideAccountInfo.Checked
$hideAccountInfo.Add_CheckedChanged({
    try{
        if(!$UiTest){Write-JsonAtomic $script:privacyPath @{hideAccountInfo=[bool]$hideAccountInfo.Checked}}
        $script:accountPrivacy.Enabled=$hideAccountInfo.Checked
        Update-UiSummary
    }catch{Show-Error ('Không lưu được tùy chọn ẩn thông tin: '+$_.Exception.Message)}
})
Update-UiSummary
