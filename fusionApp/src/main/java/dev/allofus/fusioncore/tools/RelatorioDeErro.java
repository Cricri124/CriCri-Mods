package dev.allofus.fusioncore.tools;

import android.app.ActivityManager;
import android.content.Context;
import android.content.SharedPreferences;
import android.content.pm.PackageInfo;
import android.os.Build;

import java.io.File;
import java.io.FileInputStream;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.util.Locale;

import dev.allofus.fusioncore.BuildConfig;

/**
 * CriCri Mods (11/11/2026): when the app or the game closed by itself, the next opening offers the report so the player can send it
 * (CriCri, after the launch: "tão reclamando da compatibilidade aí, tem gente conseguindo e outras não"). The crash files are written by
 * CrashDetector into the app's own folder, where a player can't reach them. Nothing is sent by the app: the player chooses to share or
 * copy. The report carries no account data: device, Android, ABIs, memory, versions, the switched-on mods and the crash text.
 */
public final class RelatorioDeErro {
    private static final String PREFS = "relatorio_de_erro";
    private static final String VISTO = "visto";
    private static final int MAX_TEXTO = 24000;

    private RelatorioDeErro() {}

    /** The newest crash file not offered yet, or null. */
    public static File novo(Context context) {
        File pasta;
        try { pasta = Utilities.getExternalFusionCoreDirectory(null, null); } catch (Exception e) { return null; }
        File[] arquivos = pasta.listFiles((d, nome) -> (nome.startsWith("exit_info_") || nome.startsWith("java_crash_")) && nome.endsWith(".txt"));
        if (arquivos == null || arquivos.length == 0) return null;
        File maisNovo = null;
        for (File f : arquivos) if (maisNovo == null || f.lastModified() > maisNovo.lastModified()) maisNovo = f;
        String visto = prefs(context).getString(VISTO, "");
        return maisNovo.getName().equals(visto) ? null : maisNovo;
    }

    public static void marcarVisto(Context context, File arquivo) {
        if (arquivo != null) prefs(context).edit().putString(VISTO, arquivo.getName()).apply();
    }

    public static String montar(Context context, File arquivo) {
        StringBuilder sb = new StringBuilder();
        sb.append("Relatório do CriCri Mods ").append(BuildConfig.VERSION_NAME).append('\n');
        sb.append("Celular: ").append(Build.MANUFACTURER).append(' ').append(Build.MODEL).append('\n');
        sb.append("Android: ").append(Build.VERSION.RELEASE).append(" (API ").append(Build.VERSION.SDK_INT).append(")\n");
        sb.append("ABIs: ").append(android.text.TextUtils.join(", ", Build.SUPPORTED_ABIS)).append(Build.SUPPORTED_64_BIT_ABIS.length > 0 ? " (64 bits)" : " (SÓ 32 BITS)").append('\n');
        try {
            ActivityManager am = context.getSystemService(ActivityManager.class);
            ActivityManager.MemoryInfo mi = new ActivityManager.MemoryInfo();
            am.getMemoryInfo(mi);
            sb.append(String.format(Locale.ROOT, "Memória: %.1f GB no total, %.1f GB livre agora\n", mi.totalMem / 1073741824.0, mi.availMem / 1073741824.0));
        } catch (Exception ignored) {}
        try {
            PackageInfo jogo = context.getPackageManager().getPackageInfo(BuildConfig.TARGET_PACKAGE, 0);
            sb.append("Among Us: ").append(jogo.versionName).append('\n');
        } catch (Exception e) {
            sb.append("Among Us: não instalado\n");
        }
        try {
            for (ModsManager.Mod mod : ModsManager.catalog(context)) {
                sb.append("Mod: ").append(mod.name).append(' ').append(mod.version)
                        .append(mod.hidden || ModsManager.isEnabled(context, mod) ? " (ligado)" : " (desligado)").append('\n');
            }
        } catch (Exception ignored) {}
        // (11/11, ele: "demora muito pra carregar") quanto cada etapa das últimas aberturas levou: só nomes de etapa e milissegundos
        try {
            String aberturas = Abertura.resumo(context);
            if (!aberturas.isEmpty()) sb.append("Aberturas (ms):\n").append(aberturas).append('\n');
        } catch (Exception ignored) {}
        sb.append("==========\n");
        sb.append(ler(arquivo));
        // (12/11) O erro de Java da mesma queda, quando existe (ver javaDaMesmaQueda): é ele que diz ONDE quebrou.
        if (arquivo.getName().startsWith("exit_info_")) {
            File java = javaDaMesmaQueda(arquivo.getParentFile(), arquivo);
            if (java != null) sb.append("\n==========\nO erro de Java gravado na hora da queda:\n").append(ler(java));
        }
        String texto = semSegredos(sb.toString());
        return texto.length() > MAX_TEXTO ? texto.substring(0, MAX_TEXTO) + "\n[cortado]" : texto;
    }

    /**
     * (12/11/2026, primeiro relatório que chegou por e-mail: um Samsung com Android 16, "APP CRASH(EXCEPTION)" e mais nada.)
     * Uma queda por erro de Java deixa DOIS arquivos: o "java_crash_&lt;ms&gt;.txt", escrito pelo CrashDetector na hora, com o erro inteiro
     * (a pilha de chamadas), e o "exit_info_&lt;ms&gt;.txt", escrito na abertura SEGUINTE com o que o Android guardou, e para erro de Java o
     * Android não guarda a pilha ("Java runtime crashes do not populate ApplicationExitInfo trace streams"). O relatório pegava só o
     * arquivo mais novo, que é sempre o segundo: saía sem o erro. Agora o primeiro vai junto.
     *
     * Devolve o "java_crash_" mais perto do instante da queda (o número no nome do exit_info), de 2 minutos antes a 30 s depois; null se
     * não houver (queda nativa, processo morto pelo sistema, ou o erro aconteceu antes de o CrashDetector ser ligado).
     */
    static File javaDaMesmaQueda(File pasta, File exitInfo) {
        if (pasta == null || exitInfo == null) return null;
        long quando = numeroDoNome(exitInfo.getName(), "exit_info_");
        if (quando <= 0) return null;
        File[] javas = pasta.listFiles((d, nome) -> nome.startsWith("java_crash_") && nome.endsWith(".txt"));
        if (javas == null) return null;
        File melhor = null;
        long menor = Long.MAX_VALUE;
        for (File f : javas) {
            long t = numeroDoNome(f.getName(), "java_crash_");
            if (t <= 0) continue;
            long antes = quando - t;   // o arquivo de Java é escrito logo ANTES de o processo morrer
            if (antes < -30_000L || antes > 120_000L) continue;
            if (Math.abs(antes) < menor) {
                menor = Math.abs(antes);
                melhor = f;
            }
        }
        return melhor;
    }

    /** "exit_info_1762855112345.txt" com o prefixo "exit_info_" -> 1762855112345; 0 se o nome não for assim. */
    static long numeroDoNome(String nome, String prefixo) {
        if (nome == null || !nome.startsWith(prefixo) || !nome.endsWith(".txt")) return 0;
        try {
            return Long.parseLong(nome.substring(prefixo.length(), nome.length() - ".txt".length()));
        } catch (NumberFormatException e) {
            return 0;
        }
    }

    private static final java.util.regex.Pattern JWT =java.util.regex.Pattern.compile("eyJ[A-Za-z0-9_-]{8,}\\.[A-Za-z0-9_-]{8,}\\.[A-Za-z0-9_-]{8,}");
    private static final java.util.regex.Pattern PARAMETRO = java.util.regex.Pattern.compile("(?i)([A-Za-z0-9_]*(?:token|chave|key|senha|password|authorization))(\\s*[=:]\\s*)([^\\s&\"',;]{8,})");

    /**
     * (11/11, revisão de segurança) O relatório sai do celular pela mão do jogador (compartilhar/copiar) e vai parar em grupo de WhatsApp e
     * Discord. O texto da queda vem do sistema (despejo do processo, últimas linhas do log): se um login do Google (um JWT, três pedaços
     * começando por "eyJ") ou um "token=..."/"chave=..." aparecer ali, sai trocado por [removido]. Nada disso é escrito pelo app hoje; é a
     * rede de segurança para o que o jogo ou um mod escrever no log.
     */
    static String semSegredos(String texto) {
        String limpo = JWT.matcher(texto).replaceAll("[login removido]");
        return PARAMETRO.matcher(limpo).replaceAll("$1$2[removido]");
    }

    private static String ler(File arquivo) {
        try (InputStream in = new FileInputStream(arquivo)) {
            byte[] b = new byte[(int) Math.min(arquivo.length(), MAX_TEXTO * 2L)];
            int n = 0, r;
            while (n < b.length && (r = in.read(b, n, b.length - n)) > 0) n += r;
            return new String(b, 0, n, StandardCharsets.UTF_8);
        } catch (Exception e) {
            return "(não deu para ler o arquivo do erro)";
        }
    }

    private static SharedPreferences prefs(Context context) {
        return context.getSharedPreferences(PREFS, Context.MODE_PRIVATE);
    }
}
