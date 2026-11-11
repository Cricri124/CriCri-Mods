package dev.allofus.fusioncore.tools;

import android.content.Context;
import android.os.Handler;
import android.os.Looper;
import android.util.Log;

import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

import dev.allofus.fusioncore.BuildConfig;

/**
 * "Conectar conta do CRICRI BR" with one tap, the way a TV signs in: the app asks for a one-time link, the person confirms on the site in this
 * phone's browser, and the app receives the key.
 *
 * The secret "id" stays in the app and only travels in a POST body. The short code in the link is worthless without it. The key is never logged
 * and never goes in a URL. Pasting the key by hand stays as the fallback (the site refuses when the browser is on another network).
 */
public final class ConectarBr {
    private static final String TAG = "CriCriMods";
    private static final String BASE = BuildConfig.SITE_URL;
    private static final int LIMITE_RESPOSTA = 8 * 1024;

    public static final int CONECTADA = 0, RECUSADA = 1, VENCEU = 2, SEM_SERVIDOR = 3;

    public interface Abrir { void url(String endereco); }
    public interface Fim { void resultado(int como); }

    private static final ExecutorService fila = Executors.newSingleThreadExecutor();
    private static volatile int vez;
    private static volatile String linkAberto;

    /** The confirmation page still waiting: without a site login it has to be opened again after /site in the lobby. Null when nothing is waiting. */
    public static String linkEmAndamento() {
        return linkAberto;
    }

    private ConectarBr() {
    }

    /** What the server answered to "iniciar". */
    static final class Inicio {
        final String id, url;
        final int expiraSeg, intervaloSeg;

        Inicio(String id, String url, int expiraSeg, int intervaloSeg) {
            this.id = id;
            this.url = url;
            this.expiraSeg = expiraSeg;
            this.intervaloSeg = intervaloSeg;
        }
    }

    /** Null unless the answer is complete and the link really is this site's page: the app never opens another address. */
    static Inicio lerInicio(String corpo, String base) {
        try {
            JSONObject j = new JSONObject(corpo);
            String id = j.getString("id"), url = j.getString("url");
            if (id.length() < 32 || !url.startsWith(base + "/conta/ligar?")) return null;
            return new Inicio(id, url, Math.max(30, Math.min(j.optInt("expiraEmSeg", 600), 900)), Math.max(2, Math.min(j.optInt("intervaloSeg", 3), 30)));
        } catch (Exception e) {
            return null;
        }
    }

    /** Starts over if a previous attempt is still waiting. Both callbacks run on the main thread. */
    public static void comecar(Context context, Abrir abrir, Fim fim) {
        Context app = context.getApplicationContext();
        Handler principal = new Handler(Looper.getMainLooper());
        int minha = ++vez;
        fila.execute(() -> {
            int como = SEM_SERVIDOR;
            try {
                como = conectar(app, principal, abrir, minha);
            } catch (Exception e) {
                Log.i(TAG, "conectar conta: " + e.getClass().getSimpleName());
            }
            if (como >= 0) {
                linkAberto = null;
                int fimDeFato = como;
                principal.post(() -> fim.resultado(fimDeFato));
            }
        });
    }

    // -1 = a newer attempt took over: nobody is waiting for this one
    private static int conectar(Context app, Handler principal, Abrir abrir, int minha) throws Exception {
        if (!BASE.startsWith("https://")) return SEM_SERVIDOR;
        Resposta r = post(BASE + "/api/mod/ligar/iniciar", "");
        if (r.codigo != 200) return SEM_SERVIDOR;
        Inicio inicio = lerInicio(r.corpo, BASE);
        if (inicio == null) return SEM_SERVIDOR;
        linkAberto = inicio.url;
        principal.post(() -> abrir.url(inicio.url));

        String pedido = new JSONObject().put("id", inicio.id).toString();
        long fim = System.currentTimeMillis() + inicio.expiraSeg * 1000L;
        long espera = inicio.intervaloSeg * 1000L;
        while (System.currentTimeMillis() < fim) {
            Thread.sleep(espera);
            if (minha != vez) return -1;
            espera = inicio.intervaloSeg * 1000L;
            Resposta p;
            try {
                p = post(BASE + "/api/mod/ligar/resultado", pedido);
            } catch (IOException e) {
                continue;   // the phone switching apps or networks: try again until the link expires
            }
            switch (p.codigo) {
                case 200:
                    String chave = ChaveBr.limpar(new JSONObject(p.corpo).optString("chave"));
                    if (chave == null) return SEM_SERVIDOR;
                    ChaveBr.salvar(app, chave);
                    return CONECTADA;
                case 403:
                    return RECUSADA;
                case 410:
                    return VENCEU;
                case 429:
                    espera = Math.max(espera, p.esperarSeg * 1000L);
                    break;
                default:
                    break;   // 202: still waiting for the confirmation
            }
        }
        return VENCEU;
    }

    private static final class Resposta {
        int codigo, esperarSeg;
        String corpo = "";
    }

    private static Resposta post(String endereco, String json) throws IOException {
        HttpURLConnection con = (HttpURLConnection) new URL(endereco).openConnection();
        con.setConnectTimeout(8000);
        con.setReadTimeout(15000);
        con.setInstanceFollowRedirects(false);
        con.setRequestMethod("POST");
        con.setRequestProperty("Content-Type", "application/json");
        con.setRequestProperty("User-Agent", "CriCriMods/" + BuildConfig.VERSION_NAME);
        con.setDoOutput(true);
        try {
            try (OutputStream out = con.getOutputStream()) {
                out.write(json.getBytes(StandardCharsets.UTF_8));
            }
            Resposta r = new Resposta();
            r.codigo = con.getResponseCode();
            r.esperarSeg = Math.max(5, Math.min(con.getHeaderFieldInt("Retry-After", 30), 300));
            if (r.codigo == 200) {
                try (InputStream in = con.getInputStream()) {
                    r.corpo = lerTudo(in);
                }
            }
            return r;
        } finally {
            con.disconnect();
        }
    }

    private static String lerTudo(InputStream in) throws IOException {
        ByteArrayOutputStream saida = new ByteArrayOutputStream();
        byte[] pedaco = new byte[1024];
        int n;
        while ((n = in.read(pedaco)) > 0) {
            saida.write(pedaco, 0, n);
            if (saida.size() > LIMITE_RESPOSTA) throw new IOException("resposta grande demais");
        }
        return saida.toString("UTF-8");
    }
}
