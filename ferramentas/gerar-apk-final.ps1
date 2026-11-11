# =====================================================================================================
#  GERAR O CRICRI MODS PRA DISTRIBUIR  -  .\ferramentas\gerar-apk-final.ps1 -Codigo <endereco do codigo>
#
#  Gera, assinado com a SUA chave (ferramentas\criar-chave.ps1), o CriCri-Mods-<versao>.apk do site e poe em
#  Downloads\CriCri-Mods-<versao>\ com o sha256.txt e a impressao digital SHA-256 do certificado (a que vai no
#  registro do app na verificacao de desenvolvedor do Google). -ComAab gera tambem o .aab da Play Store.
#
#  Decisao do CriCri (08/11/2026): so o APK no site, sem Play Store. Por isso o build final PODE baixar na 1a
#  abertura a libunity desestripada e as bibliotecas da Unity (o CriCri Replay precisa; a regra do Google contra
#  baixar codigo e da Play Store). Mods novos chegam pelo catalogo assinado do site (ferramentas\publicar-mods.js).
#
#  Travas (a licenca GPL-3.0 do Fusion Core exige o codigo publicado de CADA versao distribuida):
#    - -Codigo tem que ser o endereco https do codigo publicado desta versao (vai na tela "Sobre" do app);
#    - o repositorio nao pode ter mudanca sem commit (senao o codigo publicado nao e o deste APK).
#  A senha da chave e pedida aqui e so vive durante o build (nao fica em arquivo nenhum).
# =====================================================================================================
param(
  [Parameter(Mandatory = $true)][string]$Codigo,
  [string]$Chave = "$env:USERPROFILE\CriCri-Mods-chave\cricri-mods.jks",
  [string]$Apelido = "cricri-mods",
  [string]$Jdk = "$env:USERPROFILE\Ghidra\jdk-21.0.12.1+1",
  [string]$Sdk = "$env:USERPROFILE\Android\Sdk",
  [string]$Saida = "$env:USERPROFILE\Downloads",
  [switch]$ComAab
)
$ErrorActionPreference = "Stop"
$raiz = Split-Path -Parent $PSScriptRoot

if ($Codigo -notmatch '^https://') {
  throw "-Codigo tem que ser o endereco https do codigo publicado (ex.: https://github.com/<voce>/CriCri-Mods/tree/v0.1.0)"
}
if (-not (Test-Path -LiteralPath $Chave)) { throw "nao achei a chave $Chave (rode ferramentas\criar-chave.ps1 antes)" }
$sujo = & git -C $raiz status --porcelain
if ($sujo) { throw "o repositorio tem mudancas sem commit: o codigo publicado nao seria o deste APK. Faca o commit e o push antes." }
$marca = Join-Path $raiz "fusionApp\build\mods-de-teste.txt"
if ((Test-Path -LiteralPath $marca) -and ((Get-Content -LiteralPath $marca -TotalCount 1).Trim() -ne "oficial")) {
  throw "os mods do app sao de teste: rode 'node ferramentas/mods-de-teste.js oficial' ou 'volta' antes de gerar o APK final."
}

$segura = Read-Host "Senha da chave" -AsSecureString
$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($segura)
$senha = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)

$env:JAVA_HOME = $Jdk
$env:ANDROID_HOME = $Sdk
$env:KEYSTORE_PATH = $Chave
$env:KEY_ALIAS = $Apelido
$env:KEYSTORE_PASSWORD = $senha
$env:KEY_PASSWORD = $senha
Push-Location $raiz
try {
  $tarefas = @(":fusionApp:assembleRelease")
  if ($ComAab) { $tarefas += ":fusionApp:bundleRelease" }
  & .\gradlew.bat @tarefas "-PcricriSourceUrl=$Codigo" "-PcricriPermitirDownloads=true" --console=plain
  if ($LASTEXITCODE -ne 0) { throw "o build falhou (codigo $LASTEXITCODE)" }
} finally {
  Remove-Item Env:KEYSTORE_PASSWORD, Env:KEY_PASSWORD -ErrorAction SilentlyContinue
  $senha = $null
  & .\gradlew.bat --stop | Out-Null
  Pop-Location
}

$gradle = [IO.File]::ReadAllText((Join-Path $raiz "fusionApp\build.gradle.kts"))
$versao = [regex]::Match($gradle, 'versionName\s*=\s*"([^"]+)"').Groups[1].Value
$destino = Join-Path $Saida "CriCri-Mods-$versao"
New-Item -ItemType Directory -Force $destino | Out-Null

$apk = Get-ChildItem (Join-Path $raiz "fusionApp\build\outputs\apk\release\*.apk") | Select-Object -First 1
if (-not $apk) { throw "o build terminou mas nao achei o .apk de release" }
Copy-Item $apk.FullName (Join-Path $destino "CriCri-Mods-$versao.apk") -Force
if ($ComAab) {
  $aab = Get-ChildItem (Join-Path $raiz "fusionApp\build\outputs\bundle\release\*.aab") | Select-Object -First 1
  if (-not $aab) { throw "nao achei o .aab de release" }
  Copy-Item $aab.FullName (Join-Path $destino "CriCri-Mods-$versao.aab") -Force
}

$linhas = @(Get-ChildItem $destino -File -Include *.apk, *.aab -Recurse | ForEach-Object {
  (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower() + "  " + $_.Name
})
$linhas += "codigo: $Codigo"
# the certificate fingerprint asked when registering the app with Google (read from the signed APK: no password)
$apksigner = Get-ChildItem (Join-Path $Sdk "build-tools\*\apksigner.bat") | Sort-Object FullName -Descending | Select-Object -First 1
if ($apksigner) {
  $certs = & $apksigner.FullName verify --print-certs (Join-Path $destino "CriCri-Mods-$versao.apk") 2>$null
  $digital = ($certs | Select-String "SHA-256 digest:").ToString() -replace '.*SHA-256 digest:\s*', ''
  if ($digital) {
    $linhas += "certificado SHA-256 (registro no Google): $digital"
    Write-Host "Impressao digital SHA-256 do certificado (cole no registro do app no Google): $digital" -ForegroundColor Cyan
  }
}
[IO.File]::WriteAllLines((Join-Path $destino "sha256.txt"), $linhas)
Write-Host "Pronto: $destino" -ForegroundColor Green
