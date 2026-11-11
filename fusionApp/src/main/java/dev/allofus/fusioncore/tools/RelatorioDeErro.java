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
        sb.append("==========\n");
        sb.append(ler(arquivo));
        return sb.length() > MAX_TEXTO ? sb.substring(0, MAX_TEXTO) + "\n[cortado]" : sb.toString();
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
