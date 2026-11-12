package dev.allofus.fusioncore.tools;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNotEquals;
import static org.junit.Assert.assertNotNull;
import static org.junit.Assert.assertNull;
import static org.junit.Assert.assertTrue;

import android.content.Context;

import androidx.test.core.app.ApplicationProvider;

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
import java.security.MessageDigest;
import java.util.Arrays;
import java.util.HashSet;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Properties;
import java.util.Random;

/**
 * O que roda por baixo do JOGAR (11/11, o CriCri: "erros de compatibilidade", "demora muito pra carregar", "vulnerabilidades"):
 * a conferência do aparelho, a medida da abertura, os resultados guardados (libunity, jogo sem ofuscar, núcleo do BepInEx) e o
 * relatório sem segredo. Tudo aqui roda no PC; o que só o celular responde está no relatório da missão.
 */
@RunWith(RobolectricTestRunner.class)
@Config(sdk = 35)
public class MotorDaAberturaTest {
    @Rule public TemporaryFolder pasta = new TemporaryFolder();

    private static final String[] ARM64 = { "arm64-v8a" };
    private static final long MB = 1024L * 1024L;

    private static void escrever(File f, byte[] dados) throws IOException {
        File pai = f.getParentFile();
        if (pai != null) pai.mkdirs();
        try (FileOutputStream out = new FileOutputStream(f)) {
            out.write(dados);
        }
    }

    private static byte[] ler(File f) throws IOException {
        try (InputStream in = new FileInputStream(f)) {
            byte[] b = new byte[(int) f.length()];
            int n = 0, r;
            while (n < b.length && (r = in.read(b, n, b.length - n)) > 0) n += r;
            return b;
        }
    }

    private static String sha256(byte[] dados) throws Exception {
        StringBuilder s = new StringBuilder();
        for (byte b : MessageDigest.getInstance("SHA-256").digest(dados)) s.append(String.format(Locale.ROOT, "%02x", b));
        return s.toString();
    }

    // ---------------- Aparelho: cada celular que não roda recebe UMA frase, e na ordem certa ----------------

    @Test
    public void aparelhoBomPassa() {
        assertNull(Aparelho.conferirPuro(35, ARM64, "arm64", true, true, 0, 10 * MB));
        assertNull("com espaco de sobra", Aparelho.conferirPuro(26, ARM64, "arm64", true, true, 200 * MB, 2000 * MB));
    }

    @Test
    public void cadaProblemaConhecidoTemOSeuCodigo() {
        assertEquals(Aparelho.SEM_64_BITS, Aparelho.conferirPuro(35, new String[0], "arm64", true, true, 0, 0).codigo);
        assertEquals(Aparelho.SEM_64_BITS, Aparelho.conferirPuro(35, new String[] { "x86_64" }, "x86_64", true, true, 0, 0).codigo);
        assertEquals(Aparelho.ANDROID_ANTIGO, Aparelho.conferirPuro(25, ARM64, "arm64", true, true, 0, 0).codigo);
        assertEquals(Aparelho.SEM_JOGO, Aparelho.conferirPuro(35, ARM64, null, false, false, 0, 0).codigo);
        assertEquals(Aparelho.JOGO_32_BITS, Aparelho.conferirPuro(35, ARM64, "arm", true, true, 0, 0).codigo);
        assertEquals(Aparelho.JOGO_OUTRO_PROCESSADOR, Aparelho.conferirPuro(35, new String[] { "x86_64", "arm64-v8a" }, "x86_64", true, true, 0, 0).codigo);
        assertEquals(Aparelho.JOGO_SEM_BIBLIOTECAS, Aparelho.conferirPuro(35, ARM64, "arm64", false, true, 0, 0).codigo);
        assertEquals(Aparelho.JOGO_SEM_BIBLIOTECAS, Aparelho.conferirPuro(35, ARM64, "arm64", true, false, 0, 0).codigo);
        assertEquals(Aparelho.SEM_ESPACO, Aparelho.conferirPuro(35, ARM64, "arm64", true, true, 200 * MB, 100 * MB).codigo);
    }

    @Test
    public void asFrasesSaoCurtasEDizemOQueFazer() {
        Aparelho.Problema[] todos = {
                Aparelho.conferirPuro(35, new String[0], "arm64", true, true, 0, 0),
                Aparelho.conferirPuro(25, ARM64, "arm64", true, true, 0, 0),
                Aparelho.conferirPuro(35, ARM64, null, false, false, 0, 0),
                Aparelho.conferirPuro(35, ARM64, "arm", true, true, 0, 0),
                Aparelho.conferirPuro(35, ARM64, "arm64", false, true, 0, 0),
                Aparelho.conferirPuro(35, ARM64, "arm64", true, true, 200 * MB, 100 * MB),
        };
        for (Aparelho.Problema p : todos) {
            assertNotNull(p);
            assertTrue(p.codigo + ": " + p.mensagem.length() + " letras", p.mensagem.length() <= 150);
            assertFalse("sem palavra de programador: " + p.mensagem, p.mensagem.matches("(?i).*(exception|null|package|context|abi\\b).*"));
        }
    }

    @Test
    public void faltaDeEspacoArredondaParaCima() {
        // precisa de 200 + 150 de folga, tem 100: faltam 250
        assertTrue(Aparelho.conferirPuro(35, ARM64, "arm64", true, true, 200 * MB, 100 * MB).mensagem.contains("250 MB"));
        assertTrue(Aparelho.faltaEspaco(1).contains("50 MB"));
        assertTrue(Aparelho.faltaEspaco(51 * MB).contains("100 MB"));
    }

    // ---------------- Abertura: a medida que vai para o log e para o relatório ----------------

    @Test
    public void aberturaGuardaAsEtapasEAsMarcasDoNativo() throws Exception {
        Context context = ApplicationProvider.getApplicationContext();
        File nativo = pasta.newFolder("dados-do-jogo");
        Abertura.comecar(context, nativo);
        Abertura.etapa(Abertura.CONFERIR);
        Abertura.etapa(Abertura.DADOS);
        Abertura.marcarPrimeiraVez();
        Abertura.etapa(Abertura.ABRIR);
        assertEquals(Abertura.ABRIR, Abertura.etapaAtual());
        Abertura.fim(true, null);
        Abertura.fim(false, "segunda chamada nao escreve outra linha");

        String resumo = Abertura.resumo(context);
        assertEquals("uma linha por abertura: " + resumo, 1, resumo.split("\n").length);
        assertTrue(resumo, resumo.contains("primeira vez"));
        assertTrue(resumo, resumo.matches("(?s).*app \\d+ ms: conferir \\d+ dados \\d+ abrir \\d+.*"));
        assertFalse(resumo, resumo.contains("NAO ABRIU"));
    }

    @Test
    public void aberturaQueFalhaDizOndeEPorQue() {
        Context context = ApplicationProvider.getApplicationContext();
        Abertura.comecar(context, null);
        Abertura.etapa(Abertura.DADOS);
        Abertura.fim(false, "Failed to copy\nthe Unity Data assets");

        String resumo = Abertura.resumo(context);
        assertTrue(resumo, resumo.contains("de novo"));
        assertTrue(resumo, resumo.contains("NAO ABRIU: Failed to copy the Unity Data assets"));
    }

    @Test
    public void marcasDoNativoSoContamDepoisDoJogar() {
        String texto = "biblioteca=900\nmotor_pronto=1500\nil2cpp_init=2000\nlixo\nbepinex_pronto=abc\n";
        assertEquals(" | nativo: motor_pronto +500 il2cpp_init +1000", Abertura.formatarNativo(texto, 1000));
        assertEquals("", Abertura.formatarNativo(null, 1000));
        assertEquals("", Abertura.formatarNativo("biblioteca=10\n", 1000));
    }

    @Test
    public void soAsUltimasAberturasFicamGuardadas() throws Exception {
        File arquivo = new File(pasta.getRoot(), "abertura.txt");
        for (int i = 1; i <= 9; i++) {
            Abertura.guardar(arquivo, "abertura " + i);
            Abertura.guardar(arquivo, "  jogo na tela: +" + i);
        }
        String texto = new String(ler(arquivo), StandardCharsets.UTF_8);
        assertFalse(texto, texto.contains("abertura 3\n"));
        assertTrue(texto, texto.startsWith("abertura 4\n  jogo na tela: +4\n"));
        assertTrue(texto, texto.endsWith("abertura 9\n  jogo na tela: +9\n"));
    }

    // ---------------- o jogo é ofuscado? resposta guardada, e a busca sem montar a lista de textos ----------------

    @Test
    public void buscaDeTextoNovaRespondeIgualAAntiga() {
        Random sorte = new Random(11);
        String[] procurados = { "il2cpp_init", "il2cpp_ini", "l2cpp_init", "abcd", "xil2cpp_init" };
        for (int rodada = 0; rodada < 300; rodada++) {
            byte[] dados = new byte[200 + sorte.nextInt(400)];
            for (int i = 0; i < dados.length; i++) {
                int tipo = sorte.nextInt(10);
                dados[i] = tipo < 2 ? 0 : tipo < 3 ? (byte) sorte.nextInt(256) : (byte) ("abcdil2p_xnt".charAt(sorte.nextInt(12)));
            }
            if (rodada % 3 == 0) {
                // planta o texto em posições diferentes: começo, meio (com e sem zero antes/depois), fim
                byte[] alvo = "il2cpp_init".getBytes(StandardCharsets.US_ASCII);
                int onde = rodada % 4 == 0 ? 0 : sorte.nextInt(dados.length - alvo.length - 1);
                System.arraycopy(alvo, 0, dados, onde, alvo.length);
                if (rodada % 2 == 0) dados[onde + alvo.length] = 0;
                if (onde > 0 && rodada % 5 != 0) dados[onde - 1] = 0;
            }
            for (String p : procurados) {
                assertEquals("rodada " + rodada + " procurando " + p,
                        Il2CppApiMapper.containsCStringPelaLista(dados, p), Il2CppApiMapper.containsCString(dados, p));
            }
        }
    }

    @Test
    public void jogoSemOfuscarSoEhLidoUmaVez() throws Exception {
        byte[] comMarca = "\0lixo\0il2cpp_init\0mais\0".getBytes(StandardCharsets.US_ASCII);
        File doJogo = new File(pasta.getRoot(), "jogo/libunity.so");
        File il2cpp = new File(pasta.getRoot(), "jogo/libil2cpp.so");
        File baixada = new File(pasta.getRoot(), "app/libunity.so");
        escrever(doJogo, comMarca);
        escrever(il2cpp, new byte[] {1, 2, 3});
        escrever(baixada, comMarca);

        assertTrue(Il2CppApiMapper.prepare(doJogo, il2cpp, baixada).isEmpty());
        File memo = new File(baixada.getParentFile(), "il2cpp-sem-ofuscar.txt");
        assertTrue("a resposta fica guardada ao lado da libunity", memo.isFile());

        // prova de que a segunda vez não lê os arquivos: com o MESMO tamanho e a MESMA data, o conteúdo virou lixo que antes daria erro
        long data = doJogo.lastModified();
        byte[] lixo = new byte[comMarca.length];
        Arrays.fill(lixo, (byte) 'z');
        escrever(doJogo, lixo);
        assertTrue(doJogo.setLastModified(data));
        assertTrue(Il2CppApiMapper.prepare(doJogo, il2cpp, baixada).isEmpty());

        // jogo atualizado (outro tamanho/data): a resposta guardada não vale mais e os arquivos são lidos de novo
        escrever(doJogo, "\0outra versao sem a marca\0".getBytes(StandardCharsets.US_ASCII));
        try {
            Il2CppApiMapper.prepare(doJogo, il2cpp, baixada);
        } catch (IOException esperado) {
            // um "jogo ofuscado" de mentira não tem a tabela de símbolos: o que importa é que foi LER de novo
        }
        assertFalse("resposta velha apagada", memo.isFile());
    }

    // ---------------- libunity baixada: conferida uma vez, depois só tamanho e data ----------------

    @Test
    public void libunityConferidaUmaVezNaoEhLidaDeNovo() throws Exception {
        File dir = pasta.newFolder("motor");
        byte[] lib = "biblioteca da unity".getBytes(StandardCharsets.UTF_8);
        byte[] sym = "simbolos".getBytes(StandardCharsets.UTF_8);
        File arquivoLib = new File(dir, "libunity.so"), arquivoSym = new File(dir, "libunity.sym.so");
        escrever(arquivoLib, lib);
        escrever(arquivoSym, sym);
        Properties meta = new Properties();
        meta.setProperty("cacheKey", "teste-9.9.9|arm64-v8a");
        meta.setProperty("libunitySize", Long.toString(lib.length));
        meta.setProperty("libunitySymSize", Long.toString(sym.length));
        try (FileOutputStream out = new FileOutputStream(new File(dir, "libunity.cache.properties"))) {
            meta.store(out, null);
        }
        LibUnityDownloader.aceitarParaTeste("teste-9.9.9|arm64-v8a", sha256(lib), sha256(sym));
        File conferido = new File(dir, "libunity.conferido.txt");

        assertTrue(LibUnityDownloader.downloadAndCache(dir, "teste-9.9.9", "arm64", null));
        assertTrue("o resultado fica guardado", conferido.isFile());
        String guardado = new String(ler(conferido), StandardCharsets.UTF_8);

        assertTrue("segunda vez: vale o guardado", LibUnityDownloader.downloadAndCache(dir, "teste-9.9.9", "arm64", null));
        assertEquals(guardado, new String(ler(conferido), StandardCharsets.UTF_8));

        // arquivo trocado (outra data): a identidade muda, então a conferência completa roda de novo
        assertTrue(arquivoLib.setLastModified(arquivoLib.lastModified() + 10_000L));
        assertNotEquals(guardado, LibUnityDownloader.identidade(new String[] { sha256(lib), sha256(sym) }, arquivoLib, arquivoSym));
        assertTrue("mesmo conteudo, data nova: confere de novo e passa", LibUnityDownloader.downloadAndCache(dir, "teste-9.9.9", "arm64", null));
        assertNotEquals(guardado, new String(ler(conferido), StandardCharsets.UTF_8));
    }

    // ---------------- Integridade: o núcleo do BepInEx com a lista oficial guardada ----------------

    @Test
    public void nucleoAlteradoEhRestauradoMesmoComAListaGuardada() throws Exception {
        Context context = ApplicationProvider.getApplicationContext();
        File bepinex = pasta.newFolder("BepInEx");
        File lista = new File(context.getNoBackupFilesDir(), "nucleo-oficial.txt");
        //noinspection ResultOfMethodCallIgnored
        lista.delete();

        long t0 = System.nanoTime();
        assertTrue(Integridade.conferir(context, bepinex, new HashSet<>(), "com.innersloth.spacemafia"));
        long primeira = System.nanoTime() - t0;
        File[] nucleo = new File(bepinex, "core").listFiles();
        assertNotNull(nucleo);
        assertTrue("o nucleo veio do zip do app: " + nucleo.length, nucleo.length > 20);
        assertTrue("a lista oficial fica guardada", lista.isFile());

        // a segunda leitura dos arquivos recém-escritos paga o antivírus do PC; por isso a medida é das seguintes, com tudo já lido uma vez
        assertTrue(Integridade.conferir(context, bepinex, new HashSet<>(), "com.innersloth.spacemafia"));
        long novo = Long.MAX_VALUE, antigo = Long.MAX_VALUE;
        for (int i = 0; i < 3; i++) {
            t0 = System.nanoTime();
            assertTrue(Integridade.conferir(context, bepinex, new HashSet<>(), "com.innersloth.spacemafia"));
            novo = Math.min(novo, System.nanoTime() - t0);
            // sem a lista guardada, o caminho é o de antes: descompacta o zip e faz o sha256 dos dois lados
            assertTrue(lista.delete());
            t0 = System.nanoTime();
            assertTrue(Integridade.conferir(context, bepinex, new HashSet<>(), "com.innersloth.spacemafia"));
            antigo = Math.min(antigo, System.nanoTime() - t0);
            assertTrue(lista.isFile());
        }
        System.out.println("[medida no PC] Integridade do nucleo (8 MB, 47 arquivos): criar " + primeira / 1_000_000
                + " ms; conferir pelo caminho de antes (zip) " + antigo / 1_000_000 + " ms; pelo novo (lista guardada, so o disco) "
                + novo / 1_000_000 + " ms");

        // um DLL do núcleo trocado por outro do MESMO tamanho, e um arquivo plantado
        File alvo = new File(bepinex, "core/BepInEx.Core.dll");
        byte[] oficial = ler(alvo);
        byte[] trocado = oficial.clone();
        trocado[trocado.length / 2] ^= 0x55;
        escrever(alvo, trocado);
        File plantado = new File(bepinex, "core/Virus.dll");
        escrever(plantado, new byte[] {1});

        assertTrue(Integridade.conferir(context, bepinex, new HashSet<>(), "com.innersloth.spacemafia"));

        assertTrue("o arquivo trocado voltou a ser o oficial", Arrays.equals(oficial, ler(alvo)));
        assertFalse(plantado.exists());
    }

    @Test
    public void listaGuardadaDeOutroAppNaoVale() throws Exception {
        Context context = ApplicationProvider.getApplicationContext();
        File bepinex = pasta.newFolder("BepInEx");
        assertTrue(Integridade.conferir(context, bepinex, new HashSet<>(), "com.innersloth.spacemafia"));
        File alvo = new File(bepinex, "core/BepInEx.Core.dll");
        byte[] oficial = ler(alvo);

        // alguém (com root) troca o DLL e escreve uma lista "oficial" com o sha256 do DLL trocado, mas sem o carimbo deste app
        byte[] trocado = oficial.clone();
        trocado[10] ^= 0x55;
        escrever(alvo, trocado);
        File lista = new File(context.getNoBackupFilesDir(), "nucleo-oficial.txt");
        escrever(lista, ("999999|123\nBepInEx.Core.dll\t" + sha256(trocado) + "\n").getBytes(StandardCharsets.UTF_8));

        assertTrue(Integridade.conferir(context, bepinex, new HashSet<>(), "com.innersloth.spacemafia"));
        assertTrue(Arrays.equals(oficial, ler(alvo)));
    }

    @Test
    public void variosArquivosEmParaleloDaoOMesmoSha256() throws Exception {
        File dir = pasta.newFolder("interop");
        Random sorte = new Random(7);
        File[] arquivos = new File[24];
        String[] esperados = new String[arquivos.length];
        for (int i = 0; i < arquivos.length; i++) {
            byte[] dados = new byte[1 + sorte.nextInt(300_000)];
            sorte.nextBytes(dados);
            arquivos[i] = new File(dir, "a" + i + ".dll");
            escrever(arquivos[i], dados);
            esperados[i] = sha256(dados);
        }
        List<String> achados = Integridade.sha256DeTodos(Arrays.asList(arquivos));
        assertEquals(Arrays.asList(esperados), achados);
        assertNull("arquivo que sumiu = nao deu para conferir", Integridade.sha256DeTodos(Arrays.asList(arquivos[0], new File(dir, "nao-existe.dll"))));
    }

    // ---------------- relatório de erro: sem login nem chave ----------------

    @Test
    public void relatorioNaoLevaLoginNemChave() {
        String jwt = "eyJhbGciOiJSUzI1NiIsImtpZCI6IjEyMyJ9.eyJpc3MiOiJhY2NvdW50cy5nb29nbGUuY29tIiwic3ViIjoiMTIzIn0.c2lnbmF0dXJlLWRlLW1lbnRpcmE";
        String texto = "abort message: GET https://accounts.innersloth.com/?store=google&token=" + jwt + "\n"
                + "solto no log: " + jwt + "\n"
                + "CRICRI_BR_CHAVE=abcdEFGH1234 e chave: segredo-do-mod-123\n"
                + "signal 11 (SIGSEGV), fault addr 0x7f00 em libunity.so\n";
        String limpo = RelatorioDeErro.semSegredos(texto);
        assertFalse(limpo, limpo.contains("eyJ"));
        assertFalse(limpo, limpo.contains("c2lnbmF0dXJl"));
        assertFalse(limpo, limpo.contains("segredo-do-mod-123"));
        assertFalse(limpo, limpo.contains("abcdEFGH1234"));
        assertTrue("o que serve para achar a queda fica", limpo.contains("signal 11 (SIGSEGV), fault addr 0x7f00 em libunity.so"));
        assertTrue(limpo, limpo.contains("token=[removido]") || limpo.contains("token=[login removido]"));
    }

    @Test
    public void relatorioTrazAsAberturasMedidas() throws Exception {
        Context context = ApplicationProvider.getApplicationContext();
        Abertura.comecar(context, null);
        Abertura.etapa(Abertura.SEGURANCA);
        Abertura.fim(true, null);
        File queda = new File(pasta.getRoot(), "exit_info_1.txt");
        escrever(queda, "FusionCore Exit Log:\nsignal 6".getBytes(StandardCharsets.UTF_8));

        String relatorio = RelatorioDeErro.montar(context, queda);

        assertTrue(relatorio, relatorio.contains("Aberturas (ms):"));
        assertTrue(relatorio, relatorio.contains("seguranca "));
        assertTrue(relatorio, relatorio.contains("signal 6"));
    }
}
