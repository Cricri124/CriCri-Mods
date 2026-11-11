package dev.allofus.fusioncore.tools;

import android.content.Context;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.system.Os;
import android.util.Log;

import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;
import java.security.KeyStore;
import java.util.Arrays;

import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;

/**
 * CriCri Mods: the CRICRI BR mod key (what lets the mod change the room options on the CRICRI BR server, through
 * https://cricribr.com.br/api/mod/*). Agreed with the CRICRI BR session on 08/11/2026:
 *  - the player makes it on the site (Minha conta → Chave do mod; shown once) and pastes it here ("Colar chave" reads
 *    the clipboard only on that tap); the Google login can't stand in for it (the server never sees it);
 *  - opaque text up to 64 characters; kept encrypted at rest (AES-GCM, key in the Android Keystore) in no_backup;
 *    never in a log, a URL or an error report;
 *  - at JOGAR it is handed to the game process only through an environment variable (memory, no file in clear), and
 *    the CriCriServidor plugin reads it as it loads and clears it (other mods share the process).
 */
public final class ChaveBr {
    private static final String TAG = "CriCriConta";
    /** Read and cleared by the CriCriServidor plugin. */
    public static final String VARIAVEL = "CRICRI_BR_CHAVE";
    private static final String ARQUIVO = "cricri-br-chave.bin";
    private static final String APELIDO = "cricri_br_chave";
    private static final int IV = 12;

    /** Tests only: Robolectric has no Android Keystore. */
    static SecretKey chaveParaTeste;

    private ChaveBr() {}

    /** A pasted text that can be a key (null when it can't): trimmed, 8 to 64 printable ASCII, no spaces. */
    public static String limpar(CharSequence colado) {
        if (colado == null) return null;
        String s = colado.toString().trim();
        if (s.length() < 8 || s.length() > 64) return null;
        for (int i = 0; i < s.length(); i++) {
            char c = s.charAt(i);
            if (c <= ' ' || c > '~') return null;
        }
        return s;
    }

    public static boolean temChave(Context context) {
        return arquivo(context).isFile();
    }

    /** Called by the CRICRI BR mod, inside the game, when the key arrives with the account verification. */
    public static void salvarDoJogo(String chave) throws Exception {
        String limpa = limpar(chave);
        Context app = (Context) Class.forName("android.app.ActivityThread").getMethod("currentApplication").invoke(null);
        if (limpa == null || app == null) throw new IllegalStateException("sem chave ou sem app");
        salvar(app, limpa);
    }

    public static void salvar(Context context, String chave) throws Exception {
        Cipher c = Cipher.getInstance("AES/GCM/NoPadding");
        c.init(Cipher.ENCRYPT_MODE, segredo());
        byte[] iv = c.getIV();
        byte[] cifrado = c.doFinal(chave.getBytes(StandardCharsets.UTF_8));
        byte[] tudo = new byte[iv.length + cifrado.length];
        System.arraycopy(iv, 0, tudo, 0, iv.length);
        System.arraycopy(cifrado, 0, tudo, iv.length, cifrado.length);
        File f = arquivo(context);
        File temp = new File(f.getPath() + ".tmp");
        try (OutputStream out = new FileOutputStream(temp, false)) {
            out.write(tudo);
        }
        if (f.exists() && !f.delete()) throw new IOException("nao troquei a chave");
        if (!temp.renameTo(f)) throw new IOException("nao gravei a chave");
    }

    /** The key in clear, or null (none, or unreadable: then it is deleted and has to be pasted again). */
    public static String ler(Context context) {
        File f = arquivo(context);
        if (!f.isFile()) return null;
        try (InputStream in = new FileInputStream(f)) {
            byte[] tudo = new byte[(int) f.length()];
            int lido = 0, n;
            while (lido < tudo.length && (n = in.read(tudo, lido, tudo.length - lido)) > 0) lido += n;
            Cipher c = Cipher.getInstance("AES/GCM/NoPadding");
            c.init(Cipher.DECRYPT_MODE, segredo(), new GCMParameterSpec(128, Arrays.copyOfRange(tudo, 0, IV)));
            return new String(c.doFinal(tudo, IV, tudo.length - IV), StandardCharsets.UTF_8);
        } catch (Exception e) {
            Log.w(TAG, "chave do CRICRI BR ilegivel; apagada (" + e.getClass().getSimpleName() + ")");
            //noinspection ResultOfMethodCallIgnored
            f.delete();
            return null;
        }
    }

    public static void apagar(Context context) {
        //noinspection ResultOfMethodCallIgnored
        arquivo(context).delete();
        try {
            KeyStore ks = KeyStore.getInstance("AndroidKeyStore");
            ks.load(null);
            ks.deleteEntry(APELIDO);
        } catch (Exception ignored) {
        }
    }

    /** At JOGAR (BootstrapActivity): the key goes to the game process in memory only; no key = the variable is cleared. */
    public static void prepararJogo(Context context) {
        String chave = ler(context);
        try {
            if (chave != null) Os.setenv(VARIAVEL, chave, true);
            else Os.unsetenv(VARIAVEL);
        } catch (Exception e) {
            Log.w(TAG, "setenv " + VARIAVEL + ": " + e.getClass().getSimpleName());
        }
    }

    static File arquivo(Context context) {
        return new File(context.getNoBackupFilesDir(), ARQUIVO);
    }

    private static SecretKey segredo() throws Exception {
        if (chaveParaTeste != null) return chaveParaTeste;
        KeyStore ks = KeyStore.getInstance("AndroidKeyStore");
        ks.load(null);
        if (ks.containsAlias(APELIDO)) return ((KeyStore.SecretKeyEntry) ks.getEntry(APELIDO, null)).getSecretKey();
        KeyGenerator g = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore");
        g.init(new KeyGenParameterSpec.Builder(APELIDO, KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setKeySize(256)
                .build());
        return g.generateKey();
    }
}
