#if AMIGOS_DE_MENTIRA || AMIGOS_SERVIDOR_TESTE || AMIGOS_PUBLICO
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CriCriMods.Servidor
{
    // The friends panel talking to the real server: /api/mod/amigos, pedidos, busca, convites (contract agreed with the CRICRI BR session,
    // docs\CRICRI-LAUNCHER.md; the routes come after the v1). Not in the public build yet: it needs AMIGOS_SERVIDOR_TESTE, which also points
    // at the test server, because a test key is not valid on production.
    //
    // The same rules as the room tab: the key only as Bearer, through SalaApi's client; never in a URL, a log or an error. A nick goes in the
    // body, never in the path. An answer arrives on the game's thread through SalaApi.Respostas.
    internal static class AmigosApi
    {
#if AMIGOS_SERVIDOR_TESTE
        const string Base = "https://cricribr.com.br:22034/api/mod/";
#else
        const string Base = "https://cricribr.com.br/api/mod/";
#endif
        // ids are opaque and per viewer, but they go in the path: only what is safe there
        static readonly Regex IdValido = new Regex("^[A-Za-z0-9_-]{1,128}$", RegexOptions.CultureInvariant);

        internal sealed class Resposta
        {
            public int Codigo;              // HTTP status; 0 = no connection, or refused here (no key, waiting after a 429, bad id)
            public string Etag;
            public int ProximaEmSeg = 10;   // the server paces the polling; 10 s until it says otherwise
            public string Corpo;            // 200 only
            public string Motivo;           // 409 only: why not ({"motivo":"sala"...})
        }

        internal sealed class Selo { public string Nivel, Bola, V; }

        internal sealed class Pessoa
        {
            public string Id, Nome = "", Estado = "offline", Situacao = "nada";
            public bool PodeEntrar, PodeConvidar, PodePedir;
            public Selo Selo;
            public int? Nivel;
            public int Cor = -1;
            public string Chapeu = "", Roupa = "", Visor = "", Pet = "", Banner = "";
        }

        internal sealed class Lista
        {
            public readonly List<Pessoa> Pessoas = new List<Pessoa>();
            public int Online, Pedidos;
        }

        internal sealed class Convite { public string Id, De = ""; public int ExpiraEmSeg = 60; }

        // ---------------- the routes ----------------

        public static void Amigos(string etag, Action<Resposta> depois) => Get("amigos", etag, depois);
        public static void Eu(Action<Resposta> depois) => Get("eu", null, depois);
        public static void Pedidos(string etag, Action<Resposta> depois) => Get("amigos/pedidos", etag, depois);
        public static void Bloqueados(string etag, Action<Resposta> depois) => Get("amigos/bloqueados", etag, depois);
        public static void Convites(string etag, Action<Resposta> depois) => Get("convites", etag, depois);

        public static void Buscar(string termo, Action<Resposta> depois) => Post("amigos/busca", Json("termo", termo), depois);
        public static void PedirPorCodigo(string codigo, Action<Resposta> depois) => Post("amigos/pedir", Json("codigo", codigo), depois);

        public static void Pedir(string id, Action<Resposta> depois) => ComId("amigos/", id, "/pedir", depois);
        public static void Remover(string id, Action<Resposta> depois) => ComId("amigos/", id, "/remover", depois);
        public static void Bloquear(string id, Action<Resposta> depois) => ComId("amigos/", id, "/bloquear", depois);
        public static void Convidar(string id, Action<Resposta> depois) => ComId("amigos/", id, "/convite", depois);
        public static void Entrar(string id, Action<Resposta> depois) => ComId("amigos/", id, "/entrar", depois);
        public static void AceitarPedido(string id, Action<Resposta> depois) => ComId("amigos/pedidos/", id, "/aceitar", depois);
        public static void RecusarPedido(string id, Action<Resposta> depois) => ComId("amigos/pedidos/", id, "/recusar", depois);
        public static void Desbloquear(string id, Action<Resposta> depois) => ComId("amigos/bloqueados/", id, "/desbloquear", depois);
        public static void AceitarConvite(string id, Action<Resposta> depois) => ComId("convites/", id, "/aceitar", depois);
        public static void RecusarConvite(string id, Action<Resposta> depois) => ComId("convites/", id, "/recusar", depois);

        static void ComId(string antes, string id, string depoisDoId, Action<Resposta> depois)
        {
            if (id == null || !IdValido.IsMatch(id)) { SalaApi.Respostas.Enqueue(() => depois(new Resposta())); return; }
            Post(antes + id + depoisDoId, "{}", depois);
        }

        static string Json(string campo, string valor) =>
            JsonSerializer.Serialize(new Dictionary<string, string> { [campo] = valor ?? "" });

        static void Get(string caminho, string etag, Action<Resposta> depois) => Mandar(HttpMethod.Get, caminho, null, etag, depois);
        static void Post(string caminho, string json, Action<Resposta> depois) => Mandar(HttpMethod.Post, caminho, json, null, depois);

        static void Mandar(HttpMethod metodo, string caminho, string json, string etag, Action<Resposta> depois)
        {
            var http = SalaApi.ClienteSeTiver();
            if (http == null || SalaApi.Esperando) { SalaApi.Respostas.Enqueue(() => depois(new Resposta())); return; }
            string usada = SalaApi.ChaveDeAgora;
            Task.Run(async () =>
            {
                var l = new Resposta();
                try
                {
                    using var pedido = new HttpRequestMessage(metodo, Base + caminho);
                    if (!string.IsNullOrEmpty(etag)) pedido.Headers.TryAddWithoutValidation("If-None-Match", etag);
                    if (json != null) pedido.Content = new StringContent(json, Encoding.UTF8, "application/json");
                    using var r = await http.SendAsync(pedido).ConfigureAwait(false);
                    l.Codigo = (int)r.StatusCode;
                    l.Etag = r.Headers.ETag?.ToString();
                    SalaApi.EsperarSePedir(r);
                    if (l.Codigo == 200)
                    {
                        l.Corpo = await r.Content.ReadAsStringAsync().ConfigureAwait(false);
                        l.ProximaEmSeg = Proxima(l.Corpo);
                    }
                    else if (l.Codigo == 409)
                    {
                        l.Motivo = LerMotivo(await r.Content.ReadAsStringAsync().ConfigureAwait(false));
                    }
                }
                catch { l.Codigo = 0; }
                SalaApi.Respostas.Enqueue(() => { SalaApi.Recusada(l.Codigo, usada); depois(l); });
            });
        }

        internal static string LerMotivo(string corpo)
        {
            try
            {
                using var d = JsonDocument.Parse(corpo);
                return Texto(d.RootElement, "motivo");
            }
            catch { return ""; }
        }

        static int Proxima(string corpo)
        {
            try
            {
                using var d = JsonDocument.Parse(corpo);
                if (d.RootElement.ValueKind == JsonValueKind.Object && d.RootElement.TryGetProperty("proximaEmSeg", out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out int s))
                    return Math.Max(5, Math.Min(s, 600));
            }
            catch { }
            return 10;
        }

        // ---------------- reading the answers ----------------

        // `campo` is the array's name: "amigos", "pedidos", "resultados" or "bloqueados". A person missing an id is dropped: nothing could be done with it.
        internal static Lista LerLista(string corpo, string campo)
        {
            var l = new Lista();
            try
            {
                using var d = JsonDocument.Parse(corpo);
                var raiz = d.RootElement;
                if (raiz.ValueKind != JsonValueKind.Object) return l;
                l.Online = Numero(raiz, "online");
                l.Pedidos = Numero(raiz, "pedidos");
                if (raiz.TryGetProperty(campo, out var a) && a.ValueKind == JsonValueKind.Array)
                    foreach (var p in a.EnumerateArray())
                    {
                        var pessoa = LerPessoa(p);
                        if (pessoa != null) l.Pessoas.Add(pessoa);
                    }
            }
            catch { }
            return l;
        }

        static Pessoa LerPessoa(JsonElement o)
        {
            if (o.ValueKind != JsonValueKind.Object) return null;
            string id = Texto(o, "id");
            if (!IdValido.IsMatch(id)) return null;
            var p = new Pessoa
            {
                Id = id,
                Nome = Texto(o, "nome"),
                Estado = TextoOu(o, "estado", "offline"),
                Situacao = TextoOu(o, "situacao", "nada"),
                PodeEntrar = Sim(o, "podeEntrar"),
                PodeConvidar = Sim(o, "podeConvidar"),
                PodePedir = Sim(o, "podePedir"),
                Chapeu = SemVazio(Texto(o, "chapeu"), "NoHat"),
                Roupa = SemVazio(Texto(o, "roupa"), "skin_None"),
                Visor = SemVazio(Texto(o, "visor"), "EmptyVisor"),
                Pet = SemVazio(Texto(o, "pet"), "EmptyPet"),
                Banner = SemVazio(Texto(o, "banner"), "NoPlate"),
                Cor = o.TryGetProperty("cor", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out int cor) ? cor : -1,
            };
            if (o.TryGetProperty("nivel", out var n) && n.ValueKind == JsonValueKind.Number && n.TryGetInt32(out int nivel)) p.Nivel = nivel;
            if (o.TryGetProperty("selo", out var s) && s.ValueKind == JsonValueKind.Object)
                p.Selo = new Selo { Nivel = TextoOu(s, "nivel", "verificado"), Bola = Texto(s, "bola"), V = Texto(s, "v") };
            return p;
        }

        internal static List<Convite> LerConvites(string corpo)
        {
            var l = new List<Convite>();
            try
            {
                using var d = JsonDocument.Parse(corpo);
                if (d.RootElement.ValueKind != JsonValueKind.Object || !d.RootElement.TryGetProperty("convites", out var a) || a.ValueKind != JsonValueKind.Array) return l;
                foreach (var c in a.EnumerateArray())
                {
                    string id = Texto(c, "id");
                    if (!IdValido.IsMatch(id)) continue;
                    l.Add(new Convite { Id = id, De = Texto(c, "de"), ExpiraEmSeg = c.TryGetProperty("expiraEmSeg", out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out int s) ? s : 60 });
                }
            }
            catch { }
            return l;
        }

        // "" or the game's own empty id both mean nothing; the game's ids are kept exactly as they came
        static string SemVazio(string id, string vazio) => id.IndexOf(vazio, StringComparison.OrdinalIgnoreCase) >= 0 ? "" : id;

        static string Texto(JsonElement o, string nome) =>
            o.ValueKind == JsonValueKind.Object && o.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

        static string TextoOu(JsonElement o, string nome, string padrao)
        {
            string t = Texto(o, nome);
            return t.Length > 0 ? t : padrao;
        }

        static bool Sim(JsonElement o, string nome) => o.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.True;
        static int Numero(JsonElement o, string nome) => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) ? n : 0;

        internal static string Aviso(Resposta r) => r.Codigo == 409 ? AvisoDoMotivo(r.Motivo) : Aviso(r.Codigo);

        // The server's reasons for a 409; one it adds later reads as "not now".
        internal static string AvisoDoMotivo(string motivo) => motivo switch
        {
            "sala" => "A sala não está mais disponível.",
            "mesma-sala" => "Vocês já estão na mesma sala.",
            "em-partida" => "Não dá durante uma partida.",
            "fora-do-lobby" => "Entre num lobby do CRICRI BR para convidar.",
            "so-o-host" => "Só o dono da sala pode convidar.",
            "gravacao" => "No modo gravação, entre primeiro num lobby do CRICRI BR.",
            "voce-bloqueou" => "Desbloqueie essa pessoa antes.",
            "cheio" => "Sua lista está cheia.",
            "voce-mesmo" => "Esse é o seu próprio código.",
            _ => "Não deu agora.",
        };

        // What the player reads for an answer that was not a success. 403 stays neutral: never says whether it was a block or a refusal.
        internal static string Aviso(int codigo) => codigo switch
        {
            0 => "Sem conexão com o servidor CRICRI BR agora.",
            400 => "Digite de 2 a 10 letras.",
            401 => "Sua conta foi ligada em outro aparelho. Entre numa sala do CRICRI BR para ligar neste.",
            403 => "Não foi possível fazer isso.",
            404 => "Não encontrei esse código.",
            409 => "Não deu agora.",
            410 => "Isso não está mais disponível.",
            429 => "Muitos pedidos seguidos. Espere um pouco.",
            _ => "O servidor CRICRI BR não respondeu direito. Tente de novo.",
        };
    }
}
#endif
