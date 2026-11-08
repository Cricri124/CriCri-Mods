package dev.allofus.fusioncore.tools;

import android.content.Context;
import android.content.SharedPreferences;
import android.os.Handler;
import android.os.Looper;
import android.util.Base64;
import android.util.Log;

import org.json.JSONArray;
import org.json.JSONObject;

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
import java.security.KeyFactory;
import java.security.MessageDigest;
import java.security.PublicKey;
import java.security.Signature;
import java.security.spec.X509EncodedKeySpec;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Locale;
import java.util.Set;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.regex.Pattern;

import dev.allofus.fusioncore.BuildConfig;

/**
 * CriCri Mods: mods updated from the site, without a new APK (the CriCri, 08/11: "atualizar mod sem atualizar o app";
 * he chose the site over the Play Store so a mod doesn't wait for an app release).
 *
 * The site (https://cricribr.com.br/mods/, files served as they are) has catalogo.json, catalogo.json.sig and the DLLs
 * with the version in their names. What protects the players (the CRICRI BR session asked for 1-3):
 *  1. the catalog is signed (ECDSA P-256, SHA-256) with a key that only the CriCri's PC has; the public half is FIXED
 *     in the APK (no key ever comes from the server);
 *  2. "numero" only goes up: a catalog numbered below the last one accepted is refused (an old, genuinely signed
 *     catalog can't bring an old buggy DLL back);
 *  3. every DLL is checked against the sha256 in the signed catalog before it is used, names are plain file names
 *     (no paths), https only, size limits;
 *  4. offline or with the server empty, nothing changes: the mods inside the APK are used. The check runs in the
 *     background when the app opens and never holds JOGAR.
 * A catalog entry with "retirado": true takes that mod out (also one bundled in the APK): the emergency switch for a
 * version that breaks.
 */
public final class ModsRemotos {
    private static final String TAG = "CriCriMods";
    /** The public half of the catalog signing key (catalogo-mods-privada.pem, kept off the repository) (SubjectPublicKeyInfo, DER). */
    public static final String CHAVE_PUBLICA =
            "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE+tR4fp0D0n3TRIPzaexS9oYq4wmYSeAmlmgaccxkjr9p+lB6d6aHktsF7RN0woAiz4IrL3ETjpQ5H+bK0FtgMA==";
    /** Tests only. */
    static String chaveParaTeste;

    static final String PASTA = "mods-remotos";
    private static final String CATALOGO = "catalogo.json";
    private static final String ASSINATURA = "catalogo.json.sig";
    private static final String PREFS = "cricri_mods_remotos";
    private static final long INTERVALO_MS = 10 * 60 * 1000L;
    private static final int LIMITE_CATALOGO = 256 * 1024;
    private static final int LIMITE_DLL = 24 * 1024 * 1024;
    private static final Pattern NOME_SEGURO = Pattern.compile("[A-Za-z0-9][A-Za-z0-9._-]{0,95}");

    private static final ExecutorService fila = Executors.newSingleThreadExecutor(r -> {
        Thread t = new Thread(r, "cricri-mods-remotos");
        t.setDaemon(true);
        return t;
    });

    private ModsRemotos() {}

    /** A catalog that passed every check. */
    public static final class Catalogo {
        public final int numero;
        public final String emitido;
        public final List<JSONObject> mods;

        Catalogo(int numero, String emitido, List<JSONObject> mods) {
            this.numero = numero;
            this.emitido = emitido;
            this.mods = mods;
        }
    }

    /** Why a catalog was refused (for the log and the tests). */
    public static final class Recusado extends Exception {
        Recusado(String motivo) { super(motivo); }
    }

    // ---------------- checks (pure: no network) ----------------

    static boolean assinaturaValida(byte[] dados, byte[] assinatura) {
        try {
            String chave = chaveParaTeste != null ? chaveParaTeste : CHAVE_PUBLICA;
            PublicKey publica = KeyFactory.getInstance("EC")
                    .generatePublic(new X509EncodedKeySpec(Base64.decode(chave, Base64.NO_WRAP)));
            Signature s = Signature.getInstance("SHA256withECDSA");
            s.initVerify(publica);
            s.update(dados);
            return s.verify(assinatura);
        } catch (Exception e) {
            return false;
        }
    }

    /** Parses and checks a downloaded catalog; minimo = the last number accepted (0 = none yet). */
    static Catalogo conferir(byte[] json, byte[] assinaturaBase64, int minimo) throws Recusado {
        if (json == null || json.length == 0 || json.length > LIMITE_CATALOGO) throw new Recusado("catalogo vazio ou grande demais");
        byte[] assinatura;
        try {
            assinatura = Base64.decode(new String(assinaturaBase64, StandardCharsets.US_ASCII).trim(), Base64.DEFAULT);
        } catch (Exception e) {
            throw new Recusado("assinatura ilegivel");
        }
        if (!assinaturaValida(json, assinatura)) throw new Recusado("assinatura nao confere com a chave do app");
        try {
            JSONObject raiz = new JSONObject(new String(json, StandardCharsets.UTF_8));
            if (raiz.optInt("formato", 0) != 1) throw new Recusado("formato desconhecido");
            int numero = raiz.getInt("numero");
            if (numero < minimo) throw new Recusado("numero " + numero + " abaixo do ultimo aceito (" + minimo + ")");
            JSONArray lista = raiz.getJSONArray("mods");
            List<JSONObject> mods = new ArrayList<>();
            for (int i = 0; i < lista.length(); i++) {
                JSONObject m = lista.getJSONObject(i);
                String id = m.getString("id");
                if (m.optBoolean("retirado", false)) { mods.add(m); continue; }
                String arquivo = m.getString("file"), url = m.getString("url"), sha = m.getString("sha256");
                if (!NOME_SEGURO.matcher(arquivo).matches() || !arquivo.endsWith(".dll")
                        || !NOME_SEGURO.matcher(url).matches() || !url.endsWith(".dll"))
                    throw new Recusado("nome de arquivo invalido no mod " + id);
                if (!sha.matches("[0-9a-fA-F]{64}")) throw new Recusado("sha256 invalido no mod " + id);
                m.getString("version");
                m.getString("name");
                mods.add(m);
            }
            return new Catalogo(numero, raiz.optString("emitido", ""), mods);
        } catch (Recusado r) {
            throw r;
        } catch (Exception e) {
            throw new Recusado("catalogo mal formado: " + e.getMessage());
        }
    }

    // ---------------- what the app uses ----------------

    static File pasta(Context context) {
        return new File(context.getFilesDir(), PASTA);
    }

    /** The last accepted catalog, checked again from disk (null when there is none). */
    public static Catalogo atual(Context context) {
        File dir = pasta(context);
        File json = new File(dir, CATALOGO), sig = new File(dir, ASSINATURA);
        if (!json.isFile() || !sig.isFile()) return null;
        try {
            return conferir(ler(json, LIMITE_CATALOGO), ler(sig, 4096), 0);
        } catch (Exception e) {
            Log.w(TAG, "catalogo do site guardado nao confere mais; ignorado: " + e.getMessage());
            return null;
        }
    }

    /** The downloaded DLL of a catalog entry, only if its sha256 is the signed one (null otherwise). */
    public static File baixado(Context context, String url, String sha256) {
        if (url == null || !NOME_SEGURO.matcher(url).matches()) return null;
        File f = new File(pasta(context), url);
        if (!f.isFile()) return null;
        try {
            return sha256(ler(f, LIMITE_DLL)).equalsIgnoreCase(sha256) ? f : null;
        } catch (IOException e) {
            return null;
        }
    }

    /** "1.5.10" > "1.5.9"; missing parts count as 0. */
    public static int compararVersoes(String a, String b) {
        String[] x = a.split("[.\\-]"), y = b.split("[.\\-]");
        for (int i = 0; i < Math.max(x.length, y.length); i++) {
            int p = i < x.length ? numero(x[i]) : 0, q = i < y.length ? numero(y[i]) : 0;
            if (p != q) return Integer.compare(p, q);
        }
        return 0;
    }

    private static int numero(String s) {
        try { return Integer.parseInt(s.replaceAll("[^0-9]", "")); } catch (Exception e) { return 0; }
    }

    // ---------------- the background check ----------------

    /** Checks the site at most every 10 minutes (or now, with forcar); depois runs on the main thread if anything
     *  changed (a new catalog accepted or a DLL downloaded). */
    public static void verificar(Context context, boolean forcar, Runnable depois) {
        Context app = context.getApplicationContext();
        SharedPreferences prefs = app.getSharedPreferences(PREFS, Context.MODE_PRIVATE);
        long agora = System.currentTimeMillis();
        if (!forcar && agora - prefs.getLong("ultima_verificacao", 0) < INTERVALO_MS) return;
        prefs.edit().putLong("ultima_verificacao", agora).apply();
        fila.execute(() -> {
            boolean mudou = false;
            try {
                mudou = atualizar(app, prefs);
            } catch (Exception e) {
                Log.i(TAG, "mods do site: sem atualizacao agora (" + e.getClass().getSimpleName() + ": " + e.getMessage() + ")");
            }
            if (mudou && depois != null) new Handler(Looper.getMainLooper()).post(depois);
        });
    }

    private static boolean atualizar(Context app, SharedPreferences prefs) throws Exception {
        String base = BuildConfig.MODS_URL;
        if (base.isEmpty() || !base.startsWith("https://")) return false;
        int ultimo = prefs.getInt("ultimo_numero", 0);
        byte[] json = baixar(base + CATALOGO, LIMITE_CATALOGO);
        byte[] sig = baixar(base + ASSINATURA, 4096);
        Catalogo c;
        try {
            c = conferir(json, sig, ultimo);
        } catch (Recusado r) {
            Log.w(TAG, "mods do site: catalogo recusado (" + r.getMessage() + ")");
            return false;
        }
        File dir = pasta(app);
        if (!dir.isDirectory() && !dir.mkdirs()) throw new IOException("sem pasta " + dir);
        boolean mudou = c.numero != ultimo;
        gravar(new File(dir, CATALOGO), json);
        gravar(new File(dir, ASSINATURA), sig);
        prefs.edit().putInt("ultimo_numero", c.numero).apply();

        Set<String> usados = new HashSet<>();
        usados.add(CATALOGO);
        usados.add(ASSINATURA);
        for (JSONObject m : c.mods) {
            if (m.optBoolean("retirado", false)) continue;
            String url = m.getString("url"), sha = m.getString("sha256");
            usados.add(url);
            if (baixado(app, url, sha) != null) continue;
            byte[] dll = baixar(base + url, LIMITE_DLL);
            if (!sha256(dll).equalsIgnoreCase(sha)) {
                Log.w(TAG, "mods do site: " + url + " nao bate com o sha256 do catalogo; nao usado");
                continue;
            }
            gravar(new File(dir, url), dll);
            mudou = true;
            Log.i(TAG, "mods do site: baixado " + m.getString("name") + " " + m.getString("version"));
        }
        // versions no longer in the catalog
        File[] todos = dir.listFiles();
        if (todos != null) for (File f : todos) if (!usados.contains(f.getName())) //noinspection ResultOfMethodCallIgnored
            f.delete();
        if (mudou) Log.i(TAG, "mods do site: catalogo n " + c.numero + " (" + c.emitido + ") aceito");
        return mudou;
    }

    private static byte[] baixar(String endereco, int limite) throws IOException {
        HttpURLConnection con = (HttpURLConnection) new URL(endereco).openConnection();
        con.setConnectTimeout(8000);
        con.setReadTimeout(20000);
        con.setInstanceFollowRedirects(false);
        try {
            int codigo = con.getResponseCode();
            if (codigo != 200) throw new IOException("HTTP " + codigo + " em " + endereco);
            try (InputStream in = con.getInputStream()) {
                return lerTudo(in, limite);
            }
        } finally {
            con.disconnect();
        }
    }

    // ---------------- small helpers ----------------

    private static void gravar(File destino, byte[] dados) throws IOException {
        File temp = new File(destino.getPath() + ".tmp");
        try (OutputStream out = new FileOutputStream(temp, false)) {
            out.write(dados);
        }
        if (destino.exists() && !destino.delete()) throw new IOException("nao troquei " + destino);
        if (!temp.renameTo(destino)) throw new IOException("nao movi " + temp);
    }

    private static byte[] ler(File f, int limite) throws IOException {
        try (InputStream in = new FileInputStream(f)) {
            return lerTudo(in, limite);
        }
    }

    private static byte[] lerTudo(InputStream in, int limite) throws IOException {
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        byte[] buffer = new byte[64 * 1024];
        int n;
        while ((n = in.read(buffer)) != -1) {
            out.write(buffer, 0, n);
            if (out.size() > limite) throw new IOException("arquivo grande demais");
        }
        return out.toByteArray();
    }

    static String sha256(byte[] dados) {
        try {
            byte[] d = MessageDigest.getInstance("SHA-256").digest(dados);
            StringBuilder hex = new StringBuilder(d.length * 2);
            for (byte b : d) hex.append(String.format(Locale.ROOT, "%02x", b));
            return hex.toString();
        } catch (Exception e) {
            throw new IllegalStateException(e);
        }
    }
}
