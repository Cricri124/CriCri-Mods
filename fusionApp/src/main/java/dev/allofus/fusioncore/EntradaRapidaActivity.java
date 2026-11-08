package dev.allofus.fusioncore;

import android.content.ActivityNotFoundException;
import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.net.Uri;
import android.os.Bundle;
import android.widget.Toast;

import androidx.appcompat.app.AppCompatActivity;

import com.google.android.material.dialog.MaterialAlertDialogBuilder;

import dev.allofus.fusioncore.hooks.GameRuntime;
import dev.allofus.fusioncore.tools.Conta;

/**
 * CriCri Mods: the second icon on the home screen ("Jogar com mods"). One tap opens Among Us with the mods.
 *  - game already open: just brings it back (never starts a second one);
 *  - the player uses a Google login and it expired: asks first (sign in again, or play as guest); after the
 *    "Compartilhar" the game opens by itself (ContaActivity sees the flag);
 *  - otherwise: straight into the game, through the home screen's own launch flow (permissions, metadata).
 */
public class EntradaRapidaActivity extends AppCompatActivity {
    private static final String PREFS = "cricri_entrada_rapida";
    private static final String JOGAR_DEPOIS_DO_LOGIN = "jogar_depois_do_login";

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);

        if (GameRuntime.jogoAberto()) {
            voltarProApp();
            return;
        }
        if (!amongUsInstalado()) {
            abrirInicio(false);
            return;
        }
        Conta.Login login = Conta.carregar(this);
        String lembrado = Conta.nomeLembrado(this);
        boolean precisaEntrar = lembrado != null && (login == null || !login.valido());
        if (!precisaEntrar) {
            abrirInicio(true);
            return;
        }
        String nome = lembrado.isEmpty() ? getString(R.string.conta_sem_nome) : lembrado;
        new MaterialAlertDialogBuilder(this)
                .setTitle(R.string.entrada_login_title)
                .setMessage(getString(R.string.entrada_login_format, nome))
                .setPositiveButton(R.string.conta_entrar_de_novo, (d, w) -> entrarEDepoisJogar())
                .setNegativeButton(R.string.entrada_jogar_convidado, (d, w) -> abrirInicio(true))
                .setOnCancelListener(d -> finish())
                .show();
    }

    private void entrarEDepoisJogar() {
        marcarJogarDepoisDoLogin(this, true);
        if (Conta.abrirLogin(this)) {
            Toast.makeText(this, R.string.login_lembrete, Toast.LENGTH_LONG).show();
        } else {
            marcarJogarDepoisDoLogin(this, false);
            Toast.makeText(this, R.string.conta_sem_navegador, Toast.LENGTH_LONG).show();
        }
        finish();
    }

    // The home screen, optionally told to press PLAY by itself.
    private void abrirInicio(boolean jogar) {
        Intent inicio = new Intent(this, SelectorActivity.class)
                .addFlags(Intent.FLAG_ACTIVITY_CLEAR_TOP | Intent.FLAG_ACTIVITY_SINGLE_TOP);
        if (jogar) inicio.putExtra(SelectorActivity.EXTRA_JOGAR_AGORA, true);
        startActivity(inicio);
        finish();
    }

    private void voltarProApp() {
        Intent abrir = getPackageManager().getLaunchIntentForPackage(getPackageName());
        if (abrir != null) startActivity(abrir.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
        finish();
    }

    private boolean amongUsInstalado() {
        try {
            getPackageManager().getPackageInfo(BuildConfig.TARGET_PACKAGE, 0);
            return true;
        } catch (PackageManager.NameNotFoundException e) {
            return false;
        }
    }

    static void marcarJogarDepoisDoLogin(Context context, boolean valor) {
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit()
                .putBoolean(JOGAR_DEPOIS_DO_LOGIN, valor)
                .putLong("quando", System.currentTimeMillis()).apply();
    }

    // True once, and only within 15 minutes of asking (an abandoned login must not start the game hours later).
    static boolean consumirJogarDepoisDoLogin(Context context) {
        var prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE);
        boolean marcado = prefs.getBoolean(JOGAR_DEPOIS_DO_LOGIN, false)
                && System.currentTimeMillis() - prefs.getLong("quando", 0) < 15 * 60 * 1000L;
        prefs.edit().putBoolean(JOGAR_DEPOIS_DO_LOGIN, false).apply();
        return marcado;
    }
}
