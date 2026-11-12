using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using BepInEx;

namespace CriCriMods
{
    // The player's choices, now made INSIDE the game (Options > "CriCri" tab) and saved by this plugin in
    // BepInEx/config/cricri-mods-otimizacoes.json. The app no longer writes this file; it only reads FPS from it to
    // ask Android for the matching screen refresh rate.
    public sealed class Opcoes
    {
        // Master switch, to compare: off = the game's own frame rate, resolution and textures (the FPS counter stays).
        public bool Ligada { get; set; } = true;
        // 0 = the game's own value; otherwise frames per second (30, 60, 90, 120)
        public int Fps { get; set; } = -1;
        // 0 = the game's own value; otherwise percent of the screen's native resolution (50, 75, 100)
        public int Resolucao { get; set; } = -1;
        public bool ContadorFps { get; set; }
        // Interpolate the players' Rigidbody2D so movement doesn't judder between physics steps at high frame rates
        public bool MovimentoSuave { get; set; } = true;
        // MSAA samples: 0, 2 or 4
        public int AntiSerrilhado { get; set; }
        // Full-resolution textures and anisotropic filtering
        public bool TexturasMaximas { get; set; } = true;
        // Colorblind mode: paint the color name under each player with that player's color
        public bool DaltonicoCorNoNome { get; set; } = true;
        // "Modo flash" (Flash.cs): no fixed wait in the opening screen, and loading gets more time per frame
        // while nobody is playing a round
        public bool ModoFlash { get; set; } = true;
        /* (11/11, ele: "menos texto, menos complexidade, mais facilidade") Um nível só no lugar de mexer opção por
           opção: 1 = Bonito, 2 = Equilibrado, 3 = Rápido (-1 = ainda não escolhido). O botão do nível escreve o
           tamanho da tela e as texturas de uma vez (PorNivel); o resto continua em "Mais opções". */
        public int Nivel { get; set; } = -1;
        /* (11/11, ele: "várias pessoas andando; animações do mapa, como a dos motores, reator") Leve.cs: animação e
           partícula que ninguém está vendo não trabalham. Ligado em todos os níveis. Sem botão (na tela nada muda, e
           "Otimização: Não" já desliga); a chave fica no arquivo para o suporte comparar. */
        public bool ForaDaTela { get; set; } = true;
        /* (11/11) Sombra.cs: pede ao jogo a sombra que ele usa no PC (placa de vídeo) no lugar da de raios do Android.
           TESTE, desligado por padrão: não foi visto em celular nenhum. */
        public bool SombraDoPc { get; set; } = false;
        /* (12/11, ele: "faça o mostrar fps ... lá na parte de baixo aonde fica o Ping") A barra do ping (BarraDoPing): 0 só o
           ping, 1 ping e FPS, 2 ping, FPS e servidor, 3 escondido. -1 = ainda não escolhido: quem tinha o contador de FPS ligado
           começa com o FPS na barra, quem não tinha começa só com o ping (com o wifi). Substitui o "Contador de FPS". */
        public int BarraPing { get; set; } = -1;

        public const int Bonito = 1, Equilibrado = 2, Rapido = 3;

        // What is actually applied (the master switch off means "the game's own" for all of it).
        [JsonIgnore] public int FpsEfetivo => Ligada ? Fps : 0;
        [JsonIgnore] public int ResolucaoEfetiva => Ligada ? Resolucao : 0;
        [JsonIgnore] public bool SuaveEfetivo => Ligada && MovimentoSuave;
        [JsonIgnore] public int AaEfetivo => Ligada ? AntiSerrilhado : 0;
        [JsonIgnore] public bool TexturasEfetivas => Ligada && TexturasMaximas;
        [JsonIgnore] public bool FlashEfetivo => Ligada && ModoFlash;
        [JsonIgnore] public bool ForaDaTelaEfetivo => Ligada && ForaDaTela;
        [JsonIgnore] public bool SombraDoPcEfetiva => Ligada && SombraDoPc;

        public static string Arquivo => Path.Combine(Paths.ConfigPath, "cricri-mods-otimizacoes.json");
        // Written by the app before each launch: what the screen can do (refresh rates).
        public static string ArquivoTela => Path.Combine(Paths.ConfigPath, "cricri-mods-tela.json");

        static Opcoes _atual;
        public static Opcoes Atual => _atual ??= Ler();

        static Opcoes Ler()
        {
            Opcoes o = null;
            bool existia = false;
            try
            {
                if (File.Exists(Arquivo))
                {
                    o = JsonSerializer.Deserialize<Opcoes>(File.ReadAllText(Arquivo));
                    existia = o != null;
                }
            }
            catch (Exception e)
            {
                Plugin.Registro?.LogWarning("[jogo] nao li " + Arquivo + ": " + e.Message);
            }
            o ??= new Opcoes();
            // First run (or an old file): as smooth as the screen goes, at the recommended size.
            // (11/11) No PC a primeira vez começa com o limite e o tamanho do próprio jogo: quem tem monitor de 144 Hz
            // não é segurado em 120, e a resolução é a que a pessoa escolheu na aba Gráficos.
            if (o.Fps < 0) o.Fps = Plataforma.Pc ? 0 : Math.Min(120, Tela.Maxima());
            if (o.Resolucao < 0) o.Resolucao = Plataforma.Pc ? 0 : Recomendada();
            // Anti-aliasing was removed (black screen in Freeplay on the CriCri's phone, 08/11): an old file may
            // still hold 2x/4x.
            o.AntiSerrilhado = 0;
            /* (11/11) O nível. Quem já usava o mod (arquivo sem nível) fica exatamente como estava: o Equilibrado é
               o que o mod já fazia, e as escolhas dele não são reescritas. Instalação nova num aparelho com pouca
               memória começa no Rápido, sem a pessoa mexer em nada. */
            if (o.BarraPing < 0 || o.BarraPing > 3) o.BarraPing = o.ContadorFps ? 1 : 0;
            if (o.Nivel < Bonito || o.Nivel > Rapido)
            {
                if (!existia && !Plataforma.Pc && PoucaMemoria()) o.PorNivel(Rapido);
                else o.Nivel = Equilibrado;
            }
            return o;
        }

        // Aparelho de até 3 GB (a Unity informa um pouco menos que o da caixa: perto de 2800 num de 3 GB, 3700 num de 4 GB).
        static bool PoucaMemoria()
        {
            try
            {
                int mb = UnityEngine.SystemInfo.systemMemorySize;
                return mb > 0 && mb <= 3300;
            }
            catch { return false; }
        }

        /* O que cada nível escreve nas opções que a pessoa enxerga (o tamanho da tela vale ao reabrir o jogo):
           Bonito = tela inteira e texturas no máximo; Equilibrado = o tamanho recomendado e texturas no máximo (o que o
           mod já fazia); Rápido = o tamanho e as texturas do próprio jogo. O "fora da tela não trabalha" (Leve.cs)
           vale igual nos três. */
        public void PorNivel(int nivel)
        {
            Nivel = nivel < Bonito || nivel > Rapido ? Equilibrado : nivel;
            Ligada = true;
            if (Nivel == Bonito) { Resolucao = 100; TexturasMaximas = true; }
            else if (Nivel == Rapido) { Resolucao = 0; TexturasMaximas = false; }
            else { Resolucao = Recomendada(); TexturasMaximas = true; }
        }

        public static string NomeDoNivel(int nivel) => nivel == Bonito ? "Bonito" : nivel == Rapido ? "Rápido" : "Equilibrado";

        // The stop of the resolution slider closest to 576 lines. 08/11, CriCri's phone (720 lines): 648 lines gave 35 to 43 FPS in a
        // round with 13 players, and the game's own 500 lines look pixelated. 0 (the game's size) when the screen cannot be read.
        public static int Recomendada()
        {
            try
            {
                int linhas = Math.Min(UnityEngine.Display.main.systemWidth, UnityEngine.Display.main.systemHeight);
                if (linhas <= 0) return 0;
                int melhor = 50;
                foreach (int p in new[] { 60, 70, 80, 90, 100 })
                    if (Math.Abs(linhas * p / 100 - 576) < Math.Abs(linhas * melhor / 100 - 576)) melhor = p;
                return melhor;
            }
            catch { return 0; }
        }

        public void Salvar()
        {
#if OTIM_TESTE
            // O build de teste roda no PC do dono: não grava nada na pasta do jogo.
            if (Arquivo.Length > 0) return;
#endif
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Arquivo));
                string temp = Arquivo + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(this));
                File.Copy(temp, Arquivo, true);
                File.Delete(temp);
            }
            catch (Exception e)
            {
                Plugin.Registro?.LogWarning("[jogo] nao salvei as opcoes: " + e.Message);
            }
        }

        public string Descrever() =>
            (Ligada ? "" : "OTIMIZACAO DESLIGADA; ") + "barra do ping " + BarraPing + ", nivel " + Nivel + ", fora da tela " + ForaDaTela + ", sombra do PC " + SombraDoPc + ", fps " + (Fps > 0 ? Fps.ToString() : "do jogo") + ", resolucao " + (Resolucao > 0 ? Resolucao + "%" : "do jogo")
            + ", contador " + ContadorFps + ", suave " + MovimentoSuave + ", AA " + AntiSerrilhado + "x, texturas max "
            + TexturasMaximas + ", daltonico cor " + DaltonicoCorNoNome + ", flash " + ModoFlash;
    }

    // The main-menu style, read by the CriCriMenu plugin (BepInEx/config/cricri-mods-menu.json, {"Estilo":"painel"}).
    // The CriCri (08/11): "fazer os 3 menus, e deixar em uma opção pra mudar pra qual quiser, 4 opções, os 3 e o normal".
    public static class EstiloMenu
    {
        public static readonly string[] Valores = { "painel", "hub", "classico", "normal" };
        public static string Arquivo => Path.Combine(Paths.ConfigPath, "cricri-mods-menu.json");
        const string Padrao = "painel";   // the same default as CriCriMenu

        public static string Ler()
        {
            try
            {
                if (File.Exists(Arquivo))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(Arquivo));
                    if (doc.RootElement.TryGetProperty("Estilo", out var e) && e.ValueKind == JsonValueKind.String
                        && Array.IndexOf(Valores, e.GetString()) >= 0)
                        return e.GetString();
                }
            }
            catch { }
            return Padrao;
        }

        public static void Salvar(string estilo)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Arquivo));
                string temp = Arquivo + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(new { Estilo = estilo }));
                File.Copy(temp, Arquivo, true);
                File.Delete(temp);
            }
            catch (Exception e) { Plugin.Registro?.LogWarning("[jogo] nao salvei o estilo do menu: " + e.Message); }
        }

        public static string Rotulo(string estilo) => estilo switch
        {
            "painel" => "Painel",
            "hub" => "Hub",
            "classico" => "Clássico+",
            _ => "Normal do jogo",
        };

        // Shown only when the menu mod is installed (it can be switched off in the app).
        public static bool ModInstalado()
        {
            try { return BepInEx.Unity.IL2CPP.IL2CPPChainloader.Instance.Plugins.ContainsKey("br.com.cricribr.menu"); }
            catch { return false; }
        }
    }

    // What the screen supports, as the app measured it (Android knows the display modes; Unity only sees the current one).
    public sealed class Tela
    {
        public int MaxHz { get; set; }
        public int[] Modos { get; set; }

        static Tela _lida;

        static Tela Ler()
        {
            try
            {
                if (File.Exists(Opcoes.ArquivoTela)) return JsonSerializer.Deserialize<Tela>(File.ReadAllText(Opcoes.ArquivoTela));
            }
            catch { }
            return null;
        }

        public static int Maxima()
        {
            _lida ??= Ler() ?? new Tela();
            if (_lida.MaxHz > 0) return _lida.MaxHz;
            try { return Math.Max(60, (int)Math.Round(UnityEngine.Screen.currentResolution.refreshRateRatio.value)); } catch { return 60; }
        }
    }
}
