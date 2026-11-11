package dev.allofus.fusioncore.tools;

import android.app.PendingIntent;
import android.content.Context;
import android.content.Intent;
import android.graphics.Bitmap;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.system.Os;
import android.util.Base64;
import android.util.Log;

import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

import dev.allofus.fusioncore.R;

/**
 * CriCri Mods: "Entrar com o Google".
 *
 * Google Play Games cannot sign in inside this app (Google ties it to the original app's signature), so the game
 * falls back to a guest account. Instead the player signs in on Innersloth's official account site
 * (accounts.innersloth.com), which comes back as https://accounts.innersloth.com/?store=google&token=<Google ID
 * token>, and shares that page to this app. The token is kept only on this device (no_backup, never in a log), and
 * the CriCriConta plugin hands it to the game when the game asks Google Play Games for it.
 */
public final class Conta {
    private static final String TAG = "CriCriConta";

    /** Among Us's Google client: the game's Play Games web client, and the one Innersloth's account site uses. */
    public static final String CLIENTE_GOOGLE = "229679017402-kjalm0imngijq5q4vhesmsvqqgm2h5t7.apps.googleusercontent.com";
    /** The link the "Google Play" button on accounts.innersloth.com opens, without its prompt. */
    private static final String LINK_BASE = "https://accounts.google.com/o/oauth2/v2/auth?client_id=" + CLIENTE_GOOGLE
            + "&redirect_uri=https%3A%2F%2Faccounts.innersloth.com%2Fgoogle&response_type=code&scope=openid%20profile";
    /** First sign-in (or after "Sair"): Google shows the account list, exactly like the site's button. */
    private static final String LINK_LOGIN = LINK_BASE + "&prompt=select_account";
    /** The last account used (Google's "sub" and the name), kept after the token expires so "Entrar de novo" can skip
     *  the account list. Cleared by "Sair". */
    private static final String PREFS_ULTIMA_CONTA = "cricri_conta";
    /** Read by the plugin (CriCriConta.dll) to find the file. */
    public static final String VARIAVEL = "CRICRI_MODS_CONTA";
    private static final String ARQUIVO = "cricri-conta.json";
    /** Left by the plugin when Epic refuses the token (just the name and the reason; the token is deleted). */
    private static final String NOTA_RECUSADO = "cricri-conta.recusado";
    /** Where the game's EOS SDK keeps the guest (Device ID) credential inside this app. */
    private static final String PREFS_DO_CONVIDADO = "EOS_KeychainPreferences";
    /** A login that expires sooner than this is treated as expired. */
    private static final long FOLGA_MS = 2 * 60 * 1000L;

    private static final Pattern ENDERECO = Pattern.compile("https://accounts\\.innersloth\\.com/\\S*");

    private Conta() {}

    public static final class Login {
        public final String token;
        public final String nome;
        public final long expiraEmMs;
        /** Google's stable id for the account (the token's "sub"); used as login_hint. */
        final String sub;

        Login(String token, String nome, long expiraEmMs, String sub) {
            this.token = token;
            this.nome = nome;
            this.expiraEmMs = expiraEmMs;
            this.sub = sub;
        }

        public boolean valido() {
            return expiraEmMs - System.currentTimeMillis() > FOLGA_MS;
        }
    }

    /** Thrown with a string resource the screen can show as is. */
    public static final class Problema extends Exception {
        public final int mensagem;

        Problema(int mensagem) {
            super("conta: " + mensagem);
            this.mensagem = mensagem;
        }
    }

    /** Pulls the Google login out of a shared or pasted text (the address of Innersloth's page). */
    public static Login extrair(String texto) throws Problema {
        if (texto == null) throw new Problema(R.string.conta_erro_sem_endereco);
        // The site checks the account right away and, for a Google account that never played Among Us, replaces the
        // address with /merge-error ("Not Found"), which no longer has the token.
        if (texto.contains("accounts.innersloth.com/merge-error")) throw new Problema(R.string.conta_erro_sem_among_us);
        Matcher m = ENDERECO.matcher(texto);
        if (!m.find()) throw new Problema(R.string.conta_erro_sem_endereco);
        Uri endereco = Uri.parse(m.group());
        String store = endereco.getQueryParameter("store");
        String token = endereco.getQueryParameter("token");
        if (token == null || token.isEmpty()) throw new Problema(R.string.conta_erro_sem_endereco);
        if (store != null && !"google".equals(store)) throw new Problema(R.string.conta_erro_nao_google);
        Login login = lerToken(token);
        if (!login.valido()) throw new Problema(R.string.conta_erro_vencido);
        return login;
    }

    /** Checks the token's own claims: issued by Google, for Among Us's client, with an expiry. */
    private static Login lerToken(String token) throws Problema {
        try {
            String[] partes = token.split("\\.");
            if (partes.length != 3) throw new Problema(R.string.conta_erro_token);
            byte[] bytes = Base64.decode(partes[1], Base64.URL_SAFE | Base64.NO_PADDING | Base64.NO_WRAP);
            JSONObject payload = new JSONObject(new String(bytes, StandardCharsets.UTF_8));
            String iss = payload.optString("iss");
            if (!"accounts.google.com".equals(iss) && !"https://accounts.google.com".equals(iss)) {
                throw new Problema(R.string.conta_erro_nao_google);
            }
            if (!CLIENTE_GOOGLE.equals(payload.optString("aud"))) throw new Problema(R.string.conta_erro_token);
            long exp = payload.optLong("exp", 0);
            if (exp <= 0) throw new Problema(R.string.conta_erro_token);
            return new Login(token, payload.optString("name", ""), exp * 1000L, payload.optString("sub", ""));
        } catch (Problema p) {
            throw p;
        } catch (Exception e) {
            throw new Problema(R.string.conta_erro_token);
        }
    }

    private static final String PREFS_LOGIN = "cricri_login_pedido", PEDIDO_EM = "em";
    private static final long VALIDADE_DO_PEDIDO_MS = 15 * 60 * 1000L;

    /** ContaActivity is reachable by any app and by "share": a login only counts right after the player asked for one in the app. */
    public static boolean loginFoiPedido(Context context) {
        long em = context.getSharedPreferences(PREFS_LOGIN, Context.MODE_PRIVATE).getLong(PEDIDO_EM, 0);
        long passou = System.currentTimeMillis() - em;
        return em > 0 && passou >= 0 && passou < VALIDADE_DO_PEDIDO_MS;
    }

    public static void esquecerPedidoDeLogin(Context context) {
        context.getSharedPreferences(PREFS_LOGIN, Context.MODE_PRIVATE).edit().remove(PEDIDO_EM).apply();
    }

    public static File arquivo(Context context) {
        return new File(context.getNoBackupFilesDir(), ARQUIVO);
    }

    public static void salvar(Context context, Login login) throws IOException {
        JSONObject json = new JSONObject();
        try {
            json.put("token", login.token);
            json.put("nome", login.nome);
        } catch (Exception e) {
            throw new IOException(e);
        }
        File destino = arquivo(context);
        File temp = new File(destino.getPath() + ".tmp");
        try (FileOutputStream out = new FileOutputStream(temp)) {
            out.write(json.toString().getBytes(StandardCharsets.UTF_8));
            out.getFD().sync();
        }
        if (!temp.renameTo(destino)) {
            temp.delete();
            throw new IOException("rename " + temp + " -> " + destino);
        }
        new File(context.getNoBackupFilesDir(), NOTA_RECUSADO).delete();
        lembrarConta(context, login);
        Log.i(TAG, "login do Google salvo (vale ate " + new java.util.Date(login.expiraEmMs) + ")");
    }

    private static void lembrarConta(Context context, Login login) {
        if (login.sub == null || login.sub.isEmpty()) return;
        context.getSharedPreferences(PREFS_ULTIMA_CONTA, Context.MODE_PRIVATE).edit()
                .putString("sub", login.sub).putString("nome", login.nome).apply();
    }

    /**
     * The link "Entrar" opens. With a remembered account it asks Google for that same account (login_hint) and
     * leaves out prompt=select_account, so Google goes straight back to Innersloth's page without the account list.
     */
    public static String linkLogin(Context context) {
        String sub = context.getSharedPreferences(PREFS_ULTIMA_CONTA, Context.MODE_PRIVATE).getString("sub", null);
        if (sub == null) {
            // Logins saved before the account was remembered.
            Login salvo = carregar(context);
            if (salvo != null && !salvo.sub.isEmpty()) {
                lembrarConta(context, salvo);
                sub = salvo.sub;
            }
        }
        return sub == null ? LINK_LOGIN : LINK_BASE + "&login_hint=" + Uri.encode(sub);
    }

    /** What the "Usar este login" button sends ContaActivity (the browser puts the page's address as the data). */
    public static final String ACAO_USAR = "br.com.cricribr.mods.USAR_ESTE_LOGIN";

    /**
     * Opens the login in a browser tab with a ✓ "Usar este login" button on its toolbar (Custom Tabs, raw extras so no
     * extra library): one tap hands the page with the token back to ContaActivity, instead of ⋮ → Compartilhar →
     * CriCri Mods. A browser without Custom Tabs ignores the extras and opens the page as before (sharing still works).
     * The tab opens in the caller's task, so ContaActivity can close it by restarting that task.
     */
    public static boolean abrirLogin(android.app.Activity tela) {
        tela.getSharedPreferences(PREFS_LOGIN, Context.MODE_PRIVATE).edit().putLong(PEDIDO_EM, System.currentTimeMillis()).apply();
        Intent abrir = new Intent(Intent.ACTION_VIEW, Uri.parse(linkLogin(tela))).addCategory(Intent.CATEGORY_BROWSABLE);
        Bundle sessao = new Bundle();
        sessao.putBinder("android.support.customtabs.extra.SESSION", null);   // marks the intent as a Custom Tab
        abrir.putExtras(sessao);

        // mutable: the browser fills in the page's address
        int flags = PendingIntent.FLAG_UPDATE_CURRENT | (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S ? PendingIntent.FLAG_MUTABLE : 0);
        PendingIntent usar = PendingIntent.getActivity(tela, 7,
                new Intent(tela, dev.allofus.fusioncore.ContaActivity.class).setAction(ACAO_USAR), flags);
        String rotulo = tela.getString(R.string.login_usar_este);

        Bundle botao = new Bundle();
        Bitmap icone = icone(tela, R.drawable.ic_usar_login);
        if (icone != null) botao.putParcelable("android.support.customtabs.customaction.ICON", icone);
        botao.putString("android.support.customtabs.customaction.DESCRIPTION", rotulo);
        botao.putParcelable("android.support.customtabs.customaction.PENDING_INTENT", usar);
        abrir.putExtra("android.support.customtabs.extra.ACTION_BUTTON_BUNDLE", botao);
        abrir.putExtra("android.support.customtabs.extra.TINT_ACTION_BUTTON", true);

        Bundle item = new Bundle();
        item.putString("android.support.customtabs.customaction.MENU_ITEM_TITLE", rotulo);
        item.putParcelable("android.support.customtabs.customaction.PENDING_INTENT", usar);
        java.util.ArrayList<Bundle> menu = new java.util.ArrayList<>();
        menu.add(item);
        abrir.putParcelableArrayListExtra("android.support.customtabs.extra.MENU_ITEMS", menu);
        abrir.putExtra("android.support.customtabs.extra.TITLE_VISIBILITY", 1);
        abrir.putExtra("android.support.customtabs.extra.TOOLBAR_COLOR", 0xFF1D1A1F);   // @color/cricri_barra
        try {
            tela.startActivity(abrir);
            return true;
        } catch (android.content.ActivityNotFoundException e) {
            return false;
        }
    }

    private static Bitmap icone(Context context, int id) {
        android.graphics.drawable.Drawable d = androidx.core.content.ContextCompat.getDrawable(context, id);
        if (d == null) return null;
        int lado = Math.round(24 * context.getResources().getDisplayMetrics().density);
        Bitmap b = Bitmap.createBitmap(lado, lado, Bitmap.Config.ARGB_8888);
        d.setBounds(0, 0, lado, lado);
        d.draw(new android.graphics.Canvas(b));
        return b;
    }

    /** The remembered account's name ("" when Google gave none), or null when "Entrar" will show the account list. */
    public static String nomeLembrado(Context context) {
        android.content.SharedPreferences prefs = context.getSharedPreferences(PREFS_ULTIMA_CONTA, Context.MODE_PRIVATE);
        if (prefs.contains("sub")) return prefs.getString("nome", "");
        Login salvo = carregar(context);
        return salvo != null ? salvo.nome : null;
    }

    /** The saved login, or null. An expired one is returned too (the screen says it expired). */
    public static Login carregar(Context context) {
        File f = arquivo(context);
        if (!f.isFile()) return null;
        try {
            JSONObject json = new JSONObject(lerTexto(f));
            Login login = lerToken(json.optString("token"));
            String nome = json.optString("nome", login.nome);
            return new Login(login.token, nome, login.expiraEmMs, login.sub);
        } catch (Exception e) {
            Log.w(TAG, "login salvo ilegivel; apagando");
            f.delete();
            return null;
        }
    }

    // minSdk 24: no java.nio.file.Files.
    private static String lerTexto(File f) throws IOException {
        try (FileInputStream in = new FileInputStream(f); ByteArrayOutputStream out = new ByteArrayOutputStream()) {
            byte[] buffer = new byte[4096];
            int n;
            while ((n = in.read(buffer)) > 0) out.write(buffer, 0, n);
            return out.toString("UTF-8");
        }
    }

    /** "Sair": forget the login and the account, so the next "Entrar" shows Google's account list again. */
    public static void sair(Context context) {
        if (arquivo(context).delete()) Log.i(TAG, "login do Google apagado");
        new File(context.getNoBackupFilesDir(), NOTA_RECUSADO).delete();
        context.getSharedPreferences(PREFS_ULTIMA_CONTA, Context.MODE_PRIVATE).edit().clear().apply();
    }

    /** {name, reason} when the game refused the last login (Epic answered with an error), else null. */
    public static String[] recusado(Context context) {
        File nota = new File(context.getNoBackupFilesDir(), NOTA_RECUSADO);
        if (!nota.isFile()) return null;
        try {
            JSONObject json = new JSONObject(lerTexto(nota));
            return new String[] { json.optString("nome", ""), json.optString("motivo", "") };
        } catch (Exception e) {
            return new String[] { "", "" };
        }
    }

    /**
     * Before the game starts (BootstrapActivity): tell the plugin where the file is, drop an expired login, and,
     * when playing with the Google account, discard this app's guest so the game has no guest to merge into it.
     */
    public static void prepararJogo(Context context) {
        try {
            Os.setenv(VARIAVEL, arquivo(context).getAbsolutePath(), true);
        } catch (Exception e) {
            Log.w(TAG, "setenv " + VARIAVEL + ": " + e);
        }
        Login login = carregar(context);
        if (login == null) return;
        if (!login.valido()) {
            Log.i(TAG, "login do Google vencido; o jogo vai entrar como convidado");
            // Only the token: the remembered account stays, so "Entrar de novo" still skips the account list.
            arquivo(context).delete();
            return;
        }
        if (new File(context.getApplicationInfo().dataDir, "shared_prefs/" + PREFS_DO_CONVIDADO + ".xml").exists()) {
            boolean apagou = context.deleteSharedPreferences(PREFS_DO_CONVIDADO);
            Log.i(TAG, "convidado deste app descartado antes do login do Google: " + apagou);
        }
    }
}
