# Regenerates the result tables in docs/performance.md and docs/performance/*.md from the result files in
# docs/performance/results/. Adding a machine or platform means dropping its result files in that folder
# and running this script; no table is edited by hand.
#
#   pwsh eng/update-performance-docs.ps1
#
# Result files are the perf-results-<app>.md files the benchmark apps write next to their exe, renamed to
# <hardware-id>__<anything>.md (for example H2__monogame-desktopgl-raw.md). The hardware id comes from the
# file name; everything else comes from the file. A page is regenerated between its
# <!-- results:begin --> and <!-- results:end --> markers, the hardware table between
# <!-- hardware:begin --> and <!-- hardware:end -->.

param(
    [string] $Root = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$resultsDir = Join-Path $Root 'docs/performance/results'
$pagesDir = Join-Path $Root 'docs/performance'
$indexFile = Join-Path $Root 'docs/performance.md'

# Platforms are listed in this order; any other platform follows alphabetically.
$platformOrder = @('MonoGame DesktopGL', 'MonoGame WindowsDX', 'KNI DesktopGL', 'KNI WebGL (Chromium)', 'KNI WebGL (Firefox)')

function Read-Run([string] $path) {
    $lines = Get-Content -LiteralPath $path
    $label = ($lines | Where-Object { $_ -like '# *' } | Select-Object -First 1).Substring(2).Trim()
    $meta = @{}
    foreach ($line in $lines) {
        if ($line -match '^- (?<k>[A-Za-z ]+): (?<v>.*)$') { $meta[$Matches.k] = $Matches.v.Trim() }
    }
    $rows = foreach ($line in $lines) {
        if ($line -notmatch '^\|') { continue }
        $cells = $line.Trim().Trim('|').Split('|') | ForEach-Object { $_.Trim() }
        if ($cells.Count -lt 10 -or $cells[0] -eq 'Page' -or $cells[0] -like '---*') { continue }
        [pscustomobject]@{
            Page = $cells[0]; Scene = $cells[1]; Count = $cells[2]; Kind = $cells[3]; Renderer = $cells[4]
            Fps = $cells[5]; MedianMs = $cells[6]; P95Ms = $cells[7]; DrawMs = $cells[8]; Note = $cells[9]
        }
    }
    $hardware = (Split-Path -Leaf $path).Split('__')[0]
    # "MonoGame DesktopGL Raw" -> platform "MonoGame DesktopGL", app "Raw"
    $app = $label.Split(' ')[-1]
    [pscustomobject]@{
        File = $path; Hardware = $hardware; Platform = $label.Substring(0, $label.Length - $app.Length).Trim()
        App = $app; Meta = $meta; Rows = @($rows)
    }
}

# Average FPS over the measured window, so frequent stalls count against the side that has them.
function Row-Fps($row) {
    $fps = 0.0
    if ($row -and [double]::TryParse($row.Fps, [ref] $fps) -and $fps -gt 0) { return $fps }
    return $null
}

# Each scene compares the fastest native renderer with Skia's direct canvas. Pre-rasterized SVG is left out
# on both sides: it measures a bitmap, not vector rendering.
function Build-PageTables([object[]] $runs, [string] $page) {
    $out = New-Object System.Text.StringBuilder
    $platforms = $runs | Select-Object -ExpandProperty Platform -Unique |
        Sort-Object { $i = $platformOrder.IndexOf($_); if ($i -lt 0) { 100 } else { $i } }, { $_ }
    $any = $false
    foreach ($platform in $platforms) {
        $platformRuns = @($runs | Where-Object { $_.Platform -eq $platform -and ($_.Rows | Where-Object Page -eq $page) })
        foreach ($hardware in ($platformRuns | Select-Object -ExpandProperty Hardware -Unique | Sort-Object)) {
            $rows = @($platformRuns | Where-Object Hardware -eq $hardware | ForEach-Object { $_.Rows } |
                Where-Object { $_.Page -eq $page -and $_.Renderer -notmatch 'pre-rasterized' })
            $lines = foreach ($scene in @($rows | Select-Object -ExpandProperty Scene -Unique)) {
                $skia = Row-Fps ($rows | Where-Object { $_.Scene -eq $scene -and $_.Renderer -eq 'Skia direct' } | Select-Object -First 1)
                $native = $rows | Where-Object { $_.Scene -eq $scene -and $_.Kind -eq 'engine' -and (Row-Fps $_) } |
                    Sort-Object { Row-Fps $_ } -Descending | Select-Object -First 1
                if (-not $skia -or -not $native) { continue }
                $nativeFps = Row-Fps $native
                $change = 100.0 * ($skia / $nativeFps - 1)
                '| {0} | {1:0} ({2}) | {3:0} | {4}{5:0}% |' -f $scene, $nativeFps, $native.Renderer, $skia, $(if ($change -ge 0) { '+' } else { '' }), $change
            }
            if (-not $lines) { continue }
            $any = $true
            [void] $out.AppendLine("### $platform, $hardware")
            [void] $out.AppendLine()
            [void] $out.AppendLine('| Scene | Native FPS | SkiaGameRendering FPS | Change |')
            [void] $out.AppendLine('|---|---:|---:|---:|')
            foreach ($line in $lines) { [void] $out.AppendLine($line) }
            [void] $out.AppendLine()
        }
    }
    if (-not $any) {
        [void] $out.AppendLine('No results yet.')
        [void] $out.AppendLine()
    }
    return $out.ToString().TrimEnd() + "`n"
}

function Build-HardwareTable([object[]] $runs) {
    $out = New-Object System.Text.StringBuilder
    [void] $out.AppendLine('| ID | CPU | GPU | OS | Power | Platforms run |')
    [void] $out.AppendLine('|---|---|---|---|---|---|')
    foreach ($hardware in ($runs | Select-Object -ExpandProperty Hardware -Unique | Sort-Object)) {
        $hardwareRuns = @($runs | Where-Object Hardware -eq $hardware)
        $first = $hardwareRuns[0].Meta
        foreach ($key in 'CPU', 'OS', 'Power') {
            $distinct = $hardwareRuns | ForEach-Object { $_.Meta[$key] } | Select-Object -Unique
            if (@($distinct).Count -gt 1) {
                Write-Warning "Hardware $hardware has more than one $key across its result files: $($distinct -join ' / ')"
            }
        }
        $platforms = ($hardwareRuns | Select-Object -ExpandProperty Platform -Unique |
            Sort-Object { $i = $platformOrder.IndexOf($_); if ($i -lt 0) { 100 } else { $i } }) -join ', '
        # A browser names the GPU through ANGLE and a desktop app through the driver, so one machine can list two spellings.
        $gpus = ($hardwareRuns | ForEach-Object { $_.Meta['GPU'] } | Select-Object -Unique) -join '; '
        [void] $out.AppendLine("| $hardware | $($first['CPU']) | $gpus | $($first['OS']) | $($first['Power']) | $platforms |")
    }
    return $out.ToString().TrimEnd() + "`n"
}

function Update-Between([string] $file, [string] $begin, [string] $end, [string] $content) {
    $text = Get-Content -LiteralPath $file -Raw
    $pattern = '(?s)' + [regex]::Escape($begin) + '.*?' + [regex]::Escape($end)
    if ($text -notmatch $pattern) { throw "$file has no $begin ... $end block." }
    $replacement = $begin + "`n`n" + $content + "`n" + $end
    $updated = [regex]::Replace($text, $pattern, { param($m) $replacement })
    if ($updated -ne $text) { Set-Content -LiteralPath $file -Value $updated -NoNewline }
}

# Several files for one hardware, platform and app (a long run split with --page) are one run.
$runs = @(Get-ChildItem -LiteralPath $resultsDir -Filter '*.md' |
    Where-Object { $_.Name -ne 'README.md' } |
    ForEach-Object { Read-Run $_.FullName } |
    Group-Object { "$($_.Hardware)|$($_.Platform)|$($_.App)" } |
    ForEach-Object {
        $first = $_.Group[0]
        $first.Rows = @($_.Group | ForEach-Object { $_.Rows })
        $first
    })

Update-Between $indexFile '<!-- hardware:begin -->' '<!-- hardware:end -->' (Build-HardwareTable $runs)

foreach ($pageFile in Get-ChildItem -LiteralPath $pagesDir -Filter '*.md') {
    $text = Get-Content -LiteralPath $pageFile.FullName -Raw
    if ($text -notmatch '<!-- results:begin -->') { continue }
    $page = [IO.Path]::GetFileNameWithoutExtension($pageFile.Name)
    Update-Between $pageFile.FullName '<!-- results:begin -->' '<!-- results:end -->' (Build-PageTables $runs $page)
}

Write-Host "Updated from $($runs.Count) result file(s)."
