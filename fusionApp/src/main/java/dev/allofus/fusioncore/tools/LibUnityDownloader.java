package dev.allofus.fusioncore.tools;

import android.util.Log;

import java.io.BufferedInputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.util.Properties;
import java.util.concurrent.ExecutionException;
import java.util.concurrent.FutureTask;

public final class LibUnityDownloader {
    private static final String TAG = "FusionCore";
    private static final String LIBUNITY_DOWNLOAD_URL = "https://github.com/All-Of-Us-Mods/FusionCore.UnityDependencies/releases/download/";
    private static final String LIBUNITY_CACHE_META_FILE = "libunity.cache.properties";
    // CriCri Mods (audit, 09/11): this is native code loaded in the process that holds the player's login, from a repository that is not ours.
    // Only the exact files below are accepted, { libunity.so, libunity.sym.so }. A version not listed uses the game's own libunity.
    // New Unity version: compare the "digest" of the release with the file, and add it here.
    private static final java.util.Map<String, String[]> ACEITOS = new java.util.HashMap<>();
    static {
        // the phone's Among Us (conferred on the device, 09/11: the file already in use there has this same hash)
        ACEITOS.put("2022.3.62f3|arm64-v8a", new String[] {
                "69e92df2e98dc260086e45e77a201df1d6f1759a146b7434667318da2cbbf5b6",
                "f73e93f8b29ef844e38564ffd5ffe42a2ae398d6f363a5ae45af4fc9078c4b36" });
        ACEITOS.put("2022.3.44f1|arm64-v8a", new String[] {
                "20ba0001f7b5fb8634cd195a9c690bc78dbbcc0f7bd98392efd210cf8c5aa287",
                "0bef84fd45d2a1a43b5182be7d1eddb950c399431a852a3a88597c076cc133ef" });
    }

    public interface DownloadProgressListener {
        void onDownloadStarted(String url, long totalBytes);
        void onDownloadProgress(long downloadedBytes, long totalBytes);
        void onDownloadFinished(boolean success, boolean usedCache);
    }

    public static boolean downloadAndCacheSafely(File outputDir,
                                                 String version,
                                                 String targetGameAbi,
                                                 DownloadProgressListener progressListener) {
        FutureTask<Boolean> task = new FutureTask<>(() -> downloadAndCache(outputDir, version, targetGameAbi, progressListener));
        Thread worker = new Thread(task, "FusionCore-LibUnityDownload");
        worker.start();

        try {
            return task.get();
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            Log.e(TAG, "Libunity download thread was interrupted", e);
            return false;
        } catch (ExecutionException e) {
            Log.e(TAG, "Libunity download failed", e.getCause() != null ? e.getCause() : e);
            return false;
        }
    }

    public static boolean downloadAndCache(File outputDir,
                                           String version,
                                           String targetGameAbi,
                                           DownloadProgressListener progressListener) {
        if (outputDir == null || version == null || version.trim().isEmpty()) {
            Log.e(TAG, "downloadAndCache called with invalid arguments");
            notifyDownloadFinished(progressListener, false, false);
            return false;
        }

        if (!outputDir.exists() && !outputDir.mkdirs()) {
            Log.e(TAG, "Failed to create output directory: " + outputDir.getAbsolutePath());
            notifyDownloadFinished(progressListener, false, false);
            return false;
        }

        String currentAbi = normalizeAbiForDownload(targetGameAbi);
        if (currentAbi == null) {
            Log.e(TAG, "Target game ABI is missing or unsupported: " + targetGameAbi);
            notifyDownloadFinished(progressListener, false, false);
            return false;
        }

        File outputLibUnity = new File(outputDir, "libunity.so");
        File outputLibUnitySym = new File(outputDir, "libunity.sym.so");
        File cacheMetaFile = new File(outputDir, LIBUNITY_CACHE_META_FILE);
        
        String downloadVersion = version.trim();
        String cacheKey = downloadVersion + "|" + currentAbi;
        String[] aceitos = ACEITOS.get(cacheKey);
        if (aceitos == null) {
            Log.w(TAG, "No pinned libunity for " + cacheKey + ": using the game's own");
            notifyDownloadFinished(progressListener, false, false);
            return false;
        }

        // CriCri Mods (11/11, ele: "demora muito pra carregar"): o sha256 dos dois arquivos (a libunity e os símbolos, dezenas de MB) era
        // refeito a cada JOGAR. Eles ficam na pasta interna do app, onde nenhum outro app escreve; depois de conferidos uma vez, o resultado
        // fica guardado com o tamanho e a data de cada um. Arquivo trocado (data ou tamanho diferente) = confere tudo de novo.
        File conferido = new File(outputDir, LIBUNITY_CONFERIDO_FILE);
        String identidade = identidade(aceitos, outputLibUnity, outputLibUnitySym);
        if (isCachedLibUnityValid(outputLibUnity, outputLibUnitySym, cacheMetaFile, cacheKey)) {
            boolean jaConferido = identidade.equals(lerTexto(conferido));
            if (jaConferido || (aceitos[0].equals(sha256(outputLibUnity)) && aceitos[1].equals(sha256(outputLibUnitySym)))) {
                if (!jaConferido) escreverTexto(conferido, identidade);
                Log.i(TAG, "Using cached libunity and symbols for " + cacheKey + " at " + outputDir.getAbsolutePath()
                        + (jaConferido ? " (already verified)" : " (verified now)"));
                notifyDownloadFinished(progressListener, true, true);
                return true;
            }
        }
        //noinspection ResultOfMethodCallIgnored
        conferido.delete();

        String baseUrl = LIBUNITY_DOWNLOAD_URL + downloadVersion + "/";
        String libUrl = baseUrl + "libunity.so." + currentAbi;
        String symUrl = baseUrl + "libunity.sym.so." + currentAbi;

        Log.i(TAG, "Downloading libunity and symbols from " + baseUrl);

        boolean libUnityDownloaded = downloadUrlToFile(libUrl, outputLibUnity, progressListener);
        if (!libUnityDownloaded) {
            notifyDownloadFinished(progressListener, false, false);
            return false;
        }

        boolean libUnitySymDownloaded = downloadUrlToFile(symUrl, outputLibUnitySym, progressListener);
        if (!libUnitySymDownloaded) {
            notifyDownloadFinished(progressListener, false, false);
            return false;
        }

        if (!aceitos[0].equals(sha256(outputLibUnity)) || !aceitos[1].equals(sha256(outputLibUnitySym))) {
            Log.e(TAG, "Downloaded libunity does not match the pinned hash: discarded");
            //noinspection ResultOfMethodCallIgnored
            outputLibUnity.delete();
            //noinspection ResultOfMethodCallIgnored
            outputLibUnitySym.delete();
            //noinspection ResultOfMethodCallIgnored
            cacheMetaFile.delete();
            notifyDownloadFinished(progressListener, false, false);
            return false;
        }

        if (!writeLibUnityCacheMeta(cacheMetaFile, cacheKey, outputLibUnity.length(), outputLibUnitySym.length())) {
            Log.w(TAG, "Downloaded files but failed to update cache metadata");
        }
        // acabaram de bater com o sha256 fixo: a próxima abertura não precisa ler tudo de novo
        escreverTexto(conferido, identidade(aceitos, outputLibUnity, outputLibUnitySym));

        Log.i(TAG, "Successfully downloaded libunity and symbols to " + outputDir.getAbsolutePath());
        notifyDownloadFinished(progressListener, true, false);
        return true;
    }

    private static boolean downloadUrlToFile(String urlString, File outputFile, DownloadProgressListener progressListener) {
        HttpURLConnection connection = null;
        File tempFile = new File(outputFile.getParentFile(), outputFile.getName() + ".download");
        
        try {
            connection = (HttpURLConnection) new URL(urlString).openConnection();
            connection.setRequestMethod("GET");
            connection.setConnectTimeout(15000);
            connection.setReadTimeout(30000);
            connection.setInstanceFollowRedirects(true);

            int statusCode = connection.getResponseCode();
            if (statusCode < 200 || statusCode >= 300) {
                Log.e(TAG, "Failed to download file from " + urlString + ", HTTP " + statusCode);
                return false;
            }

            long totalBytes = connection.getContentLengthLong();
            // CriCri Mods (11/11, revisão de segurança): o sha256 só é conferido DEPOIS de baixar. Sem teto, um servidor trocado ou com
            // defeito enchia o celular antes de ser recusado. Os arquivos aceitos têm 25 MB (libunity) e 11 MB (símbolos).
            if (totalBytes > LIMITE_DO_DOWNLOAD) {
                Log.e(TAG, "Refusing " + urlString + ": " + totalBytes + " bytes is more than any accepted file");
                return false;
            }
            notifyDownloadStarted(progressListener, urlString, totalBytes);

            byte[] buffer = new byte[8192];
            long downloadedBytes = 0L;
            long lastProgressDispatchMs = 0L;

            try (InputStream is = new BufferedInputStream(connection.getInputStream());
                 FileOutputStream fos = new FileOutputStream(tempFile, false)) {
                int count;
                while ((count = is.read(buffer)) != -1) {
                    fos.write(buffer, 0, count);
                    downloadedBytes += count;
                    if (downloadedBytes > LIMITE_DO_DOWNLOAD) {
                        Log.e(TAG, "Stopping " + urlString + ": more bytes than any accepted file");
                        return false;
                    }

                    long now = System.currentTimeMillis();
                    if (now - lastProgressDispatchMs >= 120L) {
                        notifyDownloadProgress(progressListener, downloadedBytes, totalBytes);
                        lastProgressDispatchMs = now;
                    }
                }
            }

            notifyDownloadProgress(progressListener, downloadedBytes, totalBytes);

            if (outputFile.exists() && !outputFile.delete()) {
                Log.e(TAG, "Failed to replace existing file: " + outputFile.getAbsolutePath());
                return false;
            }

            if (!tempFile.renameTo(outputFile)) {
                Log.e(TAG, "Failed to move downloaded file into place: " + outputFile.getAbsolutePath());
                return false;
            }

            return true;
        } catch (Exception e) {
            Log.e(TAG, "Failed to download " + urlString, e);
            return false;
        } finally {
            if (connection != null) {
                connection.disconnect();
            }
            if (tempFile.exists() && !tempFile.delete()) {
                Log.w(TAG, "Failed to clean temporary file: " + tempFile.getAbsolutePath());
            }
        }
    }

    private static final String LIBUNITY_CONFERIDO_FILE = "libunity.conferido.txt";
    /** Teto de cada arquivo baixado (o maior aceito hoje tem 25 MB). */
    static final long LIMITE_DO_DOWNLOAD = 96L * 1024 * 1024;

    /** Os dois sha256 aceitos + tamanho e data dos dois arquivos: o que tem de continuar igual para não conferir de novo. */
    static String identidade(String[] aceitos, File libUnity, File libUnitySym) {
        return aceitos[0] + '|' + aceitos[1] + '|' + libUnity.length() + '|' + libUnity.lastModified()
                + '|' + libUnitySym.length() + '|' + libUnitySym.lastModified();
    }

    /** Tests only: accepts one more pinned pair. */
    static void aceitarParaTeste(String chave, String shaLib, String shaSym) {
        ACEITOS.put(chave, new String[] { shaLib, shaSym });
    }

    private static String lerTexto(File f) {
        if (!f.isFile() || f.length() > 4096) return null;
        try (InputStream in = new FileInputStream(f)) {
            byte[] b = new byte[(int) f.length()];
            int n = 0, r;
            while (n < b.length && (r = in.read(b, n, b.length - n)) > 0) n += r;
            return new String(b, 0, n, java.nio.charset.StandardCharsets.UTF_8);
        } catch (IOException e) {
            return null;
        }
    }

    private static void escreverTexto(File f, String texto) {
        try (FileOutputStream out = new FileOutputStream(f, false)) {
            out.write(texto.getBytes(java.nio.charset.StandardCharsets.UTF_8));
        } catch (IOException e) {
            Log.w(TAG, "Could not remember the libunity check: " + e.getClass().getSimpleName());
        }
    }

    static String sha256(File arquivo) {
        try (InputStream in = new FileInputStream(arquivo)) {
            java.security.MessageDigest d = java.security.MessageDigest.getInstance("SHA-256");
            byte[] buffer = new byte[64 * 1024];
            int n;
            while ((n = in.read(buffer)) != -1) d.update(buffer, 0, n);
            StringBuilder hex = new StringBuilder();
            for (byte b : d.digest()) hex.append(String.format(java.util.Locale.ROOT, "%02x", b));
            return hex.toString();
        } catch (Exception e) {
            return "";
        }
    }

    private static void notifyDownloadStarted(DownloadProgressListener listener, String url, long totalBytes) {
        if (listener != null) {
            listener.onDownloadStarted(url, totalBytes);
        }
    }

    private static void notifyDownloadProgress(DownloadProgressListener listener, long downloadedBytes, long totalBytes) {
        if (listener != null) {
            listener.onDownloadProgress(downloadedBytes, totalBytes);
        }
    }

    private static void notifyDownloadFinished(DownloadProgressListener listener, boolean success, boolean usedCache) {
        if (listener != null) {
            listener.onDownloadFinished(success, usedCache);
        }
    }

    private static boolean isCachedLibUnityValid(File outputLibUnity, File outputLibUnitySym, File cacheMetaFile, String expectedCacheKey) {
        if (!outputLibUnity.exists() || !outputLibUnity.isFile() || outputLibUnity.length() <= 0) {
            return false;
        }
        if (!outputLibUnitySym.exists() || !outputLibUnitySym.isFile() || outputLibUnitySym.length() <= 0) {
            return false;
        }
        if (!cacheMetaFile.exists() || !cacheMetaFile.isFile()) {
            return false;
        }

        Properties meta = new Properties();
        try (FileInputStream fis = new FileInputStream(cacheMetaFile)) {
            meta.load(fis);
        } catch (IOException e) {
            Log.w(TAG, "Failed reading libunity cache metadata", e);
            return false;
        }

        String actualKey = meta.getProperty("cacheKey", "");
        if (!expectedCacheKey.equals(actualKey)) {
            return false;
        }

        try {
            long expectedSize = Long.parseLong(meta.getProperty("libunitySize", "0"));
            long expectedSymSize = Long.parseLong(meta.getProperty("libunitySymSize", "0"));
            return expectedSize > 0 && expectedSize == outputLibUnity.length() &&
                   expectedSymSize > 0 && expectedSymSize == outputLibUnitySym.length();
        } catch (NumberFormatException e) {
            Log.w(TAG, "Invalid libunity cache metadata size", e);
            return false;
        }
    }

    private static boolean writeLibUnityCacheMeta(File cacheMetaFile, String cacheKey, long libunitySize, long libunitySymSize) {
        Properties meta = new Properties();
        meta.setProperty("cacheKey", cacheKey);
        meta.setProperty("libunitySize", Long.toString(libunitySize));
        meta.setProperty("libunitySymSize", Long.toString(libunitySymSize));

        try (FileOutputStream fos = new FileOutputStream(cacheMetaFile, false)) {
            meta.store(fos, "libunity cache metadata");
            return true;
        } catch (IOException e) {
            Log.w(TAG, "Failed writing libunity cache metadata", e);
            return false;
        }
    }

    private static String normalizeAbiForDownload(String abiValue) {
        if (abiValue == null) {
            return null;
        }

        String normalized = abiValue.trim().toLowerCase();
        if (normalized.isEmpty()) {
            return null;
        }

        int slash = normalized.lastIndexOf('/');
        if (slash >= 0 && slash < normalized.length() - 1) {
            normalized = normalized.substring(slash + 1);
        }

        int backslash = normalized.lastIndexOf('\\');
        if (backslash >= 0 && backslash < normalized.length() - 1) {
            normalized = normalized.substring(backslash + 1);
        }

        return switch (normalized) {
            case "arm64", "aarch64", "arm64-v8a" -> "arm64-v8a";
            case "armeabi-v7a", "armeabi", "armv7" -> "armeabi-v7a";
            case "x86" -> "x86";
            case "x86_64", "x64" -> "x86_64";
            default -> null;
        };
    }
}
