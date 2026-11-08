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
    public static void extractZipFromAssetsIfChanged(Context context, String assetName, File outputFolder, @Nullable String keepExistingPrefix) {
        File stamp = new File(context.getNoBackupFilesDir(), "extraido-" + assetName + ".txt");
        String key = appStamp(context) + "|" + outputFolder.getAbsolutePath();
        long start = System.currentTimeMillis();
        if (stampStillValid(stamp, key, outputFolder)) {
            Log.i(TAG, assetName + " already unpacked, checked in " + (System.currentTimeMillis() - start) + " ms");
            return;
        }
        List<String> names = new ArrayList<>();
        try (InputStream is = context.getAssets().open(assetName);
             ZipInputStream zis = new ZipInputStream(new BufferedInputStream(is))) {
            extractZipFromStream(zis, outputFolder, keepExistingPrefix, names);
        } catch (IOException e) {
            Log.e(TAG, "Failed to extract " + assetName + " from assets!", e);
            //noinspection ResultOfMethodCallIgnored
            stamp.delete();
            return;
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
            String patched = text.replaceAll("(?m)^UnityBaseLibrariesSource\\s*=.*$",
                    java.util.regex.Matcher.quoteReplacement("UnityBaseLibrariesSource = " + source));
            if (!patched.equals(text)) {
                try (OutputStream out = new FileOutputStream(config, false)) {
                    out.write(patched.getBytes(java.nio.charset.StandardCharsets.UTF_8));
                }
                Log.i(TAG, "UnityBaseLibrariesSource = '" + source + "' in " + config.getAbsolutePath());
            }
        } catch (IOException e) {
            Log.e(TAG, "Failed to patch BepInEx.cfg", e);
        }
    }

    public static boolean copyAssets(ApplicationInfo gameInfo, File outputFolder) {
        File marker = new File(outputFolder, ".fusion_copied");
        if (marker.isFile()) {
            return true;
        }

        List<String> apks = new ArrayList<>();
        if (gameInfo.sourceDir != null) apks.add(gameInfo.sourceDir);
        if (gameInfo.splitSourceDirs != null) Collections.addAll(apks, gameInfo.splitSourceDirs);

        String outputRoot;
        try {
            outputRoot = outputFolder.getCanonicalPath() + File.separator;
        } catch (IOException e) {
            Log.e(TAG, "Failed to resolve " + outputFolder, e);
            return false;
        }

        int copied = 0;
        byte[] buffer = new byte[64 * 1024];
        for (String apk : apks) {
            try (ZipFile zip = new ZipFile(apk)) {
                Enumeration<? extends ZipEntry> entries = zip.entries();
                while (entries.hasMoreElements()) {
                    ZipEntry entry = entries.nextElement();
                    String name = entry.getName();
                    if (entry.isDirectory() || !name.startsWith("assets/")) continue;

                    File target = new File(outputFolder, name.substring("assets/".length()));
                    if (!target.getCanonicalPath().startsWith(outputRoot)) continue;
                    if (target.isFile() && target.length() == entry.getSize()) continue;

                    File parent = target.getParentFile();
                    if (parent != null && !parent.isDirectory() && !parent.mkdirs()) {
                        throw new IOException("Failed to create " + parent);
                    }
                    try (InputStream in = zip.getInputStream(entry);
                         OutputStream out = new FileOutputStream(target)) {
                        int count;
                        while ((count = in.read(buffer)) > 0) {
                            out.write(buffer, 0, count);
                        }
                    }
                    copied++;
                }
            } catch (IOException e) {
                Log.e(TAG, "Failed to copy Unity Data assets from " + apk, e);
                return false;
            }
        }

        try {
            if (!marker.createNewFile()) {
                Log.w(TAG, "Failed to create asset copy marker: " + marker.getAbsolutePath());
            }
        } catch (IOException e) {
            Log.w(TAG, "Failed to create asset copy marker", e);
        }
        Log.i(TAG, "Successfully copied " + copied + " Unity Data assets to: " + outputFolder.getAbsolutePath());
        return true;
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
