<#
.SYNOPSIS
	Gera o pacote NuGet (dotnet tool) do sza. A versao fica no csproj do CLI (<Version>).

.EXAMPLE
	.\publish.ps1 -Bump patch -Install
	.\publish.ps1 -Bump minor
	.\publish.ps1 -Version 2.0.0-beta.1
	.\publish.ps1 -Bump patch -Source https://api.nuget.org/v3/index.json -ApiKey $env:NUGET_API_KEY
#>
[CmdletBinding()]
param(
	[ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
	[string]$Version,
	[ValidateSet('major', 'minor', 'patch')]
	[string]$Bump,
	[string]$Configuration = 'Release',
	[string]$Output = (Join-Path $PSScriptRoot 'nupkg'),
	[switch]$Install,
	[string]$Source,
	[string]$ApiKey,
	[switch]$NoChangelog
)

$ErrorActionPreference = 'Stop'

if ($Version -and $Bump) { throw 'Use -Version ou -Bump, nao os dois.' }

$project = Join-Path $PSScriptRoot 'SZA.AI.CLI\SZA.AI.CLI.csproj'
$changelogPath = Join-Path $PSScriptRoot 'SZA.AI.CLI\CHANGELOG.md'
$packageId = 'SZA.AI.CLI'
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Update-Changelog {
	param([string]$Path, [string]$NewVersion, [string]$PreviousVersion)

	if (-not (Test-Path $Path)) { Write-Warning 'CHANGELOG.md nao encontrado.'; return }

	$text = [System.IO.File]::ReadAllText($Path)
	$nl = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }

	if ($text -match ('(?m)^## \[' + [regex]::Escape($NewVersion) + '\]')) { return }

	$unreleased = [regex]::Match($text, '(?m)^## \[N\S+ lan\S+\]')
	if (-not $unreleased.Success) { Write-Warning 'Secao "Nao lancado" nao encontrada no CHANGELOG.md.'; return }

	$date = Get-Date -Format 'yyyy-MM-dd'
	$text = $text.Insert($unreleased.Index + $unreleased.Length, $nl + $nl + "## [$NewVersion] - $date")

	$placeholder = "## [$PreviousVersion] - AAAA-MM-DD"
	if ($text.Contains($placeholder)) { $text = $text.Replace($placeholder, "## [$PreviousVersion] - $date") }

	$link = [regex]::Match($text, '(?m)^(?<label>\[N\S+ lan\S+\]): (?<base>\S+)/compare/\S+\.\.\.HEAD[ \t]*\r?$')
	if ($link.Success) {
		$label = $link.Groups['label'].Value
		$base = $link.Groups['base'].Value
		$replacement = "${label}: $base/compare/v$NewVersion...HEAD" + $nl + "[$NewVersion]: $base/compare/v$PreviousVersion...v$NewVersion"
		$text = $text.Remove($link.Index, $link.Length).Insert($link.Index, $replacement)
	}

	[System.IO.File]::WriteAllText($Path, $text, $utf8NoBom)
	Write-Host "CHANGELOG.md atualizado para $NewVersion." -ForegroundColor Green
}

$csproj = [System.IO.File]::ReadAllText($project)
$match = [regex]::Match($csproj, '<Version>([^<]+)</Version>')
if (-not $match.Success) { throw "Tag <Version> nao encontrada em $project." }
$current = $match.Groups[1].Value

if ($Bump) {
	$parts = @(($current -replace '-.*$', '').Split('.') | ForEach-Object { [int]$_ })
	switch ($Bump) {
		'major' { $parts = @(($parts[0] + 1), 0, 0) }
		'minor' { $parts = @($parts[0], ($parts[1] + 1), 0) }
		'patch' { $parts = @($parts[0], $parts[1], ($parts[2] + 1)) }
	}
	$Version = $parts -join '.'
}

if (-not $Version) { $Version = $current }

if ($Version -ne $current) {
	$csproj = $csproj.Remove($match.Index, $match.Length).Insert($match.Index, "<Version>$Version</Version>")
	[System.IO.File]::WriteAllText($project, $csproj, $utf8NoBom)
	Write-Host "Versao: $current -> $Version (csproj atualizado)" -ForegroundColor Cyan
	if (-not $NoChangelog) { Update-Changelog -Path $changelogPath -NewVersion $Version -PreviousVersion $current }
}
else {
	Write-Host "Versao: $Version" -ForegroundColor Cyan
}

$bundleDir = Join-Path $PSScriptRoot 'SZA.AI.CLI\proxy-bundle'
if (Test-Path $bundleDir) { Remove-Item $bundleDir -Recurse -Force }
Write-Host 'Publicando o proxy para embutir no pacote...' -ForegroundColor Cyan
dotnet publish (Join-Path $PSScriptRoot 'SZA.AI.Proxy\SZA.AI.Proxy.csproj') --configuration $Configuration --output $bundleDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish do proxy falhou ($LASTEXITCODE)." }

# O pacote vai sem providers/modelos cadastrados; os templates (provider-templates.json) seguem no pacote.
foreach ($name in 'providers.json', 'models.json') {
	[System.IO.File]::WriteAllText((Join-Path $bundleDir "Data\$name"), '[]', $utf8NoBom)
}

Write-Host "Gerando $packageId $Version ($Configuration)..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $Output | Out-Null
Remove-Item (Join-Path $Output "$packageId.$Version.nupkg") -Force -ErrorAction SilentlyContinue

dotnet pack $project --configuration $Configuration --output $Output -p:Version=$Version -p:PackageVersion=$Version
if ($LASTEXITCODE -ne 0) { throw "dotnet pack falhou ($LASTEXITCODE)." }

$package = Join-Path $Output "$packageId.$Version.nupkg"
if (-not (Test-Path $package)) { throw "Pacote nao encontrado: $package" }
Write-Host "Pacote gerado: $package" -ForegroundColor Green

if ($Install) {
	Write-Host 'Instalando como ferramenta global...' -ForegroundColor Cyan
	$installed = dotnet tool list --global | Select-String -SimpleMatch $packageId
	if ($installed) {
		dotnet tool uninstall --global $packageId
		if ($LASTEXITCODE -ne 0) { throw "dotnet tool uninstall falhou ($LASTEXITCODE)." }
	}
	dotnet tool install --global $packageId --version $Version --add-source $Output
	if ($LASTEXITCODE -ne 0) { throw "dotnet tool install falhou ($LASTEXITCODE)." }
	Write-Host 'Instalado. Abra um novo terminal e execute: sza version' -ForegroundColor Green

}

if ($Source) {
	if (-not $ApiKey) { throw 'Informe -ApiKey para enviar o pacote ao feed.' }
	Write-Host "Enviando para $Source..." -ForegroundColor Cyan
	dotnet nuget push $package --source $Source --api-key $ApiKey --skip-duplicate
	if ($LASTEXITCODE -ne 0) { throw "dotnet nuget push falhou ($LASTEXITCODE)." }
	Write-Host 'Pacote enviado.' -ForegroundColor Green
}
