using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AmongUs.Data;
using Assets.InnerNet;
using BepInEx;
using UnityEngine;

namespace CriCriMods
{
    // CriCri Mods news inside the game's News screen, for everyone using the launcher. They come signed in the site's catalog; the app writes
    // them to BepInEx/config/cricri-mods-noticias.json before each launch. Nothing here talks to the network.
    //
    // The game replaces its list whenever Innersloth's answer arrives, so every half second the news are put back and the open screen redrawn.
    // No hook: on the phone many of these methods are inlined. Numbers are negative so they never touch the game's "latest news" counter.
    internal static class Noticias
    {
        const int PrimeiroNumero = -1000;
        static List<Announcement> _nossas;

        static string Arquivo => Path.Combine(Paths.ConfigPath, "cricri-mods-noticias.json");

        static void Ler()
        {
            _nossas = new List<Announcement>();
            uint idioma = (uint)DataManager.Settings.Language.CurrentLanguage;
#if NOTICIA_DE_TESTE
            _nossas.Add(Montar(1, "Novidades do CriCri Mods", "Notícia de teste", "2026-11-08", "Se você está lendo isto, as notícias do CriCri Mods funcionam.", idioma));
#endif
            if (!File.Exists(Arquivo)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(Arquivo));
            foreach (var n in doc.RootElement.EnumerateArray())
                _nossas.Add(Montar(n.GetProperty("numero").GetInt32(), n.GetProperty("titulo").GetString(),
                    n.TryGetProperty("subtitulo", out var s) ? s.GetString() : "CriCri Mods", n.GetProperty("data").GetString(), n.GetProperty("texto").GetString(), idioma));
            Plugin.Registro.LogInfo("[noticias] " + _nossas.Count + " do CriCri Mods");
        }

        static Announcement Montar(int numero, string titulo, string subtitulo, string data, string texto, uint idioma)
        {
            return new Announcement
            {
                Id = "cricri-mods-" + numero,
                Number = PrimeiroNumero - numero,
                Language = idioma,
                Title = titulo,
                SubTitle = subtitulo,
                ShortTitle = titulo,
                PinState = true,
                Date = data,
                Text = texto,
            };
        }

        internal static void Tique()
        {
            if (!DataManager.IsPlayerLoaded || !DataManager.IsSettingsLoaded) return;
            if (_nossas == null) Ler();
            if (_nossas.Count == 0) return;
            var lista = DataManager.Player.Announcements.AllAnnouncements;
            if (lista == null) return;
            var presentes = new HashSet<int>();
            for (int i = 0; i < lista.Count; i++) presentes.Add(lista[i].Number);
            bool faltava = false;
            foreach (var n in _nossas)
            {
                if (presentes.Contains(n.Number)) continue;
                lista.Add(n);
                faltava = true;
            }
            if (!faltava) return;
            var tela = UnityEngine.Object.FindObjectOfType<AnnouncementPopUp>();
            if (tela != null && tela.isActiveAndEnabled && !tela.readingAnnouncement) tela.CreateAnnouncementList();
        }
    }
}
