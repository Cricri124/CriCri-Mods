package dev.allofus.fusioncore;

import android.content.Intent;
import android.os.Bundle;
import android.util.Log;
import android.widget.Toast;

import androidx.appcompat.app.AppCompatActivity;

import com.google.android.material.dialog.MaterialAlertDialogBuilder;

import java.text.DateFormat;
import java.util.Date;

import dev.allofus.fusioncore.hooks.GameRuntime;
import dev.allofus.fusioncore.tools.Conta;

/**
 * CriCri Mods: receives the login page after the player signs in on Innersloth's account site: from the ✓ "Usar este
 * login" button of the login tab (Conta.abrirLogin), or from "Compartilhar" in a browser without that button.
 * Runs in its own task (see the manifest) so it never sits on top of a running game; "Voltar" brings the app's task
 * back as it was (home screen or the game).
 */
public class ContaActivity extends AppCompatActivity {
    private static final String TAG = "CriCriConta";

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        Intent intent = getIntent();
        String texto = null;
        if (intent != null && Intent.ACTION_SEND.equals(intent.getAction())) {
            texto = intent.getStringExtra(Intent.EXTRA_TEXT);
        } else if (intent != null && Conta.ACAO_USAR.equals(intent.getAction())) {
            // the ✓ "Usar este login" button of the login tab: the browser sends the page's address as the data
            texto = intent.getDataString();
        }
        try {
            Conta.Login login = Conta.extrair(texto);
            Conta.salvar(this, login);
            String hora = DateFormat.getTimeInstance(DateFormat.SHORT).format(new Date(login.expiraEmMs));
            String nome = login.nome.isEmpty() ? getString(R.string.conta_sem_nome) : login.nome;
            // No dialog to tap through: say it in a toast and go straight back to the app's home screen, or, when the
            // login was asked by JOGAR or the "Jogar com mods" icon, straight into the game.
            Toast.makeText(getApplicationContext(), getString(R.string.conta_logado_format, nome, hora), Toast.LENGTH_LONG).show();
            boolean jogar = EntradaRapidaActivity.consumirJogarDepoisDoLogin(this);
            if (GameRuntime.jogoAberto()) {
                voltarProApp();   // never restart the task under a running game
            } else {
                // a fresh home screen: clearing the app's task also closes the login tab that was opened in it
                Intent inicio = new Intent(this, SelectorActivity.class)
                        .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TASK);
                if (jogar) inicio.putExtra(SelectorActivity.EXTRA_JOGAR_AGORA, true);
                startActivity(inicio);
                finish();
            }
        } catch (Conta.Problema p) {
            mostrarErro(p.mensagem);
        } catch (Exception e) {
            Log.w(TAG, "nao salvei o login: " + e.getClass().getSimpleName());
            mostrarErro(R.string.conta_erro_salvar);
        }
    }

    private void mostrarErro(int mensagem) {
        new MaterialAlertDialogBuilder(this)
                .setTitle(R.string.conta_erro_title)
                .setMessage(mensagem)
                .setPositiveButton(R.string.conta_fechar, null)
                .setOnDismissListener(d -> finish())
                .show();
    }

    private void voltarProApp() {
        Intent abrir = getPackageManager().getLaunchIntentForPackage(getPackageName());
        if (abrir != null) {
            abrir.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            startActivity(abrir);
        }
        finish();
    }
}
