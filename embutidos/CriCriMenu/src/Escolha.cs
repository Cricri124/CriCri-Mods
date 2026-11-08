using System;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using BepInEx;

namespace CriCriMods.Menu
{
    // The chosen style, in BepInEx/config/cricri-mods-menu.json ({"Estilo":"painel"}). CriCriOtimizacao writes it (its
    // selector in Settings > Graphics); this plugin only reads it, and writes it only from the "Menu clássico" button
    // of styles A/B (keeping any other key that may be in the file).
    // The file's date is checked at most once a second; a new date means "read again and re-apply on the open menu".
    internal static class Escolha
    {
        public const string Normal = "normal", Classico = "classico", Painel = "painel", Hub = "hub";
        public const string Padrao = Painel;   // no file yet = style A (the owner's choice)

        public static string Arquivo => Path.Combine(Paths.ConfigPath, "cricri-mods-menu.json");

        static string _atual;
        static bool _existia;
        static DateTime _data;
        static float _proxima;
        static string _avisado;

        public static string Atual
        {
            get
            {
                if (_atual == null) Ler(true);
                return _atual;
            }
        }

        // Called every frame; looks at the disk at most once a second. True when the style changed.
        public static bool Conferir(float agora)
        {
            if (agora < _proxima) return false;
            _proxima = agora + 1f;
            bool existe;
            DateTime data = default;
            try
            {
                existe = File.Exists(Arquivo);
                if (existe) data = File.GetLastWriteTimeUtc(Arquivo);
            }
            catch (Exception e)
            {
                Avisar("data", "[menu] nao consegui olhar " + Arquivo + ": " + e.Message);
                return false;
            }
            if (_atual != null && existe == _existia && data == _data) return false;
            string antes = _atual;
            Ler(false);
            return antes != null && antes != _atual;
        }

        static void Ler(bool primeira)
        {
            string lido = null;
            try
            {
                _existia = File.Exists(Arquivo);
                if (!_existia)
                {
                    _data = default;
                    lido = Padrao;
                }
                else
                {
                    _data = File.GetLastWriteTimeUtc(Arquivo);
                    var raiz = JsonNode.Parse(File.ReadAllText(Arquivo, Encoding.UTF8)) as JsonObject;
                    string bruto = null;
                    if (raiz != null)
                        foreach (var par in raiz)
                            if (string.Equals(par.Key, "Estilo", StringComparison.OrdinalIgnoreCase) && par.Value != null)
                            {
                                try { bruto = par.Value.GetValue<string>(); } catch { bruto = par.Value.ToString(); }
                                break;
                            }
                    lido = Normalizar(bruto);
                    if (lido == null)
                    {
                        Avisar("valor" + bruto, "[menu] estilo desconhecido em " + Arquivo + ": \"" + bruto + "\"; uso " + Padrao);
                        lido = Padrao;
                    }
                }
            }
            catch (Exception e)
            {
                // a file being rewritten right now (or broken): keep what we had, the next date change reads again
                Avisar("ler" + e.GetType().Name, "[menu] nao li " + Arquivo + ": " + e.GetType().Name + ": " + e.Message);
                lido = _atual ?? Padrao;
            }
            if (!primeira && lido != _atual) Plugin.Registro?.LogInfo("[menu] estilo do arquivo: " + lido + (_existia ? "" : " (sem arquivo: padrao)"));
            _atual = lido;
        }

        // "Clássico", " HUB " and so on become the 4 exact values; anything else is null.
        static string Normalizar(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim().ToLowerInvariant().Replace('á', 'a').Replace('â', 'a').Replace('ã', 'a');
            switch (s)
            {
                case Normal: case Classico: case Painel: case Hub: return s;
                case "a": return Painel;
                case "b": return Hub;
                default: return null;
            }
        }

        // Only the "Menu clássico" button calls this. Other keys of the file are kept.
        public static bool Gravar(string estilo)
        {
            try
            {
                JsonObject raiz = null;
                try
                {
                    if (File.Exists(Arquivo)) raiz = JsonNode.Parse(File.ReadAllText(Arquivo, Encoding.UTF8)) as JsonObject;
                }
                catch { raiz = null; }
                raiz ??= new JsonObject();
                string chave = "Estilo";
                foreach (var par in raiz)
                    if (string.Equals(par.Key, "Estilo", StringComparison.OrdinalIgnoreCase)) { chave = par.Key; break; }
                raiz[chave] = estilo;

                Directory.CreateDirectory(Path.GetDirectoryName(Arquivo));
                string temp = Arquivo + ".tmp";
                File.WriteAllText(temp, raiz.ToJsonString(), new UTF8Encoding(false));
                File.Copy(temp, Arquivo, true);
                File.Delete(temp);
                _atual = estilo;
                _existia = true;
                try { _data = File.GetLastWriteTimeUtc(Arquivo); } catch { }
                Plugin.Registro?.LogInfo("[menu] estilo gravado: " + estilo);
                return true;
            }
            catch (Exception e)
            {
                Plugin.Registro?.LogWarning("[menu] nao gravei o estilo " + estilo + ": " + e.GetType().Name + ": " + e.Message);
                return false;
            }
        }

        static void Avisar(string chave, string texto)
        {
            if (_avisado == chave) return;
            _avisado = chave;
            Plugin.Registro?.LogWarning(texto);
        }
    }
}
