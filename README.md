# CriCri Mods (fork of Fusion Core)

**CriCri Mods** is a modified version of [Fusion Core](https://github.com/All-Of-Us-Mods/FusionCore) by All Of Us,
forked from release `0.045` (commit `bf4c69d`) on 2026-11-07. It is licensed under the GNU GPL v3, like the original
(see `LICENSE`).

The app opens the Among Us the player already installed from Google Play, with the CriCri mods loaded. It is
distributed as an APK on the CRICRI BR site (no Play Store). The game itself is never changed or copied.

What this fork changes:

- Launches only Among Us (`com.innersloth.spacemafia`), found through a `<queries>` entry instead of `QUERY_ALL_PACKAGES`.
- Stores everything in the app-specific directory (`Android/data/<app>/files`) instead of `/sdcard/FusionCore`,
  so it no longer asks for all-files access.
- Drops the camera, microphone, billing, media and storage permissions (what is left: internet, network state,
  Wi-Fi multicast for local games, notifications and wake lock).
- Run-time downloads are a build switch: without `-PcricriPermitirDownloads=true` nothing is downloaded; the site build
  (`ferramentas/gerar-apk-final.ps1`) turns it on, and then, like the original Fusion Core, it downloads the
  unstripped libunity and the Unity base libraries from the `FusionCore.UnityDependencies` releases.
- Removes the il2cpp2mono runtime, which Among Us does not use.
- A home screen with one PLAY button, the mods list (icon, author, version, what each one does), the Google login
  through the official Innersloth accounts page, and a home-screen shortcut "Among Us (mod)".

## The mods

| Mod | Source | What it does |
|---|---|---|
| Otimização | `embutidos/CriCriOtimizacao` | FPS limit and counter, resolution slider, quick start, streamer mode |
| Login | `embutidos/CriCriConta` | Hands the login made in the app to the game; keeps the account's outfit |
| Servidor CRICRI BR | `embutidos/CriCriServidor` | The CRICRI BR region, a CRICRI BR button on the main menu, the room options tab |
| CriCri Replay | binary only (`fusionApp/src/main/assets/mods/CriCriReplay.dll`) | Watch your matches again inside the game |

`embutidos/CriCriMenu` is kept for reference and is not in the app. Every DLL in `assets/mods` is listed in
`mods.json` with its SHA-256, and the app refuses a file whose hash does not match. None of the mods send game
packets or give an advantage during a match (the Replay only records it, and plays it back after it ends).

**Mods updated from the site.** The app reads a catalog from `https://cricribr.com.br/mods/`, signed with ECDSA P-256.
Only the public key is in the app (`tools/ModsRemotos.java`, `ferramentas/catalogo-mods-publica.txt`); the private key
and the APK signing key never go into this repository (`ferramentas/publicar-mods.js` reads them from outside it).

**CRICRI BR mod key.** A player may paste the key of their CRICRI BR account; the app keeps it encrypted with the
Android Keystore and hands it to the game for one launch through an environment variable, which the Servidor mod
reads and clears. It is sent only as `Authorization: Bearer` to `https://cricribr.com.br/api/mod/*`.

## Building

```
gradlew :fusionApp:testDebugUnitTest :fusionApp:assembleDebug
```

The mods build with `dotnet build -c Release` against the interop BepInEx generates on the phone
(`-p:InteropDir=<folder>`).

This mod is not affiliated with Among Us or Innersloth LLC, and the content contained therein is not endorsed or
otherwise sponsored by Innersloth LLC. Portions of the materials contained herein are property of Innersloth LLC.
© Innersloth LLC.

---

# Fusion Core (original README)

[![](https://dcbadge.limes.pink/api/server/MzufgkERdS)](https://discord.gg/MzufgkERdS)

Unity modding framework for Android IL2CPP games.

## Compatibility

Current minimum supported Android version is 7.0 (API level 24).
No guarantees it will work on that version though, 
as it has only been tested on Android 11 (API level 30) and above.

As of writing this, PC versions of BepInEx and MelonLoader still use .NET 6.0 and legacy MonoMod.
However, FusionCore uses .NET 10.0 and MonoMod reorganized, so if you directly use MonoMod in your mods,
you will need to make it compatible with the newer MonoMod version.

There is no Lemon/Melonloader support yet, just BepInEx Fusion (my custom fork of BepInEx).

FusionCore automatically detects Unity version, then downloads the unstripped libunity.so file. You only need to follow the configuration steps below to make things work.

## Can you add support for "X" game?

Make a new issue with the following information:
- Game name and version
- Android package name (e.g. `com.example.game`)
- Unity version (if known, otherwise I can check it myself)
- Any other relevant information (e.g. if the game uses a custom UnityPlayerActivity, or if it has anti-cheat)

We will try to add support, but there are no guarantees. If you want to help, you can also submit a pull request with the necessary changes to support the game.

## Configuration

Previous versions of FusionCore required adding the game activity to the manifest and modifying the list of supported games. This is no longer required as FusionCore scans installed apps for IL2CPP unity games and can launch them without manifest requirements.

Just download the APK (or build the app, if not available), and run!

IF you are using il2cpp2mono in your game, you CANNOT just use all of the original PC dlls. Unity's managed dlls are platform specific. this also goes for libraries like FMOD too.

## Build

Use the Gradle wrapper from the repository root:

```powershell
.\gradlew.bat :fusionApp:assembleDebug
```

Optional clean build:

```powershell
.\gradlew.bat clean :fusionApp:assembleDebug
```
