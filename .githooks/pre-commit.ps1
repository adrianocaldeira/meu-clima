# Pre-commit hook (PowerShell) — Meu Clima
# Variante para devs no Windows sem bash. Não é chamado automaticamente pelo git;
# use o pre-commit (bash) que o git encontra via core.hooksPath.
# Este arquivo serve para execução manual: powershell .githooks/pre-commit.ps1

$ErrorActionPreference = 'Stop'

if (-not (Get-Command gitleaks -ErrorAction SilentlyContinue)) {
    Write-Host "[gitleaks] binário não encontrado no PATH — pulando varredura." -ForegroundColor Yellow
    Write-Host "[gitleaks] Instale: winget install gitleaks  ou  https://github.com/gitleaks/gitleaks"
    exit 0
}

Write-Host "[gitleaks] varrendo arquivos staged..."
& gitleaks protect --staged --verbose --redact --config .gitleaks.toml
