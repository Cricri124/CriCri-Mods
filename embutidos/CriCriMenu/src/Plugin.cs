using BepInEx;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace CriCriMods.Menu
{
    // CriCri Mods inside the game: the main menu in one of 4 styles, chosen in BepInEx/config/cricri-mods-menu.json:
    //   normal   = the game's menu exactly as it comes (this plugin touches nothing)
    //   classico = the game's menu + an "Amigos" card in the free space (the Replays button is the CriCri Replay's own)
    //   painel   = style A (default when the file does not exist): top bar, JOGAR column, your crewmate, friends card
    //   hub      = style B: left rail, crewmate + JOGAR in the middle, friends rail on the right
    //
    // Android rule (the game there inlines thousands of methods): no Harmony at all. A DontDestroyOnLoad MonoBehaviour
    // watches for the MainMenu scene and a new MainMenuManager, and builds the chosen style once per menu instance.
    // Every action reuses the game: our buttons are clones of a game button whose click calls the original button's
    // OnClick (or a MainMenuManager method). Anything that fails puts the game's own menu back.
    [BepInPlugin(Id, "CriCri Mods - menu", Versao)]
    public sealed class Plugin : BasePlugin
    {
        public const string Id = "br.com.cricribr.menu";
        public const string Versao = "0.1.1";
        internal static BepInEx.Logging.ManualLogSource Registro;

        public override void Load()
        {
            Registro = Log;
            ClassInjector.RegisterTypeInIl2Cpp<TrocaDeMenu>();
            var objeto = new GameObject("CriCriMenu");
            Object.DontDestroyOnLoad(objeto);
            objeto.hideFlags = HideFlags.HideAndDontSave;
            objeto.AddComponent<TrocaDeMenu>();
            Log.LogInfo("[menu] CriCri Mods menu " + Versao + ": estilo " + Escolha.Atual + " (" + Escolha.Arquivo + ")");
        }
    }
}
