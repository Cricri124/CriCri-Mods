package dev.allofus.fusioncore.tools;

import android.content.Context;
import android.content.pm.ApplicationInfo;
import android.os.Build;
import android.os.Environment;
import android.util.Log;
import android.view.View;
import android.view.WindowInsets;

import androidx.annotation.Nullable;

import java.io.BufferedInputStream;
import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.util.ArrayList;
import java.util.Collections;
import java.util.Enumeration;
import java.util.List;
import java.util.zip.ZipEntry;
import java.util.zip.ZipFile;
import java.util.zip.ZipInputStream;

public class Utilities {
    private static final String TAG = "FusionCore";

    private static volatile File baseDir;

    // CriCri Mods: everything lives in the app-specific external directory (Android/data/<app>/files),
    // which needs no storage permission, instead of /sdcard/FusionCore (MANAGE_EXTERNAL_STORAGE).
    public static void init(Context context) {
        if (baseDir != null) {
            return;
        }
        Context app = context.getApplicationContext() != null ? context.getApplicationContext() : context;
        File dir = app.getExternalFilesDir(null);
        if (dir == null) {
            Log.w(TAG, "External files dir unavailable, using internal files dir");
            dir = app.getFilesDir();
        }
        baseDir = dir;
        Log.i(TAG, "Storage directory: " + dir.getAbsolutePath());
    }

    public static File getExternalFusionCoreDirectory(@Nullable String targetPackage, @Nullable String Folder) {
        if (baseDir == null) {
            throw new IllegalStateException("Utilities.init(Context) was not called before using the storage directory");
        }
        File fusionStorage = baseDir;
        if (targetPackage != null) {
            fusionStorage = new File(fusionStorage, targetPackage);
        }
        if(Folder != null){
            fusionStorage = new File(fusionStorage, Folder);
        }
        if (!fusionStorage.exists()) {
            fusionStorage.mkdirs();
        }
        return fusionStorage;
    }
    public static void applyWindowInsets(View root, int basePadding) {
        root.setOnApplyWindowInsetsListener((v, insets) -> {
            int insetTop;
            int insetBottom;
            int insetLeft;
            int insetRight;

            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                android.graphics.Insets bars = insets.getInsets(WindowInsets.Type.systemBars());
                insetTop = bars.top;
                insetBottom = bars.bottom;
                insetLeft = bars.left;
                insetRight = bars.right;
            } else {
                insetTop = insets.getSystemWindowInsetTop();
                insetBottom = insets.getSystemWindowInsetBottom();
                insetLeft = insets.getSystemWindowInsetLeft();
                insetRight = insets.getSystemWindowInsetRight();
            }

            v.setPadding(
                    basePadding + insetLeft,
                    basePadding + insetTop,
                    basePadding + insetRight,
                    basePadding + insetBottom
            );
            return insets;
        });
        root.requestApplyInsets();
    }

    public static String formatVersionText(String versionName, long versionCode) {
        if (versionCode > 0L) {
            return "v" + versionName + " (" + versionCode + ")";
        }
        return "v" + versionName;
    }
    public static void extractZipFromStream(ZipInputStream zis, File outputFolder) throws IOException {
        extractZipFromStream(zis, outputFolder, null);
    }

    public static void extractZipFromStream(ZipInputStream zis, File outputFolder, @Nullable String keepExistingPrefix) throws IOException {
        extractZipFromStream(zis, outputFolder, keepExistingPrefix, null);
    }

    // CriCri Mods: the runtime zips (BepInEx 8 MB, .NET 67 MB unpacked) were unpacked on EVERY launch, 0.8 s + 3.8 s
    // on the CriCri's Redmi 14C (08/11). Now only when this app was installed/updated since the last time or a file
    // is missing or has another size: the list of unpacked files is kept with the stamp and checked (one stat per
    // file). Entries under keepExistingPrefix (BepInEx.cfg, changed on purpose) only have to exist.
    // (11/11) Devolve false quando NÃO deu para descompactar (antes só escrevia no log e o jogo abria quebrado, sem o .NET ou o BepInEx).
    public static boolean extractZipFromAssetsIfChanged(Context context, String assetName, File outputFolder, @Nullable String keepExistingPrefix) {
        File stamp = new File(context.getNoBackupFilesDir(), "extraido-" + assetName + ".txt");
        String key = appStamp(context) + "|" + outputFolder.getAbsolutePath();
        long start = System.currentTimeMillis();
        if (stampStillValid(stamp, key, outputFolder)) {
            Log.i(TAG, assetName + " already unpacked, checked in " + (System.currentTimeMillis() - start) + " ms");
            return true;
        }
        Abertura.marcarPrimeiraVez();
        List<String> names = new ArrayList<>();
        try (InputStream is = context.getAssets().open(assetName);
             ZipInputStream zis = new ZipInputStream(new BufferedInputStream(is))) {
            extractZipFromStream(zis, outputFolder, keepExistingPrefix, names);
        } catch (IOException e) {
            Log.e(TAG, "Failed to extract " + assetName + " from assets!", e);
            //noinspection ResultOfMethodCallIgnored
            stamp.delete();
            return false;
        }
        StringBuilder sb = new StringBuilder(key).append('\n');
        for (String name : names) {
            boolean keep = keepExistingPrefix != null && name.startsWith(keepExistingPrefix);
            sb.append(keep ? -1L : new File(outputFolder, name).length()).append('\t').append(name).append('\n');
        }
        try (OutputStream out = new FileOutputStream(stamp, false)) {
            out.write(sb.toString().getBytes(java.nio.charset.StandardCharsets.UTF_8));
        } catch (IOException e) {
            Log.w(TAG, "Failed to write " + stamp, e);
        }
        Log.i(TAG, "Unpacked " + assetName + " (" + names.size() + " files) in " + (System.currentTimeMillis() - start) + " ms");
        return true;
    }

    private static String appStamp(Context context) {
        try {
            var info = context.getPackageManager().getPackageInfo(context.getPackageName(), 0);
            long code = Build.VERSION.SDK_INT >= Build.VERSION_CODES.P ? info.getLongVersionCode() : info.versionCode;
            return code + ":" + info.lastUpdateTime;
        } catch (Exception e) {
            return "?" + System.currentTimeMillis();   // unknown: never matches, unpacks as before
        }
    }

    private static boolean stampStillValid(File stamp, String key, File outputFolder) {
        if (!stamp.isFile()) return false;
        try (java.io.BufferedReader reader = new java.io.BufferedReader(new java.io.FileReader(stamp))) {
            if (!key.equals(reader.readLine())) return false;
            String line;
            int files = 0;
            while ((line = reader.readLine()) != null) {
                int tab = line.indexOf('\t');
                if (tab <= 0) return false;
                long size = Long.parseLong(line.substring(0, tab));
                File f = new File(outputFolder, line.substring(tab + 1));
                if (!f.isFile() || (size >= 0 && f.length() != size)) {
                    Log.i(TAG, "Unpacking again: " + f + " is missing or changed");
                    return false;
                }
                files++;
            }
            return files > 0;
        } catch (Exception e) {
            return false;
        }
    }

    public static void extractZipFromStream(ZipInputStream zis, File outputFolder, @Nullable String keepExistingPrefix,
                                            @Nullable List<String> extractedFiles) throws IOException {
        if (!outputFolder.exists() && !outputFolder.mkdirs()) {
            throw new IOException("Failed to create output directory: " + outputFolder.getAbsolutePath());
        }
        byte[] buffer = new byte[8192];
            String outputRoot = outputFolder.getCanonicalPath() + File.separator;
            ZipEntry ze;
            while ((ze = zis.getNextEntry()) != null) {
                String entryName = ze.getName();
                if (entryName == null || entryName.isEmpty()) {
                    zis.closeEntry();
                    continue;
                }

                File target = new File(outputFolder, entryName);
                String targetPath = target.getCanonicalPath();

                if (!targetPath.startsWith(outputRoot)) {
                    throw new IOException("Blocked zip entry outside output folder: " + entryName);
                }

                if (ze.isDirectory()) {
                    if (!target.exists() && !target.mkdirs()) {
                        throw new IOException("Failed to create directory: " + targetPath);
                    }
                } else if (keepExistingPrefix != null && entryName.startsWith(keepExistingPrefix) && target.isFile()) {
                    Log.i(TAG, "Keeping existing " + targetPath);
                    if (extractedFiles != null) extractedFiles.add(entryName);
                } else {
                    if (extractedFiles != null) extractedFiles.add(entryName);
                    File parent = target.getParentFile();
                    if (parent != null && !parent.exists() && !parent.mkdirs()) {
                        throw new IOException("Failed to create parent directory: " + parent.getAbsolutePath());
                    }

                    try (FileOutputStream fos = new FileOutputStream(target)) {
                        int count;
                        while ((count = zis.read(buffer)) != -1) {
                            fos.write(buffer, 0, count);
                        }
                    }
                }

                zis.closeEntry();
            }
    }
    public static void extractZipFromAssets(Context context, String assetName, File outputFolder) {
        extractZipFromAssets(context, assetName, outputFolder, null);
    }

    public static void extractZipFromAssets(Context context, String assetName, File outputFolder, @Nullable String keepExistingPrefix) {
        try {
            try (InputStream is = context.getAssets().open(assetName);
                 ZipInputStream zis = new ZipInputStream(new BufferedInputStream(is))) {
                  extractZipFromStream(zis, outputFolder, keepExistingPrefix);
            }
        } catch (IOException e) {
            Log.e(TAG, "Failed to extract " + assetName + " from assets!", e);
        }
    }

    public static final String DEFAULT_UNITY_BASE_LIBRARIES_SOURCE = "https://unity.bepinex.dev/libraries/{VERSION}.zip";

    // CriCri Mods: an empty UnityBaseLibrariesSource makes BepInExFusion skip the Unity base libraries
    // download (Il2CppInteropManager.DownloadUnityAssemblies returns early) and generate the interop
    // without them. The config is kept between launches, so the value is written every time; a test build
    // that allows downloads puts the default back.
    public static void setUnityBaseLibrariesSource(File bepInExDir, String source) {
        setBepInExOption(bepInExDir, "UnityBaseLibrariesSource", source);
    }

    /** The file that keeps the dummy assemblies on a phone (see {@link #setFirstLaunchOptions}). */
    public static final String MANTER_DUMMY = "manter-dummy";

    /**
     * CriCri Mods (12/11, "a primeira vez demora"): two options of the BepInEx.cfg inside BepInEx-arm64.zip come ON, while plain
     * BepInEx ships them OFF, and no CriCri mod needs them. Measured on CriCri's Redmi (fresh install of 0.3.1), of the ~2 min 40 s
     * the game spends preparing the mods the first time: the method cross-reference scan took 8.9 s and writing the 94 dummy
     * assemblies (27 MB) about 14 s. Measured again with both OFF (same phone, same game): from Cpp2IL to the mods loaded
     * 161.5 s -> 133.2 s, the 4 mods load, no new error. The PC copy of the game already runs with ScanMethodRefs=false, and the tool that rebuilds the
     * phone's interop on the PC (cricriqol/android/gerar-interop) uses the same options.
     * The dummy assemblies are what that tool needs after a game update: a phone with config/manter-dummy keeps writing them
     * ("adb shell touch .../BepInEx/config/manter-dummy").
     * Only a new generation (first launch, game update) uses the options; an interop already made stays as it is.
     */
    public static void setFirstLaunchOptions(File bepInExDir) {
        setBepInExOption(bepInExDir, "ScanMethodRefs", "false");
        boolean manter = new File(bepInExDir, "config/" + MANTER_DUMMY).isFile();
        setBepInExOption(bepInExDir, "DumpDummyAssemblies", manter ? "true" : "false");
    }

    /**
     * Writes one existing "Key = value" line of BepInEx.cfg (only when it differs). The config/ folder is kept between launches
     * (extractZipFromAssetsIfChanged leaves it), so the app writes its options on every launch.
     */
    public static void setBepInExOption(File bepInExDir, String key, String value) {
        File config = new File(bepInExDir, "config/BepInEx.cfg");
        if (!config.isFile()) {
            Log.w(TAG, "BepInEx.cfg not found at " + config.getAbsolutePath());
            return;
        }
        try {
            String text;
            try (InputStream in = new java.io.FileInputStream(config)) {
                java.io.ByteArrayOutputStream bytes = new java.io.ByteArrayOutputStream();
                byte[] buffer = new byte[8192];
                int count;
                while ((count = in.read(buffer)) != -1) {
                    bytes.write(buffer, 0, count);
                }
                text = bytes.toString("UTF-8");
            }
            String patched = text.replaceAll("(?m)^" + java.util.regex.Pattern.quote(key) + "\\s*=.*$",
                    java.util.regex.Matcher.quoteReplacement(key + " = " + value));
            if (!patched.equals(text)) {
                try (OutputStream out = new FileOutputStream(config, false)) {
                    out.write(patched.getBytes(java.nio.charset.StandardCharsets.UTF_8));
                }
                Log.i(TAG, key + " = '" + value + "' in " + config.getAbsolutePath());
            }
        } catch (IOException e) {
            Log.e(TAG, "Failed to patch BepInEx.cfg", e);
        }
    }

    /** Sem espaço para copiar os dados do jogo: faltam = quantos bytes a pessoa precisa liberar. */
    public static final class SemEspaco extends IOException {
        public final long faltam;

        SemEspaco(long faltam) {
            super("sem espaco: faltam " + faltam + " bytes");
            this.faltam = faltam;
        }
    }

    /** O andamento da cópia (bytes), para a tela de espera. */
    public interface Andamento {
        void bytes(long feitos, long total);
    }

    static final String MARCA_DA_COPIA = ".fusion_copied";
    /** A única parte dos assets do jogo que alguém lê da cópia: é o que vira FUSION_GAME_DATA_DIR para o BepInEx. */
    static final String SO_ISTO = "assets/bin/Data/";
    private static final long FOLGA_DA_COPIA = 64L * 1024 * 1024;

    public static boolean copyAssets(ApplicationInfo gameInfo, File outputFolder) {
        try {
            return copyAssets(gameInfo, outputFolder, null, 0L, null);
        } catch (SemEspaco e) {
            Log.e(TAG, "No space to copy the Unity Data assets", e);
            return false;
        }
    }

    /**
     * Copia os dados da Unity (assets/bin/Data dos APKs do jogo) para a pasta que o BepInEx lê.
     *
     * CriCri Mods (11/11, ele: "erros de compatibilidade" e "demora muito pra carregar"). Três consertos sobre o que o Fusion Core fazia:
     *  1. A marca ".fusion_copied" nunca vencia: depois de o Among Us ATUALIZAR, a cópia continuava a da versão velha (o global-metadata.dat
     *     antigo com a libil2cpp nova = interop gerado errado, jogo que fecha). Agora a marca guarda a versão do jogo (carimbo) e o CRC de cada
     *     arquivo (o do próprio APK): jogo atualizado = só o que mudou é copiado de novo.
     *  2. Copiava TODOS os assets de todos os APKs do jogo, inclusive os pacotes de arte (perto de 1 GB no celular, na primeira abertura), mas
     *     só bin/Data é lido da cópia (o resto a Unity lê direto do APK). Agora copia só bin/Data, e o que sobrou das versões antigas é apagado.
     *  3. Sem espaço, copiava até encher e seguia "talvez funcione". Agora confere o espaço antes e diz quanto falta (SemEspaco).
     *
     * carimbo = versão + data de atualização do jogo (null = não sei: vale a marca que existir, como antes). atualizadoEm = quando o jogo foi
     * instalado/atualizado (ms), para reconhecer uma cópia das versões antigas do app que ainda serve.
     */
    public static boolean copyAssets(ApplicationInfo gameInfo, File outputFolder, @Nullable String carimbo, long atualizadoEm,
                                     @Nullable Andamento andamento) throws SemEspaco {
        File marker = new File(outputFolder, MARCA_DA_COPIA);
        java.util.Map<String, long[]> antes = new java.util.HashMap<>();   // nome -> {crc, tamanho} da última cópia
        if (marker.isFile()) {
            String carimboGuardado = lerMarca(marker, antes);
            if (carimbo == null || carimbo.equals(carimboGuardado)) {
                return true;
            }
            if (carimboGuardado.isEmpty() && atualizadoEm > 0 && marker.lastModified() >= atualizadoEm) {
                // cópia feita por uma versão antiga do app (marca vazia) DEPOIS da última atualização do jogo: está certa. Só passa a ter carimbo.
                Log.i(TAG, "Unity Data copy from an older app version still matches the game: keeping it");
                // (11/11, revisão) a limpeza vem ANTES do carimbo: se o processo morrer no meio dela, a próxima abertura
                // cai aqui de novo e termina de limpar (com o carimbo primeiro, o espaço nunca mais voltava)
                limparSobras(outputFolder);
                escreverMarca(marker, carimbo, antes);
                return true;
            }
            Log.i(TAG, "The game changed since the last copy of the Unity Data: copying what changed");
        }

        // Uma cópia interrompida (a pessoa saiu do app no meio) continua de onde parou, mas só se era DESTA versão do jogo: o arquivo
        // ".andamento" diz de qual era. Sem saber de que versão são os arquivos que estão lá (nenhuma lista com CRC), começa do zero.
        File emAndamento = new File(outputFolder, MARCA_DA_COPIA + ".andamento");
        boolean retomando = carimbo != null && emAndamento.isFile() && carimbo.equals(lerMarca(emAndamento, new java.util.HashMap<>()));
        if (carimbo != null && !retomando && antes.isEmpty()) {
            Integridade.apagar(new File(outputFolder, "bin"));
        }

        List<String> apks = new ArrayList<>();
        if (gameInfo.sourceDir != null) apks.add(gameInfo.sourceDir);
        if (gameInfo.splitSourceDirs != null) Collections.addAll(apks, gameInfo.splitSourceDirs);

        String outputRoot;
        try {
            if (!outputFolder.isDirectory() && !outputFolder.mkdirs()) throw new IOException("Failed to create " + outputFolder);
            outputRoot = outputFolder.getCanonicalPath() + File.separator;
        } catch (IOException e) {
            Log.e(TAG, "Failed to resolve " + outputFolder, e);
            return false;
        }

        // 1a passada: o que falta e quanto pesa (só o índice dos zips, não lê os arquivos)
        java.util.Map<String, long[]> agora = new java.util.LinkedHashMap<>();
        java.util.Set<String> faltam = new java.util.HashSet<>();
        long total = 0;
        // (11/11, revisão) "total" é o que vai ser copiado (a barra); "cresce" é o espaço que a cópia ainda vai ocupar. Um arquivo que
        // mudou é regravado POR CIMA do antigo: só a diferença de tamanho pesa. Contar o arquivo inteiro dava "Falta espaço" para quem
        // precisava de quase nada depois de o jogo atualizar.
        long cresce = 0;
        try {
            for (String apk : apks) {
                try (ZipFile zip = new ZipFile(apk)) {
                    Enumeration<? extends ZipEntry> entries = zip.entries();
                    while (entries.hasMoreElements()) {
                        ZipEntry entry = entries.nextElement();
                        String name = entry.getName();
                        if (entry.isDirectory() || !name.startsWith(SO_ISTO)) continue;
                        String relativo = name.substring("assets/".length());
                        File target = new File(outputFolder, relativo);
                        if (!target.getCanonicalPath().startsWith(outputRoot)) continue;
                        long[] novo = { entry.getCrc(), entry.getSize() };
                        agora.put(relativo, novo);
                        long[] velho = antes.get(relativo);
                        boolean mesmoCrc = velho != null && velho[0] == novo[0] && novo[0] != -1L && velho[1] == novo[1];
                        boolean igual = target.isFile() && target.length() == novo[1]
                                && (carimbo == null || mesmoCrc || (retomando && velho == null));
                        if (!igual) {
                            faltam.add(name);
                            total += Math.max(0L, novo[1]);
                            cresce += Math.max(0L, novo[1] - (target.isFile() ? target.length() : 0L));
                        }
                    }
                }
            }
        } catch (IOException e) {
            Log.e(TAG, "Failed to read the game's APKs", e);
            return false;
        }

        // (11/11, revisão) Os pacotes de arte que as versões antigas copiavam à toa (centenas de MB) saem ANTES da conta de espaço e
        // antes da cópia: quem estava sem espaço por causa deles não é barrado, e uma limpeza interrompida recomeça na próxima abertura.
        limparSobras(outputFolder);
        long livre = outputFolder.getUsableSpace();
        if (cresce > 0 && livre > 0 && livre < cresce + FOLGA_DA_COPIA) {
            throw new SemEspaco(cresce + FOLGA_DA_COPIA - livre);
        }

        if (total > 0) Abertura.marcarPrimeiraVez();
        if (carimbo != null && total > 0) escreverMarca(emAndamento, carimbo, new java.util.HashMap<>());

        int copied = 0;
        long feitos = 0;
        long ultimoAviso = 0;
        byte[] buffer = new byte[256 * 1024];
        if (andamento != null && total > 0) andamento.bytes(0, total);
        for (String apk : apks) {
            try (ZipFile zip = new ZipFile(apk)) {
                Enumeration<? extends ZipEntry> entries = zip.entries();
                while (entries.hasMoreElements()) {
                    ZipEntry entry = entries.nextElement();
                    String name = entry.getName();
                    if (!faltam.contains(name)) continue;

                    File target = new File(outputFolder, name.substring("assets/".length()));
                    File parent = target.getParentFile();
                    if (parent != null && !parent.isDirectory() && !parent.mkdirs()) {
                        throw new IOException("Failed to create " + parent);
                    }
                    try (InputStream in = zip.getInputStream(entry);
                         OutputStream out = new FileOutputStream(target)) {
                        int count;
                        while ((count = in.read(buffer)) > 0) {
                            out.write(buffer, 0, count);
                            feitos += count;
                            long agoraMs = System.currentTimeMillis();
                            if (andamento != null && agoraMs - ultimoAviso >= 150L) {
                                andamento.bytes(feitos, total);
                                ultimoAviso = agoraMs;
                            }
                        }
                    }
                    copied++;
                }
            } catch (IOException e) {
                Log.e(TAG, "Failed to copy Unity Data assets from " + apk, e);
                return false;
            }
        }

        escreverMarca(marker, carimbo, agora);
        //noinspection ResultOfMethodCallIgnored
        emAndamento.delete();
        limparSobras(outputFolder);
        Log.i(TAG, "Successfully copied " + copied + " Unity Data assets (" + (total / (1024 * 1024)) + " MB) to: " + outputFolder.getAbsolutePath());
        return true;
    }

    /** Lê a marca: devolve o carimbo ("" na marca vazia das versões antigas) e enche o mapa nome -> {crc, tamanho}. */
    private static String lerMarca(File marker, java.util.Map<String, long[]> arquivos) {
        try (java.io.BufferedReader reader = new java.io.BufferedReader(new java.io.InputStreamReader(
                new java.io.FileInputStream(marker), java.nio.charset.StandardCharsets.UTF_8))) {
            String carimbo = reader.readLine();
            if (carimbo == null) return "";
            String line;
            while ((line = reader.readLine()) != null) {
                String[] p = line.split("\t", 3);
                if (p.length != 3) continue;
                try {
                    arquivos.put(p[2], new long[] { Long.parseLong(p[0]), Long.parseLong(p[1]) });
                } catch (NumberFormatException ignored) {
                }
            }
            return carimbo;
        } catch (IOException e) {
            return "";
        }
    }

    private static void escreverMarca(File marker, @Nullable String carimbo, java.util.Map<String, long[]> arquivos) {
        StringBuilder sb = new StringBuilder(carimbo == null ? "" : carimbo).append('\n');
        if (carimbo != null) {
            for (java.util.Map.Entry<String, long[]> e : arquivos.entrySet()) {
                sb.append(e.getValue()[0]).append('\t').append(e.getValue()[1]).append('\t').append(e.getKey()).append('\n');
            }
        }
        File temp = new File(marker.getPath() + ".tmp");
        try (OutputStream out = new FileOutputStream(temp, false)) {
            out.write(sb.toString().getBytes(java.nio.charset.StandardCharsets.UTF_8));
        } catch (IOException e) {
            Log.w(TAG, "Failed to create asset copy marker", e);
            return;
        }
        //noinspection ResultOfMethodCallIgnored
        marker.delete();
        if (!temp.renameTo(marker)) Log.w(TAG, "Failed to create asset copy marker: " + marker.getAbsolutePath());
    }

    /**
     * Apaga da pasta da cópia tudo o que não é "bin" nem a marca: são os pacotes de arte que as versões antigas copiavam sem ninguém ler
     * (centenas de MB no celular). Só mexe DENTRO da pasta da cópia; os dados do jogador (PersistentData, replays) e o BepInEx são pastas
     * irmãs e não são tocados.
     */
    static void limparSobras(File outputFolder) {
        File[] itens = outputFolder.listFiles();
        if (itens == null) return;
        for (File item : itens) {
            String nome = item.getName();
            if (nome.equals("bin") || nome.startsWith(MARCA_DA_COPIA)) continue;
            if (Integridade.apagar(item)) Log.i(TAG, "Removed unused copy of game assets: " + nome);
        }
    }

    public static boolean deleteRecursive(File file) {
        if (file == null || !file.exists()) {
            return true;
        }

        if (file.isDirectory()) {
            File[] files = file.listFiles();
            if (files != null) {
                for (File f : files) {
                    if (!deleteRecursive(f)) {
                        return false;
                    }
                }
            }
        }

        return file.delete();
    }

    public static void extractZipFromInputStream(InputStream inputStream, File OutPutFolder) {
            try (ZipInputStream zis = new ZipInputStream(new BufferedInputStream(inputStream))) {
                extractZipFromStream(zis, OutPutFolder);
        } catch (IOException e) {
            Log.e(TAG, "Failed to extract zip from InputStream!", e);
        }
    }
}
