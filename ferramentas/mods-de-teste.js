#!/usr/bin/env node
// CriCri Mods: puts a test build of the Servidor mod (and, if it exists, the newer Replay) into the app's assets, so the next
// `gradlew assembleDebug` makes an APK to try on the phone. Nothing here publishes or commits anything.
//
//   node ferramentas/mods-de-teste.js mentira    fake friends, to look at the panel
//   node ferramentas/mods-de-teste.js servidor   the real panel against the test server (port 22034)
//   node ferramentas/mods-de-teste.js oficial    the release set: no test data, refuses a changed mod with an already published version
//   node ferramentas/mods-de-teste.js volta      back to exactly what git has (always do this before a release)
//
// A test build must never reach the catalog: "volta" restores the DLLs and mods.json from git, and the script refuses to run with those
// files already changed by something else, so nobody's work is overwritten.
'use strict';
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const { execFileSync } = require('child_process');

const REPO = path.resolve(__dirname, '..');
const ASSETS = path.join(REPO, 'fusionApp/src/main/assets/mods');
const SERVIDOR = path.join(REPO, 'embutidos/CriCriServidor');
const HOME = require('os').homedir();
const REPLAY_NOVO = path.join(HOME, 'Downloads/cricriqol/replay/bin/Android-mandar/CriCriReplay.dll');
const CONFERE = path.join(HOME, 'Downloads/cricriqol/android/confere-assinaturas/bin/Release/net8.0/ConfereAssinaturas.exe');
const INTEROP = path.join(HOME, 'Downloads/cricriqol/android/interop-celular-2026.11.3');
const CORE = 'C:/Program Files (x86)/Steam/steamapps/common/Among Us/BepInEx/core';
const OTIMIZACAO = path.join(REPO, 'embutidos/CriCriOtimizacao');
const REPLAY_CSPROJ = path.join(HOME, 'Downloads/cricriqol/replay/CriCriReplay.csproj');
const OFICINA_CSPROJ = path.join(HOME, 'Downloads/cricriqol/oficina/mod/CriCriOficina.csproj');
const OFICINA_NOVA = path.join(HOME, 'Downloads/cricriqol/oficina/mod/bin/Android-mandar/CriCriOficina.dll');
const ARQUIVOS = ['CriCriServidor.dll', 'CriCriOtimizacao.dll', 'CriCriReplay.dll', 'CriCriOficina.dll', 'mods.json'].map((n) => 'fusionApp/src/main/assets/mods/' + n);
const MARCA = path.join(ASSETS, '..', '..', '..', '..', 'build', 'mods-de-teste.txt');

const CONSTANTES = { mentira: 'AMIGOS_DE_MENTIRA', servidor: 'AMIGOS_SERVIDOR_TESTE', oficial: 'AMIGOS_PUBLICO' };
const PUBLICADOS = path.join(HOME, 'CriCri-Mods-publicar/mods');
// nothing of a test build may reach a release; "oficial" refuses the DLL if any of these is inside it
// the path and the name of whoever builds come from this computer: nothing personal is written in the repository
const USUARIO = require('os').userInfo().username;
const PROIBIDO = ['CriarMentira', '22034', 'Notícia de teste', 'C:\\Users\\' + USUARIO, USUARIO];

function falhar(msg) { console.error('ERRO: ' + msg); process.exit(1); }
function git(...a) { return execFileSync('git', a, { cwd: REPO, encoding: 'utf8' }); }
const sha256 = (b) => crypto.createHash('sha256').update(b).digest('hex');

const modo = process.argv[2];
if (modo === 'volta') {
  if (!fs.existsSync(MARCA)) falhar('nao ha versao de teste aplicada; nada a voltar (mudancas nos assets ficam como estao)');
  const noGit = git('ls-files', '--', ...ARQUIVOS).split('\n').filter(Boolean);
  git('checkout', '--', ...noGit);
  for (const a of ARQUIVOS) if (!noGit.includes(a)) fs.rmSync(path.join(REPO, a), { force: true });   // not in git yet
  fs.rmSync(MARCA, { force: true });
  console.log('Assets iguais ao git: ' + git('log', '-1', '--format=%h').trim());
  process.exit(0);
}
const constante = CONSTANTES[modo];
if (!constante) falhar('use: mentira | servidor | oficial | volta');

if (git('status', '--porcelain', '--', ...ARQUIVOS).trim() && !fs.existsSync(MARCA))
  falhar('os mods do app foram mudados por outra coisa; confira com "git status" antes');

function compilar(csproj, saida, extras) {
  execFileSync('dotnet', ['build', csproj, '-c', 'Release', '--no-incremental', '--nologo', '-v', 'q',
    '-nodeReuse:false', '-p:UseSharedCompilation=false', '-p:DebugType=none', '-p:DebugSymbols=false', ...extras, '-o', saida],
    { stdio: ['ignore', 'ignore', 'inherit'] });
}
const saida = path.join(HOME, 'CriCri-Mods-teste', modo);
compilar(path.join(SERVIDOR, 'CriCriServidor.csproj'), saida, ['-p:DefineConstants=' + constante]);
const saidaOtimizacao = path.join(HOME, 'CriCri-Mods-teste', 'otimizacao');
compilar(path.join(OTIMIZACAO, 'CriCriOtimizacao.csproj'), saidaOtimizacao, modo === 'oficial' ? [] : ['-p:DefineConstants=NOTICIA_DE_TESTE']);

const pecas = {
  'CriCriServidor.dll': path.join(saida, 'CriCriServidor.dll'),
  'CriCriOtimizacao.dll': path.join(saidaOtimizacao, 'CriCriOtimizacao.dll'),
};
execFileSync('dotnet', ['build', REPLAY_CSPROJ, '-c', 'Release', '--no-incremental', '--nologo', '-v', 'q', '-nodeReuse:false', '-p:UseSharedCompilation=false',
  '-p:Alvo=Android', '-p:InteropAndroid=' + path.join(INTEROP, 'interop').replace(/\\/g, '/'), '-p:GuardasComoErro=false', '-p:DebugType=none', '-p:DebugSymbols=false', '-p:OutputPath=bin/Android-mandar/'],
  { stdio: ['ignore', 'ignore', 'inherit'], cwd: path.dirname(REPLAY_CSPROJ) });
pecas['CriCriReplay.dll'] = REPLAY_NOVO;
// the Oficina uses the Replay's screens, so it comes after it. CriCri, 11/11: "eu nao quero liberar nada da oficina ainda, é pra ficar em breve":
// only the test sets carry it; the release set never does (the app shows an "Em breve" card instead)
if (modo !== 'oficial') {
execFileSync('dotnet', ['build', OFICINA_CSPROJ, '-c', 'Release', '--no-incremental', '--nologo', '-v', 'q', '-nodeReuse:false', '-p:UseSharedCompilation=false',
  '-p:Alvo=Android', '-p:InteropAndroid=' + path.join(INTEROP, 'interop').replace(/\\/g, '/'), '-p:OutputPath=bin/Android-mandar/'],
  { stdio: ['ignore', 'ignore', 'inherit'], cwd: path.dirname(OFICINA_CSPROJ) });
pecas['CriCriOficina.dll'] = OFICINA_NOVA;
}

// the version shown and published is always the one in each project
const VERSOES = {
  'CriCriServidor.dll': path.join(SERVIDOR, 'CriCriServidor.csproj'),
  'CriCriOtimizacao.dll': path.join(OTIMIZACAO, 'CriCriOtimizacao.csproj'),
  'CriCriReplay.dll': REPLAY_CSPROJ,
  'CriCriOficina.dll': OFICINA_CSPROJ,
};
const versaoDe = (csproj) => /<Version>([^<]+)<\/Version>/.exec(fs.readFileSync(csproj, 'utf8'))[1];

const j = JSON.parse(fs.readFileSync(path.join(ASSETS, 'mods.json'), 'utf8'));
if (modo === 'oficial') {
  // a test set may have left the Oficina in the assets: the release takes it out
  j.mods = j.mods.filter((x) => x.file !== 'CriCriOficina.dll');
  fs.rmSync(path.join(ASSETS, 'CriCriOficina.dll'), { force: true });
}
const escritas = [];
for (const [nome, origem] of Object.entries(pecas)) {
  let r;
  try { r = execFileSync(CONFERE, ['confere', origem, path.join(INTEROP, 'interop'), CORE], { cwd: path.dirname(INTEROP), encoding: 'utf8' }); }
  catch (e) { if (typeof e.stdout !== 'string' || !e.stdout.includes('FALTA')) throw e; r = e.stdout; }   // the tool exits 1 when something is missing; the lines below decide
  // what the Oficina calls in the Replay is checked by the compiler; the tool only knows the game
  const faltas = r.split('\n').filter((l) => l.startsWith('FALTA') && !(nome === 'CriCriOficina.dll' && l.includes('CriCriReplay.')));
  if (faltas.length || !/faltas: \d+/.test(r)) falhar(nome + ' nao bate com o jogo do celular:\n' + r);
  const b = fs.readFileSync(origem);
  let m = j.mods.find((x) => x.file === nome);
  if (!m && nome === 'CriCriOficina.dll') {
    m = Object.assign({}, j.mods.find((x) => x.file === 'CriCriReplay.dll'), {
      id: 'com.cricri.oficina', name: 'CriCri Oficina', file: nome,
      description: 'Ande em mapas criados na Oficina, dentro do jogo.',
      details: ['Abre o mapa no Freeplay, com paredes, pisos, objetos, portas e regras.', 'Precisa do CriCri Replay ligado.', 'Ainda não funciona em salas online.'],
      where: 'Menu principal do jogo → Replays → OFICINA',
    });
    j.mods.push(m);
  }
  m.version = versaoDe(VERSOES[nome]);
  if (modo === 'oficial') {
    const latin = b.toString('latin1'), u16 = [b.toString('utf16le'), b.subarray(1).toString('utf16le')];
    for (const p of PROIBIDO) if (latin.includes(p) || u16.some((t) => t.includes(p))) falhar(nome + ' tem "' + p + '": nao e versao oficial');
    const publicado = path.join(PUBLICADOS, nome.replace(/\.dll$/, '') + '-' + m.version + '.dll');
    if (fs.existsSync(publicado) && sha256(fs.readFileSync(publicado)) !== sha256(b)) falhar(nome + ' mudou e continua na versao ' + m.version + ': suba a versao do mod e rode de novo');
  }
  escritas.push([nome, b]);
  m.sha256 = sha256(b);
  console.log(nome + '  ' + m.version + '  ' + m.sha256.slice(0, 16));
}
for (const [nome, b] of escritas) fs.writeFileSync(path.join(ASSETS, nome), b);
fs.writeFileSync(path.join(ASSETS, 'mods.json'), JSON.stringify(j, null, 2) + '\n');
fs.mkdirSync(path.dirname(MARCA), { recursive: true });
fs.writeFileSync(MARCA, modo + '\n');
console.log(modo === 'oficial' ? 'Pronto (oficial): estes sao os mods da publicacao.' : 'Pronto (' + modo + '). Gere o APK de debug. NAO publique: antes rode "node ferramentas/mods-de-teste.js oficial".');
