#!/usr/bin/env node
// CriCri Mods: prepares the mods folder for the site (https://cricribr.com.br/mods/), so a mod reaches the players
// WITHOUT a new APK. Writes catalogo.json (numbered, signed), catalogo.json.sig and the DLLs with the version in their
// names. Nothing is sent anywhere: the CRICRI BR session uploads the folder, and only with the CriCri's "pode".
//
//   node ferramentas/publicar-mods.js                       the mods inside the app (fusionApp/src/main/assets/mods)
//   node ferramentas/publicar-mods.js --origem <pasta>      another mods.json + DLLs (a mod update)
//   node ferramentas/publicar-mods.js --retirar <id>        emergency: takes that mod out of every phone
//   node ferramentas/publicar-mods.js --nova-chave          new signing key (then the app needs a new APK)
//
// Rules the app enforces (ModsRemotos.java): signature with the key fixed in the APK, "numero" never goes down,
// sha256 of every DLL, plain file names. Rules for the mods themselves (the CRICRI BR rule for public mods):
// a DLL on the site is public and can be decompiled, so it never builds game packets (no Hazel) and talks to the
// server only through /api/mod/* with the account key.
'use strict';
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

const REPO = path.resolve(__dirname, '..');
const args = process.argv.slice(2);
function opcao(nome, padrao) {
  const i = args.indexOf(nome);
  return i >= 0 && i + 1 < args.length ? args[i + 1] : padrao;
}
function varias(nome) {
  const r = [];
  args.forEach((a, i) => { if (a === nome && i + 1 < args.length) r.push(args[i + 1]); });
  return r;
}

const PASTA_CHAVES = path.join(require('os').homedir(), 'CriCri-Mods-chaves');   // never inside the repository
const CHAVE = opcao('--chave', path.join(PASTA_CHAVES, 'catalogo-mods-privada.pem'));
const PUBLICA = path.join(path.dirname(CHAVE), 'catalogo-mods-publica.txt');
const ORIGEM = opcao('--origem', path.join(REPO, 'fusionApp/src/main/assets/mods'));
const SAIDA = opcao('--saida', path.join(require('os').homedir(), 'CriCri-Mods-publicar', 'mods'));
const NOME_SEGURO = /^[A-Za-z0-9][A-Za-z0-9._-]{0,95}$/;

function falhar(msg) { console.error('ERRO: ' + msg); process.exit(1); }
const sha256 = (b) => crypto.createHash('sha256').update(b).digest('hex');

if (args.includes('--nova-chave')) {
  if (fs.existsSync(CHAVE) && !args.includes('--forcar'))
    falhar('ja existe ' + CHAVE + ' (use --forcar pra trocar; os celulares com o APK velho param de aceitar o catalogo)');
  const { publicKey, privateKey } = crypto.generateKeyPairSync('ec', { namedCurve: 'P-256' });
  fs.mkdirSync(path.dirname(CHAVE), { recursive: true });
  fs.writeFileSync(CHAVE, privateKey.export({ type: 'pkcs8', format: 'pem' }), { mode: 0o600 });
  const pub = publicKey.export({ type: 'spki', format: 'der' }).toString('base64');
  fs.writeFileSync(PUBLICA, pub + '\n');
  console.log('Chave nova. Ponha esta publica em ModsRemotos.CHAVE_PUBLICA e gere um APK novo:\n' + pub);
  process.exit(0);
}

if (!fs.existsSync(CHAVE)) falhar('nao achei a chave privada em ' + CHAVE);
const origem = JSON.parse(fs.readFileSync(path.join(ORIGEM, 'mods.json'), 'utf8'));
fs.mkdirSync(SAIDA, { recursive: true });

// the number only goes up: one more than the last catalog written here
let numero = 1;
const anterior = path.join(SAIDA, 'catalogo.json');
if (fs.existsSync(anterior)) numero = (JSON.parse(fs.readFileSync(anterior, 'utf8')).numero || 0) + 1;

const retirar = new Set(varias('--retirar'));
const mods = [];
let bytes = 0;
for (const m of origem.mods) {
  if (retirar.has(m.id)) { mods.push({ id: m.id, file: m.file, retirado: true }); retirar.delete(m.id); continue; }
  if (!NOME_SEGURO.test(m.file) || !m.file.endsWith('.dll')) falhar('nome de arquivo invalido: ' + m.file);
  const dll = fs.readFileSync(path.join(ORIGEM, m.file));
  const hash = sha256(dll);
  if (m.sha256 && m.sha256.toLowerCase() !== hash) falhar(m.file + ': sha256 ' + hash + ' nao bate com o mods.json (' + m.sha256 + ')');
  const url = m.file.replace(/\.dll$/, '') + '-' + m.version + '.dll';
  if (!NOME_SEGURO.test(url)) falhar('versao gera nome invalido: ' + url);
  const destino = path.join(SAIDA, url);
  // a published versioned file never changes (the server caches DLLs for long): same name = same bytes
  if (fs.existsSync(destino) && sha256(fs.readFileSync(destino)) !== hash)
    falhar(url + ' ja foi publicado com outro conteudo: suba a versao do mod');
  fs.writeFileSync(destino, dll);
  bytes += dll.length;
  mods.push(Object.assign({}, m, { sha256: hash, url }));
}
for (const id of retirar) mods.push({ id, retirado: true });

const catalogo = { formato: 1, numero, emitido: new Date().toISOString(), mods };
const json = Buffer.from(JSON.stringify(catalogo, null, 2) + '\n', 'utf8');
const privada = crypto.createPrivateKey(fs.readFileSync(CHAVE));
const assinatura = crypto.sign('sha256', json, privada);   // DER, what Java's SHA256withECDSA reads

// check with the public half that goes in the APK, before writing anything final
if (fs.existsSync(PUBLICA)) {
  const publica = crypto.createPublicKey({ key: Buffer.from(fs.readFileSync(PUBLICA, 'utf8').trim(), 'base64'), format: 'der', type: 'spki' });
  if (!crypto.verify('sha256', json, publica, assinatura)) falhar('a assinatura nao confere com ' + PUBLICA);
}
fs.writeFileSync(path.join(SAIDA, 'catalogo.json'), json);
fs.writeFileSync(path.join(SAIDA, 'catalogo.json.sig'), assinatura.toString('base64') + '\n');

// old versioned DLLs stay (phones on the previous catalog may still be downloading them); list what is there
const arquivos = fs.readdirSync(SAIDA);
let total = 0;
for (const f of arquivos) total += fs.statSync(path.join(SAIDA, f)).size;
console.log('Catalogo n ' + numero + ' em ' + SAIDA);
for (const m of mods) console.log('  ' + (m.retirado ? 'RETIRADO ' + m.id : m.name + ' ' + m.version + ' -> ' + m.url));
console.log(arquivos.length + ' arquivos, ' + (total / 1024 / 1024).toFixed(2) + ' MB na pasta (' + (bytes / 1024 / 1024).toFixed(2) + ' MB de DLLs neste catalogo)');
console.log('Pra subir: peca pra sessao CriCri Br, com o "pode" do CriCri, copiar esta pasta pra /mods/ do site.');
