package dev.allofus.fusioncore.tools;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNull;
import static org.junit.Assert.assertTrue;

import org.junit.Rule;
import org.junit.Test;
import org.junit.rules.TemporaryFolder;
import org.junit.runner.RunWith;
import org.robolectric.RobolectricTestRunner;
import org.robolectric.annotation.Config;

import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.util.zip.ZipEntry;
import java.util.zip.ZipOutputStream;

/**
 * (12/11) A fresh install crashed on CriCri's phone because BepInEx could not download the Unity base libraries. These tests check,
 * without internet, what decides whether the app opens the game: the zip check and leaving alone what is not ours.
 */
@RunWith(RobolectricTestRunner.class)
@Config(sdk = 35)
public class UnityBaseLibrariesTest {
    private static final String DEFAULT_LINE = "UnityBaseLibrariesSource = https://unity.bepinex.dev/libraries/{VERSION}.zip";

    @Rule
    public TemporaryFolder pasta = new TemporaryFolder();

    @Test
    public void zipComOModuloPrincipalPassa() throws IOException {
        File zip = zip("a.zip", UnityBaseLibraries.CORE, "UnityEngine.InputModule.dll");
        assertTrue(UnityBaseLibraries.valid(zip, null));
        assertTrue(UnityBaseLibraries.valid(zip, UnityBaseLibraries.sha256(zip)));
    }

    @Test
    public void zipComOutroConteudoNaoPassa() throws IOException {
        File zip = zip("a.zip", UnityBaseLibraries.CORE);
        // a versão conhecida exige exatamente os bytes conferidos à mão (o espelho do site só vale assim)
        assertFalse(UnityBaseLibraries.valid(zip, UnityBaseLibraries.KNOWN.get("2022.3.62")));
        assertFalse(UnityBaseLibraries.valid(zip("b.zip", "UnityEngine.InputModule.dll"), null));
    }

    @Test
    public void arquivoQuebradoNaoPassa() throws IOException {
        File meio = pasta.newFile("meio.zip");
        byte[] inteiro = bytes(zip("c.zip", UnityBaseLibraries.CORE));
        try (FileOutputStream out = new FileOutputStream(meio)) {
            out.write(inteiro, 0, inteiro.length / 2);   // download cortado no meio
        }
        assertFalse(UnityBaseLibraries.valid(meio, null));
        assertFalse(UnityBaseLibraries.valid(new File(pasta.getRoot(), "nao-existe.zip"), null));
    }

    @Test
    public void zipJaNoLugarAbreSemInternet() throws IOException {
        File bepInEx = bepInEx(DEFAULT_LINE);
        File libs = new File(bepInEx, "unity-libs");
        assertTrue(libs.mkdirs());
        zipEm(new File(libs, "2099.1.5.zip"), UnityBaseLibraries.CORE);
        String antes = config(bepInEx);

        assertTrue(UnityBaseLibraries.resolve(bepInEx, "2099.1.5f1", () -> { throw new AssertionError("baixou sem precisar"); }));
        assertEquals(antes, config(bepInEx));
    }

    @Test
    public void fonteEscritaAMaoNaoEMexida() throws IOException {
        File bepInEx = bepInEx("UnityBaseLibrariesSource = https://exemplo.invalid/{VERSION}.zip");
        String antes = config(bepInEx);
        assertTrue(UnityBaseLibraries.resolve(bepInEx, "2022.3.62f1", () -> { throw new AssertionError("baixou sem precisar"); }));
        assertEquals(antes, config(bepInEx));
        assertFalse(new File(bepInEx, "unity-libs").exists());
    }

    @Test
    public void versaoReservaDeOutraAbertura() {
        assertEquals("2022.3.61", UnityBaseLibraries.earlierFallback("https://unity.bepinex.dev/libraries/2022.3.61.zip"));
        assertNull(UnityBaseLibraries.earlierFallback("https://unity.bepinex.dev/libraries/{VERSION}.zip"));
        assertNull(UnityBaseLibraries.earlierFallback("https://outro.invalid/libraries/2022.3.61.zip"));
    }

    private File bepInEx(String linha) throws IOException {
        File bepInEx = pasta.newFolder("BepInEx");
        File cfg = new File(bepInEx, "config/BepInEx.cfg");
        assertTrue(cfg.getParentFile().mkdirs());
        try (FileOutputStream out = new FileOutputStream(cfg)) {
            out.write(("[IL2CPP]\r\n\r\n" + linha + "\r\n\r\n[Logging]\r\n").getBytes(StandardCharsets.UTF_8));
        }
        return bepInEx;
    }

    private static String config(File bepInEx) throws IOException {
        return new String(bytes(new File(bepInEx, "config/BepInEx.cfg")), StandardCharsets.UTF_8);
    }

    private File zip(String nome, String... entradas) throws IOException {
        File zip = new File(pasta.getRoot(), nome);
        zipEm(zip, entradas);
        return zip;
    }

    private static void zipEm(File zip, String... entradas) throws IOException {
        try (ZipOutputStream out = new ZipOutputStream(new FileOutputStream(zip))) {
            for (String entrada : entradas) {
                out.putNextEntry(new ZipEntry(entrada));
                // bytes que não comprimem: o zip passa de 1 KB como um de verdade
                byte[] corpo = new byte[4096];
                int x = entrada.length();
                for (int i = 0; i < corpo.length; i++) {
                    x = x * 1103515245 + 12345;
                    corpo[i] = (byte) (x >>> 16);
                }
                out.write(corpo);
                out.closeEntry();
            }
        }
    }

    private static byte[] bytes(File file) throws IOException {
        try (InputStream in = new FileInputStream(file)) {
            return in.readAllBytes();
        }
    }
}
