package dev.allofus.fusioncore.tools;

import android.app.Activity;
import android.content.Context;
import android.content.SharedPreferences;
import android.os.Build;
import android.util.Log;
import android.view.Display;
import android.view.Surface;
import android.view.SurfaceView;
import android.view.View;
import android.view.ViewGroup;
import android.view.Window;
import android.view.WindowManager;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.List;

import dev.allofus.fusioncore.BuildConfig;

// CriCri Mods: graphics options. Since 0.2 the player picks them INSIDE the game (Options > "Mods" tab), and the
// embedded plugin (embutidos/CriCriOtimizacao) owns BepInEx/config/cricri-mods-otimizacoes.json. The app only:
//  - writes what the screen can do (cricri-mods-tela.json: refresh rates), which Unity can't see on its own;
//  - moves the choices made in the app's old menu into that file once;
//  - asks Android for the display mode that matches the chosen frame rate when the game window comes up
//    (a 120 Hz phone stays at 60 Hz unless the app asks for the mode).
public final class Otimizacoes {
    private static final String TAG = "CriCriMods";
    private static final String PREFS_NAME = "cricri_otimizacoes";
    public static final String CONFIG_FILE = "cricri-mods-otimizacoes.json";
    public static final String TELA_FILE = "cricri-mods-tela.json";
    public static final String NOTICIAS_FILE = "cricri-mods-noticias.json";
    private static final int[] FPS_POSSIVEIS = {60, 90, 120};

    private Otimizacoes() {
    }

    // The refresh rates this screen can show, from the display's modes.
    public static List<Integer> taxasDaTela(Display display) {
        float maxima = 60f;
        if (display != null && Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
            for (Display.Mode mode : display.getSupportedModes()) {
                maxima = Math.max(maxima, mode.getRefreshRate());
            }
        }
        List<Integer> lista = new ArrayList<>();
        for (int fps : FPS_POSSIVEIS) {
            if (fps <= maxima + 0.5f) {
                lista.add(fps);
            }
        }
        return lista;
    }

    // Before each launch (BootstrapActivity).
    public static void prepararJogo(Context context, Display display, File bepInExDir) {
        File configDir = new File(bepInExDir, "config");
        if (!configDir.isDirectory() && !configDir.mkdirs()) {
            Log.e(TAG, "Failed to create " + configDir);
            return;
        }
        List<Integer> taxas = taxasDaTela(display);
        try {
            JSONObject tela = new JSONObject();
            tela.put("MaxHz", taxas.get(taxas.size() - 1));
            tela.put("Modos", new JSONArray(taxas));
            escrever(new File(configDir, TELA_FILE), tela.toString());
        } catch (IOException | JSONException e) {
            Log.e(TAG, "Failed to write " + TELA_FILE, e);
        }
        migrarEscolhasDoApp(context, new File(configDir, CONFIG_FILE));
        try {
            ModsRemotos.Catalogo site = ModsRemotos.atual(context);
            escrever(new File(configDir, NOTICIAS_FILE), site != null && site.noticias != null ? site.noticias.toString() : "[]");
        } catch (IOException e) {
            Log.e(TAG, "Failed to write " + NOTICIAS_FILE, e);
        }
    }

    // The app's old menu kept FPS/resolution in its preferences; bring them into the game's file once.
    private static void migrarEscolhasDoApp(Context context, File config) {
        SharedPreferences prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE);
        if (prefs.getBoolean("migrado", false)) return;
        try {
            if (!config.isFile() && (prefs.contains("fps") || prefs.contains("resolucao"))) {
                JSONObject json = new JSONObject();
                json.put("Fps", prefs.getInt("fps", 0));
                json.put("Resolucao", prefs.getInt("resolucao", 0));
                escrever(config, json.toString());
                Log.i(TAG, "Otimizacoes do app levadas pro jogo: " + json);
            }
            prefs.edit().putBoolean("migrado", true).apply();
        } catch (IOException | JSONException e) {
            Log.e(TAG, "Failed to migrate " + config, e);
        }
    }

    // When a game window is created or resumed: the display mode with the chosen frame rate (or the closest above).
    public static void pedirTaxaDaTela(Activity activity) {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.M) return;
        try {
            int fps = fpsEscolhido();
            Window window = activity.getWindow();
            Display display = activity.getWindowManager().getDefaultDisplay();
            if (window == null || display == null || fps <= 0) return;
            Display.Mode atual = display.getMode();
            Display.Mode melhor = null;
            for (Display.Mode m : display.getSupportedModes()) {
                if (m.getPhysicalWidth() != atual.getPhysicalWidth() || m.getPhysicalHeight() != atual.getPhysicalHeight()) continue;
                if (melhor == null) { melhor = m; continue; }
                float r = m.getRefreshRate(), b = melhor.getRefreshRate();
                boolean rServe = r + 0.5f >= fps, bServe = b + 0.5f >= fps;
                // the lowest rate that still reaches the target; if none reaches it, the highest one
                if ((rServe && (!bServe || r < b)) || (!rServe && !bServe && r > b)) melhor = m;
            }
            if (melhor == null) return;
            WindowManager.LayoutParams lp = window.getAttributes();
            if (lp.preferredDisplayModeId != melhor.getModeId()) {
                lp.preferredDisplayModeId = melhor.getModeId();
                window.setAttributes(lp);
                Log.i(TAG, "Tela: modo " + melhor.getModeId() + " a " + melhor.getRefreshRate() + " Hz (fps escolhido " + fps + ")");
            }
            // First phone test (08/11): the window asked for 120 Hz and the game still ran at 60. Also tell the
            // Unity drawing surface its frame rate, the hint Android uses per surface (Android 11+).
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                final float taxa = fps;
                final View decor = window.getDecorView();
                decor.postDelayed(() -> marcarSuperficie(decor, taxa), 1500);
            }
        } catch (Exception e) {
            Log.w(TAG, "pedirTaxaDaTela: " + e);
        }
    }

    // NOT DONE, on purpose (phone test 08/11 04:38): remaking the game's SurfaceView (hidden and shown) to make a
    // runtime resolution change take. The new surface stayed 1640x720 (the size did not take) and Unity's still of
    // the last frame stayed on top for good (it is removed only by the real pause/resume path): the game kept
    // running behind a frozen picture. The slider's size takes when the game starts (the plugin says so on screen).

    // The game's SurfaceView (Unity draws in it), wherever it is in the window.
    private static void marcarSuperficie(View v, float fps) {
        try {
            if (v instanceof SurfaceView) {
                Surface s = ((SurfaceView) v).getHolder().getSurface();
                if (s != null && s.isValid()) {
                    if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                        s.setFrameRate(fps, Surface.FRAME_RATE_COMPATIBILITY_FIXED_SOURCE, Surface.CHANGE_FRAME_RATE_ALWAYS);
                    } else {
                        s.setFrameRate(fps, Surface.FRAME_RATE_COMPATIBILITY_FIXED_SOURCE);
                    }
                    Log.i(TAG, "Tela: superficie do jogo marcada pra " + fps + " Hz");
                }
                return;
            }
            if (v instanceof ViewGroup) {
                ViewGroup g = (ViewGroup) v;
                for (int i = 0; i < g.getChildCount(); i++) marcarSuperficie(g.getChildAt(i), fps);
            }
        } catch (Exception e) {
            Log.w(TAG, "marcarSuperficie: " + e);
        }
    }

    // The frame rate the plugin will use (0 = the game's own, also when the master switch is off).
    private static int fpsEscolhido() {
        try {
            File base = Utilities.getExternalFusionCoreDirectory(BuildConfig.TARGET_PACKAGE, null);
            File config = new File(base, "BepInEx/config/" + CONFIG_FILE);
            if (!config.isFile()) return 0;
            JSONObject json = new JSONObject(ler(config));
            if (!json.optBoolean("Ligada", true)) return 0;
            return json.optInt("Fps", 0);
        } catch (Exception e) {
            return 0;
        }
    }

    private static void escrever(File arquivo, String texto) throws IOException {
        try (OutputStream out = new FileOutputStream(arquivo, false)) {
            out.write(texto.getBytes(StandardCharsets.UTF_8));
        }
    }

    private static String ler(File f) throws IOException {
        try (FileInputStream in = new FileInputStream(f); ByteArrayOutputStream out = new ByteArrayOutputStream()) {
            byte[] buffer = new byte[4096];
            int n;
            while ((n = in.read(buffer)) > 0) out.write(buffer, 0, n);
            return out.toString("UTF-8");
        }
    }
}
