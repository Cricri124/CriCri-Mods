using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CriCriMods.Servidor
{
    // The CRICRI BR room options over https://cricribr.com.br/api/mod/* (agreed with the CRICRI BR session on
    // 08/11/2026; the routes go live with the contract after the server's v1 of 09/11):
    //   GET /api/mod/sala/atual       → {"sala":null} or {"versao","codigo","souHost","opcoes":[...],"valores":{...}}
    //                                   (ETag tied to "versao"; If-None-Match → 304)
    //   PUT /api/mod/sala/atual       {"valores":{...}}  the room this account hosts now (409 = not hosting)
    //   PUT /api/mod/sala/predefinicao {"valores":{...}} the next rooms this account creates
    // Rules: the account's mod key (pasted in the app, handed over by environment variable, read once and cleared)
    // goes only as "Authorization: Bearer" to cricribr.com.br; never in a URL, a log or an error. No browser-like
    // headers. 401 = key refused, 429 = wait Retry-After, 403 = refused. Nothing here sends game packets.
    internal static class SalaApi
    {
        const string Variavel = "CRICRI_BR_CHAVE";
        const string Base = "https://cricribr.com.br/api/mod/sala/";

        static string _chave;
        static HttpClient _http;
        static DateTime _naoAntes = DateTime.MinValue;
        static bool _ocupado;
        internal static readonly ConcurrentQueue<Action> Respostas = new ConcurrentQueue<Action>();

        [DllImport("libc", EntryPoint = "unsetenv")]
        static extern int unsetenv(string nome);

        // At load: the key goes to this plugin's memory and leaves the process environment (other mods share it).
        internal static void PegarChave()
        {
            try
            {
                string k = Environment.GetEnvironmentVariable(Variavel);
                Environment.SetEnvironmentVariable(Variavel, null);
                try { unsetenv(Variavel); } catch { }
                _chave = string.IsNullOrWhiteSpace(k) ? null : k.Trim();
            }
            catch { _chave = null; }
        }

        internal static bool TemChave => _chave != null;
        internal static bool Ocupado => _ocupado;

        static readonly object _travaCliente = new object();

#if CRICRI_PC
        // PC: the account screen (PcConta.cs) changes the key while the game runs, and the client keeps the old one in its headers.
        internal static void TrocarChave(string k) { lock (_travaCliente) { _chave = k; _http?.Dispose(); _http = null; } }
#endif

        static HttpClient Cliente()
        {
            lock (_travaCliente)
            {
                if (_http != null) return _http;
                // 60 s: a whole match film can take a while on a phone connection (the room calls answer in milliseconds)
                var h = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
                h.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _chave);
                h.DefaultRequestHeaders.UserAgent.ParseAdd("CriCriMods-Servidor/" + Plugin.Versao);
                _http = h;
                return h;
            }
        }

        // For ApiDoMod (the other CriCri mods): the client only when there is a key.
        internal static HttpClient ClienteSeTiver() => _chave == null ? null : Cliente();
        internal static bool Esperando => DateTime.UtcNow < _naoAntes;
        internal static void EsperarSePedir(HttpResponseMessage r) => Esperar(r);

        internal sealed class Leitura
        {
            public int Codigo;                 // HTTP status (0 = no connection)
            public string Etag;
            public JsonDocument Corpo;         // null on 304 and on errors
        }

        // GET the room the account is in; the answer arrives on the main thread through Respostas.
        internal static void Ler(string etag, Action<Leitura> depois)
        {
            if (_chave == null || _ocupado || DateTime.UtcNow < _naoAntes) return;
            _ocupado = true;
            Task.Run(async () =>
            {
                var l = new Leitura();
                try
                {
                    using var pedido = new HttpRequestMessage(HttpMethod.Get, Base + "atual");
                    if (!string.IsNullOrEmpty(etag)) pedido.Headers.TryAddWithoutValidation("If-None-Match", etag);
                    using var r = await Cliente().SendAsync(pedido).ConfigureAwait(false);
                    l.Codigo = (int)r.StatusCode;
                    l.Etag = r.Headers.ETag?.ToString();
                    Esperar(r);
                    if (r.StatusCode == HttpStatusCode.OK)
                        l.Corpo = JsonDocument.Parse(await r.Content.ReadAsStringAsync().ConfigureAwait(false));
                }
                catch { l.Codigo = 0; }
                Respostas.Enqueue(() => { _ocupado = false; depois(l); });
            });
        }

        // PUT the values to the current room (agora) or to the account's next rooms (padrao).
        internal static void Gravar(bool padrao, Dictionary<string, object> valores, Action<int> depois)
        {
            if (_chave == null || DateTime.UtcNow < _naoAntes) { depois(429); return; }
            string json = JsonSerializer.Serialize(new Dictionary<string, object> { ["valores"] = valores });
            Task.Run(async () =>
            {
                int codigo;
                try
                {
                    using var corpo = new StringContent(json, Encoding.UTF8, "application/json");
                    using var r = await Cliente().PutAsync(Base + (padrao ? "predefinicao" : "atual"), corpo).ConfigureAwait(false);
                    codigo = (int)r.StatusCode;
                    Esperar(r);
                }
                catch { codigo = 0; }
                Respostas.Enqueue(() => depois(codigo));
            });
        }

        static void Esperar(HttpResponseMessage r)
        {
            if ((int)r.StatusCode != 429) return;
            var depois = r.Headers.RetryAfter;
            TimeSpan t = depois?.Delta ?? (depois?.Date != null ? depois.Date.Value - DateTimeOffset.UtcNow : TimeSpan.FromSeconds(60));
            _naoAntes = DateTime.UtcNow + (t < TimeSpan.FromSeconds(5) ? TimeSpan.FromSeconds(5) : t);
        }

        // The words for a status, as the player reads them in the tab.
        internal static string Aviso(int codigo) => codigo switch
        {
#if CRICRI_PC
            401 => "A chave do CRICRI BR foi recusada. Gere uma nova em cricribr.com.br (Minha conta) e cole no botão CONTA BR do menu.",
#else
            401 => "A chave do CRICRI BR foi recusada. Gere uma nova em cricribr.com.br → Minha conta e cole no app.",
#endif
            403 => "O servidor CRICRI BR recusou o pedido.",
            409 => "Não deu pra mudar agora: só o dono, e só no lobby antes da partida.",
            429 => "Muitos pedidos seguidos. Espere um minuto.",
            0 => "Sem conexão com o servidor CRICRI BR agora.",
            _ => "As opções da sala chegam com o servidor CRICRI BR.",
        };
    }
}
