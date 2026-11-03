$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'sim-render-timeline.source.html'
$preview = Join-Path $PSScriptRoot 'sim-render-timeline.html'
$fragment = [System.IO.File]::ReadAllText($source).Trim()
$document = [System.IO.File]::ReadAllText($preview)
$pattern = [regex]::new('&lt;!-- architecture-source:start --&gt;[\s\S]*?&lt;!-- architecture-source:end --&gt;')
if ($pattern.Matches($document).Count -ne 1) {
    throw 'Expected exactly one architecture source block in the standalone preview.'
}
if (-not $fragment.StartsWith('<!-- architecture-source:start -->') -or
    -not $fragment.EndsWith('<!-- architecture-source:end -->')) {
    throw 'Keep the architecture source markers around the editable fragment.'
}
$encoded = [System.Net.WebUtility]::HtmlEncode($fragment)
$updated = $pattern.Replace($document, [System.Text.RegularExpressions.MatchEvaluator] {
    param($match)
    $encoded
}, 1)
[System.IO.File]::WriteAllText($preview, $updated, [System.Text.UTF8Encoding]::new($false))
Write-Output "Updated $preview"
