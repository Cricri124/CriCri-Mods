package dev.allofus.fusioncore.tools;

import android.content.Context;
import android.os.SystemClock;
import android.util.Log;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;
import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Date;
import java.util.List;
import java.util.Locale;

/**
 * CriCri Mods (11/11, ele: "tão falando que demora muito pra carregar"): a abertura MEDIDA. Cada etapa do JOGAR até o jogo diz quanto levou;
 * o total vai para o log ("CriCriAbertura") e para o relatório de erro (RelatorioDeErro), para o próximo teste no celular dizer ONDE está o tempo
 * em vez de alguém chutar. Antes disso só existia uma medida feita à mão no celular dele (08/11: 19,4 s do JOGAR ao menu).
 *
 * Tem duas metades com o mesmo relógio (tempo desde que o celular ligou, CLOCK_BOOTTIME):
 *  - a do app (esta classe): do JOGAR até entregar o jogo para a Unity;
 *  - a do nativo (fusion.cpp escreve abertura-nativo.txt): biblioteca do jogo carregada, il2cpp_init, .NET/BepInEx de pé.
 * E uma marca do fim: quando o aviso "Carregando" do jogo some (JniBridge.SetLoadingState(false)).
 *
 * Nada daqui vai para a rede e nada tem dado de conta: só nomes de etapa e milissegundos.
 */
public final class Abertura {
    private static final String TAG = "CriCriAbertura";
    private static final String ARQUIVO = "abertura.txt";
    public static final String ARQUIVO_NATIVO = "abertura-nativo.txt";
    private static final int GUARDAR = 6;

    /** Quem desenha a tela de espera (BootstrapActivity): nome curto da etapa, qual é e quantas são; e o andamento de uma etapa longa. */
    public interface Ouvinte {
        void etapa(String nome, int indice, int total);
        void progresso(long feito, long total);
    }

    /** As etapas, na ordem. O nome é a chave que a tela usa; não muda sem avisar quem desenha a tela. */
    public static final String CONFERIR = "conferir", DADOS = "dados", UNITY = "unity", PECAS = "pecas", MODS = "mods",
            SEGURANCA = "seguranca", CONTA = "conta", MAPA = "mapa", GANCHOS = "ganchos", ABRIR = "abrir";
    private static final String[] ORDEM = { CONFERIR, DADOS, UNITY, PECAS, MODS, SEGURANCA, CONTA, MAPA, GANCHOS, ABRIR };

    private static final Object trava = new Object();
    private static volatile Ouvinte ouvinte;
    private static long inicioMs;          // relógio desde o boot
    private static long inicioRelogio;     // hora de parede, só para a pessoa ler
    private static long etapaDesde;
    private static String etapaAtual;
    private static boolean primeira;
    private static boolean fechada;
    private static final List<String> nomes = new ArrayList<>();
    private static final List<Long> tempos = new ArrayList<>();
    private static File pasta, pastaNativo;
    private static volatile long carregadorSumiuMs;

    private Abertura() {}

    public static void ouvir(Ouvinte o) {
        ouvinte = o;
    }

    /** Começo do JOGAR. pastaDoNativo = onde o fusion.cpp escreve as marcas dele (a pasta de dados do jogo dentro do app). */
    public static void comecar(Context context, File pastaDoNativo) {
        synchronized (trava) {
            pasta = context.getFilesDir();
            pastaNativo = pastaDoNativo;
            inicioMs = SystemClock.elapsedRealtime();
            inicioRelogio = System.currentTimeMillis();
            etapaDesde = inicioMs;
            etapaAtual = null;
            primeira = false;
            fechada = false;
            nomes.clear();
            tempos.clear();
            carregadorSumiuMs = 0;
        }
        if (pastaDoNativo != null) {
            //noinspection ResultOfMethodCallIgnored
            pastaDoNativo.mkdirs();
            // as marcas da abertura passada não podem ser lidas como desta
            //noinspection ResultOfMethodCallIgnored
            new File(pastaDoNativo, ARQUIVO_NATIVO).delete();
        }
    }

    /** Esta abertura teve de preparar alguma coisa grande (copiar dados, descompactar, baixar): é "primeira vez" no resumo. */
    public static void marcarPrimeiraVez() {
        synchronized (trava) {
            primeira = true;
        }
    }

    /** Fecha a etapa anterior (guardando quanto levou) e abre esta. */
    public static void etapa(String nome) {
        int indice;
        synchronized (trava) {
            fecharEtapa();
            etapaAtual = nome;
            indice = indiceDe(nome);
        }
        Ouvinte o = ouvinte;
        if (o != null) o.etapa(nome, indice, ORDEM.length);
    }

    public static void progresso(long feito, long total) {
        Ouvinte o = ouvinte;
        if (o != null) o.progresso(feito, total);
    }

    /** Fim da parte do app: ok = o jogo foi entregue para a Unity. Grava a linha no arquivo. */
    public static void fim(boolean ok, String erro) {
        String linha;
        File destino;
        synchronized (trava) {
            if (inicioMs == 0 || fechada) return;   // cada abertura escreve uma linha só
            fechada = true;
            fecharEtapa();
            linha = montarLinha(ok, erro);
            destino = pasta == null ? null : new File(pasta, ARQUIVO);
        }
        Log.i(TAG, linha);
        if (destino != null) guardar(destino, linha);
    }

    /** O aviso "Carregando" do jogo sumiu: o menu está na tela. Só a primeira vez de cada abertura conta. */
    public static void carregadorSumiu() {
        File destino;
        long desde;
        synchronized (trava) {
            if (inicioMs == 0 || carregadorSumiuMs != 0) return;
            carregadorSumiuMs = SystemClock.elapsedRealtime();
            desde = carregadorSumiuMs - inicioMs;
            destino = pasta == null ? null : new File(pasta, ARQUIVO);
        }
        // quem chama está na linha principal do jogo abrindo: o arquivo é lido e gravado fora dela
        Thread t = new Thread(() -> {
            String linha = "  jogo na tela: +" + desde + " ms do JOGAR" + lerNativo();
            Log.i(TAG, linha.trim());
            if (destino != null) guardar(destino, linha);
        }, "cricri-abertura");
        t.setDaemon(true);
        t.start();
    }

    /** Uma abertura que não chegou ao jogo e não é culpa do aparelho: vira um arquivo que o RelatorioDeErro oferece na próxima vez. */
    public static void guardarFalha(String etapaOndeParou, String tecnico, Throwable erro) {
        try {
            File pastaDosErros = Utilities.getExternalFusionCoreDirectory(null, null);
            File arquivo = new File(pastaDosErros, "java_crash_" + System.currentTimeMillis() + ".txt");
            StringBuilder sb = new StringBuilder("CriCri Mods: o jogo nao abriu\n");
            sb.append("Etapa: ").append(etapaOndeParou == null ? "?" : etapaOndeParou).append('\n');
            sb.append("Motivo: ").append(tecnico == null ? "?" : umaLinha(tecnico)).append('\n');
            if (erro != null) sb.append(Log.getStackTraceString(erro));
            try (OutputStream out = new FileOutputStream(arquivo, false)) {
                out.write(sb.toString().getBytes(StandardCharsets.UTF_8));
            }
        } catch (Exception e) {
            Log.w(TAG, "nao guardei a falha: " + e.getClass().getSimpleName());
        }
    }

    /** A etapa aberta agora (para dizer onde uma falha parou), ou null. */
    public static String etapaAtual() {
        synchronized (trava) {
            return etapaAtual;
        }
    }

    /** As últimas aberturas, para o relatório de erro ("" quando ainda não houve nenhuma). */
    public static String resumo(Context context) {
        String texto = ler(new File(context.getFilesDir(), ARQUIVO));
        return texto == null ? "" : texto.trim();
    }

    // ---------------- por dentro ----------------

    private static void fecharEtapa() {
        long agora = SystemClock.elapsedRealtime();
        if (etapaAtual != null) {
            nomes.add(etapaAtual);
            tempos.add(agora - etapaDesde);
        }
        etapaDesde = agora;
        etapaAtual = null;
    }

    private static int indiceDe(String nome) {
        for (int i = 0; i < ORDEM.length; i++) if (ORDEM[i].equals(nome)) return i + 1;
        return 0;
    }

    private static String montarLinha(boolean ok, String erro) {
        StringBuilder sb = new StringBuilder();
        sb.append(new SimpleDateFormat("dd/MM HH:mm:ss", Locale.ROOT).format(new Date(inicioRelogio)));
        sb.append(primeira ? " primeira vez" : " de novo");
        sb.append(" | app ").append(SystemClock.elapsedRealtime() - inicioMs).append(" ms:");
        for (int i = 0; i < nomes.size(); i++) sb.append(' ').append(nomes.get(i)).append(' ').append(tempos.get(i));
        if (!ok) sb.append(" | NAO ABRIU").append(erro == null || erro.isEmpty() ? "" : ": " + umaLinha(erro));
        return sb.toString();
    }

    /** As marcas do nativo em relação ao JOGAR: " | nativo: bootstrap +1200 il2cpp_init +2800 ...". "" quando não há. */
    private static String lerNativo() {
        File f;
        long inicio;
        synchronized (trava) {
            if (pastaNativo == null) return "";
            f = new File(pastaNativo, ARQUIVO_NATIVO);
            inicio = inicioMs;
        }
        return formatarNativo(ler(f), inicio);
    }

    /** texto = linhas "nome=milissegundos desde o boot" (o que o fusion.cpp escreve). Só entram marcas depois do começo desta abertura. */
    static String formatarNativo(String texto, long inicio) {
        if (texto == null) return "";
        StringBuilder sb = new StringBuilder();
        for (String linha : texto.split("\n")) {
            int igual = linha.indexOf('=');
            if (igual <= 0) continue;
            try {
                long quando = Long.parseLong(linha.substring(igual + 1).trim());
                if (quando < inicio) continue;
                sb.append(' ').append(umaLinha(linha.substring(0, igual))).append(" +").append(quando - inicio);
            } catch (NumberFormatException ignored) {
            }
        }
        return sb.length() == 0 ? "" : " | nativo:" + sb;
    }

    private static String umaLinha(String s) {
        String limpo = s.replace('\n', ' ').replace('\r', ' ').trim();
        return limpo.length() > 160 ? limpo.substring(0, 160) : limpo;
    }

    /** Acrescenta a linha e fica só com as últimas aberturas (cada abertura = a linha dela e as linhas "  ..." que vêm depois). */
    static void guardar(File destino, String linha) {
        synchronized (trava) {
            String antes = ler(destino);
            List<String> linhas = new ArrayList<>();
            if (antes != null) for (String l : antes.split("\n")) if (!l.trim().isEmpty()) linhas.add(l);
            linhas.add(linha);
            int aberturas = 0, corte = 0;
            for (int i = linhas.size() - 1; i >= 0; i--) {
                if (!linhas.get(i).startsWith("  ")) aberturas++;
                if (aberturas > GUARDAR) {
                    corte = i + 1;
                    while (corte < linhas.size() && linhas.get(corte).startsWith("  ")) corte++;   // as linhas "  ..." da abertura que saiu
                    break;
                }
            }
            StringBuilder sb = new StringBuilder();
            for (int i = corte; i < linhas.size(); i++) sb.append(linhas.get(i)).append('\n');
            try (OutputStream out = new FileOutputStream(destino, false)) {
                out.write(sb.toString().getBytes(StandardCharsets.UTF_8));
            } catch (Exception e) {
                Log.w(TAG, "nao guardei a medida: " + e.getClass().getSimpleName());
            }
        }
    }

    private static String ler(File f) {
        if (f == null || !f.isFile() || f.length() > 64 * 1024) return null;
        try (InputStream in = new FileInputStream(f)) {
            ByteArrayOutputStream out = new ByteArrayOutputStream();
            byte[] b = new byte[4096];
            int n;
            while ((n = in.read(b)) > 0) out.write(b, 0, n);
            return out.toString("UTF-8");
        } catch (Exception e) {
            return null;
        }
    }
}
