# =====================================================================================================
#  TESTAR O CRICRI MODS NO CELULAR PELO CABO  -  .\ferramentas\testar-no-celular.ps1
#
#  Precisa: celular com "Depuracao USB" ligada, no cabo, com "Permitir depuracao USB" aceito na tela dele.
#  Instala o APK (por cima, sem apagar os dados), abre o app e grava o log do celular num arquivo enquanto
#  voce testa. Ctrl+C para de gravar. Depois busca tambem o LogOutput.log do BepInEx.
#  Pelo cabo a trava de "desenvolvedor nao verificado" (Brasil, desde 30/09/2026) nao vale.
# =====================================================================================================
param(
  [string]$Apk = "",
  [string]$Sdk = "$env:USERPROFILE\Android\Sdk",
  [string]$Saida = "$env:USERPROFILE\Downloads\CriCri-Mods-logs"
)
$ErrorActionPreference = "Stop"
$raiz = Split-Path -Parent $PSScriptRoot
$pacote = "br.com.cricribr.mods"
$adb = Join-Path $Sdk "platform-tools\adb.exe"
if (-not (Test-Path -LiteralPath $adb)) { throw "nao achei o adb em $adb" }
if (-not $Apk) { $Apk = Join-Path $raiz "fusionApp\build\outputs\apk\debug\fusionApp-debug.apk" }
if (-not (Test-Path -LiteralPath $Apk)) { throw "nao achei o APK $Apk" }

$aparelhos = @(& $adb devices | Select-String "\tdevice$")
if ($aparelhos.Count -ne 1) { throw "precisa de exatamente 1 celular ligado e autorizado (achei $($aparelhos.Count)). Veja a tela do celular." }

New-Item -ItemType Directory -Force $Saida | Out-Null
$hora = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
$log = Join-Path $Saida "logcat-$hora.txt"

Write-Host "Instalando $Apk ..." -ForegroundColor Cyan
& $adb install -r $Apk
if ($LASTEXITCODE -ne 0) { throw "o adb install falhou" }

& $adb logcat -c
& $adb shell monkey -p $pacote -c android.intent.category.LAUNCHER 1 | Out-Null
Write-Host "App aberto. Gravando o log em $log (Ctrl+C para parar)." -ForegroundColor Green
try {
  & $adb logcat -v time | Tee-Object -FilePath $log | Select-String -Pattern "FusionCore|CriCriMods|CrashDetector|BepInEx|Unity|AndroidRuntime|DEBUG|libc" | ForEach-Object { $_.Line }
} finally {
  $remoto = "/sdcard/Android/data/$pacote/files/com.innersloth.spacemafia/BepInEx/LogOutput.log"
  & $adb pull $remoto (Join-Path $Saida "LogOutput-$hora.log") 2>$null | Out-Null
  Write-Host "Logs em $Saida" -ForegroundColor Green
}
