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
#if AMIGOS_SERVIDOR_TESTE
        internal const string Site = "https://cricribr.com.br:22034";
#else
        internal const string Site = "https://cricribr.com.br";
#endif
        const string Base = Site + "/api/mod/sala/";

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
        internal static string ChaveDeAgora => _chave;

        // 401 on the phone: the key stopped working (the account was linked somewhere else). Without it the next lobby offers to link this device, with no site.
        // Only the key that was refused: a new one may have arrived while the answer travelled. The saved copy is replaced when the link succeeds.
        internal static void Recusada(int codigo, string usada)
        {
#if !CRICRI_PC
            if (codigo == 401 && usada != null && usada == _chave) TrocarChave(null);
#else
            if (usada == null || usada != _chave) return;
            if (codigo == 401) { _recusada = true; _recusadaAte = DateTime.UtcNow.AddMinutes(5); }
            else if (codigo >= 200 && codigo < 400) { _recusada = false; _recusadaAte = DateTime.MinValue; }
#endif
        }

        // (11/11, achado no teste do PC) No PC a chave recusada (401) continua guardada, para a pessoa ver o aviso e trocar. Só que
        // ninguém avisava: a tela CONTA BR seguia dizendo "guardada", o painel de amigos virava o do jogo sem explicar, e o mod
        // seguia perguntando ao servidor com a chave morta o jogo inteiro (convites a cada 2 min, a aba a cada 5 a 30 s, o painel a
        // cada minuto). O servidor conta chave errada POR REDE e, passando do limite, responde 429 para a casa toda, o celular
        // tentando ligar a conta incluído. Agora: a recusa fica marcada (a tela CONTA BR mostra, ver PcConta.cs) e, por 5 minutos
        // depois de cada 401, nenhum pedido sai com essa chave: quem pede recebe o mesmo 401 na hora, sem rede. Passados os 5
        // minutos um pedido vai de verdade, para o caso de a chave ter voltado a valer (termos aceitos no site, conta liberada).
        // Trocar a chave (COLAR, APAGAR) zera tudo. No celular nada disso existe: lá a chave recusada é largada na hora.
#if CRICRI_PC
        static bool _recusada;
        static DateTime _recusadaAte = DateTime.MinValue;
        internal static bool ChaveRecusada => _recusada && _chave != null;
        internal static bool PausaDaRecusa => _recusada && _chave != null && DateTime.UtcNow < _recusadaAte;
#else
        internal const bool ChaveRecusada = false, PausaDaRecusa = false;
#endif
        internal static bool Ocupado => _ocupado;

        static readonly object _travaCliente = new object();

        // PC: the account screen (PcConta.cs) changes the key while the game runs; phone: the key arrives with the verification. The client keeps the old one in its headers.
        // (11/11, revisão) Sem o "#if" que ficava em volta: o Recusada (logo acima) chama isto em todo build de celular, e o build de
        // pessoas de mentira (AMIGOS_DE_MENTIRA, o do "mods-de-teste.js mentira") não compilava mais: "TrocarChave não existe".
        internal static void TrocarChave(string k)
        {
            lock (_travaCliente) { _chave = k; _http?.Dispose(); _http = null; }
#if CRICRI_PC
            _recusada = false;
            _recusadaAte = DateTime.MinValue;
#endif
        }

        // (11/11, revisão: "vulnerabilidades") A mesma regra do app (ChaveBr.limpar) e do PC (PcCofre.Limpar): 8 a 64 caracteres
        // ASCII visíveis, sem espaço. Uma chave que chega do servidor passa por aqui ANTES de virar cabeçalho: com quebra de linha
        // ou espaço o cabeçalho nem monta, e o mod ficava dizendo "sem conexão" a sessão inteira sem ninguém saber por quê.
        internal static bool ChaveValida(string k)
        {
            if (k == null || k.Length < 8 || k.Length > 64) return false;
            foreach (char c in k) if (c <= ' ' || c > '~') return false;
            return true;
        }

        // (11/11, revisão) Tetos do que o mod aceita ler de uma resposta montada na memória. As respostas de verdade têm poucos KB;
        // o filme da partida não passa por aqui (o ApiDoMod lê em fluxo, com o teto de quem chamou).
        // (11/11, revisão) 4 MB: o servidor devolve TODOS os pedidos de amizade de uma vez, e uma conta de criador com milhares
        // de pedidos passava de 1 MB (a aba Pedidos ficava em "Sem conexão" para sempre). O mod continua desenhando só os primeiros.
        const long TetoDaResposta = 4L * 1024 * 1024;
        const long TetoSemChave = 64 * 1024;

        // (11/11, revisão) O mod só fala com o cricribr.com.br: não segue redirecionamento. O cabeçalho da chave já não seguia
        // (o .NET tira), mas um 307/308 reenviaria o CORPO do pedido (o id secreto do "ligar") para onde o redirecionamento mandasse.
        // Nenhuma rota de /api/mod/* redireciona (conferido no fonte do servidor); se um dia vier um 30x, ele vira erro e não passeio.
        static HttpClient Novo(int segundos, long teto)
        {
            var h = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                Timeout = TimeSpan.FromSeconds(segundos),
                MaxResponseContentBufferSize = teto,
            };
            h.DefaultRequestHeaders.UserAgent.ParseAdd("CriCriMods-Servidor/" + Plugin.Versao);
            return h;
        }

        // Para as chamadas SEM chave (ligar a conta): um cliente por chamada, descartado por quem pediu.
        internal static HttpClient ClienteSemChave() => Novo(20, TetoSemChave);

        static HttpClient Cliente()
        {
            lock (_travaCliente)
            {
                if (_http != null) return _http;
                // 60 s: a whole match film can take a while on a phone connection (the room calls answer in milliseconds)
                var h = Novo(60, TetoDaResposta);
                h.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _chave);
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
#pragma warning disable CS0162   // no celular PausaDaRecusa é a constante "false"
            if (PausaDaRecusa) { Respostas.Enqueue(() => depois(new Leitura { Codigo = 401 })); return; }
#pragma warning restore CS0162
            _ocupado = true;
            string usada = _chave;
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
                Respostas.Enqueue(() => { _ocupado = false; Recusada(l.Codigo, usada); depois(l); });
            });
        }

        // PUT the values to the current room (agora) or to the account's next rooms (padrao).
        internal static void Gravar(bool padrao, Dictionary<string, object> valores, Action<int, string> depois)
        {
            if (_chave == null || DateTime.UtcNow < _naoAntes) { depois(429, null); return; }
#pragma warning disable CS0162
            if (PausaDaRecusa) { depois(401, null); return; }
#pragma warning restore CS0162
            string usada = _chave;
            Task.Run(async () =>
            {
                int codigo;
                string resposta = null;
                try
                {
                    // (11/11, "tão falando que trava") O JSON é montado AQUI, fora da linha do jogo: a primeira vez que o
                    // System.Text.Json roda num celular custa dezenas de milissegundos (ele se prepara por reflexão), e isso caía
                    // no meio do lobby, 0,6 s depois do primeiro toque numa opção. O dicionário é novo a cada gravação e ninguém
                    // mexe nele depois de entregar, então ler daqui é seguro.
                    string json = JsonSerializer.Serialize(new Dictionary<string, object> { ["valores"] = valores });
                    using var corpo = new StringContent(json, Encoding.UTF8, "application/json");
                    using var r = await Cliente().PutAsync(Base + (padrao ? "predefinicao" : "atual"), corpo).ConfigureAwait(false);
                    codigo = (int)r.StatusCode;
                    Esperar(r);
                    if (codigo == 200) resposta = await r.Content.ReadAsStringAsync().ConfigureAwait(false);   // "recusas": what did not go through
                }
                catch { codigo = 0; }
                Respostas.Enqueue(() => { Recusada(codigo, usada); depois(codigo, resposta); });
            });
        }

        static void Esperar(HttpResponseMessage r)
        {
            if ((int)r.StatusCode != 429) return;
            var depois = r.Headers.RetryAfter;
            TimeSpan t = depois?.Delta ?? (depois?.Date != null ? depois.Date.Value - DateTimeOffset.UtcNow : TimeSpan.FromSeconds(60));
            // (11/11, revisão) Entre 5 s e 10 min: um Retry-After torto (data errada, número enorme) deixava o mod mudo até fechar o jogo.
            if (t < TimeSpan.FromSeconds(5)) t = TimeSpan.FromSeconds(5);
            if (t > TimeSpan.FromMinutes(10)) t = TimeSpan.FromMinutes(10);
            _naoAntes = DateTime.UtcNow + t;
        }

        // The words for a status, as the player reads them in the tab.
        // (11/11, ele: "menos texto") Frases curtas. O 401 do celular não afirma mais "foi ligada em outro aparelho": desde a chave
        // por aparelho (11/11) o 401 também é termos vencidos ou conta suspensa, e a frase antiga dizia uma coisa que podia não ser.
        internal static string Aviso(int codigo) => codigo switch
        {
#if CRICRI_PC
            401 => "Chave recusada. Gere outra em cricribr.com.br (Minha conta) e cole no CONTA BR do menu.",
#else
            401 => "Conta desligada neste aparelho. Entre numa sala do CRICRI BR para ligar de novo.",
#endif
            403 => "Só o dono da sala muda isso.",
            409 => "Só dá para mudar no lobby.",
            429 => "Muitos pedidos. Espere um minuto.",
            0 => "Sem conexão com o CRICRI BR.",
            _ => "O CRICRI BR não respondeu. Tente de novo.",
        };
    }
}
