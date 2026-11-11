#if (AMIGOS_SERVIDOR_TESTE || AMIGOS_PUBLICO) && !CRICRI_PC
using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace CriCriMods.Servidor
{
    // CriCri, 08/11: an account verifying through the game with the mod gets the key by itself. When the server's verification pop-up is on screen and
    // there is no key yet, this asks the site for a one-time "ligar" request and adds it to the pop-up's link (only the link, not the text shown).
    // On the site the terms form then shows a pre-ticked "Ligar o CriCri Mods a esta conta"; whoever unticks it just verifies. Same flow as the app's
    // Conectar button: the secret id stays here and only travels in a POST body. Nothing goes over the game's connection, and nothing here is logged
    // but the outcome. Polling runs off the main thread, so it keeps going while the player is in the browser.
    internal sealed class LigarNaVerificacao : MonoBehaviour
    {
        public LigarNaVerificacao(IntPtr ptr) : base(ptr) { }

        // Only the CRICRI BR site: another server's pop-up never receives a request code.
        static readonly Regex LinkDoV = new Regex(@"<link=(https://cricribr\.com\.br(?::\d+)?/v/[A-Za-z0-9_\-]{4,64})>", RegexOptions.CultureInvariant);
        static readonly Regex CodigoDaUrl = new Regex(@"[?&]c=([A-Za-z0-9_\-]{4,64})$", RegexOptions.CultureInvariant);

        readonly ConcurrentQueue<Action> _fila = new ConcurrentQueue<Action>();
        float _prox;
        int _vez;
        string _linkTentado, _codigoDoLigar;

        void Update()
        {
            try
            {
                while (_fila.TryDequeue(out var acao)) acao();
                if (Time.unscaledTime < _prox) return;
                _prox = Time.unscaledTime + 0.3f;
                OlharPopup();
            }
            catch (Exception e) { Plugin.Registro.LogWarning("[ligar] " + e.GetType().Name); _prox = Time.unscaledTime + 5f; }
        }

        void OlharPopup()
        {
            if (SalaApi.TemChave || !DestroyableSingleton<DisconnectPopup>.InstanceExists) return;
            var popup = DestroyableSingleton<DisconnectPopup>.Instance;
            if (!popup.gameObject.activeInHierarchy || popup._textArea == null) return;
            string texto = popup._textArea.text;
            if (string.IsNullOrEmpty(texto) || texto.Contains("?ligar=")) return;
            var achado = LinkDoV.Match(texto);
            if (!achado.Success) return;
            string link = achado.Groups[1].Value;
            if (link == _linkTentado)
            {
                // the same pop-up shown again comes with the original text
                if (_codigoDoLigar != null) TrocarLink(popup, link, _codigoDoLigar);
                return;
            }
            _linkTentado = link;
            _codigoDoLigar = null;
            // the request is created on this build's site, so it only fits a link of that same site (only the address goes to the log, never the code)
            string origem = link.Substring(0, link.IndexOf("/v/", StringComparison.Ordinal));
            Plugin.Registro.LogInfo("[ligar] verificacao em " + origem + " (este mod fala com " + SalaApi.Site + ")");
            if (origem != SalaApi.Site) return;
            int minha = ++_vez;   // a new link replaces a request still waiting
            Task.Run(() => Iniciar(popup, link, minha));
        }

        static StringContent Json(string corpo) => new StringContent(corpo, Encoding.UTF8, "application/json");

        async Task Iniciar(DisconnectPopup popup, string link, int minha)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("CriCriMods-Servidor/" + Plugin.Versao);
                using var r = await http.PostAsync(SalaApi.Site + "/api/mod/ligar/iniciar", Json("")).ConfigureAwait(false);
                if ((int)r.StatusCode != 200) { Fim("o site nao liga agora: " + (int)r.StatusCode); return; }
                using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync().ConfigureAwait(false));
                var raiz = doc.RootElement;
                string id = raiz.GetProperty("id").GetString(), url = raiz.GetProperty("url").GetString();
                var codigo = CodigoDaUrl.Match(url ?? "");
                if (id == null || id.Length < 32 || !url.StartsWith(SalaApi.Site + "/conta/ligar?") || !codigo.Success) { Fim("resposta inesperada"); return; }
                int expira = Math.Clamp(raiz.TryGetProperty("expiraEmSeg", out var e) && e.TryGetInt32(out int x) ? x : 600, 30, 900);
                int intervalo = Math.Clamp(raiz.TryGetProperty("intervaloSeg", out var i) && i.TryGetInt32(out int y) ? y : 3, 2, 30);
                string c = codigo.Groups[1].Value;
                _fila.Enqueue(() => { if (minha == _vez) { _codigoDoLigar = c; TrocarLink(popup, link, c); } });
                await Aguardar(http, id, expira, intervalo, minha).ConfigureAwait(false);
            }
            catch (Exception e) { Fim(e.GetType().Name); }
        }

        void TrocarLink(DisconnectPopup popup, string link, string c)
        {
            if (popup == null || popup._textArea == null) return;
            string antes = "<link=" + link + ">", texto = popup._textArea.text;
            if (texto != null && texto.Contains(antes)) popup._textArea.text = texto.Replace(antes, "<link=" + link + "?ligar=" + c + ">");
        }

        async Task Aguardar(HttpClient http, string id, int expiraSeg, int intervaloSeg, int minha)
        {
            string pedido = JsonSerializer.Serialize(new { id });
            var fim = DateTime.UtcNow.AddSeconds(expiraSeg);
            int espera = intervaloSeg;
            while (DateTime.UtcNow < fim)
            {
                await Task.Delay(espera * 1000).ConfigureAwait(false);
                if (minha != _vez) return;
                espera = intervaloSeg;
                HttpResponseMessage r;
                try { r = await http.PostAsync(SalaApi.Site + "/api/mod/ligar/resultado", Json(pedido)).ConfigureAwait(false); }
                catch (Exception) { continue; }   // the phone changing apps or networks: keep trying until it expires
                using (r)
                {
                    int status = (int)r.StatusCode;
                    if (status == 200)
                    {
                        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync().ConfigureAwait(false));
                        string chave = doc.RootElement.GetProperty("chave").GetString();
                        _fila.Enqueue(() => Receber(chave));
                        return;
                    }
                    if (status == 403 || status == 410) { Fim("nao ligou: " + status); return; }
                    if (status == 429) espera = Math.Clamp((int)(r.Headers.RetryAfter?.Delta?.TotalSeconds ?? 30), 5, 300);
                }
            }
            Fim("venceu");
        }

        void Fim(string motivo)
        {
            _fila.Enqueue(() => Plugin.Registro.LogInfo("[ligar] " + motivo));
        }

        void Receber(string chave)
        {
            _codigoDoLigar = null;
            Guardar(chave);
        }

        // POST without the key: {status, body}. Also for the in-game way of linking (AmigosCricriBr).
        internal static async Task<(int status, string corpo)> Postar(string rota, string json)
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("CriCriMods-Servidor/" + Plugin.Versao);
            using var r = await http.PostAsync(SalaApi.Site + rota, Json(json)).ConfigureAwait(false);
            int status = (int)r.StatusCode;
            return (status, status == 200 ? await r.Content.ReadAsStringAsync().ConfigureAwait(false) : null);
        }

        // Main thread only: starts using the key now and keeps it for the next launches.
        internal static bool Guardar(string chave)
        {
            SalaApi.TrocarChave(chave);
            try
            {
                var classe = new AndroidJavaClass("dev.allofus.fusioncore.tools.ChaveBr");
                var argumentos = new Il2CppReferenceArray<Il2CppSystem.Object>(1);
                argumentos[0] = new Il2CppSystem.Object(IL2CPP.ManagedStringToIl2Cpp(chave));
                classe.CallStatic("salvarDoJogo", argumentos);
                Plugin.Registro.LogInfo("[ligar] conta ligada");
                return true;
            }
            catch (Exception e) { Plugin.Registro.LogWarning("[ligar] a chave vale nesta sessao, mas nao foi guardada: " + e.GetType().Name); return false; }
        }
    }
}
#endif
