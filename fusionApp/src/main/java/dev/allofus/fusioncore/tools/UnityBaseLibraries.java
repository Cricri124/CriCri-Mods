package dev.allofus.fusioncore.tools;

import android.util.Log;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.util.Collections;
import java.util.HashMap;
import java.util.Locale;
import java.util.Map;
import java.util.regex.Matcher;
import java.util.regex.Pattern;
import java.util.zip.ZipFile;

/**
 * The Unity base libraries (the UnityEngine.*.dll of the game's Unity version) that BepInEx needs the FIRST time it generates the
 * interop. BepInEx downloads them by itself from unity.bepinex.dev; when that fails the interop is not generated and the game
 * aborts at once ("Could not load file or assembly 'UnityEngine.CoreModule'").
 *
 * CriCri Mods (12/11): a fresh install crashed on CriCri's phone. Some Brazilian connections cannot reach the Cloudflare
 * addresses of unity.bepinex.dev (the connection times out); BepInEx kept trying for almost two minutes and then the game closed.
 * Now the app puts the zip in BepInEx/unity-libs BEFORE opening the game (BepInEx uses a zip with the same name instead of
 * downloading), taken from the CriCri BR site (only a zip whose sha256 is written below) or from unity.bepinex.dev, and checked.
 * If none of that works and there is no interop from an earlier launch, {@link #resolve} says so, and the app shows a message
 * instead of opening into a crash.
 */
public final class UnityBaseLibraries {
    private static final String TAG = "UnityBaseLibraries";
    private static final String SERVER = "https://unity.bepinex.dev/libraries/";
    private static final String DEFAULT_SOURCE = SERVER + "{VERSION}.zip";
    // The same zips on the CriCri BR site (a VPS of its own, not behind Cloudflare), with a simple name in /mods/.
    // Only used for a version whose sha256 is in KNOWN: the libraries end up inside the code the game runs.
    private static final String MIRROR = "https://cricribr.com.br/mods/unity-libs-";
    private static final String KEY = "UnityBaseLibrariesSource";
    private static final String AUTO_MARKER = ".fusion_base_libraries";
    static final String CORE = "UnityEngine.CoreModule.dll";
    private static final int CONNECT_TIMEOUT_MS = 10000;
    private static final int READ_TIMEOUT_MS = 30000;
    private static final long MAX_BYTES = 64L * 1024 * 1024;

    /** sha256 of the zips checked by hand. 2022.3.62 = the Among Us of the Play Store (17.x), 1,900,589 bytes, 66 DLLs. */
    static final Map<String, String> KNOWN;
    static {
        Map<String, String> known = new HashMap<>();
        known.put("2022.3.62", "575e7d600f69de8200ccf4db700b3ae6252366c22e8c3434c860e428974518d1");
        KNOWN = Collections.unmodifiableMap(known);
    }

    enum Download { OK, MISSING, FAILED }

    private UnityBaseLibraries() {}

    public static boolean resolve(File bepInExDir, String unityVersion) {
        return resolve(bepInExDir, unityVersion, null);
    }

    /**
     * Makes sure BepInEx finds the Unity base libraries without downloading them.
     *
     * @param onDownload called once, before the first download (the screen says "downloading a piece of the game")
     * @return false only when the game would surely crash: no zip could be put in place and there is no interop made before
     */
    public static boolean resolve(File bepInExDir, String unityVersion, Runnable onDownload) {
        Matcher match = Pattern.compile("^(\\d+)\\.(\\d+)\\.(\\d+)").matcher(unityVersion);
        if (!match.find()) return true;
        String majorMinor = match.group(1) + "." + match.group(2);
        int patch = Integer.parseInt(match.group(3));
        String exact = majorMinor + "." + patch;

        File config = new File(bepInExDir, "config/BepInEx.cfg");
        File marker = new File(config.getParentFile(), AUTO_MARKER);
        File libs = new File(bepInExDir, "unity-libs");
        String text;
        Matcher line;
        String current;
        try {
            text = readText(config);
            line = Pattern.compile("(?m)^" + KEY + "\\s*=\\s*(.*)$").matcher(text);
            if (!line.find()) return true;
            current = line.group(1).trim();
            String auto = marker.isFile() ? readText(marker).trim() : null;
            // a source written by hand in BepInEx.cfg (or none, the build without downloads): not ours to change
            if (!current.equals(DEFAULT_SOURCE) && !current.equals(auto)) return true;
        } catch (IOException e) {
            Log.w(TAG, "Could not read BepInEx.cfg", e);
            return true;
        }

        Runnable[] before = { onDownload };
        String chosen = null;
        Download exactResult = ensure(libs, exact, before);
        if (exactResult == Download.OK) {
            chosen = exact;
        } else {
            // A game on a Unity patch nobody built libraries for: the closest older patch (only when the server answered "no such
            // file"; with no connection, the one chosen on an earlier launch, if its zip is still here).
            String fallback = null;
            if (exactResult == Download.MISSING) {
                try {
                    fallback = findFallback(majorMinor, patch);
                } catch (IOException e) {
                    Log.w(TAG, "Could not list the Unity base libraries", e);
                }
            }
            if (fallback == null) fallback = earlierFallback(current);
            if (fallback != null && ensure(libs, fallback, before) == Download.OK) {
                chosen = fallback;
                Log.i(TAG, "No Unity base libraries for " + exact + ", using " + fallback);
            }
        }

        if (chosen != null) {
            String source = chosen.equals(exact) ? DEFAULT_SOURCE : SERVER + chosen + ".zip";
            try {
                if (!source.equals(current)) {
                    writeText(config, text.substring(0, line.start(1)) + source + text.substring(line.end(1)));
                }
                if (source.equals(DEFAULT_SOURCE)) {
                    //noinspection ResultOfMethodCallIgnored
                    marker.delete();
                } else {
                    writeText(marker, source);
                }
            } catch (IOException e) {
                Log.w(TAG, "Could not write BepInEx.cfg", e);
            }
            return true;
        }

        boolean interop = new File(bepInExDir, "interop/" + CORE).isFile();
        Log.e(TAG, "No Unity base libraries for " + exact
                + (interop ? ": the interop made on an earlier launch is used" : " and no interop yet: the game would crash"));
        return interop;
    }

    /** The zip of this version in unity-libs: the one already there if it passes the check, otherwise downloaded. */
    static Download ensure(File libs, String version, Runnable[] onDownload) {
        File zip = new File(libs, version + ".zip");
        String sha = KNOWN.get(version);
        if (zip.isFile()) {
            if (valid(zip, sha)) return Download.OK;
            Log.w(TAG, "Discarding a broken " + zip.getName() + " (" + zip.length() + " bytes)");
            //noinspection ResultOfMethodCallIgnored
            zip.delete();
        }
        if (!libs.isDirectory() && !libs.mkdirs()) {
            Log.w(TAG, "Could not create " + libs);
            return Download.FAILED;
        }
        if (onDownload != null && onDownload[0] != null) {
            onDownload[0].run();
            onDownload[0] = null;
        }
        // the site first: it answers for everyone who can use the app at all; unity.bepinex.dev is the one that some connections
        // in Brazil cannot reach
        if (sha != null && download(MIRROR + version + ".zip", zip, sha) == Download.OK) return Download.OK;
        return download(SERVER + version + ".zip", zip, sha);
    }

    private static Download download(String url, File dest, String sha) {
        File part = new File(dest.getPath() + ".part");
        HttpURLConnection connection = null;
        long started = System.currentTimeMillis();
        try {
            connection = open(url);
            connection.setRequestProperty("User-Agent", "CriCriMods");
            int code = connection.getResponseCode();
            if (code == HttpURLConnection.HTTP_NOT_FOUND) {
                Log.i(TAG, "Not found: " + url);
                return Download.MISSING;
            }
            if (code != HttpURLConnection.HTTP_OK) {
                Log.w(TAG, "HTTP " + code + " for " + url);
                return Download.FAILED;
            }
            if (connection.getContentLengthLong() > MAX_BYTES) {
                Log.w(TAG, "Too big: " + url);
                return Download.FAILED;
            }
            try (InputStream in = connection.getInputStream(); OutputStream out = new FileOutputStream(part, false)) {
                byte[] buffer = new byte[64 * 1024];
                long total = 0;
                int count;
                while ((count = in.read(buffer)) != -1) {
                    total += count;
                    if (total > MAX_BYTES) throw new IOException("more than " + MAX_BYTES + " bytes");
                    out.write(buffer, 0, count);
                }
            }
            if (!valid(part, sha)) {
                Log.w(TAG, "The zip from " + url + " did not pass the check (" + part.length() + " bytes)");
                return Download.FAILED;
            }
            //noinspection ResultOfMethodCallIgnored
            dest.delete();
            if (!part.renameTo(dest)) throw new IOException("could not rename " + part + " to " + dest.getName());
            Log.i(TAG, "Unity base libraries saved from " + url + " (" + dest.length() + " bytes, "
                    + (System.currentTimeMillis() - started) + " ms)");
            return Download.OK;
        } catch (IOException e) {
            Log.w(TAG, "Failed to download " + url + " after " + (System.currentTimeMillis() - started) + " ms", e);
            return Download.FAILED;
        } finally {
            if (connection != null) connection.disconnect();
            //noinspection ResultOfMethodCallIgnored
            part.delete();
        }
    }

    /** A real zip with UnityEngine.CoreModule.dll inside and, for a version in KNOWN, exactly the bytes checked by hand. */
    static boolean valid(File zip, String sha) {
        if (!zip.isFile() || zip.length() < 1024) return false;
        try {
            if (sha != null && !sha.equals(sha256(zip))) return false;
            try (ZipFile file = new ZipFile(zip)) {
                return file.getEntry(CORE) != null;
            }
        } catch (IOException e) {
            return false;
        }
    }

    /** The version of a fallback chosen on an earlier launch ("https://unity.bepinex.dev/libraries/2022.3.61.zip"), or null. */
    static String earlierFallback(String current) {
        Matcher m = Pattern.compile("^" + Pattern.quote(SERVER) + "(\\d+\\.\\d+\\.\\d+)\\.zip$").matcher(current);
        return m.find() ? m.group(1) : null;
    }

    static String sha256(File file) throws IOException {
        MessageDigest digest;
        try {
            digest = MessageDigest.getInstance("SHA-256");
        } catch (NoSuchAlgorithmException e) {
            throw new IOException(e);
        }
        try (InputStream in = new FileInputStream(file)) {
            byte[] buffer = new byte[64 * 1024];
            int count;
            while ((count = in.read(buffer)) != -1) digest.update(buffer, 0, count);
        }
        StringBuilder hex = new StringBuilder();
        for (byte b : digest.digest()) hex.append(String.format(Locale.ROOT, "%02x", b));
        return hex.toString();
    }

    private static String findFallback(String majorMinor, int patch) throws IOException {
        HttpURLConnection connection = open(SERVER);
        String listing;
        try (InputStream in = connection.getInputStream()) {
            listing = new String(readAll(in), StandardCharsets.UTF_8);
        } finally {
            connection.disconnect();
        }

        Matcher entry = Pattern.compile("href=\"\\./" + Pattern.quote(majorMinor) + "\\.(\\d+)\\.zip\"").matcher(listing);
        int best = -1;
        while (entry.find()) {
            int candidate = Integer.parseInt(entry.group(1));
            if (candidate < patch && candidate > best) best = candidate;
        }
        return best < 0 ? null : majorMinor + "." + best;
    }

    private static HttpURLConnection open(String url) throws IOException {
        HttpURLConnection connection = (HttpURLConnection) new URL(url).openConnection();
        connection.setConnectTimeout(CONNECT_TIMEOUT_MS);
        connection.setReadTimeout(READ_TIMEOUT_MS);
        return connection;
    }

    private static String readText(File file) throws IOException {
        try (InputStream in = new FileInputStream(file)) {
            return new String(readAll(in), StandardCharsets.UTF_8);
        }
    }

    private static void writeText(File file, String text) throws IOException {
        try (FileOutputStream out = new FileOutputStream(file, false)) {
            out.write(text.getBytes(StandardCharsets.UTF_8));
        }
    }

    private static byte[] readAll(InputStream in) throws IOException {
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        byte[] buffer = new byte[16 * 1024];
        int count;
        while ((count = in.read(buffer)) != -1) {
            out.write(buffer, 0, count);
        }
        return out.toByteArray();
    }
}
