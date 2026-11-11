# CRICRI BR for PC: installer and uninstaller. Goes into the package as INSTALAR-CRICRI-BR.ps1.
# PcMontarPacote.ps1 rewrites it as UTF-8 WITH BOM (Windows PowerShell 5.1 reads a file without BOM as ANSI).
# Player-facing text only uses characters of console code page 850: no long dashes, no ellipsis.
#
# Rules it keeps:
#  - it only ever touches, inside the game folder: BepInEx, dotnet, winhttp.dll, doorstop_config.ini, .doorstop_version;
#  - it never overwrites or moves another mod: with BepInEx 6 IL2CPP already there it only adds CriCriServidor.dll, and with
#    anything it does not recognize it refuses and changes nothing;
#  - the game folder's Among Us files are never touched; steam_appid.txt is not shipped;
#  - winhttp.dll goes in LAST and comes out FIRST: without it the loader does not run, so stopping in the middle leaves a normal game;
#  - before copying, a note in BepInEx lists every file; an interrupted install is undone by that note on the next run;
#  - uninstall removes a file only if its SHA256 is the one installed (BepInEx.cfg excepted: BepInEx rewrites it);
#  - nothing here runs elevated, downloads anything or starts the game.
param(
  [string]$Pasta = "",
  [switch]$Desinstalar,
  [switch]$Sim,            # answers yes to the confirmations (unattended use and the tests)
  [switch]$ApagarChave,    # with -Sim: also deletes the saved account key on uninstall
  [string]$NomeDoProcesso = "Among Us",
  [int]$TesteFalharEm = 0  # tests only: simulates a failure after N copied files
)

$ErrorActionPreference = "Stop"
$AppId = "945360"
$pacote = $PSScriptRoot
$PLUGIN = "BepInEx\plugins\CriCriServidor.dll"
$NOTA = "BepInEx\CriCriBR-instalacao.txt"
$CONFIG = "BepInEx\config\BepInEx.cfg"
$PORTA = "winhttp.dll"
$IL2CPP = "BepInEx\core\BepInEx.Unity.IL2CPP.dll"
$DO_CARREGADOR = @("BepInEx", "dotnet", "winhttp.dll", "doorstop_config.ini", ".doorstop_version")
$NOME_DLL = "CriCriServidor.dll"
$UTF8 = New-Object Text.UTF8Encoding($false)
$TAB = [string][char]9

function J([string]$a, [string]$b) { return [IO.Path]::Combine($a, $b) }
function Titulo($t) { Write-Host ""; Write-Host ("== " + $t) -ForegroundColor Cyan }
function Info($t)   { Write-Host ("   " + $t) }
function Bom($t)    { Write-Host ("   " + $t) -ForegroundColor Green }
function Aviso($t)  { Write-Host ("   " + $t) -ForegroundColor Yellow }
function Ruim($t)   { Write-Host ("   " + $t) -ForegroundColor Red }
function Curto($er) {
  try { $ex = $er.Exception; while ($ex.InnerException -ne $null) { $ex = $ex.InnerException }; return $ex.Message } catch { return "?" }
}
function Existe([string]$p) { return ([IO.File]::Exists($p) -or [IO.Directory]::Exists($p)) }
function HashDe([string]$arq) { return (Get-FileHash -LiteralPath $arq -Algorithm SHA256).Hash }

# Yes/no. End of input or three answers not understood count as no: it must never loop forever.
function Confirmar([string]$texto) {
  if ($Sim) { return $true }
  for ($i = 0; $i -lt 3; $i++) {
    $r = Read-Host ("   " + $texto + " (S/N)")
    if ($r -eq $null) { return $false }
    $r = $r.Trim().ToUpperInvariant()
    if ($r -eq "S") { return $true }
    if ($r -eq "N") { return $false }
    Aviso "Digite S ou N."
  }
  return $false
}

function JogoAberto {
  $p = @(Get-Process -Name $NomeDoProcesso -ErrorAction SilentlyContinue)
  return ($p.Count -gt 0)
}

function PastaDoJogoValida([string]$p) {
  if ([string]::IsNullOrWhiteSpace($p)) { return $false }
  try {
    return ([IO.File]::Exists((J $p "Among Us.exe")) -and [IO.File]::Exists((J $p "GameAssembly.dll")) -and [IO.Directory]::Exists((J $p "Among Us_Data")))
  } catch { return $false }
}

function Normalizar([string]$p) {
  try { return [IO.Path]::GetFullPath($p.Trim().Trim('"')).TrimEnd("\") } catch { return $p.Trim().Trim('"').TrimEnd("\") }
}

# "x64", "x86" or "?" from the PE header.
function Arquitetura([string]$arq) {
  try {
    $fs = [IO.File]::OpenRead($arq)
    try {
      $br = New-Object IO.BinaryReader($fs)
      $fs.Position = 60
      $fs.Position = $br.ReadInt32() + 4
      $maquina = $br.ReadUInt16()
      if ($maquina -eq 0x8664) { return "x64" }
      if ($maquina -eq 0x014c) { return "x86" }
      return "?"
    } finally { $fs.Dispose() }
  } catch { return "?" }
}

function RaizesDaSteam {
  $lista = New-Object System.Collections.Generic.List[string]
  $chaves = @(
    @("HKLM:\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
    @("HKLM:\SOFTWARE\Valve\Steam", "InstallPath"),
    @("HKCU:\Software\Valve\Steam", "SteamPath")
  )
  foreach ($c in $chaves) {
    try {
      $v = (Get-ItemProperty -LiteralPath $c[0] -Name $c[1] -ErrorAction Stop).($c[1])
      if (-not [string]::IsNullOrWhiteSpace($v)) { $lista.Add((Normalizar ($v -replace "/", "\"))) }
    } catch { }
  }
  return ,$lista
}

function AcharJogos {
  $achados = New-Object System.Collections.Generic.List[string]
  $vistos = @{}
  $bibliotecas = New-Object System.Collections.Generic.List[string]
  foreach ($raiz in (RaizesDaSteam)) {
    $bibliotecas.Add($raiz)
    $vdf = J $raiz "steamapps\libraryfolders.vdf"
    if (-not [IO.File]::Exists($vdf)) { continue }
    try {
      foreach ($m in [regex]::Matches([IO.File]::ReadAllText($vdf), '"path"\s+"([^"]+)"')) { $bibliotecas.Add((Normalizar $m.Groups[1].Value.Replace("\\", "\"))) }
    } catch { }
  }
  foreach ($lib in $bibliotecas) {
    $nome = "Among Us"
    $acf = J $lib ("steamapps\appmanifest_" + $AppId + ".acf")
    if ([IO.File]::Exists($acf)) {
      try {
        $m = [regex]::Match([IO.File]::ReadAllText($acf), '"installdir"\s+"([^"]+)"')
        if ($m.Success) { $nome = $m.Groups[1].Value }
      } catch { }
    }
    $p = Normalizar (J $lib (J "steamapps\common" $nome))
    $k = $p.ToLowerInvariant()
    if ($vistos.ContainsKey($k)) { continue }
    $vistos[$k] = $true
    if (PastaDoJogoValida $p) { $achados.Add($p) }
  }
  return ,$achados
}

function LerTabela([string]$arq) {
  $lista = New-Object System.Collections.Generic.List[object]
  foreach ($l in [IO.File]::ReadAllLines($arq, $UTF8)) {
    $partes = $l.Split([char]9)
    if ($partes.Count -ge 2) { $lista.Add($partes) }
  }
  return ,$lista
}

# A path read from a file only counts if it is relative, has no "..", and lives under one of the loader's items.
function RelSeguro([string]$rel) {
  if ([string]::IsNullOrWhiteSpace($rel)) { return $false }
  if ($rel.Contains(":") -or $rel.StartsWith("\") -or $rel.StartsWith("/")) { return $false }
  $segs = $rel.Split([char[]]@('\', '/'))
  foreach ($seg in $segs) { if ($seg -eq ".." -or $seg -eq "." -or $seg -eq "") { return $false } }
  return ($DO_CARREGADOR -contains $segs[0])
}

function GravarNota([string]$raiz, [string]$modo, [string]$estado, $entradas) {
  $linhas = New-Object System.Collections.Generic.List[string]
  $linhas.Add("CRICRI BR no PC: o que o instalador colocou nesta pasta. O desinstalador usa este arquivo.")
  $linhas.Add("modo" + $TAB + $modo)
  $linhas.Add("estado" + $TAB + $estado)
  foreach ($e in $entradas) { $linhas.Add("arquivo" + $TAB + $e[0] + $TAB + $e[1]) }
  $temporario = (J $raiz $NOTA) + ".tmp"
  [IO.File]::WriteAllLines($temporario, $linhas.ToArray(), $UTF8)
  if ([IO.File]::Exists((J $raiz $NOTA))) { [IO.File]::Delete((J $raiz $NOTA)) }
  [IO.File]::Move($temporario, (J $raiz $NOTA))
}

function ApagarPastasVazias([string]$raiz) {
  foreach ($topo in @("BepInEx", "dotnet")) {
    $t = J $raiz $topo
    if (-not [IO.Directory]::Exists($t)) { continue }
    $subs = @([IO.Directory]::GetDirectories($t, "*", [IO.SearchOption]::AllDirectories) | Sort-Object -Property Length -Descending)
    foreach ($s in $subs) { try { if (@([IO.Directory]::GetFileSystemEntries($s)).Count -eq 0) { [IO.Directory]::Delete($s) } } catch { } }
    try { if (@([IO.Directory]::GetFileSystemEntries($t)).Count -eq 0) { [IO.Directory]::Delete($t) } } catch { }
  }
}

# Removes the note's files whose hash still matches. Returns the problems found (empty = all out).
function TirarArquivos([string]$raiz, $entradas) {
  $problemas = New-Object System.Collections.Generic.List[string]
  foreach ($e in $entradas) {
    $rel = $e[1]
    if (-not (RelSeguro $rel)) { $problemas.Add("linha estranha na anotacao: " + $rel); continue }
    if ($rel -eq $NOTA) { continue }
    $p = J $raiz $rel
    if (-not [IO.File]::Exists($p)) { continue }
    if ($rel -ne $CONFIG) {
      $mesmo = $false
      try { $mesmo = ((HashDe $p) -eq $e[0]) } catch { }
      if (-not $mesmo) { Aviso ("ficou, outro programa trocou: " + $rel); continue }
    }
    try { [IO.File]::Delete($p) } catch { $problemas.Add("nao consegui apagar " + $rel + " (" + (Curto $_) + ")") }
  }
  return ,$problemas
}

function EntradasDaNota([string]$raiz) {
  $lista = New-Object System.Collections.Generic.List[object]
  foreach ($e in (LerTabela (J $raiz $NOTA))) {
    if ($e[0] -eq "arquivo" -and $e.Count -ge 3) { $lista.Add(@($e[1], $e[2])) }
  }
  return ,$lista
}

function ValorDaNota([string]$raiz, [string]$chave) {
  foreach ($e in (LerTabela (J $raiz $NOTA))) { if ($e[0] -eq $chave) { return $e[1] } }
  return ""
}

# Undoes a full install that stopped in the middle. Everything in the note was created by that install.
function DesfazerInstalacao([string]$raiz) {
  $entradas = EntradasDaNota $raiz
  $porta = J $raiz $PORTA
  if ([IO.File]::Exists($porta)) {
    foreach ($e in $entradas) {
      if ($e[1] -eq $PORTA -and (HashDe $porta) -eq $e[0]) { [IO.File]::Delete($porta) }
    }
  }
  $problemas = TirarArquivos $raiz $entradas
  if ($problemas.Count -eq 0) { [IO.File]::Delete((J $raiz $NOTA)) }
  ApagarPastasVazias $raiz
  return ,$problemas
}

function ConferirPacote {
  $problemas = New-Object System.Collections.Generic.List[string]
  $arquivoLista = J $pacote "lista.txt"
  if (-not [IO.File]::Exists($arquivoLista)) { $problemas.Add("falta lista.txt"); return ,$problemas }
  $n = 0
  foreach ($e in (LerTabela $arquivoLista)) {
    if ($e[0] -ne "arquivo" -or $e.Count -lt 3) { continue }
    $n++
    if (-not (RelSeguro $e[2])) { $problemas.Add("linha estranha na lista: " + $e[2]); continue }
    $f = J (J $pacote "jogo") $e[2]
    if (-not [IO.File]::Exists($f)) { $problemas.Add("falta " + $e[2]); continue }
    if ((HashDe $f) -ne $e[1]) { $problemas.Add("diferente do original: " + $e[2]) }
  }
  if ($n -eq 0) { $problemas.Add("a lista do pacote esta vazia") }
  foreach ($r in @($PLUGIN, $PORTA, $IL2CPP, "dotnet\coreclr.dll")) {
    if (-not [IO.File]::Exists((J (J $pacote "jogo") $r))) { $problemas.Add("falta " + $r) }
  }
  return ,$problemas
}

function EntradasDoPacote {
  $lista = New-Object System.Collections.Generic.List[object]
  foreach ($e in (LerTabela (J $pacote "lista.txt"))) {
    if ($e[0] -eq "arquivo" -and $e.Count -ge 3) { $lista.Add(@($e[1], $e[2])) }
  }
  return ,$lista
}

function CopiarConferindo([string]$origem, [string]$destino, [string]$hashEsperado, [bool]$podeSobrescrever) {
  $dirDestino = [IO.Path]::GetDirectoryName($destino)
  [IO.Directory]::CreateDirectory($dirDestino) | Out-Null
  [IO.File]::Copy($origem, $destino, $podeSobrescrever)
  if ((HashDe $destino) -ne $hashEsperado) { throw ("copia diferente do original: " + $destino) }
}

# Puts only the mod's DLL in place: to a temporary name first, so a failed copy leaves the old one intact.
function PorSoODll([string]$raiz) {
  $origem = J (J $pacote "jogo") $PLUGIN
  $hashDll = HashDe $origem
  $destino = J $raiz $PLUGIN
  $temporario = $destino + ".novo"
  CopiarConferindo $origem $temporario $hashDll $true
  if ([IO.File]::Exists($destino)) { [IO.File]::Delete($destino) }
  [IO.File]::Move($temporario, $destino)
  return $hashDll
}

function ApagarChaveGuardada {
  $local = $env:LOCALAPPDATA
  if ([string]::IsNullOrWhiteSpace($local)) { return }
  $dirChave = J $local "CriCri Mods"
  $arqChave = J $dirChave "chave-cricri-br.dpapi"
  if (-not [IO.File]::Exists($arqChave)) { return }
  $apagar = $ApagarChave
  if (-not $Sim) { $apagar = Confirmar "Apagar tambem a chave da sua conta guardada neste PC?" }
  if (-not $apagar) { Info "A chave da conta continua guardada neste PC."; return }
  [IO.File]::Delete($arqChave)
  try { if (@([IO.Directory]::GetFileSystemEntries($dirChave)).Count -eq 0) { [IO.Directory]::Delete($dirChave) } } catch { }
  Bom "Chave da conta apagada deste PC."
}

function Desinstalacao {
  $temNota = [IO.File]::Exists((J $jogo $NOTA))
  $temDll = [IO.File]::Exists((J $jogo $PLUGIN))
  if (-not $temNota -and -not $temDll) {
    Aviso "O CRICRI BR nao esta instalado nesta pasta. Nada foi mudado."
    ApagarChaveGuardada
    return 0
  }
  $modo = ""
  $entradas = New-Object System.Collections.Generic.List[object]
  if ($temNota) { $modo = ValorDaNota $jogo "modo"; $entradas = EntradasDaNota $jogo }

  $outros = New-Object System.Collections.Generic.List[string]
  foreach ($sub in @("BepInEx\plugins", "BepInEx\patchers")) {
    $d = J $jogo $sub
    if (-not [IO.Directory]::Exists($d)) { continue }
    foreach ($f in Get-ChildItem -LiteralPath $d -Recurse -File -Force -Filter "*.dll") {
      if ($f.Name -ne $NOME_DLL) { $outros.Add($f.FullName.Substring($jogo.Length + 1)) }
    }
  }

  $hashPorta = ""
  foreach ($e in $entradas) { if ($e[1] -eq $PORTA) { $hashPorta = $e[0] } }
  $portaNossa = $false
  if ($hashPorta -and [IO.File]::Exists((J $jogo $PORTA))) { $portaNossa = ((HashDe (J $jogo $PORTA)) -eq $hashPorta) }
  $tudo = ($modo -eq "completa") -and ($outros.Count -eq 0) -and $portaNossa

  if ($tudo) {
    Info "Vou tirar o mod e o BepInEx que este instalador colocou. O jogo volta ao normal."
  } else {
    if ($outros.Count -gt 0) { Aviso ("Tem outro mod usando o BepInEx (" + (($outros | Select-Object -First 4) -join ", ") + "): o BepInEx fica.") }
    Info "Vou tirar so o CriCriServidor.dll."
  }
  if (-not (Confirmar "Continuar?")) { Aviso "Cancelado. Nada foi mudado."; return 2 }
  if (JogoAberto) { Ruim "O Among Us foi aberto no meio. Feche e rode de novo. Nada foi mudado."; return 1 }

  if ($tudo) {
    try { [IO.File]::Delete((J $jogo $PORTA)) }
    catch { Ruim ("Nao consegui tirar o winhttp.dll (" + (Curto $_) + "). Nada foi tirado."); return 1 }
    Bom "winhttp.dll tirado: o BepInEx nao roda mais."
    $problemas = TirarArquivos $jogo $entradas
    # what BepInEx generated while running: no other plugin is left, so it belongs to this install
    foreach ($g in @("BepInEx\interop", "BepInEx\unity-libs", "BepInEx\cache")) {
      $p = J $jogo $g
      try { if ([IO.Directory]::Exists($p)) { [IO.Directory]::Delete($p, $true) } } catch { $problemas.Add("nao consegui apagar " + $g + " (" + (Curto $_) + ")") }
    }
    foreach ($g in @("BepInEx\LogOutput.log", "BepInEx\ErrorLog.log", "BepInEx\config\br.com.cricribr.servidor.cfg", "BepInEx\config\br.com.cricribr.servidor.pc.cfg")) {
      $p = J $jogo $g
      try { if ([IO.File]::Exists($p)) { [IO.File]::Delete($p) } } catch { $problemas.Add("nao consegui apagar " + $g + " (" + (Curto $_) + ")") }
    }
    if ($problemas.Count -gt 0) {
      Ruim "Nao deu pra tirar tudo:"
      foreach ($x in $problemas) { Ruim ("  - " + $x) }
      Ruim "Feche o que estiver usando a pasta do jogo (antivirus, Explorer) e rode de novo."
      Info "O jogo ja abre normal: sem o winhttp.dll o BepInEx nao roda."
      return 1
    }
    [IO.File]::Delete((J $jogo $NOTA))
    ApagarPastasVazias $jogo
    if ([IO.Directory]::Exists((J $jogo "BepInEx"))) {
      $resto = @(Get-ChildItem -LiteralPath (J $jogo "BepInEx") -Recurse -File -Force | Select-Object -First 5 | ForEach-Object { $_.FullName.Substring($jogo.Length + 1) })
      if ($resto.Count -gt 0) { Aviso ("A pasta BepInEx ficou com o que nao e deste instalador: " + ($resto -join ", ")) }
    }
    Bom "CRICRI BR tirado (mod e BepInEx)."
  } else {
    try { if ($temDll) { [IO.File]::Delete((J $jogo $PLUGIN)) } }
    catch { Ruim ("Nao consegui apagar o " + $NOME_DLL + " (" + (Curto $_) + ")"); return 1 }
    # A full install keeps its note while the loader stays: it is what lets a later run remove the loader.
    if ($temNota -and $modo -ne "completa") { [IO.File]::Delete((J $jogo $NOTA)) }
    Bom "CRICRI BR tirado (so o CriCriServidor.dll)."
  }
  ApagarChaveGuardada
  return 0
}

try {
  Write-Host ""
  if ($Desinstalar) { Write-Host "  CRICRI BR no PC - desinstalador" -ForegroundColor Cyan }
  else { Write-Host "  CRICRI BR no PC - instalador" -ForegroundColor Cyan }

  if (-not $Desinstalar) {
    Titulo "conferindo o pacote"
    $conferencia = ConferirPacote
    if ($conferencia.Count -gt 0) {
      foreach ($c in ($conferencia | Select-Object -First 8)) { Ruim ("  " + $c) }
      Info "Extraia o zip INTEIRO de novo: botao direito no zip > Extrair tudo, e rode o instalador da pasta extraida."
      exit 1
    }
    $listaPacote = EntradasDoPacote
    Bom ("" + $listaPacote.Count + " arquivos conferidos (SHA256)")
  }

  Titulo "procurando o Among Us da Steam"
  $jogo = ""
  if (-not [string]::IsNullOrWhiteSpace($Pasta)) {
    $jogo = Normalizar $Pasta
    if (-not (PastaDoJogoValida $jogo)) { Ruim ("Essa pasta nao tem o Among Us: " + $jogo); exit 1 }
  } else {
    $achados = AcharJogos
    if ($achados.Count -eq 1) { $jogo = $achados[0] }
    elseif ($achados.Count -gt 1) {
      Info "Achei mais de um Among Us:"
      for ($i = 0; $i -lt $achados.Count; $i++) { Info ("  " + ($i + 1) + ") " + $achados[$i]) }
      $escolha = Read-Host "   Qual? Digite o numero"
      $indice = 0
      if ([int]::TryParse($escolha, [ref]$indice) -and $indice -ge 1 -and $indice -le $achados.Count) { $jogo = $achados[$indice - 1] }
      else { Aviso "Cancelado. Nada foi mudado."; exit 2 }
    }
    while ([string]::IsNullOrWhiteSpace($jogo)) {
      Aviso "Nao achei o Among Us da Steam sozinho."
      Info "Na Steam: botao direito em Among Us > Gerenciar > Ver arquivos locais. Copie o endereco da pasta que abrir."
      $digitada = Read-Host "   Cole aqui a pasta do jogo (ou so Enter pra cancelar)"
      if ([string]::IsNullOrWhiteSpace($digitada)) { Aviso "Cancelado. Nada foi mudado."; exit 2 }
      $candidata = Normalizar $digitada
      if (PastaDoJogoValida $candidata) { $jogo = $candidata } else { Ruim ("Essa pasta nao tem o Among Us: " + $candidata) }
    }
  }
  Bom ("Pasta do jogo: " + $jogo)
  if ($jogo.ToLowerInvariant() -eq (Normalizar $pacote).ToLowerInvariant()) { Ruim "O pacote foi extraido dentro da pasta do jogo. Extraia em outra pasta."; exit 1 }

  if (JogoAberto) { Ruim "O Among Us esta ABERTO. Feche o jogo e rode de novo. Nada foi mudado."; exit 1 }

  # a full install that stopped in the middle: undone first, whatever is asked now
  if ([IO.File]::Exists((J $jogo $NOTA)) -and (ValorDaNota $jogo "estado") -eq "instalando") {
    Aviso "Achei uma instalacao que parou no meio. Desfazendo antes de continuar."
    $problemasAntes = DesfazerInstalacao $jogo
    if ($problemasAntes.Count -gt 0) {
      foreach ($x in $problemasAntes) { Ruim ("  - " + $x) }
      Ruim "Feche o que estiver usando a pasta do jogo e rode de novo."
      exit 1
    }
    Bom "Desfeito."
  }

  if ($Desinstalar) { exit (Desinstalacao) }

  Titulo "olhando o que ja esta na pasta"
  $arquiteturaJogo = Arquitetura (J $jogo "Among Us.exe")
  if ($arquiteturaJogo -ne "x64") {
    Ruim ("Este pacote e para o Among Us de 64 bits, e o jogo desta pasta e " + $arquiteturaJogo + ". Nada foi mudado.")
    exit 1
  }
  if (-not [IO.File]::Exists((J $jogo "Among Us_Data\Plugins\x86_64\steam_api64.dll"))) {
    Aviso "Essa pasta nao parece ser a versao da Steam. Epic, Xbox e Microsoft Store nao foram testadas."
    if (-not (Confirmar "Continuar mesmo assim?")) { Aviso "Cancelado. Nada foi mudado."; exit 2 }
  }

  $bep = J $jogo "BepInEx"
  $temBep = [IO.Directory]::Exists($bep) -and (@([IO.Directory]::GetFileSystemEntries($bep)).Count -gt 0)
  $temIl2cpp = [IO.File]::Exists((J $jogo $IL2CPP))
  $restos = New-Object System.Collections.Generic.List[string]
  foreach ($x in @("dotnet", "winhttp.dll", "doorstop_config.ini", ".doorstop_version", "version.dll")) { if (Existe (J $jogo $x)) { $restos.Add($x) } }
  if ([IO.Directory]::Exists((J $jogo "MelonLoader"))) { Ruim "Tem MelonLoader nesta pasta. Tire ele antes de instalar. Nada foi mudado."; exit 1 }
  if ($temBep -and -not $temIl2cpp) { Ruim "O BepInEx desta pasta nao e o BepInEx 6 IL2CPP. Nada foi mudado."; exit 1 }
  if (-not $temBep -and $restos.Count -gt 0) { Ruim ("Tem restos de outro carregador de mod (" + ($restos -join ", ") + "). Nada foi mudado."); exit 1 }

  $modo = "completa"
  if ($temBep) {
    if (-not [IO.File]::Exists((J $jogo $PORTA))) { Ruim "O BepInEx desta pasta nao tem o winhttp.dll, entao ele nao roda. Nada foi mudado."; exit 1 }
    $arquiteturaPorta = Arquitetura (J $jogo $PORTA)
    if ($arquiteturaPorta -ne $arquiteturaJogo) {
      Ruim ("O BepInEx desta pasta e de " + $arquiteturaPorta + " e o jogo e de " + $arquiteturaJogo + ": ele nao carrega nenhum mod.")
      Info "Desinstale o mod antigo (ele parou quando o jogo virou 64 bits) e rode este instalador de novo. Nada foi mudado."
      exit 1
    }
    $modo = "sodll"
    $versaoJogo = "" ; $versaoPacote = ""
    try {
      $versaoJogo = ([Diagnostics.FileVersionInfo]::GetVersionInfo((J $jogo $IL2CPP)).ProductVersion -split "\+")[0]
      $versaoPacote = ([Diagnostics.FileVersionInfo]::GetVersionInfo((J (J $pacote "jogo") $IL2CPP)).ProductVersion -split "\+")[0]
    } catch { }
    if ($versaoJogo -ne $versaoPacote) {
      Aviso ("O BepInEx desta pasta e o " + $versaoJogo + " e o mod foi feito no " + $versaoPacote + ". Pode nao carregar.")
      if (-not (Confirmar "Colocar o mod mesmo assim?")) { Aviso "Cancelado. Nada foi mudado."; exit 2 }
    }
  }

  if ($modo -eq "completa") { Info "Vou instalar o BepInEx 64 bits e o mod. Nada que ja esta nesta pasta muda." }
  elseif ([IO.File]::Exists((J $jogo $NOTA))) { Info "O CRICRI BR ja esta aqui: vou atualizar so o CriCriServidor.dll." }
  else { Info "Esta pasta ja tem o BepInEx: vou colocar so o CriCriServidor.dll, junto dos seus outros mods." }
  if (-not (Confirmar "Instalar?")) { Aviso "Cancelado. Nada foi mudado."; exit 2 }
  if (JogoAberto) { Ruim "O Among Us foi aberto no meio. Feche e rode de novo. Nada foi mudado."; exit 1 }

  Titulo "instalando"
  if ($modo -eq "sodll") {
    $hashDll = PorSoODll $jogo
    $modoAnterior = ""
    if ([IO.File]::Exists((J $jogo $NOTA))) { $modoAnterior = ValorDaNota $jogo "modo" }
    if ($modoAnterior -eq "completa") {
      # updating our own full install: the list keeps the loader, with the new DLL's hash
      $antigas = EntradasDaNota $jogo
      $novas = New-Object System.Collections.Generic.List[object]
      foreach ($e in $antigas) {
        if ($e[1] -eq $PLUGIN) { $novas.Add(@($hashDll, $PLUGIN)) } else { $novas.Add($e) }
      }
      GravarNota $jogo "completa" "ok" $novas
    } else {
      $so = New-Object System.Collections.Generic.List[object]
      $so.Add(@($hashDll, $PLUGIN))
      GravarNota $jogo "sodll" "ok" $so
    }
    Bom "CriCriServidor.dll colocado e conferido."
  } else {
    [IO.Directory]::CreateDirectory($bep) | Out-Null
    GravarNota $jogo "completa" "instalando" $listaPacote
    $copiados = 0
    try {
      $ordem = @($listaPacote | Where-Object { $_[1] -ne $PORTA }) + @($listaPacote | Where-Object { $_[1] -eq $PORTA })
      foreach ($e in $ordem) {
        CopiarConferindo (J (J $pacote "jogo") $e[1]) (J $jogo $e[1]) $e[0] $false
        $copiados++
        if ($TesteFalharEm -gt 0 -and $copiados -eq $TesteFalharEm) { throw "falha simulada" }
      }
    } catch {
      Ruim ("A copia falhou (" + (Curto $_) + "). Desfazendo.")
      $problemasCopia = DesfazerInstalacao $jogo
      if ($problemasCopia.Count -gt 0) {
        foreach ($x in $problemasCopia) { Ruim ("  - " + $x) }
        Ruim "Rode o instalador de novo: ele termina de desfazer primeiro."
      } else { Aviso "Desfeito: a pasta do jogo voltou a ser o que era." }
      exit 1
    }
    GravarNota $jogo "completa" "ok" $listaPacote
    Bom ("" + $copiados + " arquivos colocados e conferidos.")
  }

  Write-Host ""
  Bom "Pronto. Abra o Among Us pela Steam."
  if ($modo -eq "completa") { Info "A primeira abertura demora alguns minutos e mostra uma janela preta de texto. Isso so acontece uma vez." }
  Info "No menu principal aparece o botao CONTA BR, e a regiao CriCri BR entra na lista de servidores."
  exit 0
} catch {
  Ruim ("Erro: " + (Curto $_))
  exit 1
}
