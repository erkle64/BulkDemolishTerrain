
$startname = "UnfoundryStart"
$cpath = Get-Location
$modname = Split-Path -Path $cpath -leaf

$startauthor = "erkle64"
$author = Read-Host -Prompt 'Enter author name'

$title    = "Renaming from '$startname' to '$modname'"
$question = 'Are you sure you want to proceed?'
$choices  = '&Yes', '&No'
$decision = $Host.UI.PromptForChoice($title, $question, $choices, 1)
if ($decision -eq 0) {
	Write-Host "Renaming folders"
	Rename-Item -Path .\$startname -NewName $modname

	Write-Host "Renaming files"
	Rename-Item -Path .\$startname.sln -NewName "$modname.sln"
	Rename-Item -Path .\$modname\$startname.csproj -NewName "$modname.csproj"

	Write-Host "Search and replace"
	((Get-Content -path .\$modname.sln -Raw) -replace $startname,$modname) | Set-Content -Path .\$modname.sln
	((Get-Content -path .\.gitignore -Raw) -replace $startname,$modname) | Set-Content -Path .\.gitignore
	((Get-Content -path .\Foundry.props -Raw) -replace $startname,$modname) | Set-Content -Path .\Foundry.props
	((Get-Content -path .\$modname\Plugin.cs -Raw) -replace $startauthor,$author) | Set-Content -Path .\$modname\Plugin.cs
	((Get-Content -path .\$modname\Plugin.cs -Raw) -replace $startname,$modname) | Set-Content -Path .\$modname\Plugin.cs
	((Get-Content -path .\Mod\modInfo.json -Raw) -replace $startauthor,$author) | Set-Content -Path .\Mod\modInfo.json
	((Get-Content -path .\Mod\modInfo.json -Raw) -replace $startname,$modname) | Set-Content -Path .\Mod\modInfo.json
	((Get-Content -path .\$modname\$modname.csproj -Raw) -replace $startname,$modname) | Set-Content -Path .\$modname\$modname.csproj

	Write-Host "Done"

	Read-Host "Press enter key to exit"
}

Exit 0
