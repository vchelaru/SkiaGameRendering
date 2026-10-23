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

function Format-Fps([string] $fps) {
    if ($fps -eq 'n/a') { return 'n/a' }
    return $fps
}

function Build-PageTables([object[]] $runs, [string] $page) {
    $out = New-Object System.Text.StringBuilder
    $platforms = $runs | Select-Object -ExpandProperty Platform -Unique |
        Sort-Object { $i = $platformOrder.IndexOf($_); if ($i -lt 0) { 100 } else { $i } }, { $_ }
    $any = $false
    foreach ($platform in $platforms) {
        $platformRuns = @($runs | Where-Object { $_.Platform -eq $platform -and ($_.Rows | Where-Object Page -eq $page) })
        if (-not $platformRuns) { continue }
        $any = $true
        [void] $out.AppendLine("### $platform")
        [void] $out.AppendLine()

        foreach ($hardware in ($platformRuns | Select-Object -ExpandProperty Hardware -Unique | Sort-Object)) {
            $hardwareRuns = @($platformRuns | Where-Object Hardware -eq $hardware)
            $rows = @($hardwareRuns | ForEach-Object { $_.Rows } | Where-Object Page -eq $page)

            $stamp = ($hardwareRuns | ForEach-Object { "$($_.App) $($_.Meta['Date']) at $($_.Meta['Commit'])" }) -join '; '
            [void] $out.AppendLine("**$hardware**: $stamp")
            [void] $out.AppendLine()

            $engine = @($rows | Where-Object Kind -eq 'engine' | Select-Object -ExpandProperty Renderer -Unique)
            $skia = @($rows | Where-Object Kind -eq 'skia' | Select-Object -ExpandProperty Renderer -Unique)
            $columns = @($engine) + @($skia)
            $scenes = @($rows | Select-Object -ExpandProperty Scene -Unique)

            $header = '| Scene | ' + (($columns | ForEach-Object { "$_ FPS" }) -join ' | ')
            $rule = '|---|' + (($columns | ForEach-Object { '---:' }) -join '|') + '|'
            $ratioColumns = @($skia | Where-Object { $_ -notmatch '\(' })
            if ($engine.Count -gt 0) {
                $header += ' | ' + (($ratioColumns | ForEach-Object { "$_ / $($engine[0])" }) -join ' | ')
                $rule += (($ratioColumns | ForEach-Object { '---:' }) -join '|') + '|'
            }
            [void] $out.AppendLine($header + ' |')
            [void] $out.AppendLine($rule)

            foreach ($scene in $scenes) {
                $cells = foreach ($column in $columns) {
                    $row = $rows | Where-Object { $_.Scene -eq $scene -and $_.Renderer -eq $column } | Select-Object -First 1
                    if ($row) { Format-Fps $row.Fps } else { '' }
                }
                $line = "| $scene | " + ($cells -join ' | ')
                if ($engine.Count -gt 0) {
                    $base = $rows | Where-Object { $_.Scene -eq $scene -and $_.Renderer -eq $engine[0] } | Select-Object -First 1
                    $ratios = foreach ($column in $ratioColumns) {
                        $row = $rows | Where-Object { $_.Scene -eq $scene -and $_.Renderer -eq $column } | Select-Object -First 1
                        if ($base -and $row -and $base.Fps -match '^\d+$' -and $row.Fps -match '^\d+$' -and [double] $base.Fps -gt 0) {
                            '{0:0}%' -f (100.0 * [double] $row.Fps / [double] $base.Fps)
                        } else { '' }
                    }
                    $line += ' | ' + ($ratios -join ' | ')
                }
                [void] $out.AppendLine($line + ' |')
            }
            [void] $out.AppendLine()

            $notes = @($rows | Where-Object { $_.Note } | ForEach-Object { "- $($_.Scene), $($_.Renderer): $($_.Note)" } | Select-Object -Unique)
            if ($notes) {
                [void] $out.AppendLine('Not supported here:')
                [void] $out.AppendLine()
                foreach ($note in $notes) { [void] $out.AppendLine($note) }
                [void] $out.AppendLine()
            }

            if (-not ($rows | Where-Object DrawMs)) { continue }

            [void] $out.AppendLine('<details><summary>Median draw CPU time per frame (ms)</summary>')
            [void] $out.AppendLine()
            [void] $out.AppendLine('| Scene | ' + (($columns | ForEach-Object { $_ }) -join ' | ') + ' |')
            [void] $out.AppendLine('|---|' + (($columns | ForEach-Object { '---:' }) -join '|') + '|')
            foreach ($scene in $scenes) {
                $cells = foreach ($column in $columns) {
                    $row = $rows | Where-Object { $_.Scene -eq $scene -and $_.Renderer -eq $column } | Select-Object -First 1
                    if ($row -and $row.DrawMs) { $row.DrawMs } else { '' }
                }
                [void] $out.AppendLine("| $scene | " + ($cells -join ' | ') + ' |')
            }
            [void] $out.AppendLine()
            [void] $out.AppendLine('</details>')
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
        foreach ($key in 'CPU', 'GPU', 'OS', 'Power') {
            $distinct = $hardwareRuns | ForEach-Object { $_.Meta[$key] } | Select-Object -Unique
            if (@($distinct).Count -gt 1) {
                Write-Warning "Hardware $hardware has more than one $key across its result files: $($distinct -join ' / ')"
            }
        }
        $platforms = ($hardwareRuns | Select-Object -ExpandProperty Platform -Unique |
            Sort-Object { $i = $platformOrder.IndexOf($_); if ($i -lt 0) { 100 } else { $i } }) -join ', '
        [void] $out.AppendLine("| $hardware | $($first['CPU']) | $($first['GPU']) | $($first['OS']) | $($first['Power']) | $platforms |")
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

$runs = @(Get-ChildItem -LiteralPath $resultsDir -Filter '*.md' |
    Where-Object { $_.Name -ne 'README.md' } |
    ForEach-Object { Read-Run $_.FullName })

Update-Between $indexFile '<!-- hardware:begin -->' '<!-- hardware:end -->' (Build-HardwareTable $runs)

foreach ($pageFile in Get-ChildItem -LiteralPath $pagesDir -Filter '*.md') {
    $text = Get-Content -LiteralPath $pageFile.FullName -Raw
    if ($text -notmatch '<!-- results:begin -->') { continue }
    $page = [IO.Path]::GetFileNameWithoutExtension($pageFile.Name)
    Update-Between $pageFile.FullName '<!-- results:begin -->' '<!-- results:end -->' (Build-PageTables $runs $page)
}

Write-Host "Updated from $($runs.Count) result file(s)."
