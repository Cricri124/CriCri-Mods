using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AmongUs.Data;
using BepInEx;
using UnityEngine;

namespace CriCriMods.Conta
{
    // The outfit of the player's account, kept by this plugin (08/11, the CriCri: "a minha skin e as minhas coisas
    // somem sempre quando eu vou renovar o login").
    //
    // Why it vanished (disassembled, and seen in the phone's log at 03:31 that day): on every login the game loads the
    // account's inventory from Epic (InventoryManager.OnInventoryOpenCallback): ResetInventory empties the local
    // purchase list and saves, the list is filled with what the account owns, then CheckEquipped puts the default back
    // on every equipped hat/skin/visor/pet/nameplate/cosmicube missing from the list, and saves. The Google login lasts
    // an hour; past it the game logs in as a GUEST, whose inventory is empty ("Couldn't find folder 'hats'...",
    // "legacyPurchases": []), so the account's outfit is overwritten with the defaults. Back on the account, the
    // purchases return but the outfit was already saved as default.
    //
    // What this does: while the game is on the FULL account (from EndFinalPartsOfLoginFlowFullAccount on, i.e. after
    // that account's inventory loaded) it keeps a copy of the outfit; when the full account logs in again and the
    // outfit changed in between (a guest session), the copy goes back and the game's own CheckEquipped runs again, so
    // nothing the account doesn't own is ever worn.
    internal sealed class Roupa : MonoBehaviour
    {
        public Roupa(IntPtr ptr) : base(ptr) { }

        public sealed class Copia
        {
            public string Hat { get; set; }
            public string Skin { get; set; }
            public string Visor { get; set; }
            public string Pet { get; set; }
            public string NamePlate { get; set; }
            public string Cubo { get; set; }
        }

        static BepInEx.Logging.ManualLogSource Log => Plugin.Registro;
        static string Arquivo => Path.Combine(Paths.ConfigPath, "cricri-mods-roupa.json");

        static bool _contaCompleta;
        static string _chave;      // the Google account (sub) on screen
        static Copia _ultima;      // last copy written for it
        float _prox;

        // A login flow is starting or the session is about to expire: stop copying until a full account is back
        // (the inventory reload in between saves the defaults first).
        internal static void Pausar() => _contaCompleta = false;

        // The game finished logging in with a full account (its inventory is loaded and checked).
        internal static void ContaCompleta(string chave)
        {
            _chave = string.IsNullOrEmpty(chave) ? "conta" : chave;
            try { Devolver(); }
            catch (Exception e) { Log.LogWarning("[roupa] nao devolvi a roupa: " + e.GetType().Name + ": " + e.Message); }
            _ultima = null;
            _contaCompleta = true;
        }

        void Update()
        {
            if (!_contaCompleta) return;
            float agora = Time.unscaledTime;
            if (agora < _prox) return;
            _prox = agora + 2f;
            try { Guardar(); }
            catch (Exception e)
            {
                _prox = agora + 30f;
                Log.LogWarning("[roupa] nao guardei a roupa: " + e.GetType().Name + ": " + e.Message);
            }
        }

        static Copia Atual()
        {
            if (!DataManager.IsPlayerLoaded) return null;
            var c = DataManager.Player.Customization;
            return new Copia
            {
                Hat = c.Hat, Skin = c.Skin, Visor = c.Visor, Pet = c.Pet, NamePlate = c.NamePlate,
                Cubo = DataManager.Player.Store.ActiveCosmicube,
            };
        }

        static void Guardar()
        {
            var atual = Atual();
            if (atual == null || Igual(atual, _ultima)) return;
            var todas = Ler();
            todas[_chave] = atual;
            Escrever(todas);
            bool primeira = _ultima == null;
            _ultima = atual;
            if (!primeira) Log.LogInfo("[roupa] guardei a roupa nova da conta: " + Descrever(atual));
        }

        static void Devolver()
        {
            var todas = Ler();
            if (!todas.TryGetValue(_chave, out var guardada) || guardada == null) return;
            var atual = Atual();
            if (atual == null || Igual(atual, guardada)) return;

            var c = DataManager.Player.Customization;
            if (Trocar(atual.Hat, guardada.Hat)) c.Hat = guardada.Hat;
            if (Trocar(atual.Skin, guardada.Skin)) c.Skin = guardada.Skin;
            if (Trocar(atual.Visor, guardada.Visor)) c.Visor = guardada.Visor;
            if (Trocar(atual.Pet, guardada.Pet)) c.Pet = guardada.Pet;
            if (Trocar(atual.NamePlate, guardada.NamePlate)) c.NamePlate = guardada.NamePlate;
            if (Trocar(atual.Cubo, guardada.Cubo)) DataManager.Player.Store.ActiveCosmicube = guardada.Cubo;
            // the game's own rule: whatever this account doesn't own goes back to the default
            if (DestroyableSingleton<InventoryManager>.InstanceExists) DestroyableSingleton<InventoryManager>.Instance.CheckEquipped();
            DataManager.Player.Save();
            Log.LogInfo("[roupa] devolvi a roupa da conta (outra sessao tinha trocado): era " + Descrever(atual)
                + "; agora " + Descrever(Atual()));
        }

        static bool Trocar(string atual, string guardado) => !string.IsNullOrEmpty(guardado) && guardado != atual;

        static bool Igual(Copia a, Copia b) => b != null && a.Hat == b.Hat && a.Skin == b.Skin && a.Visor == b.Visor
            && a.Pet == b.Pet && a.NamePlate == b.NamePlate && a.Cubo == b.Cubo;

        static string Descrever(Copia c) => c == null ? "-" : c.Hat + ", " + c.Skin + ", " + c.Visor + ", " + c.Pet + ", " + c.NamePlate + ", " + c.Cubo;

        static Dictionary<string, Copia> Ler()
        {
            try
            {
                if (File.Exists(Arquivo))
                    return JsonSerializer.Deserialize<Dictionary<string, Copia>>(File.ReadAllText(Arquivo)) ?? new Dictionary<string, Copia>();
            }
            catch (Exception e) { Log.LogWarning("[roupa] arquivo da roupa ilegivel: " + e.GetType().Name); }
            return new Dictionary<string, Copia>();
        }

        static void Escrever(Dictionary<string, Copia> todas)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Arquivo));
            string temp = Arquivo + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(todas));
            File.Copy(temp, Arquivo, true);
            File.Delete(temp);
        }
    }
}
