$ErrorActionPreference = "Stop"

$repo = Split-Path -Parent $MyInvocation.MyCommand.Path
$bundledNode = Join-Path $env:USERPROFILE ".cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin"
$bundledPnpm = Join-Path $env:USERPROFILE ".cache\codex-runtimes\codex-primary-runtime\dependencies\bin\pnpm.cmd"

if (Test-Path $bundledNode) {
  $env:Path = "$bundledNode;$env:Path"
}

Set-Location $repo

if (-not (Test-Path ".\node_modules\electron\dist\electron.exe")) {
  if (Test-Path $bundledPnpm) {
    & $bundledPnpm install
  } else {
    pnpm install
  }
}

if (Test-Path ".\node_modules\electron\cli.js") {
  node ".\node_modules\electron\cli.js" .
} else {
  pnpm start
}
