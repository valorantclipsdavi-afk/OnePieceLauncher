# Script de Publicação Automática do Launcher - One Piece
# Requer: .NET SDK (dotnet CLI) e GitHub CLI (gh) autenticado.

$ErrorActionPreference = "Stop"

# Limpar token de ambiente temporário para evitar conflitos de credenciais
$env:GITHUB_TOKEN = $null

# Fechar instâncias do Launcher ativas para liberar arquivos e evitar erros de permissão
Write-Host ">>> Fechando instâncias ativas do Launcher para liberar arquivos..." -ForegroundColor Cyan
Get-Process -Name "OnePieceLauncher" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

# 1. Obter e opcionalmente atualizar a versão
Write-Host ">>> Verificando versão atual do Launcher..." -ForegroundColor Cyan
if (Test-Path "Form1.cs") {
    $formContent = Get-Content "Form1.cs" -Raw
    if ($formContent -match 'CurrentLauncherVersion = new Version\("([^"]+)"\)') {
        $currentVersion = $Matches[1]
        Write-Host "Versão atual detectada no Form1.cs: v$currentVersion" -ForegroundColor Green
        
        $newVersion = Read-Host "Digite a nova versão (ou aperte Enter para manter v$currentVersion)"
        if (-not [string]::IsNullOrWhiteSpace($newVersion)) {
            if ($newVersion.StartsWith("v", [System.StringComparison]::OrdinalIgnoreCase)) {
                $newVersion = $newVersion.Substring(1)
            }
            # Atualizar Form1.cs com a nova versão
            $formContent = $formContent -replace 'CurrentLauncherVersion = new Version\("[^"]+"\)', "CurrentLauncherVersion = new Version(`"$newVersion`")"
            Set-Content "Form1.cs" -Value $formContent -NoNewline
            $version = $newVersion
            Write-Host "Versão atualizada no Form1.cs para: v$version" -ForegroundColor Green
        } else {
            $version = $currentVersion
        }
    } else {
        $version = Read-Host "Versão não detectada no Form1.cs. Digite a versão desejada (ex: 1.0.0)"
    }
} else {
    $version = Read-Host "Arquivo Form1.cs não encontrado. Digite a versão desejada (ex: 1.0.0)"
}

$zipName = "Launcher-v$version.zip"
$zipPath = Join-Path (Get-Location) $zipName
$publishFolder = Join-Path (Get-Location) "Publish"

# 2. Inicialização e sincronização do Git (garante que há commits antes de fazer a release)
Write-Host "`n>>> Sincronizando repositório Git local..." -ForegroundColor Cyan
if (-not (Test-Path ".git")) {
    Write-Host "Inicializando repositório Git local..." -ForegroundColor Yellow
    git init
    git branch -M main
    git remote add origin "https://github.com/valorantclipsdavi-afk/OnePieceLauncher.git"
}

# Fazer commit de arquivos locais se houver mudanças
$gitStatus = git status --porcelain
if (-not [string]::IsNullOrEmpty($gitStatus)) {
    Write-Host "Registrando e comitando alterações locais..." -ForegroundColor Yellow
    git add .
    git commit -m "Atualizando Launcher para v$version"
}

# Realizar o push para garantir que o GitHub possui commits
Write-Host "Enviando commits para a branch 'main' no GitHub..." -ForegroundColor Yellow
git push -u origin main
if ($LASTEXITCODE -ne 0) {
    Write-Host "Tentando realizar push forçado (force push) devido a possíveis conflitos..." -ForegroundColor Yellow
    git push -u origin main --force
}

# 3. Compilar o projeto dotnet como Arquivo Único (Single File)
Write-Host "`n>>> Compilando Launcher para Windows (Release/win-x64 como Single File)..." -ForegroundColor Cyan
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o $publishFolder

# 4. Compactar a pasta Publish em um arquivo ZIP
Write-Host "`n>>> Compactando arquivos compilados em $zipName..." -ForegroundColor Cyan
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

# Utiliza compressão nativa do PowerShell (Compress-Archive)
Compress-Archive -Path "$publishFolder\*" -DestinationPath $zipPath -Force
Write-Host "Compactação concluída com sucesso." -ForegroundColor Green

# 5. Solicitar notas de lançamento
Write-Host "`n>>> Notas de Lançamento (Release Notes):" -ForegroundColor Cyan
$releaseNotes = Read-Host "Digite o que mudou nesta versão (pressione Enter para concluir)"
if ([string]::IsNullOrWhiteSpace($releaseNotes)) {
    $releaseNotes = "Lançamento da versão v$version"
}

# 6. Fazer upload para o GitHub usando a ferramenta 'gh'
Write-Host "`n>>> Criando release v$version no GitHub e fazendo upload de $zipName..." -ForegroundColor Cyan

# Executa o comando gh e captura o código de retorno
& gh release create "v$version" $zipPath --repo "valorantclipsdavi-afk/OnePieceLauncher" -t "v$version" -n "$releaseNotes"
if ($LASTEXITCODE -ne 0) {
    Write-Host "`n[ERRO] Ocorreu um erro ao criar a release no GitHub." -ForegroundColor Red
    Write-Host "Possíveis causas:" -ForegroundColor Yellow
    Write-Host "1. A tag 'v$version' já existe no repositório." -ForegroundColor Yellow
    Write-Host "2. O repositório 'valorantclipsdavi-afk/OnePieceLauncher' está com problemas." -ForegroundColor Yellow
} else {
    Write-Host "`n>>> Sucesso! A versão v$version do Launcher foi publicada no repositório com sucesso!" -ForegroundColor Green
}

# 7. Limpar arquivos temporários
Write-Host "`n>>> Limpando arquivos temporários..." -ForegroundColor Cyan
if (Test-Path $publishFolder) {
    Remove-Item $publishFolder -Recurse -Force
}
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}
Write-Host "Limpeza concluída." -ForegroundColor Green
