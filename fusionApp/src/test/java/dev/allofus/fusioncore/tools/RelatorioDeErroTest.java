package dev.allofus.fusioncore.tools;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertNotNull;
import static org.junit.Assert.assertNull;

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

/**
 * O relatório de erro (12/11/2026). O primeiro relatório que chegou por e-mail dizia só "APP CRASH(EXCEPTION)": o arquivo com o erro de
 * Java (escrito na hora da queda) ficava de fora, porque o relatório pegava o arquivo mais novo, o que o Android escreve na abertura
 * seguinte e que nunca tem a pilha de um erro de Java. Aqui: o par certo é achado pelo instante da queda.
 */
@RunWith(RobolectricTestRunner.class)
@Config(sdk = 35)
public class RelatorioDeErroTest {
    @Rule public TemporaryFolder pasta = new TemporaryFolder();

    private File arquivo(String nome, String texto) throws IOException {
        File f = new File(pasta.getRoot(), nome);
        try (FileOutputStream out = new FileOutputStream(f)) {
            out.write(texto.getBytes(StandardCharsets.UTF_8));
        }
        return f;
    }

    @Test public void quedaDeJava_oErroGravadoNaHoraVaiJunto() throws IOException {
        long queda = 1762855112000L;
        File exit = arquivo("exit_info_" + queda + ".txt", "FusionCore Exit Log:\nreason=4 (APP CRASH(EXCEPTION))\n");
        File java = arquivo("java_crash_" + (queda - 350L) + ".txt", "FusionCore Java Crash Log:\njava.lang.IllegalStateException: Cannot prepare activity\n");
        assertEquals(java, RelatorioDeErro.javaDaMesmaQueda(pasta.getRoot(), exit));
    }

    @Test public void variasQuedas_pegaADoMesmoInstante() throws IOException {
        long queda = 1762855112000L;
        File exit = arquivo("exit_info_" + queda + ".txt", "x");
        arquivo("java_crash_" + (queda - 3_600_000L) + ".txt", "a de uma hora antes");
        File certa = arquivo("java_crash_" + (queda - 900L) + ".txt", "a desta queda");
        arquivo("java_crash_" + (queda + 600_000L) + ".txt", "a de dez minutos depois");
        assertEquals(certa, RelatorioDeErro.javaDaMesmaQueda(pasta.getRoot(), exit));
    }

    @Test public void semErroDeJavaPerto_naoInventa() throws IOException {
        long queda = 1762855112000L;
        File exit = arquivo("exit_info_" + queda + ".txt", "queda nativa");
        arquivo("java_crash_" + (queda - 3_600_000L) + ".txt", "outra queda, de uma hora antes");
        arquivo("java_crash_lixo.txt", "nome sem instante");
        assertNull(RelatorioDeErro.javaDaMesmaQueda(pasta.getRoot(), exit));
        assertNull(RelatorioDeErro.javaDaMesmaQueda(pasta.getRoot(), arquivo("exit_info_sem-numero.txt", "x")));
        assertNull(RelatorioDeErro.javaDaMesmaQueda(null, exit));
    }

    @Test public void oArquivoDeJavaEscritoUmPoucoDepois_tambemVale() throws IOException {
        // o relógio do sistema e o do app não batem no milissegundo: o instante do Android pode vir um pouco ANTES do arquivo
        long queda = 1762855112000L;
        File exit = arquivo("exit_info_" + queda + ".txt", "x");
        File java = arquivo("java_crash_" + (queda + 1_200L) + ".txt", "erro");
        assertNotNull(RelatorioDeErro.javaDaMesmaQueda(pasta.getRoot(), exit));
        assertEquals(java, RelatorioDeErro.javaDaMesmaQueda(pasta.getRoot(), exit));
    }

    @Test public void numeroDoNome() {
        assertEquals(1762855112000L, RelatorioDeErro.numeroDoNome("exit_info_1762855112000.txt", "exit_info_"));
        assertEquals(0L, RelatorioDeErro.numeroDoNome("exit_info_abc.txt", "exit_info_"));
        assertEquals(0L, RelatorioDeErro.numeroDoNome("java_crash_1.txt", "exit_info_"));
        assertEquals(0L, RelatorioDeErro.numeroDoNome(null, "exit_info_"));
    }
}
