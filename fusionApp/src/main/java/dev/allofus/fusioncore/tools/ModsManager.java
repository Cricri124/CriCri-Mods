package dev.allofus.fusioncore.tools;

import android.content.Context;
import android.content.SharedPreferences;
import android.util.Log;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.util.ArrayList;
import java.util.Collections;
import java.util.HashSet;
import java.util.List;
import java.util.Locale;
import java.util.Set;

// CriCri Mods: the mods ship inside the APK (assets/mods) and are never downloaded, so publishing or
// updating a mod means publishing a new version of the app. Google Play does not allow apps to fetch
// executable code from anywhere else, and this also keeps anyone from pushing a fake mod to players.
public final class ModsManager {
    private static final String TAG = "CriCriMods";
    private static final String PREFS_NAME = "cricri_mods";
    private static final String CATALOG_ASSET = "mods/mods.json";
    private static final String MODS_ASSET_DIR = "mods/";

    private ModsManager() {
    }

    public static final class Mod {
        public final String id;
        public final String name;
        public final String version;
        public final String file;
        public final String sha256;
        public final String description;
        public final boolean enabledByDefault;
        // Part of the app itself (e.g. the in-game options, the login): always installed and on, no switch.
        public final boolean hidden;
        // Shown in the list (with "Sempre ligado" when hidden): every mod by default.
        public final boolean listed;
        public final String author;
        // Key of the mod's icon drawn for the app (SelectorActivity.icone), "" = the generic one
        public final String icon;
        // The details sheet: what it does (one line each) and where it is in the game
        public final List<String> details;
        public final String where;
        public final List<String> gameVersions;
        // From the site's signed catalog (ModsRemotos), downloaded and checked; url = the versioned file there
        public final boolean remoto;
        public final String url;

        Mod(JSONObject json) throws JSONException {
            this(json, false);
        }

        Mod(JSONObject json, boolean remoto) throws JSONException {
            this.remoto = remoto;
            url = json.optString("url", "");
            id = json.getString("id");
            name = json.getString("name");
            version = json.getString("version");
            file = json.getString("file");
            sha256 = json.getString("sha256").toLowerCase(Locale.ROOT);
            description = json.optString("description", "");
            enabledByDefault = json.optBoolean("enabledByDefault", true);
            hidden = json.optBoolean("hidden", false);
            listed = json.optBoolean("listed", !hidden);
            author = json.optString("author", "");
            icon = json.optString("icon", "");
            where = json.optString("where", "");
            List<String> itens = new ArrayList<>();
            JSONArray detalhes = json.optJSONArray("details");
            if (detalhes != null) {
                for (int i = 0; i < detalhes.length(); i++) {
                    itens.add(detalhes.getString(i));
                }
            }
            details = Collections.unmodifiableList(itens);
            List<String> versions = new ArrayList<>();
            JSONArray array = json.optJSONArray("gameVersions");
            if (array != null) {
                for (int i = 0; i < array.length(); i++) {
                    versions.add(array.getString(i));
                }
            }
            gameVersions = Collections.unmodifiableList(versions);
        }

        // An empty list means the mod does not depend on the game version.
        public boolean supportsGame(String gameVersion) {
            if (gameVersions.isEmpty()) {
                return true;
            }
            if (gameVersion == null) {
                return false;
            }
            for (String supported : gameVersions) {
                if (gameVersion.contains(supported)) {
                    return true;
                }
            }
            return false;
        }
    }

    /** The mods to show and install: the ones inside the APK, updated by the site's signed catalog (ModsRemotos):
     *  a newer version that is already downloaded and checked replaces the packaged one, a mod only on the site is
     *  added once downloaded, and an entry marked "retirado" takes the mod out. */
    public static List<Mod> catalog(Context context) {
        List<Mod> mods = packaged(context);
        ModsRemotos.Catalogo site = ModsRemotos.atual(context);
        if (site == null) {
            return mods;
        }
        for (JSONObject entrada : site.mods) {
            try {
                String id = entrada.getString("id");
                int i = indexOf(mods, id);
                if (entrada.optBoolean("retirado", false)) {
                    if (i >= 0) mods.remove(i);
                    continue;
                }
                Mod remoto = new Mod(entrada, true);
                if (ModsRemotos.baixado(context, remoto.url, remoto.sha256) == null) continue;   // not here yet
                if (i < 0) {
                    mods.add(remoto);
                } else if (ModsRemotos.compararVersoes(remoto.version, mods.get(i).version) > 0) {
                    mods.set(i, remoto);
                }
            } catch (JSONException e) {
                Log.w(TAG, "Skipping a site catalog entry: " + e.getMessage());
            }
        }
        return mods;
    }

    /** Files of mods the site's catalog took out ("retirado"): removed from BepInEx/plugins at the next JOGAR. */
    static List<String> retiredFiles(Context context) {
        List<String> files = new ArrayList<>();
        ModsRemotos.Catalogo site = ModsRemotos.atual(context);
        if (site == null) return files;
        List<Mod> packaged = packaged(context);
        for (JSONObject entrada : site.mods) {
            if (!entrada.optBoolean("retirado", false)) continue;
            int i = indexOf(packaged, entrada.optString("id"));
            String file = i >= 0 ? packaged.get(i).file : entrada.optString("file", "");
            if (!file.isEmpty() && !file.contains("/") && !file.contains("\\")) files.add(file);
        }
        return files;
    }

    /** DLLs this app shipped before and no longer ships ("removidos" in assets/mods/mods.json): plain file names only. */
    static List<String> removedFiles(Context context) {
        List<String> files = new ArrayList<>();
        try (InputStream in = context.getAssets().open(CATALOG_ASSET)) {
            JSONArray array = new JSONObject(new String(readAll(in), StandardCharsets.UTF_8)).optJSONArray("removidos");
            if (array != null) {
                for (int i = 0; i < array.length(); i++) {
                    String file = array.optString(i, "");
                    if (file.endsWith(".dll") && !file.contains("/") && !file.contains("\\")) files.add(file);
                }
            }
        } catch (IOException | JSONException e) {
            Log.e(TAG, "Failed to read the removed mods list", e);
        }
        return files;
    }

    static List<Mod> packaged(Context context) {
        List<Mod> mods = new ArrayList<>();
        try (InputStream in = context.getAssets().open(CATALOG_ASSET)) {
            JSONArray array = new JSONObject(new String(readAll(in), StandardCharsets.UTF_8)).getJSONArray("mods");
            for (int i = 0; i < array.length(); i++) {
                mods.add(new Mod(array.getJSONObject(i)));
            }
        } catch (IOException | JSONException e) {
            Log.e(TAG, "Failed to read the mod catalog", e);
        }
        return mods;
    }

    private static int indexOf(List<Mod> mods, String id) {
        for (int i = 0; i < mods.size(); i++) if (mods.get(i).id.equals(id)) return i;
        return -1;
    }

    public static boolean isEnabled(Context context, Mod mod) {
        if (mod.hidden) {
            return true;
        }
        return prefs(context).getBoolean(mod.id + ":enabled", mod.enabledByDefault);
    }

    public static void setEnabled(Context context, Mod mod, boolean enabled) {
        prefs(context).edit().putBoolean(mod.id + ":enabled", enabled).apply();
    }

    // Puts the enabled mods in BepInEx/plugins and removes the disabled ones. Only the files listed in the
    // catalog are touched. A packaged DLL whose hash does not match the catalog is never installed.
    // Returns the files verified in the folder: nothing else may stay there (Integridade).
    public static Set<String> install(Context context, File bepInExDir) {
        Set<String> verificados = new HashSet<>();
        File plugins = new File(bepInExDir, "plugins");
        if (!plugins.isDirectory() && !plugins.mkdirs()) {
            Log.e(TAG, "Failed to create " + plugins.getAbsolutePath());
            return verificados;
        }

        // taken out by the site's catalog (a version that breaks): never left in the plugins folder
        for (String file : retiredFiles(context)) {
            File target = new File(plugins, file);
            if (target.isFile() && target.delete()) Log.i(TAG, "Removed " + file + " (retirado pelo catalogo do site)");
        }

        List<Mod> mods = catalog(context);
        // a mod this app shipped before and no longer ships (the Menu, 08/11): out of the plugins folder too
        for (String file : removedFiles(context)) {
            boolean voltou = false;
            for (Mod mod : mods) if (mod.file.equalsIgnoreCase(file)) { voltou = true; break; }
            if (voltou) continue;
            File target = new File(plugins, file);
            if (target.isFile() && target.delete()) Log.i(TAG, "Removed " + file + " (o app nao traz mais esse mod)");
        }

        for (Mod mod : mods) {
            File target = new File(plugins, mod.file);
            if (!isEnabled(context, mod)) {
                if (target.isFile() && !target.delete()) {
                    Log.w(TAG, "Failed to remove disabled mod " + target.getAbsolutePath());
                } else {
                    Log.i(TAG, mod.name + " is disabled");
                }
                continue;
            }

            try {
                // (11/11) o que já está na pasta com o sha256 do catálogo está instalado e conferido: não precisa abrir o do APK de novo
                if (target.isFile() && sha256(target).equals(mod.sha256)) {
                    Log.i(TAG, mod.name + " " + mod.version + " already installed");
                    verificados.add(mod.file);
                    continue;
                }
                // inside the APK, or downloaded from the site (ModsRemotos checked the signature and the sha256)
                byte[] packaged;
                if (mod.remoto) {
                    File baixado = ModsRemotos.baixado(context, mod.url, mod.sha256);
                    if (baixado == null) {
                        Log.e(TAG, "Refusing to install " + mod.url + ": the downloaded file is gone or changed");
                        continue;
                    }
                    try (InputStream in = new FileInputStream(baixado)) {
                        packaged = readAll(in);
                    }
                } else {
                    try (InputStream in = context.getAssets().open(MODS_ASSET_DIR + mod.file)) {
                        packaged = readAll(in);
                    }
                }
                String packagedHash = sha256(packaged);
                if (!packagedHash.equals(mod.sha256)) {
                    Log.e(TAG, "Refusing to install " + mod.file + ": sha256 " + packagedHash
                            + " does not match the catalog (" + mod.sha256 + ")");
                    continue;
                }

                File temp = new File(plugins, mod.file + ".tmp");
                try (OutputStream out = new FileOutputStream(temp, false)) {
                    out.write(packaged);
                }
                if (target.exists() && !target.delete()) {
                    throw new IOException("Failed to replace " + target.getAbsolutePath());
                }
                if (!temp.renameTo(target)) {
                    throw new IOException("Failed to move " + temp.getAbsolutePath());
                }
                Log.i(TAG, "Installed " + mod.name + " " + mod.version + " to " + target.getAbsolutePath());
                verificados.add(mod.file);
            } catch (IOException e) {
                Log.e(TAG, "Failed to install " + mod.name, e);
            }
        }
        return verificados;
    }

    private static SharedPreferences prefs(Context context) {
        return context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE);
    }

    private static String sha256(File file) throws IOException {
        try (InputStream in = new FileInputStream(file)) {
            return sha256(readAll(in));
        }
    }

    private static String sha256(byte[] data) {
        try {
            byte[] digest = MessageDigest.getInstance("SHA-256").digest(data);
            StringBuilder hex = new StringBuilder(digest.length * 2);
            for (byte b : digest) {
                hex.append(String.format(Locale.ROOT, "%02x", b));
            }
            return hex.toString();
        } catch (NoSuchAlgorithmException e) {
            throw new IllegalStateException(e);
        }
    }

    private static byte[] readAll(InputStream in) throws IOException {
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        byte[] buffer = new byte[64 * 1024];
        int count;
        while ((count = in.read(buffer)) != -1) {
            out.write(buffer, 0, count);
        }
        return out.toByteArray();
    }
}
