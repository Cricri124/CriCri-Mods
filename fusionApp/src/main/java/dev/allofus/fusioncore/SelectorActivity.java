package dev.allofus.fusioncore;

import android.content.ActivityNotFoundException;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.Intent;
import android.content.pm.PackageInfo;
import android.content.pm.PackageManager;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.util.Log;
import android.view.LayoutInflater;
import android.view.View;
import android.view.ViewGroup;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.TextView;
import android.widget.Toast;

import androidx.activity.result.ActivityResultLauncher;
import androidx.activity.result.contract.ActivityResultContracts;
import androidx.appcompat.app.AlertDialog;
import androidx.appcompat.app.AppCompatActivity;
import androidx.core.content.ContextCompat;

import com.google.android.material.button.MaterialButton;
import com.google.android.material.button.MaterialButtonToggleGroup;
import com.google.android.material.materialswitch.MaterialSwitch;

import java.io.InputStream;
import java.text.DateFormat;
import java.util.ArrayList;
import java.util.Date;
import java.util.List;
import java.util.Map;

import dev.allofus.fusioncore.tools.Atalho;
import dev.allofus.fusioncore.tools.ChaveBr;
import dev.allofus.fusioncore.tools.Conta;
import dev.allofus.fusioncore.tools.CrashDetector;
import dev.allofus.fusioncore.tools.GlobalMetadata;
import dev.allofus.fusioncore.tools.Guia;
import dev.allofus.fusioncore.tools.ModsManager;
import dev.allofus.fusioncore.tools.ModsRemotos;
import dev.allofus.fusioncore.tools.Otimizacoes;
import dev.allofus.fusioncore.tools.Utilities;

// CriCri Mods: the home screen. One game (Among Us), the mods packaged in the app with an on/off switch each,
// and the PLAY button. The launch flow below (permissions, metadata, BootstrapActivity) is Fusion Core's.
public class SelectorActivity extends AppCompatActivity {
    private static final String TAG = "FusionCore";
    /** Sent by the "Jogar com mods" icon: press PLAY right away. */
    public static final String EXTRA_JOGAR_AGORA = "dev.allofus.fusioncore.JOGAR_AGORA";

    private String pendingLaunchPackage;
    private String pendingMetadataPackage;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        Utilities.init(this);
        setContentView(R.layout.activity_selector);

        View root = findViewById(R.id.selector_root);
        Utilities.applyWindowInsets(root, 0);
        // the content scrolls under the fixed play bar: keep its end reachable above the bar
        View barra = findViewById(R.id.barra_jogar);
        View scroll = findViewById(R.id.scroll);
        barra.addOnLayoutChangeListener((v, l, t, r, b, ol, ot, or, ob) ->
                scroll.setPadding(scroll.getPaddingLeft(), scroll.getPaddingTop(), scroll.getPaddingRight(), b - t));

        findViewById(R.id.play_button).setOnClickListener(v -> jogar());
        findViewById(R.id.install_game_button).setOnClickListener(v -> openGameStorePage());
        findViewById(R.id.about_button).setOnClickListener(v -> showAbout());
        findViewById(R.id.about_link).setOnClickListener(v -> showAbout());
        findViewById(R.id.tutorial_link).setOnClickListener(v -> mostrarGuia());
        findViewById(R.id.conta_entrar).setOnClickListener(v -> entrar());
        findViewById(R.id.conta_mais).setOnClickListener(this::menuDaConta);
        findViewById(R.id.atalho_button).setOnClickListener(v -> pedirAtalho());

        CrashDetector.init(this);
        lancandoAgora = jogarSePedido(getIntent());
    }

    // true while this screen was opened only to launch the game ("Jogar com mods"): no guide over it then
    private boolean lancandoAgora;

    @Override
    protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        setIntent(intent);
        jogarSePedido(intent);
    }

    private boolean jogarSePedido(Intent intent) {
        if (intent == null || !intent.getBooleanExtra(EXTRA_JOGAR_AGORA, false)) return false;
        intent.removeExtra(EXTRA_JOGAR_AGORA);
        if (findGame() == null) return false;
        maybeLaunchBootstrap(BuildConfig.TARGET_PACKAGE);
        return true;
    }

    @Override
    protected void onResume() {
        super.onResume();
        // The game may have been installed or updated while we were in the background.
        refresh();
        // Mods from the site (signed catalog), in the background; the list redraws if something new arrived.
        ModsRemotos.verificar(this, false, () -> {
            if (!isFinishing()) refresh();
        });
        java.io.File erro = lancandoAgora ? null : dev.allofus.fusioncore.tools.RelatorioDeErro.novo(this);
        if (erro != null) {
            findViewById(R.id.selector_root).postDelayed(() -> {
                if (!isFinishing() && getLifecycle().getCurrentState().isAtLeast(androidx.lifecycle.Lifecycle.State.RESUMED)) oferecerRelatorio(erro);
            }, 450);
        } else if (!lancandoAgora && !Guia.jaVisto(this)) {
            findViewById(R.id.selector_root).postDelayed(() -> {
                if (!isFinishing() && getLifecycle().getCurrentState().isAtLeast(androidx.lifecycle.Lifecycle.State.RESUMED)) mostrarGuia();
            }, 450);
        } else if (!lancandoAgora && !Atalho.jaOferecido(this) && findGame() != null && Atalho.suportado(this)) {
            // once, after the guide: the shortcut that keeps the habit from opening the plain game
            findViewById(R.id.selector_root).postDelayed(() -> {
                if (!isFinishing() && getLifecycle().getCurrentState().isAtLeast(androidx.lifecycle.Lifecycle.State.RESUMED)) oferecerAtalho();
            }, 600);
        }
        lancandoAgora = false;
    }

    // CriCri Mods (11/11): the app or the game closed by itself last time. The player chooses to share or copy the report; nothing is sent by us.
    private void oferecerRelatorio(java.io.File erro) {
        dev.allofus.fusioncore.tools.RelatorioDeErro.marcarVisto(this, erro);
        String texto = dev.allofus.fusioncore.tools.RelatorioDeErro.montar(this, erro);
        new com.google.android.material.dialog.MaterialAlertDialogBuilder(this)
                .setTitle(R.string.relatorio_titulo)
                .setMessage(R.string.relatorio_texto)
                .setPositiveButton(R.string.relatorio_mandar, (d, w) -> {
                    Intent enviar = new Intent(Intent.ACTION_SEND).setType("text/plain")
                            .putExtra(Intent.EXTRA_SUBJECT, getString(R.string.relatorio_assunto))
                            .putExtra(Intent.EXTRA_TEXT, texto);
                    try { startActivity(Intent.createChooser(enviar, getString(R.string.relatorio_mandar))); }
                    catch (Exception e) { copiarRelatorio(texto); }
                })
                // (11/11, ele: "menos complexidade") two buttons: the phone's share sheet already offers "Copiar"; without any app to
                // share with, "Mandar" copies by itself (the catch above)
                .setNegativeButton(R.string.relatorio_agora_nao, null)
                .show();
    }

    private void copiarRelatorio(String texto) {
        android.content.ClipboardManager cm = getSystemService(android.content.ClipboardManager.class);
        if (cm != null) cm.setPrimaryClip(android.content.ClipData.newPlainText(getString(R.string.relatorio_assunto), texto));
        android.widget.Toast.makeText(this, R.string.relatorio_copiado, android.widget.Toast.LENGTH_SHORT).show();
    }

    // First-run guide (also from "Como usar o app" at the bottom). (11/11, ele: "menos texto, menos complexidade") Two steps, not four:
    // the account is optional, and JOGAR. The game's line and the mods' rows say what they are on the screen itself; "gráficos e
    // FPS ficam dentro do jogo" is in the details of the Otimização mod ("Onde fica").
    private void mostrarGuia() {
        // without the game the one thing to do is on the button; the guide waits for the game (it is not marked as seen)
        if (findGame() == null) return;
        Guia.mostrar(this, java.util.Arrays.asList(
                new Guia.Passo(findViewById(R.id.bloco_conta), R.string.guia_conta_titulo, R.string.guia_conta_texto),
                new Guia.Passo(findViewById(R.id.play_button), R.string.guia_jogar_titulo, R.string.guia_jogar_texto)));
    }

    private void refresh() {
        PackageInfo game = findGame();
        String gameVersion = game != null ? game.versionName : null;

        ImageView icon = findViewById(R.id.game_icon);
        TextView status = findViewById(R.id.game_status);
        if (game != null) {
            icon.setImageDrawable(getPackageManager().getApplicationIcon(game.applicationInfo));
            boolean conferido = modsConferidos(gameVersion);
            // (11/11, ele: "menos texto") the version number left this line: it is in "Sobre" (the i up there) and, for a mod that
            // was not checked on it, in that mod's details
            status.setText(conferido ? R.string.jogo_pronto : R.string.jogo_nao_conferido);
            status.setTextColor(ContextCompat.getColor(this, conferido ? R.color.cricri_ok : R.color.cricri_warning));
            Log.i(TAG, "Among Us installed: " + gameVersion);
        } else {
            icon.setImageResource(R.drawable.ic_jogo_ausente);
            status.setText(R.string.game_not_installed);
            status.setTextColor(ContextCompat.getColor(this, R.color.cricri_erro));
        }
        // without the game, the one action becomes installing it: no account, no shortcut and no guide to offer yet
        findViewById(R.id.play_button).setVisibility(game != null ? View.VISIBLE : View.GONE);
        // the shortcut button is there only while there is something to do: it leaves once the shortcut is on the home screen
        findViewById(R.id.atalho_button).setVisibility(
                game != null && Atalho.suportado(this) && !atalhoNaTelaInicial() ? View.VISIBLE : View.GONE);
        findViewById(R.id.install_game_button).setVisibility(game != null ? View.GONE : View.VISIBLE);
        findViewById(R.id.secao_conta).setVisibility(game != null ? View.VISIBLE : View.GONE);
        findViewById(R.id.tutorial_link).setVisibility(game != null ? View.VISIBLE : View.GONE);

        populateConta();
        populateChaveBr();
        populateMods(game != null ? gameVersion : null);
        atualizarResumo(game != null);
    }

    // The "Among Us (mod)" shortcut is pinned on the home screen (the launcher tells the system; a launcher that does not answer
    // counts as "not there", and the button stays).
    private boolean atalhoNaTelaInicial() {
        try {
            for (androidx.core.content.pm.ShortcutInfoCompat atalho : androidx.core.content.pm.ShortcutManagerCompat
                    .getShortcuts(this, androidx.core.content.pm.ShortcutManagerCompat.FLAG_MATCH_PINNED)) {
                if (Atalho.ID.equals(atalho.getId())) return true;
            }
        } catch (Exception e) {
            Log.w(TAG, "nao li os atalhos da tela inicial: " + e.getClass().getSimpleName());
        }
        return false;
    }

    // every mod that is on was checked on this game version
    private boolean modsConferidos(String gameVersion) {
        for (ModsManager.Mod mod : ModsManager.catalog(this)) {
            if (!mod.hidden && ModsManager.isEnabled(this, mod) && !mod.supportsGame(gameVersion)) return false;
        }
        return true;
    }

    // The line above JOGAR: who the game logs in as and how many mods are on.
    private void atualizarResumo(boolean temJogo) {
        TextView resumo = findViewById(R.id.jogar_resumo);
        if (!temJogo) {
            resumo.setText(R.string.resumo_sem_jogo);
            return;
        }
        Conta.Login login = Conta.carregar(this);
        String quem = login != null && login.valido()
                ? getString(R.string.resumo_conta_format, login.nome.isEmpty() ? getString(R.string.conta_sem_nome) : login.nome)
                : getString(R.string.resumo_convidado);
        int ligados = 0;
        for (ModsManager.Mod mod : ModsManager.catalog(this)) {
            if (!mod.hidden && ModsManager.isEnabled(this, mod)) ligados++;
        }
        String mods = ligados == 0 ? getString(R.string.resumo_sem_mods)
                : ligados == 1 ? getString(R.string.resumo_um_mod)
                : getString(R.string.resumo_mods_format, ligados);
        resumo.setText(getString(R.string.resumo_format, quem, mods));
    }

    // "Conta": who the game will log in as. The token itself is never shown.
    private void populateConta() {
        Conta.Login login = Conta.carregar(this);
        TextView avatar = findViewById(R.id.conta_avatar);
        TextView nome = findViewById(R.id.conta_nome);
        TextView status = findViewById(R.id.conta_status);
        MaterialButton entrar = findViewById(R.id.conta_entrar);
        String[] recusado = login == null ? Conta.recusado(this) : null;
        // The last account, kept after its login expires: "Entrar de novo" goes straight back to it.
        String lembrado = Conta.nomeLembrado(this);
        int corStatus = R.color.cricri_warning;
        String nomeConta;
        entrar.setVisibility(View.VISIBLE);
        if (login != null && login.valido()) {
            nomeConta = login.nome.isEmpty() ? getString(R.string.conta_sem_nome) : login.nome;
            String hora = DateFormat.getTimeInstance(DateFormat.SHORT).format(new Date(login.expiraEmMs));
            status.setText(getString(R.string.conta_valido_format, hora));
            corStatus = 0;
            // (11/11, ele: "menos complexidade") a login that is valid asks for nothing: the action is JOGAR. "Renovar" left the
            // screen and lives in the ⋮ of the account ("Renovar o login").
            entrar.setVisibility(View.GONE);
        } else if (recusado != null) {
            nomeConta = recusado[0].isEmpty() ? getString(R.string.conta_sem_nome) : recusado[0];
            status.setText(R.string.conta_recusado_curto);
            entrar.setText(R.string.conta_entrar_de_novo);
        } else if (login != null || lembrado != null) {
            String n = login != null ? login.nome : lembrado;
            nomeConta = n.isEmpty() ? getString(R.string.conta_sem_nome) : n;
            status.setText(R.string.conta_vencido_curto);
            entrar.setText(R.string.conta_entrar_de_novo);
        } else {
            nomeConta = null;
            status.setText(R.string.conta_convidado_status);
            corStatus = 0;
            entrar.setText(R.string.conta_entrar_curto);
        }
        nome.setText(nomeConta != null ? nomeConta : getString(R.string.conta_convidado_nome));
        avatar.setText(nomeConta != null && !nomeConta.isEmpty() ? nomeConta.substring(0, 1).toUpperCase() : "?");
        // amber only when something needs doing (expired or refused); otherwise the normal secondary text
        status.setTextColor(corStatus != 0 ? ContextCompat.getColor(this, corStatus) : corSecundaria());
    }

    private int corSecundaria() {
        android.util.TypedValue tv = new android.util.TypedValue();
        getTheme().resolveAttribute(android.R.attr.textColorSecondary, tv, true);
        return tv.resourceId != 0 ? ContextCompat.getColor(this, tv.resourceId) : tv.data;
    }

    // "Entrar": the first time, say what will happen (the step people miss is "Compartilhar"); after that, go.
    private void entrar() {
        if (Conta.nomeLembrado(this) != null) {
            Toast.makeText(this, R.string.login_lembrete, Toast.LENGTH_LONG).show();
            abrirLoginDoGoogle();
            return;
        }
        new com.google.android.material.dialog.MaterialAlertDialogBuilder(this)
                .setTitle(R.string.login_como_title)
                .setView(R.layout.dialog_login_como)
                .setPositiveButton(R.string.login_abrir, (d, w) -> abrirLoginDoGoogle())
                .setNegativeButton(R.string.login_cancelar, null)
                .show();
    }

    private void menuDaConta(View ancora) {
        android.widget.PopupMenu menu = new android.widget.PopupMenu(this, ancora);
        Conta.Login atual = Conta.carregar(this);
        // with a valid login the "Renovar" button is not on the screen any more: it is the first item here
        if (atual != null && atual.valido()) menu.getMenu().add(0, 4, 0, R.string.conta_renovar);
        menu.getMenu().add(0, 1, 1, R.string.conta_menu_como);
        menu.getMenu().add(0, 2, 2, R.string.conta_menu_colar);
        boolean temConta = atual != null || Conta.recusado(this) != null || Conta.nomeLembrado(this) != null;
        if (temConta) menu.getMenu().add(0, 3, 3, R.string.conta_menu_sair);
        menu.setOnMenuItemClickListener(item -> {
            switch (item.getItemId()) {
                case 4:
                    entrar();
                    return true;
                case 1:
                    new com.google.android.material.dialog.MaterialAlertDialogBuilder(this)
                            .setTitle(R.string.login_como_title).setView(R.layout.dialog_login_como)
                            .setPositiveButton(R.string.conta_fechar, null).show();
                    return true;
                case 2:
                    colarEndereco();
                    return true;
                case 3:
                    Conta.sair(this);
                    Toast.makeText(this, R.string.conta_sair_feito, Toast.LENGTH_SHORT).show();
                    refresh();
                    return true;
                default:
                    return false;
            }
        });
        menu.show();
    }

    private boolean abrirLoginDoGoogle() {
        if (Conta.abrirLogin(this)) return true;
        Toast.makeText(this, R.string.conta_sem_navegador, Toast.LENGTH_LONG).show();
        return false;
    }

    // ---- the CRICRI BR mod key (tools/ChaveBr): pasted from the site, kept encrypted ----

    private void populateChaveBr() {
        boolean tem = ChaveBr.temChave(this);
        ((TextView) findViewById(R.id.chave_br_status)).setText(tem ? R.string.chave_br_ligada : R.string.chave_br_sem);
        MaterialButton botao = findViewById(R.id.chave_br_botao);
        String aguardando = tem ? null : dev.allofus.fusioncore.tools.ConectarBr.linkEmAndamento();
        // The account links by itself in the first CRICRI BR lobby. The button exists only while a link opened on the site waits to be
        // confirmed ("Abrir site"). (11/11, ele: "menos complexidade") A linked account shows "Ligada" and nothing to press: erasing
        // the key was a filled "Apagar" beside it, the loudest thing of the block, done in one tap with no question; it is in the ⋮ now.
        botao.setVisibility(aguardando != null ? View.VISIBLE : View.GONE);
        botao.setText(R.string.chave_br_abrir_de_novo);
        View mais = findViewById(R.id.chave_br_mais);
        mais.setVisibility(tem ? View.VISIBLE : View.GONE);
        mais.setOnClickListener(this::menuDaChaveBr);
        TextView outroJeito = findViewById(R.id.chave_br_colar_link);
        outroJeito.setVisibility(tem ? View.GONE : View.VISIBLE);
        outroJeito.setOnClickListener(v -> outrasFormasChaveBr());
        botao.setOnClickListener(v -> {
            String link = dev.allofus.fusioncore.tools.ConectarBr.linkEmAndamento();
            if (link != null && !ChaveBr.temChave(this)) abrirNoNavegador(link);
            populateChaveBr();
        });
    }

    // The ⋮ of a linked CRICRI BR account: the one thing left to do with it.
    private void menuDaChaveBr(View ancora) {
        android.widget.PopupMenu menu = new android.widget.PopupMenu(this, ancora);
        menu.getMenu().add(0, 1, 0, R.string.chave_br_apagar);
        menu.setOnMenuItemClickListener(item -> {
            ChaveBr.apagar(this);
            Toast.makeText(this, R.string.chave_br_apagada, Toast.LENGTH_SHORT).show();
            populateChaveBr();
            return true;
        });
        menu.show();
    }

    // Only if linking in the lobby did not work: the site and the key by hand.
    private void outrasFormasChaveBr() {
        CharSequence[] formas = {
                getString(R.string.chave_br_conectar_site),
                getString(R.string.chave_br_colar_manual),
                getString(R.string.chave_como_titulo),
        };
        new com.google.android.material.dialog.MaterialAlertDialogBuilder(this)
                .setTitle(R.string.chave_br_outro_jeito)
                .setItems(formas, (d, qual) -> {
                    if (qual == 0) conectarBr();
                    else if (qual == 1) colarChaveBr();
                    else comoPegarChaveBr();
                    populateChaveBr();
                })
                .setNegativeButton(R.string.conta_fechar, null)
                .show();
    }

    // One tap: the site confirms in this phone's browser and the key arrives by itself. Pasting by hand stays as the fallback.
    private void conectarBr() {
        View botao = findViewById(R.id.chave_br_botao);
        botao.setEnabled(false);   // two quick taps would open the site twice
        Toast.makeText(this, R.string.chave_br_abrindo, Toast.LENGTH_LONG).show();
        dev.allofus.fusioncore.tools.ConectarBr.comecar(this, url -> {
            abrirNoNavegador(url);
            botao.setEnabled(true);
            if (!isDestroyed()) populateChaveBr();
        }, como -> {
            botao.setEnabled(true);
            if (isDestroyed()) return;
            populateChaveBr();
            switch (como) {
                case dev.allofus.fusioncore.tools.ConectarBr.CONECTADA:
                    Toast.makeText(this, R.string.chave_br_conectada, Toast.LENGTH_SHORT).show();
                    populateChaveBr();
                    break;
                case dev.allofus.fusioncore.tools.ConectarBr.RECUSADA:
                    Toast.makeText(this, R.string.chave_br_recusada, Toast.LENGTH_LONG).show();
                    break;
                case dev.allofus.fusioncore.tools.ConectarBr.VENCEU:
                    Toast.makeText(this, R.string.chave_br_venceu, Toast.LENGTH_LONG).show();
                    break;
                default:
                    Toast.makeText(this, R.string.chave_br_sem_servidor, Toast.LENGTH_LONG).show();
                    comoPegarChaveBr();
                    break;
            }
        });
    }

    private void abrirNoNavegador(String url) {
        try {
            startActivity(new Intent(Intent.ACTION_VIEW, Uri.parse(url)));
        } catch (ActivityNotFoundException e) {
            Toast.makeText(this, R.string.chave_sem_navegador, Toast.LENGTH_LONG).show();
        }
    }

    private void comoPegarChaveBr() {
        new com.google.android.material.dialog.MaterialAlertDialogBuilder(this)
                .setTitle(R.string.chave_como_titulo)
                .setView(R.layout.dialog_chave_como)
                .setPositiveButton(R.string.chave_abrir_site, (d, w) -> {
                    try {
                        startActivity(new Intent(Intent.ACTION_VIEW, Uri.parse("https://cricribr.com.br/conta")));
                    } catch (ActivityNotFoundException e) {
                        Toast.makeText(this, R.string.chave_sem_navegador, Toast.LENGTH_LONG).show();
                    }
                })
                .setNegativeButton(R.string.conta_fechar, null)
                .show();
    }

    // the clipboard is read only on this tap
    private void colarChaveBr() {
        ClipboardManager clipboard = (ClipboardManager) getSystemService(CLIPBOARD_SERVICE);
        ClipData clip = clipboard != null ? clipboard.getPrimaryClip() : null;
        CharSequence texto = clip != null && clip.getItemCount() > 0 ? clip.getItemAt(0).coerceToText(this) : null;
        String chave = ChaveBr.limpar(texto);
        if (chave == null) {
            Toast.makeText(this, R.string.chave_br_invalida, Toast.LENGTH_LONG).show();
            comoPegarChaveBr();
            return;
        }
        try {
            ChaveBr.salvar(this, chave);
            if (clipboard != null && android.os.Build.VERSION.SDK_INT >= 28) clipboard.clearPrimaryClip();   // the key does not stay around for other apps
            Toast.makeText(this, R.string.chave_br_colada, Toast.LENGTH_SHORT).show();
        } catch (Exception e) {
            Log.w(TAG, "nao guardei a chave do CRICRI BR: " + e.getClass().getSimpleName());
            Toast.makeText(this, R.string.conta_erro_salvar, Toast.LENGTH_LONG).show();
        }
    }

    // ---- the habit of opening the plain game (tools/Atalho) ----

    private void oferecerAtalho() {
        Atalho.marcarOferecido(this);
        new com.google.android.material.dialog.MaterialAlertDialogBuilder(this)
                .setTitle(R.string.atalho_dica_titulo)
                .setMessage(R.string.atalho_dica_texto)
                .setPositiveButton(R.string.atalho_por, (d, w) -> pedirAtalho())
                .setNegativeButton(R.string.login_cancelar, null)
                .show();
    }

    private void pedirAtalho() {
        boolean pedido = Atalho.pedir(this);
        String fabricante = Build.MANUFACTURER == null ? "" : Build.MANUFACTURER.toLowerCase(java.util.Locale.ROOT);
        boolean xiaomi = fabricante.contains("xiaomi") || fabricante.contains("redmi") || fabricante.contains("poco");
        if (pedido && !xiaomi) {
            Toast.makeText(this, R.string.atalho_pedido, Toast.LENGTH_LONG).show();
            return;
        }
        // Xiaomi/HyperOS drops the request silently unless the app may add home-screen shortcuts
        new com.google.android.material.dialog.MaterialAlertDialogBuilder(this)
                .setTitle(R.string.atalho_xiaomi_titulo)
                .setMessage(pedido ? R.string.atalho_xiaomi_texto : R.string.atalho_negado_texto)
                .setPositiveButton(R.string.atalho_abrir_permissoes, (d, w) -> {
                    try {
                        startActivity(new Intent(android.provider.Settings.ACTION_APPLICATION_DETAILS_SETTINGS,
                                Uri.fromParts("package", getPackageName(), null)));
                    } catch (ActivityNotFoundException e) {
                        Toast.makeText(this, R.string.atalho_negado_texto, Toast.LENGTH_LONG).show();
                    }
                })
                .setNegativeButton(R.string.conta_fechar, null)
                .show();
    }

    // JOGAR: with the login of an account the person uses expired (it lasts an hour), ask first, like the quick icon.
    // As a guest the game would empty the inventory and put the default outfit on (the plugin now puts it back).
    // "Entrar de novo" opens the login and the game starts by itself after the ✓.
    private void jogar() {
        Conta.Login login = Conta.carregar(this);
        String lembrado = Conta.nomeLembrado(this);
        if (lembrado == null || (login != null && login.valido())) {
            maybeLaunchBootstrap(BuildConfig.TARGET_PACKAGE);
            return;
        }
        String nome = lembrado.isEmpty() ? getString(R.string.conta_sem_nome) : lembrado;
        new com.google.android.material.dialog.MaterialAlertDialogBuilder(this)
                .setTitle(R.string.entrada_login_title)
                .setMessage(getString(R.string.entrada_login_format, nome))
                .setPositiveButton(R.string.conta_entrar_de_novo, (d, w) -> {
                    EntradaRapidaActivity.marcarJogarDepoisDoLogin(this, true);
                    if (abrirLoginDoGoogle()) Toast.makeText(this, R.string.login_lembrete, Toast.LENGTH_LONG).show();
                    else EntradaRapidaActivity.marcarJogarDepoisDoLogin(this, false);
                })
                .setNegativeButton(R.string.entrada_jogar_convidado, (d, w) -> maybeLaunchBootstrap(BuildConfig.TARGET_PACKAGE))
                .show();
    }

    // Fallback for browsers whose share sheet doesn't list the app: copy the page's address, then tap here.
    private void colarEndereco() {
        ClipboardManager clipboard = (ClipboardManager) getSystemService(CLIPBOARD_SERVICE);
        ClipData clip = clipboard != null ? clipboard.getPrimaryClip() : null;
        CharSequence texto = clip != null && clip.getItemCount() > 0 ? clip.getItemAt(0).coerceToText(this) : null;
        if (texto == null || texto.length() == 0) {
            Toast.makeText(this, R.string.conta_colar_vazio, Toast.LENGTH_LONG).show();
            return;
        }
        try {
            Conta.Login login = Conta.extrair(texto.toString());
            Conta.salvar(this, login);
            refresh();
        } catch (Conta.Problema p) {
            new com.google.android.material.dialog.MaterialAlertDialogBuilder(this).setTitle(R.string.conta_erro_title).setMessage(p.mensagem)
                    .setPositiveButton(R.string.conta_fechar, null).show();
        } catch (Exception e) {
            Toast.makeText(this, R.string.conta_erro_salvar, Toast.LENGTH_LONG).show();
        }
    }

    private PackageInfo findGame() {
        try {
            PackageManager pm = getPackageManager();
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                return pm.getPackageInfo(BuildConfig.TARGET_PACKAGE, PackageManager.PackageInfoFlags.of(0));
            }
            return pm.getPackageInfo(BuildConfig.TARGET_PACKAGE, 0);
        } catch (PackageManager.NameNotFoundException e) {
            return null;
        }
    }

    private void populateMods(String gameVersion) {
        LinearLayout list = findViewById(R.id.mods_list);
        list.removeAllViews();
        LayoutInflater inflater = LayoutInflater.from(this);

        List<ModsManager.Mod> mods = ModsManager.catalog(this);
        if (mods.isEmpty()) {
            TextView empty = new TextView(this);
            empty.setText(R.string.mods_empty);
            list.addView(empty);
            return;
        }

        // the ones the person can switch first, then the ones that are part of the app
        List<ModsManager.Mod> ordem = new ArrayList<>();
        for (ModsManager.Mod mod : mods) if (mod.listed && !mod.hidden) ordem.add(mod);
        for (ModsManager.Mod mod : mods) if (mod.listed && mod.hidden) ordem.add(mod);

        for (ModsManager.Mod mod : ordem) {
            View row = inflater.inflate(R.layout.item_mod, list, false);
            ((ImageView) row.findViewById(R.id.mod_icone)).setImageResource(icone(mod.icon));
            ((TextView) row.findViewById(R.id.mod_name)).setText(mod.name);
            // (11/11, ele: "menos texto") who made it and the version are in the details sheet; the row names the author only when
            // the mod is not the app's own
            TextView autor = row.findViewById(R.id.mod_autor);
            autor.setText(autorEVersao(mod, R.string.mod_autor_format));
            boolean doApp = mod.author.isEmpty() || mod.author.equalsIgnoreCase(getString(R.string.autor_do_app));
            autor.setVisibility(doApp ? View.GONE : View.VISIBLE);
            ((TextView) row.findViewById(R.id.mod_description)).setText(mod.description);

            TextView warning = row.findViewById(R.id.mod_warning);
            if (gameVersion != null && !mod.supportsGame(gameVersion)) {
                // short on the row; the version number and what to do about it are in the details sheet
                warning.setText(R.string.mod_nao_conferido_curto);
                warning.setVisibility(View.VISIBLE);
            }

            MaterialSwitch toggle = row.findViewById(R.id.mod_switch);
            if (mod.hidden) {
                toggle.setVisibility(View.GONE);
                row.findViewById(R.id.mod_sempre).setVisibility(View.VISIBLE);
            } else {
                toggle.setChecked(ModsManager.isEnabled(this, mod));
                toggle.setContentDescription(mod.name);
                toggle.setOnCheckedChangeListener((button, checked) -> {
                    ModsManager.setEnabled(this, mod, checked);
                    atualizarResumo(gameVersion != null);
                });
            }
            // the row opens what the mod does; the switch alone turns it on and off
            row.setOnClickListener(v -> abrirDetalhes(mod, gameVersion, toggle));
            list.addView(row);
        }
        emBreve(list, inflater);
    }

    // CriCri, 11/11: "eu nao quero liberar nada da oficina ainda, é pra ficar em breve ela". Only a card: no switch, no download, nothing of
    // the Oficina inside the app.
    private void emBreve(LinearLayout list, LayoutInflater inflater) {
        View row = inflater.inflate(R.layout.item_mod, list, false);
        ((ImageView) row.findViewById(R.id.mod_icone)).setImageResource(R.drawable.ic_mod_padrao);
        ((TextView) row.findViewById(R.id.mod_name)).setText(R.string.oficina_nome);
        ((TextView) row.findViewById(R.id.mod_description)).setText(R.string.oficina_descricao);
        row.findViewById(R.id.mod_switch).setVisibility(View.GONE);
        TextView selo = row.findViewById(R.id.mod_sempre);
        selo.setText(R.string.oficina_em_breve);
        selo.setVisibility(View.VISIBLE);
        row.setAlpha(0.6f);
        row.setContentDescription(getString(R.string.oficina_nome) + ", " + getString(R.string.oficina_em_breve));
        list.addView(row);
    }

    // The icons drawn for each mod (res/drawable/ic_mod_*). A switch instead of getIdentifier: the resource
    // shrinker keeps only what the code names.
    static int icone(String chave) {
        switch (chave) {
            case "replay": return R.drawable.ic_mod_replay;
            case "otimizacao": return R.drawable.ic_mod_otimizacao;
            case "conta": return R.drawable.ic_mod_conta;
            case "servidor": return R.drawable.ic_mod_servidor;
            case "menu": return R.drawable.ic_mod_menu;
            default: return R.drawable.ic_mod_padrao;
        }
    }

    private String autorEVersao(ModsManager.Mod mod, int formato) {
        String autor = mod.author.isEmpty() ? getString(R.string.app_name) : mod.author;
        String texto = getString(formato, autor, mod.version);
        return mod.remoto ? texto + getString(R.string.mod_do_site) : texto;
    }

    // The details of one mod, from the bottom of the screen: what it does, where it is in the game, the game
    // version it was checked on and its switch (the same as the row's).
    private void abrirDetalhes(ModsManager.Mod mod, String gameVersion, MaterialSwitch chaveDaLinha) {
        var folha = new com.google.android.material.bottomsheet.BottomSheetDialog(this);
        View v = LayoutInflater.from(this).inflate(R.layout.sheet_mod, null, false);
        ((ImageView) v.findViewById(R.id.sheet_icone)).setImageResource(icone(mod.icon));
        ((TextView) v.findViewById(R.id.sheet_nome)).setText(mod.name);
        ((TextView) v.findViewById(R.id.sheet_autor)).setText(autorEVersao(mod, R.string.sheet_autor_format));
        ((TextView) v.findViewById(R.id.sheet_resumo)).setText(mod.description);

        LinearLayout itens = v.findViewById(R.id.sheet_itens);
        for (String item : mod.details) {
            TextView t = new TextView(this);
            t.setTextSize(android.util.TypedValue.COMPLEX_UNIT_SP, 15);
            // hanging indent: the wrapped lines start under the text, not under the bullet
            String linha = getString(R.string.sheet_item_format, item);
            String marca = linha.substring(0, linha.length() - item.length());
            android.text.SpannableString recuado = new android.text.SpannableString(linha);
            recuado.setSpan(new android.text.style.LeadingMarginSpan.Standard(0, Math.round(t.getPaint().measureText(marca))),
                    0, linha.length(), android.text.Spanned.SPAN_EXCLUSIVE_EXCLUSIVE);
            t.setText(recuado);
            t.setTextColor(corPrimaria());
            t.setPadding(0, dp(8), 0, 0);
            itens.addView(t);
        }
        v.findViewById(R.id.sheet_titulo_itens).setVisibility(mod.details.isEmpty() ? View.GONE : View.VISIBLE);
        v.findViewById(R.id.sheet_titulo_onde).setVisibility(mod.where.isEmpty() ? View.GONE : View.VISIBLE);
        TextView onde = v.findViewById(R.id.sheet_onde);
        onde.setText(mod.where);
        onde.setVisibility(mod.where.isEmpty() ? View.GONE : View.VISIBLE);

        TextView compat = v.findViewById(R.id.sheet_compat);
        if (gameVersion != null && !mod.supportsGame(gameVersion)) {
            compat.setText(getString(R.string.mod_not_checked_format, gameVersion));
            compat.setTextColor(ContextCompat.getColor(this, R.color.cricri_warning));
        } else if (!mod.gameVersions.isEmpty()) {
            compat.setText(getString(R.string.sheet_compat_format, mod.gameVersions.get(mod.gameVersions.size() - 1)));
        } else {
            compat.setVisibility(View.GONE);
        }

        TextView estado = v.findViewById(R.id.sheet_estado);
        MaterialSwitch chave = v.findViewById(R.id.sheet_chave);
        if (mod.hidden) {
            estado.setText(R.string.sheet_parte_do_app);
            estado.setTextColor(corSecundaria());
            chave.setVisibility(View.GONE);
        } else {
            boolean ligado = ModsManager.isEnabled(this, mod);
            chave.setChecked(ligado);
            chave.setContentDescription(mod.name);
            estado.setText(ligado ? R.string.sheet_ligado : R.string.sheet_desligado);
            chave.setOnCheckedChangeListener((b, marcado) -> {
                estado.setText(marcado ? R.string.sheet_ligado : R.string.sheet_desligado);
                // the row's switch saves it and updates the line above JOGAR
                if (chaveDaLinha.isChecked() != marcado) chaveDaLinha.setChecked(marcado);
            });
            v.findViewById(R.id.sheet_linha_chave).setOnClickListener(x -> chave.toggle());
        }
        folha.setContentView(v);
        folha.show();
        ultimaFolha = folha;
    }

    // for the tests: the details sheet on screen
    android.app.Dialog ultimaFolha;

    private int corPrimaria() {
        android.util.TypedValue tv = new android.util.TypedValue();
        getTheme().resolveAttribute(android.R.attr.textColorPrimary, tv, true);
        return tv.resourceId != 0 ? ContextCompat.getColor(this, tv.resourceId) : tv.data;
    }

    private int dp(int valor) {
        return Math.round(valor * getResources().getDisplayMetrics().density);
    }

    // GPL-3.0 section 5(d): an interactive program shows its legal notices (copyright, no warranty,
    // the license and how to see it). This is also where the source address lives.
    private void showAbout() {
        String source = BuildConfig.SOURCE_URL.isEmpty() ? getString(R.string.about_source_missing) : BuildConfig.SOURCE_URL;
        // the game's version left the home screen (11/11, "menos texto"); it is here for whoever asks "qual a sua versão?"
        PackageInfo jogo = findGame();
        String versoes = BuildConfig.VERSION_NAME + (jogo != null ? " · Among Us " + jogo.versionName : "");
        new com.google.android.material.dialog.MaterialAlertDialogBuilder(this)
                .setTitle(R.string.about_title)
                .setMessage(getString(R.string.about_text_format, versoes, source))
                .setPositiveButton(R.string.about_close, null)
                // (11/11, ele: "menos complexidade") two buttons, side by side: with three the dialog stacked them and the last one fell off
                // a normal screen. "Licenças" lists every piece of the app and its license (TERCEIROS.txt) and carries the "Ver licença"
                // button, which opens the GPL text.
                .setNegativeButton(R.string.about_third_party, (d, w) -> showTextAsset(R.string.about_third_party, "legal/TERCEIROS.txt", true))
                .show();
    }

    private void showTextAsset(int titleRes, String asset, boolean comVerLicenca) {
        String text;
        try (InputStream in = getAssets().open(asset)) {
            java.io.ByteArrayOutputStream out = new java.io.ByteArrayOutputStream();
            byte[] buffer = new byte[16 * 1024];
            int count;
            while ((count = in.read(buffer)) != -1) {
                out.write(buffer, 0, count);
            }
            text = out.toString("UTF-8");
        } catch (Exception e) {
            Log.e(TAG, "Failed to read " + asset, e);
            text = asset;
        }

        TextView view = new TextView(this);
        int pad = Math.round(getResources().getDisplayMetrics().density * 20f);
        view.setPadding(pad, pad / 2, pad, pad / 2);
        view.setTextSize(12f);
        view.setTextIsSelectable(true);
        view.setText(text);
        android.widget.ScrollView scroll = new android.widget.ScrollView(this);
        scroll.addView(view);

        var dialogo = new com.google.android.material.dialog.MaterialAlertDialogBuilder(this)
                .setTitle(titleRes)
                .setView(scroll)
                .setPositiveButton(R.string.about_close, null);
        if (comVerLicenca) {
            dialogo.setNegativeButton(R.string.about_view_license, (d, w) -> showTextAsset(R.string.about_view_license, "legal/LICENSE.txt", false));
        }
        dialogo.show();
    }

    private void openGameStorePage() {
        Uri market = Uri.parse("market://details?id=" + BuildConfig.TARGET_PACKAGE);
        Uri web = Uri.parse("https://play.google.com/store/apps/details?id=" + BuildConfig.TARGET_PACKAGE);
        try {
            startActivity(new Intent(Intent.ACTION_VIEW, market));
        } catch (Exception e) {
            try {
                startActivity(new Intent(Intent.ACTION_VIEW, web));
            } catch (Exception inner) {
                Toast.makeText(this, R.string.no_store, Toast.LENGTH_LONG).show();
            }
        }
    }

    private void launchBootstrap(String packageName) {
        if (requestMetadataIfNeeded(packageName)) {
            return;
        }

        Intent intent = new Intent(this, BootstrapActivity.class);
        intent.putExtra(BootstrapActivity.EXTRA_TARGET_PACKAGE, packageName);
        intent.putExtra(BootstrapActivity.EXTRA_USE_ORIGINAL_LIBUNITY,
                !FusionSettings.getUseUnstrippedLibUnityForGame(this, packageName));
        intent.putExtra(BootstrapActivity.EXTRA_USE_IL2CPP2MONO,
                FusionSettings.getUseIl2Cpp2Mono(this, packageName));

        intent.addFlags(Intent.FLAG_ACTIVITY_NO_ANIMATION | Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TOP);
        startActivity(intent);
        //noinspection deprecation
        overridePendingTransition(0, 0);
        finish();
        //noinspection deprecation
        overridePendingTransition(0, 0);
    }

    private final ActivityResultLauncher<String[]> requestPermissionsLauncher =
            registerForActivityResult(new ActivityResultContracts.RequestMultiplePermissions(), isGrantedMap -> {
                for (Map.Entry<String, Boolean> entry : isGrantedMap.entrySet()) {
                    String permission = entry.getKey();
                    boolean isGranted = entry.getValue();

                    if (isGranted) {
                        Log.i(TAG, "Got permission: " +permission);
                    } else {
                        Log.e(TAG, "Permission denied: " +permission);
                    }
                }

                String packageName = pendingLaunchPackage;
                pendingLaunchPackage = null;
                if (packageName != null) {
                    launchBootstrap(packageName);
                }
            });

    private void maybeLaunchBootstrap(String packageName) {
        if (requestMetadataIfNeeded(packageName)) {
            return;
        }

        try {
            var packageInfo = getPackageManager().getPackageInfo(packageName, PackageManager.GET_PERMISSIONS);
            var perms = packageInfo.requestedPermissions;
            // CriCri Mods: Fusion Core asked for ALL the game's permissions on every launch, 1.2 s of a system screen
            // each time on the CriCri's phone (08/11), and most can never be granted to this app (ads, billing, ad
            // id): only the ones this app declares can be. Each is asked once; Android keeps the answer.
            var mine = getPackageManager().getPackageInfo(getPackageName(), PackageManager.GET_PERMISSIONS).requestedPermissions;
            java.util.Set<String> declared = new java.util.HashSet<>();
            if (mine != null) java.util.Collections.addAll(declared, mine);
            var asked = getSharedPreferences("cricri_permissoes", MODE_PRIVATE);
            if (perms != null) {
                ArrayList<String> newPerms = new ArrayList<>();
                for (var p : perms) {
                    if (!declared.contains(p) || asked.getBoolean(p, false)) continue;
                    if (ContextCompat.checkSelfPermission(this, p) != PackageManager.PERMISSION_GRANTED) {
                        newPerms.add(p);
                    }
                }

                if (!newPerms.isEmpty()) {
                    var edit = asked.edit();
                    for (var p : newPerms) edit.putBoolean(p, true);
                    edit.apply();
                    pendingLaunchPackage = packageName;
                    requestPermissionsLauncher.launch(newPerms.toArray(new String[0]));
                    return;
                }
            }
        } catch (Exception e) {
            Log.e(TAG, "Failure getting package info for " + packageName, e);
        }

        launchBootstrap(packageName);
    }

    private final ActivityResultLauncher<String[]> pickMetadataLauncher =
            registerForActivityResult(new ActivityResultContracts.OpenDocument(), uri -> {
                String packageName = pendingMetadataPackage;
                pendingMetadataPackage = null;
                if (uri == null || packageName == null) {
                    return;
                }

                new Thread(() -> {
                    boolean installed;
                    try (InputStream in = getContentResolver().openInputStream(uri)) {
                        installed = in != null && GlobalMetadata.installOverride(in, packageName);
                    } catch (Exception e) {
                        Log.e(TAG, "Failed to install metadata override for " + packageName, e);
                        installed = false;
                    }

                    boolean success = installed;
                    runOnUiThread(() -> {
                        if (success) {
                            maybeLaunchBootstrap(packageName);
                        } else {
                            Toast.makeText(this, getString(R.string.selector_metadata_invalid), Toast.LENGTH_LONG).show();
                        }
                    });
                }).start();
            });

    private boolean requestMetadataIfNeeded(String packageName) {
        if (FusionSettings.getUseIl2Cpp2Mono(this, packageName)) {
            return false;
        }
        if (GlobalMetadata.hasValidOverride(packageName)) {
            return false;
        }
        try {
            if (!GlobalMetadata.isEncrypted(getPackageManager().getApplicationInfo(packageName, 0))) {
                return false;
            }
        } catch (PackageManager.NameNotFoundException e) {
            return false;
        }

        new com.google.android.material.dialog.MaterialAlertDialogBuilder(this)
                .setTitle(R.string.selector_metadata_title)
                .setMessage(getString(R.string.selector_metadata_message))
                .setPositiveButton(R.string.selector_metadata_select, (dialog, which) -> {
                    pendingMetadataPackage = packageName;
                    pickMetadataLauncher.launch(new String[]{"*/*"});
                })
                .setNegativeButton(android.R.string.cancel, null)
                .show();
        return true;
    }
}
