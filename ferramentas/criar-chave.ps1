# =====================================================================================================
#  CRIAR A CHAVE DE ASSINATURA DO CRICRI MODS  -  .\ferramentas\criar-chave.ps1
#
#  A chave assina o APK. So quem tem a chave consegue ATUALIZAR o app nos celulares: perdeu a chave,
#  o app instalado nunca mais recebe versao nova (na Play Store o Google guarda a chave do app e esta
#  vira a "chave de upload", que da pra trocar pelo suporte deles; fora da loja nao tem volta).
#
#  O keytool do JDK pergunta a senha e os dados: VOCE digita, o script nao ve a senha.
#  Depois: guarde uma copia do .jks e a senha em dois lugares (pendrive + nuvem).
#  NUNCA ponha o .jks no GitHub (o .gitignore do repositorio ja barra *.jks).
# =====================================================================================================
param(
  [string]$Arquivo = "$env:USERPROFILE\CriCri-Mods-chave\cricri-mods.jks",
  [string]$Apelido = "cricri-mods",
  [string]$Jdk = "$env:USERPROFILE\Ghidra\jdk-21.0.12.1+1"
)
$ErrorActionPreference = "Stop"

$keytool = Join-Path $Jdk "bin\keytool.exe"
if (-not (Test-Path -LiteralPath $keytool)) { throw "nao achei o keytool em $keytool (passe -Jdk <pasta do JDK>)" }
if (Test-Path -LiteralPath $Arquivo) {
  throw "ja existe uma chave em $Arquivo. Nao vou sobrescrever: trocar a chave trava as atualizacoes de quem ja instalou."
}
New-Item -ItemType Directory -Force (Split-Path $Arquivo) | Out-Null

Write-Host "Criando a chave em $Arquivo" -ForegroundColor Cyan
Write-Host "O keytool vai pedir a senha (2 vezes) e alguns dados (nome, cidade...). Os dados podem ficar em branco (Enter)." -ForegroundColor Yellow
& $keytool -genkeypair -v -keystore $Arquivo -alias $Apelido -keyalg RSA -keysize 4096 -validity 10000 -storetype PKCS12
if ($LASTEXITCODE -ne 0) { throw "o keytool falhou (codigo $LASTEXITCODE)" }

Write-Host ""
Write-Host "Chave criada: $Arquivo (apelido: $Apelido)" -ForegroundColor Green
Write-Host "AGORA: copie esse arquivo pra um pendrive E pra nuvem, e guarde a senha junto." -ForegroundColor Yellow
