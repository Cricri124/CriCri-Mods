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

        // What is actually applied (the master switch off means "the game's own" for all of it).
        [JsonIgnore] public int FpsEfetivo => Ligada ? Fps : 0;
        [JsonIgnore] public int ResolucaoEfetiva => Ligada ? Resolucao : 0;
        [JsonIgnore] public bool SuaveEfetivo => Ligada && MovimentoSuave;
        [JsonIgnore] public int AaEfetivo => Ligada ? AntiSerrilhado : 0;
        [JsonIgnore] public bool TexturasEfetivas => Ligada && TexturasMaximas;
        [JsonIgnore] public bool FlashEfetivo => Ligada && ModoFlash;

        public static string Arquivo => Path.Combine(Paths.ConfigPath, "cricri-mods-otimizacoes.json");
        // Written by the app before each launch: what the screen can do (refresh rates).
        public static string ArquivoTela => Path.Combine(Paths.ConfigPath, "cricri-mods-tela.json");

        static Opcoes _atual;
        public static Opcoes Atual => _atual ??= Ler();

        static Opcoes Ler()
        {
            Opcoes o = null;
            try
            {
                if (File.Exists(Arquivo)) o = JsonSerializer.Deserialize<Opcoes>(File.ReadAllText(Arquivo));
            }
            catch (Exception e)
            {
                Plugin.Registro?.LogWarning("[jogo] nao li " + Arquivo + ": " + e.Message);
            }
            o ??= new Opcoes();
            // First run (or an old file): as smooth as the screen goes, at the recommended size.
            if (o.Fps < 0) o.Fps = Math.Min(120, Tela.Maxima());
            if (o.Resolucao < 0) o.Resolucao = Recomendada();
            // Anti-aliasing was removed (black screen in Freeplay on the CriCri's phone, 08/11): an old file may
            // still hold 2x/4x.
            o.AntiSerrilhado = 0;
            return o;
        }

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
            (Ligada ? "" : "OTIMIZACAO DESLIGADA; ") + "fps " + (Fps > 0 ? Fps.ToString() : "do jogo") + ", resolucao " + (Resolucao > 0 ? Resolucao + "%" : "do jogo")
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
