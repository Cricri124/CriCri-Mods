package dev.allofus.fusioncore.tools;

import android.content.Context;
import android.content.pm.PackageInfo;
import android.os.Build;
import android.util.Log;

import java.io.BufferedInputStream;
import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.Collections;
import java.util.HashSet;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.TreeMap;
import java.util.zip.ZipEntry;
import java.util.zip.ZipInputStream;

/**
 * CriCri Mods (09/11, the CriCri: "nao conseguir colocar nenhuma dll nao oficial do cricri"): before every launch, only official code stays in the
 * folders BepInEx loads from. They live in app-specific external storage: on Android 10 and below any app with the storage permission can write
 * there, and a person can use a cable or a file manager on any version.
 *
 * plugins, patchers and monomod keep only what ModsManager has just verified. core is compared with the zip inside the signed APK and repaired.
 * interop is generated on the phone from the game, so there is nothing to compare it with: it is remembered per game version and regenerated if it changes.
 *
 * This stops planted or swapped files. It does not stop someone who rebuilds the APK or has root on their own phone: the server must never trust the client.
 */
public final class Integridade {
    private static final String TAG = "CriCriMods";
    private static final String ZIP_DO_BEPINEX = "BepInEx-arm64.zip";
    private static final String NUCLEO = "core/";
    private static final String MEMORIA_INTEROP = "interop-conferido.txt";

    private Integridade() {}

    /** false = something unofficial could not be removed: the game must not open. */
    public static boolean conferir(Context context, File bepInExDir, Set<String> pluginsOficiais, String pacoteDoJogo) {
        boolean ok = limparPasta(new File(bepInExDir, "plugins"), pluginsOficiais);
        ok &= limparPasta(new File(bepInExDir, "patchers"), Collections.emptySet());
        ok &= limparPasta(new File(bepInExDir, "monomod"), Collections.emptySet());
        ok &= conferirNucleo(context, bepInExDir);
        conferirInterop(new File(bepInExDir, "interop"), new File(context.getNoBackupFilesDir(), MEMORIA_INTEROP), versaoDoJogo(context, pacoteDoJogo));
        return ok;
    }

    /** Leaves in the folder only the files named in permitidos (first level). Subfolders never stay. */
    static boolean limparPasta(File pasta, Set<String> permitidos) {
        File[] itens = pasta.listFiles();
        if (itens == null) return true;
        boolean ok = true;
        for (File item : itens) {
            if (item.isFile() && permitidos.contains(item.getName())) continue;
            Log.w(TAG, "Arquivo nao oficial removido de " + pasta.getName() + ": " + item.getName());
            ok &= apagar(item);
        }
        return ok;
    }

    static boolean apagar(File alvo) {
        // a link leading out of the folder is removed itself, never followed
        if (alvo.isDirectory() && !ehLink(alvo)) {
            File[] filhos = alvo.listFiles();
            if (filhos != null) for (File f : filhos) apagar(f);
        }
        return alvo.delete() || !alvo.exists();
    }

    private static boolean ehLink(File f) {
        try {
            File pai = f.getParentFile();
            if (pai == null) return false;
            return !new File(pai.getCanonicalFile(), f.getName()).getCanonicalPath().equals(new File(pai.getCanonicalFile(), f.getName()).getAbsolutePath());
        } catch (IOException e) {
            return true;
        }
    }

    private static boolean conferirNucleo(Context context, File bepInExDir) {
        Set<String> doZip = new HashSet<>();
        File nucleo = new File(bepInExDir, NUCLEO);
        try (ZipInputStream zip = new ZipInputStream(new BufferedInputStream(context.getAssets().open(ZIP_DO_BEPINEX)))) {
            ZipEntry e;
            while ((e = zip.getNextEntry()) != null) {
                String nome = e.getName();
                if (e.isDirectory() || !nome.startsWith(NUCLEO)) continue;
                if (nome.contains("..") || nome.contains("\\")) return false;
                byte[] oficial = lerTudo(zip);
                doZip.add(nome.substring(NUCLEO.length()));
                File destino = new File(bepInExDir, nome);
                if (destino.isFile() && destino.length() == oficial.length && sha256(destino).equals(sha256(oficial))) continue;
                Log.w(TAG, "Arquivo do nucleo alterado, restaurado: " + nome);
                File pai = destino.getParentFile();
                if (pai != null && !pai.isDirectory() && !pai.mkdirs()) return false;
                try (OutputStream out = new FileOutputStream(destino, false)) {
                    out.write(oficial);
                }
            }
        } catch (IOException e) {
            Log.e(TAG, "Nao consegui conferir o nucleo: " + e.getClass().getSimpleName());
            return false;
        }
        return limparNucleo(nucleo, "", doZip);
    }

    private static boolean limparNucleo(File pasta, String prefixo, Set<String> doZip) {
        File[] itens = pasta.listFiles();
        if (itens == null) return true;
        boolean ok = true;
        for (File item : itens) {
            String relativo = prefixo + item.getName();
            if (item.isDirectory() && !ehLink(item)) {
                ok &= limparNucleo(item, relativo + "/", doZip);
                continue;
            }
            if (doZip.contains(relativo)) continue;
            Log.w(TAG, "Arquivo nao oficial removido do nucleo: " + relativo);
            ok &= apagar(item);
        }
        return ok;
    }

    private static String versaoDoJogo(Context context, String pacote) {
        try {
            PackageInfo info = context.getPackageManager().getPackageInfo(pacote, 0);
            return (Build.VERSION.SDK_INT >= 28 ? info.getLongVersionCode() : info.versionCode) + "|" + info.lastUpdateTime;
        } catch (Exception e) {
            return "?";
        }
    }

    /**
     * interop is rebuilt by BepInEx when the game updates. So a new game version only forgets the memory, and the next launch, already with the new
     * files, remembers them. Same version with different files = someone changed them: all of it goes and BepInEx generates it again.
     */
    static boolean conferirInterop(File interop, File memoria, String versao) {
        Map<String, String> agora = hashes(interop);
        if (agora == null || agora.isEmpty()) return false;
        String guardado = lerTexto(memoria);
        if (guardado == null) {
            escreverTexto(memoria, formatar(versao, agora));
            return false;
        }
        String primeira = guardado.split("\n", 2)[0];
        if (!primeira.equals(versao)) {
            //noinspection ResultOfMethodCallIgnored
            memoria.delete();
            return false;
        }
        if (guardado.equals(formatar(versao, agora))) return false;
        Log.w(TAG, "interop alterado na mesma versao do jogo: gerando de novo");
        apagar(interop);
        //noinspection ResultOfMethodCallIgnored
        memoria.delete();
        return true;
    }

    private static String formatar(String versao, Map<String, String> arquivos) {
        StringBuilder sb = new StringBuilder(versao).append('\n');
        for (Map.Entry<String, String> e : arquivos.entrySet()) sb.append(e.getKey()).append('\t').append(e.getValue()).append('\n');
        return sb.toString();
    }

    private static Map<String, String> hashes(File pasta) {
        File[] itens = pasta.listFiles();
        if (itens == null) return null;
        Map<String, String> r = new TreeMap<>();
        try {
            for (File f : itens) if (f.isFile()) r.put(f.getName(), sha256(f));
        } catch (IOException e) {
            return null;
        }
        return r;
    }

    private static String lerTexto(File f) {
        if (!f.isFile()) return null;
        try (InputStream in = new FileInputStream(f)) {
            return new String(lerTudo(in), StandardCharsets.UTF_8);
        } catch (IOException e) {
            return null;
        }
    }

    private static void escreverTexto(File f, String texto) {
        try (OutputStream out = new FileOutputStream(f, false)) {
            out.write(texto.getBytes(StandardCharsets.UTF_8));
        } catch (IOException e) {
            Log.w(TAG, "Nao guardei a memoria do interop: " + e.getClass().getSimpleName());
        }
    }

    private static byte[] lerTudo(InputStream in) throws IOException {
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        byte[] buffer = new byte[64 * 1024];
        int n;
        while ((n = in.read(buffer)) != -1) out.write(buffer, 0, n);
        return out.toByteArray();
    }

    private static String sha256(File f) throws IOException {
        try (InputStream in = new FileInputStream(f)) {
            MessageDigest d = novo();
            byte[] buffer = new byte[64 * 1024];
            int n;
            while ((n = in.read(buffer)) != -1) d.update(buffer, 0, n);
            return hex(d.digest());
        }
    }

    private static String sha256(byte[] dados) {
        return hex(novo().digest(dados));
    }

    private static MessageDigest novo() {
        try {
            return MessageDigest.getInstance("SHA-256");
        } catch (Exception e) {
            throw new IllegalStateException(e);
        }
    }

    private static String hex(byte[] d) {
        StringBuilder s = new StringBuilder(d.length * 2);
        for (byte b : d) s.append(String.format(Locale.ROOT, "%02x", b));
        return s.toString();
    }
}
