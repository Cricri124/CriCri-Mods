package dev.allofus.fusioncore.tools;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNotNull;
import static org.junit.Assert.assertNull;
import static org.junit.Assert.assertTrue;
import static org.junit.Assert.fail;

import android.content.Context;
import android.util.Base64;

import androidx.test.core.app.ApplicationProvider;
import androidx.test.ext.junit.runners.AndroidJUnit4;

import org.json.JSONArray;
import org.json.JSONObject;
import org.junit.After;
import org.junit.Before;
import org.junit.Test;
import org.junit.runner.RunWith;
import org.robolectric.annotation.Config;

import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.security.KeyPair;
import java.security.KeyPairGenerator;
import java.security.Signature;
import java.security.spec.ECGenParameterSpec;
import java.util.List;

/**
 * CriCri Mods: mods updated from the site without a new APK. What must hold for the players: only a catalog signed
 * with the CriCri's key is accepted, an older catalog never comes back, a DLL is used only with the signed sha256, a
 * newer site version replaces the one inside the APK, and "retirado" takes a mod out of the game.
 */
@RunWith(AndroidJUnit4.class)
@Config(sdk = 35)
public class ModsRemotosTest {
    private Context ctx;
    private KeyPair chaves;

    @Before
    public void preparar() throws Exception {
        ctx = ApplicationProvider.getApplicationContext();
        KeyPairGenerator g = KeyPairGenerator.getInstance("EC");
        g.initialize(new ECGenParameterSpec("secp256r1"));
        chaves = g.generateKeyPair();
        ModsRemotos.chaveParaTeste = null;
    }

    @After
    public void limpar() {
        ModsRemotos.chaveParaTeste = null;
    }

    /** What ferramentas/publicar-mods.js (Node) signs with the real key is what the app accepts with the key in the APK. */
    @Test
    public void catalogoDaFerramenta_confereComAChaveDoApp() throws Exception {
        byte[] json = recurso("mods-site/catalogo-node.json");
        byte[] sig = recurso("mods-site/catalogo-node.json.sig");
        ModsRemotos.Catalogo c = ModsRemotos.conferir(json, sig, 0);
        assertEquals(1, c.numero);
        assertTrue(c.mods.size() >= 3);
        assertTrue(c.mods.get(0).getString("url").endsWith(".dll"));
        // one changed byte and it is refused
        byte[] mexido = json.clone();
        mexido[mexido.length / 2] ^= 1;
        recusado(mexido, sig, 0, "assinatura");
    }

    @Test
    public void numeroMenorQueOUltimo_eRecusado() throws Exception {
        ModsRemotos.chaveParaTeste = publica();
        byte[] json = catalogo(4, mod("a.mod", "1.0.0", "A.dll", "A-1.0.0.dll", "x".getBytes())).toString().getBytes(StandardCharsets.UTF_8);
        ModsRemotos.conferir(json, assinar(json), 4);   // the same number again is fine
        recusado(json, assinar(json), 5, "abaixo do ultimo");
    }

    @Test
    public void nomeComCaminho_eRecusado() throws Exception {
        ModsRemotos.chaveParaTeste = publica();
        byte[] json = catalogo(1, mod("a.mod", "1.0.0", "A.dll", "../../etc/A.dll", "x".getBytes())).toString().getBytes(StandardCharsets.UTF_8);
        recusado(json, assinar(json), 0, "nome de arquivo");
    }

    @Test
    public void assinaturaDeOutraChave_eRecusada() throws Exception {
        ModsRemotos.chaveParaTeste = null;   // the real key of the APK
        byte[] json = catalogo(1, mod("a.mod", "1.0.0", "A.dll", "A-1.0.0.dll", "x".getBytes())).toString().getBytes(StandardCharsets.UTF_8);
        recusado(json, assinar(json), 0, "assinatura");   // signed with the test key
    }

    /** A newer Replay on the site (downloaded, sha256 ok) replaces the one inside the APK and is what JOGAR installs. */
    @Test
    public void versaoNovaDoSite_substituiADoApk_eEInstalada() throws Exception {
        ModsRemotos.chaveParaTeste = publica();
        byte[] dll = "replay novo".getBytes(StandardCharsets.UTF_8);
        JSONObject replay = mod("com.cricri.replay", "9.0.0", "CriCriReplay.dll", "CriCriReplay-9.0.0.dll", dll);
        guardarNoAparelho(catalogo(2, replay), "CriCriReplay-9.0.0.dll", dll);

        ModsManager.Mod usado = acharMod(ModsManager.catalog(ctx), "com.cricri.replay");
        assertTrue(usado.remoto);
        assertEquals("9.0.0", usado.version);

        File bepinex = new File(ctx.getCacheDir(), "BepInEx-teste");
        ModsManager.install(ctx, bepinex);
        byte[] instalado = Files.readAllBytes(new File(bepinex, "plugins/CriCriReplay.dll").toPath());
        assertEquals("replay novo", new String(instalado, StandardCharsets.UTF_8));
    }

    /** Not downloaded yet (or a changed file): the APK's own version stays. */
    @Test
    public void dllAdulterada_ficaADoApk() throws Exception {
        ModsRemotos.chaveParaTeste = publica();
        JSONObject replay = mod("com.cricri.replay", "9.0.0", "CriCriReplay.dll", "CriCriReplay-9.0.0.dll", "certo".getBytes());
        guardarNoAparelho(catalogo(2, replay), "CriCriReplay-9.0.0.dll", "ERRADO".getBytes());
        ModsManager.Mod usado = acharMod(ModsManager.catalog(ctx), "com.cricri.replay");
        assertFalse(usado.remoto);
        assertNull(ModsRemotos.baixado(ctx, "CriCriReplay-9.0.0.dll", replay.getString("sha256")));
    }

    /** Emergency switch: "retirado" takes the mod out of the list and out of the plugins folder. */
    @Test
    public void retirado_saiDaListaEDaPastaDosPlugins() throws Exception {
        ModsRemotos.chaveParaTeste = publica();
        File bepinex = new File(ctx.getCacheDir(), "BepInEx-teste2");
        ModsManager.install(ctx, bepinex);
        assertTrue(new File(bepinex, "plugins/CriCriReplay.dll").isFile());

        JSONObject retirado = new JSONObject().put("id", "com.cricri.replay").put("retirado", true);
        guardarNoAparelho(catalogo(3, retirado), null, null);
        assertNull(acharMod(ModsManager.catalog(ctx), "com.cricri.replay"));
        ModsManager.install(ctx, bepinex);
        assertFalse(new File(bepinex, "plugins/CriCriReplay.dll").exists());
    }

    /** The Menu left the app (08/11): its DLL from an older install goes away at the next JOGAR; a DLL of the player's stays. */
    @Test
    public void modQueOAppNaoTrazMais_saiDaPastaDosPlugins() throws Exception {
        File bepinex = new File(ctx.getCacheDir(), "BepInEx-teste3");
        File plugins = new File(bepinex, "plugins");
        assertTrue(plugins.mkdirs() || plugins.isDirectory());
        File menuVelho = new File(plugins, "CriCriMenu.dll");
        File doJogador = new File(plugins, "OutroMod.dll");
        Files.write(menuVelho.toPath(), "menu 0.1.1".getBytes(StandardCharsets.UTF_8));
        Files.write(doJogador.toPath(), "mod do jogador".getBytes(StandardCharsets.UTF_8));

        assertTrue(ModsManager.removedFiles(ctx).contains("CriCriMenu.dll"));
        assertNull(acharMod(ModsManager.catalog(ctx), "br.com.cricribr.menu"));
        ModsManager.install(ctx, bepinex);
        assertFalse(menuVelho.exists());
        assertTrue(doJogador.isFile());
        assertTrue(new File(plugins, "CriCriServidor.dll").isFile());
    }

    @Test
    public void versoes() {
        assertTrue(ModsRemotos.compararVersoes("1.5.10", "1.5.9") > 0);
        assertTrue(ModsRemotos.compararVersoes("0.4.0", "0.3.3") > 0);
        assertEquals(0, ModsRemotos.compararVersoes("1.0", "1.0.0"));
        assertTrue(ModsRemotos.compararVersoes("1.0.0", "1.0.1") < 0);
    }

    // ---------- helpers ----------

    private String publica() {
        return Base64.encodeToString(chaves.getPublic().getEncoded(), Base64.NO_WRAP);
    }

    private byte[] assinar(byte[] dados) throws Exception {
        Signature s = Signature.getInstance("SHA256withECDSA");
        s.initSign(chaves.getPrivate());
        s.update(dados);
        return Base64.encodeToString(s.sign(), Base64.NO_WRAP).getBytes(StandardCharsets.US_ASCII);
    }

    private static JSONObject catalogo(int numero, JSONObject... mods) throws Exception {
        JSONArray lista = new JSONArray();
        for (JSONObject m : mods) lista.put(m);
        return new JSONObject().put("formato", 1).put("numero", numero).put("emitido", "2026-11-08T08:00:00Z").put("mods", lista);
    }

    private static JSONObject mod(String id, String versao, String arquivo, String url, byte[] dll) throws Exception {
        return new JSONObject().put("id", id).put("name", id).put("version", versao).put("file", arquivo)
                .put("url", url).put("sha256", ModsRemotos.sha256(dll)).put("description", "teste");
    }

    // What ModsRemotos.verificar leaves on the phone after a download.
    private void guardarNoAparelho(JSONObject catalogo, String url, byte[] dll) throws Exception {
        File dir = ModsRemotos.pasta(ctx);
        dir.mkdirs();
        byte[] json = catalogo.toString().getBytes(StandardCharsets.UTF_8);
        escrever(new File(dir, "catalogo.json"), json);
        escrever(new File(dir, "catalogo.json.sig"), assinar(json));
        if (url != null) escrever(new File(dir, url), dll);
    }

    private static void escrever(File f, byte[] dados) throws Exception {
        try (FileOutputStream out = new FileOutputStream(f, false)) {
            out.write(dados);
        }
    }

    private static ModsManager.Mod acharMod(List<ModsManager.Mod> mods, String id) {
        for (ModsManager.Mod m : mods) if (m.id.equals(id)) return m;
        return null;
    }

    private static void recusado(byte[] json, byte[] sig, int minimo, String parteDoMotivo) {
        try {
            ModsRemotos.conferir(json, sig, minimo);
            fail("devia recusar (" + parteDoMotivo + ")");
        } catch (ModsRemotos.Recusado r) {
            assertTrue(r.getMessage(), r.getMessage().contains(parteDoMotivo));
        }
    }

    private byte[] recurso(String nome) throws Exception {
        try (InputStream in = getClass().getClassLoader().getResourceAsStream(nome)) {
            assertNotNull("recurso " + nome, in);
            return in.readAllBytes();
        }
    }
}
