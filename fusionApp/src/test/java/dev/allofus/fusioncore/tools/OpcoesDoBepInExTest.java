package dev.allofus.fusioncore.tools;

import static org.junit.Assert.assertEquals;
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

/**
 * (12/11) A primeira vez mais curta: o app desliga duas opções do BepInEx.cfg que vêm ligadas no zip e nenhum mod usa, sem mexer
 * no resto do arquivo. Um celular com config/manter-dummy continua guardando os dummy assemblies (o PC precisa deles).
 */
@RunWith(RobolectricTestRunner.class)
@Config(sdk = 35)
public class OpcoesDoBepInExTest {
    // o trecho do BepInEx.cfg do BepInEx-arm64.zip, com o fim de linha do Windows
    private static final String CFG = "[IL2CPP]\r\n\r\n"
            + "## Whether to run Il2CppInterop automatically.\r\nUpdateInteropAssemblies = true\r\n\r\n"
            + "UnityBaseLibrariesSource = https://unity.bepinex.dev/libraries/{VERSION}.zip\r\n\r\n"
            + "## If enabled, Il2CppInterop will use xref to find dead methods and generate CallerCount attributes.\r\n"
            + "# Default value: false\r\nScanMethodRefs = true\r\n\r\n"
            + "# Default value: false\r\nDumpDummyAssemblies = true\r\n\r\n"
            + "IL2CPPInteropAssembliesPath = {BepInEx}\r\n\r\n[Logging]\r\n";

    @Rule
    public TemporaryFolder pasta = new TemporaryFolder();

    @Test
    public void desligaAsDuasOpcoesENaoMexeNoResto() throws IOException {
        File bepInEx = bepInEx();
        Utilities.setFirstLaunchOptions(bepInEx);
        String esperado = CFG.replace("ScanMethodRefs = true", "ScanMethodRefs = false")
                .replace("DumpDummyAssemblies = true", "DumpDummyAssemblies = false");
        assertEquals(esperado, cfg(bepInEx));
        // de novo não muda nada
        Utilities.setFirstLaunchOptions(bepInEx);
        assertEquals(esperado, cfg(bepInEx));
    }

    @Test
    public void celularComAMarcaGuardaOsDummies() throws IOException {
        File bepInEx = bepInEx();
        assertTrue(new File(bepInEx, "config/" + Utilities.MANTER_DUMMY).createNewFile());
        Utilities.setFirstLaunchOptions(bepInEx);
        String texto = cfg(bepInEx);
        assertTrue(texto.contains("\r\nScanMethodRefs = false\r\n"));
        assertTrue(texto.contains("\r\nDumpDummyAssemblies = true\r\n"));
    }

    @Test
    public void aFonteDasBibliotecasContinuaIgual() throws IOException {
        File bepInEx = bepInEx();
        Utilities.setUnityBaseLibrariesSource(bepInEx, "");
        assertTrue(cfg(bepInEx).contains("\r\nUnityBaseLibrariesSource = \r\n"));
        Utilities.setUnityBaseLibrariesSource(bepInEx, Utilities.DEFAULT_UNITY_BASE_LIBRARIES_SOURCE);
        assertEquals(CFG, cfg(bepInEx));
    }

    private File bepInEx() throws IOException {
        File bepInEx = pasta.newFolder("BepInEx");
        File cfg = new File(bepInEx, "config/BepInEx.cfg");
        assertTrue(cfg.getParentFile().mkdirs());
        try (FileOutputStream out = new FileOutputStream(cfg)) {
            out.write(CFG.getBytes(StandardCharsets.UTF_8));
        }
        return bepInEx;
    }

    private static String cfg(File bepInEx) throws IOException {
        try (InputStream in = new FileInputStream(new File(bepInEx, "config/BepInEx.cfg"))) {
            return new String(in.readAllBytes(), StandardCharsets.UTF_8);
        }
    }
}
