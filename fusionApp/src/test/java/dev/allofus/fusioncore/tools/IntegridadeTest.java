package dev.allofus.fusioncore.tools;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

import org.junit.Rule;
import org.junit.Test;
import org.junit.rules.TemporaryFolder;
import org.junit.runner.RunWith;
import org.robolectric.RobolectricTestRunner;
import org.robolectric.annotation.Config;

import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.util.Arrays;
import java.util.HashSet;
import java.util.Set;

@RunWith(RobolectricTestRunner.class)
@Config(sdk = 35)
public class IntegridadeTest {
    @Rule public TemporaryFolder pasta = new TemporaryFolder();

    private static void escrever(File f, String texto) throws IOException {
        File pai = f.getParentFile();
        if (pai != null) pai.mkdirs();
        try (FileOutputStream out = new FileOutputStream(f)) {
            out.write(texto.getBytes(StandardCharsets.UTF_8));
        }
    }

    @Test
    public void soFicaOQueEOficial() throws IOException {
        File plugins = pasta.newFolder("plugins");
        escrever(new File(plugins, "CriCriReplay.dll"), "oficial");
        escrever(new File(plugins, "Virus.dll"), "x");
        escrever(new File(plugins, "CriCriReplay.dll.bak"), "x");
        escrever(new File(plugins, "sub/Escondido.dll"), "x");
        Set<String> oficiais = new HashSet<>(Arrays.asList("CriCriReplay.dll"));

        assertTrue(Integridade.limparPasta(plugins, oficiais));

        assertTrue(new File(plugins, "CriCriReplay.dll").isFile());
        assertFalse(new File(plugins, "Virus.dll").exists());
        assertFalse(new File(plugins, "CriCriReplay.dll.bak").exists());
        assertFalse(new File(plugins, "sub").exists());
    }

    @Test
    public void umaPastaComONomeOficialNaoFica() throws IOException {
        File plugins = pasta.newFolder("plugins");
        escrever(new File(plugins, "CriCriReplay.dll/Virus.dll"), "x");
        Set<String> oficiais = new HashSet<>(Arrays.asList("CriCriReplay.dll"));

        assertTrue(Integridade.limparPasta(plugins, oficiais));

        assertFalse(new File(plugins, "CriCriReplay.dll").exists());
    }

    @Test
    public void pastaQueNaoExisteNaoEProblema() {
        assertTrue(Integridade.limparPasta(new File(pasta.getRoot(), "nada"), new HashSet<>()));
    }

    @Test
    public void interopAlteradoNaMesmaVersaoEApagado() throws IOException {
        File interop = pasta.newFolder("interop");
        File memoria = new File(pasta.getRoot(), "memoria.txt");
        escrever(new File(interop, "Assembly-CSharp.dll"), "gerado");

        assertFalse("primeira vez: so lembra", Integridade.conferirInterop(interop, memoria, "1"));
        assertTrue(memoria.isFile());
        assertFalse("igual: nada muda", Integridade.conferirInterop(interop, memoria, "1"));

        escrever(new File(interop, "Assembly-CSharp.dll"), "trocado");
        assertTrue(Integridade.conferirInterop(interop, memoria, "1"));
        assertFalse(interop.exists());
        assertFalse(memoria.exists());
    }

    @Test
    public void jogoNovoNaoApagaOInterop() throws IOException {
        File interop = pasta.newFolder("interop");
        File memoria = new File(pasta.getRoot(), "memoria.txt");
        escrever(new File(interop, "Assembly-CSharp.dll"), "antigo");
        Integridade.conferirInterop(interop, memoria, "1");

        escrever(new File(interop, "Assembly-CSharp.dll"), "regenerado pelo BepInEx");
        assertFalse(Integridade.conferirInterop(interop, memoria, "2"));
        assertTrue(new File(interop, "Assembly-CSharp.dll").isFile());
        assertFalse("a proxima abertura lembra os arquivos novos", memoria.exists());
        assertEquals(1, interop.listFiles().length);
    }
}
