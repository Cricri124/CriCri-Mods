# Builds the public CRICRI BR package for PC into build\pc-pacote. Reads the game folder, never writes to it. Publishes nothing.
#   powershell -NoProfile -ExecutionPolicy Bypass -File embutidos\CriCriServidor\src\PcPacote\PcMontarPacote.ps1 [-Final]
# Without -Final the zip carries NAO-TESTADO in its name. Use -Final only after a test in the real game.
# ASCII only: Windows PowerShell 5.1 reads a file without BOM as ANSI.
#
# The loader comes from the game folder of this PC (BepInEx 6.0.0-be.735 x64), NOT from the EHR zip the Replay package uses:
# that zip is x86, and the Steam game has been x64 since the 2026.9.29 update.
param([switch]$Final)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression

$aqui = $PSScriptRoot
$projeto = [IO.Path]::GetFullPath((Join-Path $aqui "..\.."))
$repo = [IO.Path]::GetFullPath((Join-Path $projeto "..\.."))
$jogo = "C:\Program Files (x86)\Steam\steamapps\common\Among Us"
$ehrZip = "$env:USERPROFILE\Downloads\EHR.v7.8.0_Steam.zip"
$conferidor = "$env:USERPROFILE\Downloads\cricriqol\android\confere-assinaturas\bin\Release\net8.0\ConfereAssinaturas.dll"
$baseBuild = Join-Path $repo "build\pc-servidor"
$raizPacote = Join-Path $repo "build\pc-pacote"
$utf8Bom = New-Object Text.UTF8Encoding($true)
$utf8 = New-Object Text.UTF8Encoding($false)

function Passo($t) { Write-Host ""; Write-Host ("== " + $t) -ForegroundColor Cyan }
function Parar($t) { Write-Host ("ERRO: " + $t) -ForegroundColor Red; exit 1 }

function ArquiteturaDe([string]$arq) {
  $fs = [IO.File]::OpenRead($arq)
  try {
    $br = New-Object IO.BinaryReader($fs)
    $fs.Position = 60
    $fs.Position = $br.ReadInt32() + 4
    $m = $br.ReadUInt16()
    if ($m -eq 0x8664) { return "x64" }
    if ($m -eq 0x014c) { return "x86" }
    return "?"
  } finally { $fs.Dispose() }
}

# ---------------------------------------------------------------- 1. the DLL
Passo "compilando o CriCriServidor para PC"
$csproj = Join-Path $projeto "CriCriServidor.csproj"
[xml]$xml = [IO.File]::ReadAllText($csproj)
$versao = ($xml.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $versao) { Parar "sem <Version> no csproj" }
$saidaDll = Join-Path $baseBuild "bin"
$obj = (Join-Path $baseBuild "obj") + "\"
$argumentos = @("build", $csproj, "-c", "Release", "--nologo", "-v", "q", "-nodeReuse:false", "-p:UseSharedCompilation=false",
  ("-p:InteropDir=" + $jogo + "\BepInEx"), "-p:DefineConstants=CRICRI_PC%3BAMIGOS_PUBLICO", ('-p:BaseIntermediateOutputPath=' + $obj),
  '-p:DefaultItemExcludes=obj\**', "-o", $saidaDll)
$saida = & dotnet @argumentos
if ($LASTEXITCODE -ne 0) { $saida | ForEach-Object { Write-Host $_ }; Parar "o build falhou" }
$dll = Join-Path $saidaDll "CriCriServidor.dll"

Passo "conferindo o DLL"
$resultado = & dotnet $conferidor confere $dll (Join-Path $jogo "BepInEx\interop") (Join-Path $jogo "BepInEx\core")
if ($LASTEXITCODE -ne 0 -or -not (($resultado | Select-Object -Last 1) -match "faltas: 0")) { $resultado | Select-Object -Last 10 | ForEach-Object { Write-Host $_ }; Parar "assinaturas com falta" }
Write-Host ("   " + ($resultado | Select-Object -Last 1))

$bytes = [IO.File]::ReadAllBytes($dll)
$ascii = [Text.Encoding]::ASCII.GetString($bytes)
$utf16 = [Text.Encoding]::Unicode.GetString($bytes)
# a string can start on an odd byte too: read both alignments
$utf16b = [Text.Encoding]::Unicode.GetString($bytes, 1, $bytes.Length - 1)
# CriarMentira exists only in the fake-people build, 22034 only in the test-server one. The panel may name InnerNet (the game's own join
# button path); what it may never do is write a game packet, and those all go through Hazel or the Rpc calls.
foreach ($proibido in @($env:USERNAME, "C:\Users", "CriarMentira", "22034", "Hazel", "MessageWriter", "StartRpc", "FinishRpc")) {
  if ($ascii.Contains($proibido) -or $utf16.Contains($proibido) -or $utf16b.Contains($proibido)) { Parar ("o DLL tem '" + $proibido + "': nao gero o pacote") }
}
Write-Host "   sem caminho deste PC, sem dados de mentira, sem servidor de teste, sem pacote do jogo"

# ---------------------------------------------------------------- 2. the folder
$nomePasta = "CRICRI-BR-PC-v" + $versao
$pasta_saida = Join-Path $raizPacote $nomePasta
if (-not $pasta_saida.StartsWith($raizPacote + "\")) { Parar "pasta de saida inesperada" }
Passo ("montando " + $pasta_saida)
if (Test-Path -LiteralPath $pasta_saida) { Remove-Item -LiteralPath $pasta_saida -Recurse -Force -Confirm:$false }
$carga = Join-Path $pasta_saida "jogo"
New-Item -ItemType Directory -Force (Join-Path $carga "BepInEx\plugins") | Out-Null
New-Item -ItemType Directory -Force (Join-Path $carga "BepInEx\config") | Out-Null
Copy-Item -LiteralPath (Join-Path $jogo "BepInEx\core") -Destination (Join-Path $carga "BepInEx\core") -Recurse -Force
Copy-Item -LiteralPath (Join-Path $jogo "dotnet") -Destination (Join-Path $carga "dotnet") -Recurse -Force
foreach ($x in @("winhttp.dll", "doorstop_config.ini", ".doorstop_version")) { Copy-Item -LiteralPath (Join-Path $jogo $x) -Destination (Join-Path $carga $x) -Force }
Copy-Item -LiteralPath $dll -Destination (Join-Path $carga "BepInEx\plugins\CriCriServidor.dll") -Force

# BepInEx.cfg: the clean one of the EHR zip (arch-independent; the game's own has this PC's choices)
$z = [IO.Compression.ZipFile]::OpenRead($ehrZip)
try {
  $entrada = $z.Entries | Where-Object { $_.FullName -eq "BepInEx/config/BepInEx.cfg" }
  if (-not $entrada) { Parar "sem BepInEx.cfg no zip do EHR" }
  [IO.Compression.ZipFileExtensions]::ExtractToFile($entrada, (Join-Path $carga "BepInEx\config\BepInEx.cfg"), $true)
} finally { $z.Dispose() }

Passo "conferindo o carregador"
foreach ($x in @("winhttp.dll", "BepInEx\core\dobby.dll", "dotnet\coreclr.dll", "dotnet\hostpolicy.dll")) {
  $a = ArquiteturaDe (Join-Path $carga $x)
  if ($a -ne "x64") { Parar ($x + " e " + $a + ", tem que ser x64") }
}
Write-Host "   winhttp, dobby, coreclr e hostpolicy: x64"
$versaoBep = ([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $carga "BepInEx\core\BepInEx.Unity.IL2CPP.dll")).ProductVersion -split "\+")[0]
Write-Host ("   BepInEx " + $versaoBep)
$assinados = 0
$semAssinatura = New-Object System.Collections.Generic.List[string]
$baseDotnet = (Join-Path $carga "dotnet") + "\"
foreach ($f in Get-ChildItem -LiteralPath (Join-Path $carga "dotnet") -Recurse -File -Force) {
  if ($f.Extension -ne ".dll" -and $f.Extension -ne ".exe") { continue }
  $s = Get-AuthenticodeSignature -LiteralPath $f.FullName
  if ($s.Status -eq "Valid" -and $s.SignerCertificate.Subject -match "Microsoft") { $assinados++ }
  else { $semAssinatura.Add($f.FullName.Substring($baseDotnet.Length) + " [" + $s.Status + "]") }
}
Write-Host ("   dotnet: " + $assinados + " binarios com assinatura valida da Microsoft, " + $semAssinatura.Count + " sem")
foreach ($x in $semAssinatura) { Write-Host ("     sem assinatura: " + $x) -ForegroundColor Yellow }

# ---------------------------------------------------------------- 3. list, scripts, text
$linhas = New-Object System.Collections.Generic.List[string]
$linhas.Add("CRICRI BR no PC: SHA256 de cada arquivo que vai pra pasta do jogo")
$linhas.Add("versao`t" + $versao)
$linhas.Add("bepinex`t" + $versaoBep)
$baseCarga = $carga + "\"
$contagem = 0
foreach ($f in (Get-ChildItem -LiteralPath $carga -Recurse -File -Force | Sort-Object FullName)) {
  $rel = $f.FullName.Substring($baseCarga.Length)
  $linhas.Add("arquivo`t" + (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash + "`t" + $rel)
  $contagem++
}
[IO.File]::WriteAllLines((Join-Path $pasta_saida "lista.txt"), $linhas.ToArray(), $utf8)

[IO.File]::WriteAllText((Join-Path $pasta_saida "INSTALAR-CRICRI-BR.ps1"), [IO.File]::ReadAllText((Join-Path $aqui "PcInstalador.ps1"), $utf8), $utf8Bom)
Copy-Item -LiteralPath (Join-Path $aqui "PcInstalar.cmd") -Destination (Join-Path $pasta_saida "INSTALAR-CRICRI-BR.cmd") -Force
Copy-Item -LiteralPath (Join-Path $aqui "PcDesinstalar.cmd") -Destination (Join-Path $pasta_saida "DESINSTALAR-CRICRI-BR.cmd") -Force
$leia = [IO.File]::ReadAllText((Join-Path $aqui "PcLeiaMe.txt"), $utf8).Replace("{VERSAO}", $versao).Replace("{BEPINEX}", $versaoBep)
[IO.File]::WriteAllText((Join-Path $pasta_saida "LEIA-ME CRICRI BR.txt"), $leia.Replace("`r`n", "`n").Replace("`n", "`r`n"), $utf8Bom)

# ---------------------------------------------------------------- 4. the zip (entries with "/", or some extractors make a file named "BepInEx\x")
$sufixo = ""
if (-not $Final) { $sufixo = "-NAO-TESTADO" }
$zipSaida = Join-Path $raizPacote ($nomePasta + $sufixo + ".zip")
Passo ("zipando " + $zipSaida)
if (Test-Path -LiteralPath $zipSaida) { Remove-Item -LiteralPath $zipSaida -Force -Confirm:$false }
$arquivoZip = [IO.Compression.ZipFile]::Open($zipSaida, [IO.Compression.ZipArchiveMode]::Create)
try {
  $base = $pasta_saida + "\"
  foreach ($f in Get-ChildItem -LiteralPath $pasta_saida -Recurse -File -Force) {
    $nomeEntrada = $nomePasta + "/" + $f.FullName.Substring($base.Length).Replace("\", "/")
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($arquivoZip, $f.FullName, $nomeEntrada, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
  }
} finally { $arquivoZip.Dispose() }

if (-not $Final) {
  $aviso = "Este pacote foi montado em " + (Get-Date -Format "dd/MM/yyyy HH:mm") + " e NUNCA rodou no jogo. Nao publicar.`r`n" +
    "O instalador so foi testado em pastas falsas. Falta testar no Among Us de verdade (checklist em cricriqol\docs\CRICRI-LAUNCHER-PC.md).`r`n" +
    "Depois do teste: rodar PcMontarPacote.ps1 -Final, e publicar so com o pode do CriCri.`r`n"
  [IO.File]::WriteAllText((Join-Path $raizPacote "NAO-PUBLICAR-AINDA.txt"), $aviso, $utf8)
}

Passo "pronto"
Write-Host ("   pasta: " + $pasta_saida)
Write-Host ("   zip:   " + $zipSaida + " (" + [math]::Round((Get-Item -LiteralPath $zipSaida).Length / 1MB, 1) + " MB)")
Write-Host ("   SHA256 do DLL: " + (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash.Substring(0, 16))
Write-Host ("   SHA256 do zip: " + (Get-FileHash -LiteralPath $zipSaida -Algorithm SHA256).Hash.Substring(0, 16))
Write-Host ("   arquivos na lista: " + $contagem)
