$savedTabIndex=$tabs.SelectedIndex
$null=$tabs.Handle
$fixedTabPositions=@(for($i=0;$i -lt $tabs.TabPages.Count;$i++){$tabs.GetTabRect($i)})
for($selected=0;$selected -lt $tabs.TabPages.Count;$selected++){
    $tabs.SelectedIndex=$selected
    for($i=0;$i -lt $tabs.TabPages.Count;$i++){
        if($tabs.GetTabRect($i) -ne $fixedTabPositions[$i]){throw "Tab moved when selecting index $selected (tab $i)"}
    }
}
$tabs.SelectedIndex=$savedTabIndex
Write-Output 'PASS: all 20 tabs keep their original position when selecting upper or lower rows.'
