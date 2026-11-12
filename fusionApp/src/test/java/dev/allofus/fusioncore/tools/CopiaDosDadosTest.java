package dev.allofus.fusioncore.tools;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

import android.content.pm.ApplicationInfo;

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
import java.util.ArrayList;
import java.util.List;
import java.util.zip.ZipEntry;
import java.util.zip.ZipOutputStream;

/**
 * A cópia dos dados do jogo (Utilities.copyAssets), com um "APK" de mentira: um zip com assets/bin/Data e um pacote de arte.
 * O que o CriCri pediu (11/11): menos demora na primeira abertura e menos erro de compatibilidade (jogo atualizado com dados velhos).
 */
@RunWith(RobolectricTestRunner.class)
@Config(sdk = 35)
public class CopiaDosDadosTest {
    @Rule public TemporaryFolder pasta = new TemporaryFolder();

    private File apk(String nome, String... nomeEConteudo) throws IOException {
        File f = new File(pasta.getRoot(), nome);
        try (ZipOutputStream zip = new ZipOutputStream(new FileOutputStream(f))) {
            for (int i = 0; i < nomeEConteudo.length; i += 2) {
                zip.putNextEntry(new ZipEntry(nomeEConteudo[i]));
                zip.write(nomeEConteudo[i + 1].getBytes(StandardCharsets.UTF_8));
                zip.closeEntry();
            }
        }
        return f;
    }

    private static ApplicationInfo jogo(File apk) {
        ApplicationInfo info = new ApplicationInfo();
        info.sourceDir = apk.getAbsolutePath();
        return info;
    }

    private static String ler(File f) throws IOException {
        try (InputStream in = new FileInputStream(f)) {
            byte[] b = new byte[(int) f.length()];
            int n = 0, r;
            while (n < b.length && (r = in.read(b, n, b.length - n)) > 0) n += r;
            return new String(b, 0, n, StandardCharsets.UTF_8);
        }
    }

    private static void escrever(File f, String texto) throws IOException {
        File pai = f.getParentFile();
        if (pai != null) pai.mkdirs();
        try (FileOutputStream out = new FileOutputStream(f)) {
            out.write(texto.getBytes(StandardCharsets.UTF_8));
        }
    }

    @Test
    public void soBinDataEhCopiado() throws IOException {
        File apk = apk("base.apk",
                "assets/bin/Data/Managed/Metadata/global-metadata.dat", "metadados-1",
                "assets/bin/Data/data.unity3d", "cenas",
                "assets/aa/Android/pacote-de-arte.bundle", "arte que ninguem le da copia",
                "lib/arm64-v8a/libil2cpp.so", "codigo");
        File copia = new File(pasta.getRoot(), "Assets");
        List<long[]> avisos = new ArrayList<>();

        assertTrue(Utilities.copyAssets(jogo(apk), copia, "100|1", 1L, (feitos, total) -> avisos.add(new long[] {feitos, total})));

        assertEquals("metadados-1", ler(new File(copia, "bin/Data/Managed/Metadata/global-metadata.dat")));
        assertEquals("cenas", ler(new File(copia, "bin/Data/data.unity3d")));
        assertFalse("o pacote de arte nao e copiado", new File(copia, "aa").exists());
        assertFalse(new File(copia, "lib").exists());
        assertTrue(new File(copia, Utilities.MARCA_DA_COPIA).isFile());
        assertFalse("copia terminada nao deixa o arquivo de andamento", new File(copia, Utilities.MARCA_DA_COPIA + ".andamento").exists());
        assertFalse("a tela recebe o andamento", avisos.isEmpty());
        assertEquals("o total avisado e o que faltava copiar", "metadados-1".length() + "cenas".length(), avisos.get(0)[1]);
    }

    @Test
    public void mesmaVersaoDoJogoNaoCopiaDeNovo() throws IOException {
        File apk = apk("base.apk", "assets/bin/Data/data.unity3d", "cenas");
        File copia = new File(pasta.getRoot(), "Assets");
        assertTrue(Utilities.copyAssets(jogo(apk), copia, "100|1", 1L, null));
        File arquivo = new File(copia, "bin/Data/data.unity3d");
        escrever(arquivo, "mexido");   // se copiasse de novo, voltaria a "cenas"

        assertTrue(Utilities.copyAssets(jogo(apk), copia, "100|1", 1L, null));

        assertEquals("mexido", ler(arquivo));
    }

    @Test
    public void jogoAtualizadoTrocaSoOQueMudouMesmoComOMesmoTamanho() throws IOException {
        File velho = apk("v1.apk",
                "assets/bin/Data/Managed/Metadata/global-metadata.dat", "metadados-1",
                "assets/bin/Data/igual.bin", "nao muda");
        File copia = new File(pasta.getRoot(), "Assets");
        assertTrue(Utilities.copyAssets(jogo(velho), copia, "100|1", 1L, null));
        File igual = new File(copia, "bin/Data/igual.bin");
        long dataDoIgual = igual.lastModified();
        assertTrue(igual.setLastModified(dataDoIgual - 60_000L));

        // a versao nova: o global-metadata.dat tem O MESMO TAMANHO e outro conteudo (o caso que a comparacao so por tamanho deixava velho)
        File novo = apk("v2.apk",
                "assets/bin/Data/Managed/Metadata/global-metadata.dat", "metadados-2",
                "assets/bin/Data/igual.bin", "nao muda",
                "assets/bin/Data/novo.bin", "chegou agora");
        List<long[]> avisos = new ArrayList<>();
        assertTrue(Utilities.copyAssets(jogo(novo), copia, "200|2", 2L, (feitos, total) -> avisos.add(new long[] {feitos, total})));

        assertEquals("metadados-2", ler(new File(copia, "bin/Data/Managed/Metadata/global-metadata.dat")));
        assertEquals("chegou agora", ler(new File(copia, "bin/Data/novo.bin")));
        assertEquals("o arquivo igual nao foi reescrito", dataDoIgual - 60_000L, igual.lastModified());
        assertEquals("so o que mudou conta no total", "metadados-2".length() + "chegou agora".length(), avisos.get(0)[1]);
    }

    @Test
    public void copiaAntigaDoAppDepoisDaUltimaAtualizacaoDoJogoServeESobrasSaem() throws IOException {
        File apk = apk("base.apk", "assets/bin/Data/data.unity3d", "cenas");
        File copia = new File(pasta.getRoot(), "Assets");
        // como o app 0.3.0 deixou: tudo copiado, marca vazia
        escrever(new File(copia, "bin/Data/data.unity3d"), "cenas");
        escrever(new File(copia, "aa/Android/pacote-de-arte.bundle"), "centenas de MB no celular");
        File irma = new File(pasta.getRoot(), "PersistentData/CriCri Replay/partidas/1/filme.ndjson");
        escrever(irma, "replay do jogador");
        File marca = new File(copia, Utilities.MARCA_DA_COPIA);
        escrever(marca, "");
        long atualizadoEm = marca.lastModified() - 5_000L;   // o jogo foi atualizado ANTES da copia

        assertTrue(Utilities.copyAssets(jogo(apk), copia, "100|1", atualizadoEm, null));

        assertEquals("cenas", ler(new File(copia, "bin/Data/data.unity3d")));
        assertFalse("o pacote de arte copiado a toa sai", new File(copia, "aa").exists());
        assertTrue("os dados do jogador ficam", irma.isFile());
        assertTrue(ler(marca).startsWith("100|1\n"));
    }

    @Test
    public void copiaAntigaDoAppDeAntesDaAtualizacaoDoJogoEhRefeita() throws IOException {
        File apk = apk("base.apk", "assets/bin/Data/Managed/Metadata/global-metadata.dat", "metadados-2");
        File copia = new File(pasta.getRoot(), "Assets");
        escrever(new File(copia, "bin/Data/Managed/Metadata/global-metadata.dat"), "metadados-1");   // mesmo tamanho, versao velha
        escrever(new File(copia, "bin/Data/sumiu-do-jogo.bin"), "x");
        File marca = new File(copia, Utilities.MARCA_DA_COPIA);
        escrever(marca, "");
        long atualizadoEm = marca.lastModified() + 5_000L;   // o jogo foi atualizado DEPOIS da copia

        assertTrue(Utilities.copyAssets(jogo(apk), copia, "200|2", atualizadoEm, null));

        assertEquals("metadados-2", ler(new File(copia, "bin/Data/Managed/Metadata/global-metadata.dat")));
        assertFalse("o que era da versao velha sai junto", new File(copia, "bin/Data/sumiu-do-jogo.bin").exists());
    }

    @Test
    public void copiaInterrompidaContinuaSoSeForDaMesmaVersao() throws IOException {
        File apk = apk("base.apk",
                "assets/bin/Data/a.bin", "aaaa",
                "assets/bin/Data/b.bin", "bbbb");
        File copia = new File(pasta.getRoot(), "Assets");
        // a pessoa saiu no meio: "a" ja estava inteiro, "b" pela metade, e o arquivo de andamento diz de que versao era
        escrever(new File(copia, "bin/Data/a.bin"), "aaaa");
        escrever(new File(copia, "bin/Data/b.bin"), "bb");
        escrever(new File(copia, Utilities.MARCA_DA_COPIA + ".andamento"), "100|1\n");
        List<long[]> avisos = new ArrayList<>();

        assertTrue(Utilities.copyAssets(jogo(apk), copia, "100|1", 1L, (feitos, total) -> avisos.add(new long[] {feitos, total})));
        assertEquals("so o que faltava", 4L, avisos.get(0)[1]);
        assertEquals("bbbb", ler(new File(copia, "bin/Data/b.bin")));

        // outra pasta, mesma situacao, mas o andamento era de OUTRA versao do jogo: nada do que esta la e aproveitado
        File outra = new File(pasta.getRoot(), "Assets2");
        escrever(new File(outra, "bin/Data/a.bin"), "AAAA");
        escrever(new File(outra, Utilities.MARCA_DA_COPIA + ".andamento"), "99|0\n");
        assertTrue(Utilities.copyAssets(jogo(apk), outra, "100|1", 1L, null));
        assertEquals("aaaa", ler(new File(outra, "bin/Data/a.bin")));
    }

    @Test
    public void entradaQueTentaSairDaPastaNaoEhCopiada() throws IOException {
        File apk = apk("base.apk",
                "assets/bin/Data/../../../fora.txt", "zip slip",
                "assets/bin/Data/ok.bin", "ok");
        File copia = new File(pasta.getRoot(), "dentro/Assets");

        assertTrue(Utilities.copyAssets(jogo(apk), copia, "100|1", 1L, null));

        assertTrue(new File(copia, "bin/Data/ok.bin").isFile());
        assertFalse(new File(pasta.getRoot(), "dentro/fora.txt").exists());
        assertFalse(new File(pasta.getRoot(), "fora.txt").exists());
    }

    @Test
    public void limparSobrasNaoSaiDaPastaDaCopia() throws IOException {
        File copia = new File(pasta.getRoot(), "jogo/Assets");
        escrever(new File(copia, "bin/Data/data.unity3d"), "fica");
        escrever(new File(copia, "aa/x.bundle"), "sai");
        escrever(new File(copia, "solto.txt"), "sai");
        escrever(new File(copia, Utilities.MARCA_DA_COPIA), "100|1\n");
        File bepinex = new File(pasta.getRoot(), "jogo/BepInEx/config/BepInEx.cfg");
        escrever(bepinex, "config do jogador");

        Utilities.limparSobras(copia);

        assertTrue(new File(copia, "bin/Data/data.unity3d").isFile());
        assertTrue(new File(copia, Utilities.MARCA_DA_COPIA).isFile());
        assertFalse(new File(copia, "aa").exists());
        assertFalse(new File(copia, "solto.txt").exists());
        assertTrue(bepinex.isFile());
    }
}
