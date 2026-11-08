package dev.allofus.fusioncore;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNotNull;
import static org.junit.Assert.assertNull;
import static org.junit.Assert.assertTrue;

import android.app.Activity;
import android.app.Dialog;
import android.content.Context;
import android.content.Intent;
import android.content.pm.ApplicationInfo;
import android.content.pm.PackageInfo;
import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.os.Looper;
import android.util.Base64;
import android.view.View;
import android.view.ViewGroup;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.PopupMenu;
import android.widget.TextView;

import androidx.test.core.app.ApplicationProvider;
import androidx.test.ext.junit.runners.AndroidJUnit4;

import com.google.android.material.materialswitch.MaterialSwitch;

import org.json.JSONObject;
import org.junit.Before;
import org.junit.Test;
import org.junit.runner.RunWith;
import org.robolectric.Robolectric;
import org.robolectric.android.controller.ActivityController;
import org.robolectric.annotation.Config;
import org.robolectric.annotation.GraphicsMode;
import org.robolectric.shadows.ShadowDialog;
import org.robolectric.shadows.ShadowPopupMenu;
import org.robolectric.shadows.ShadowActivity;
import org.robolectric.shadows.ShadowLooper;
import org.robolectric.shadows.ShadowPackageManager;
import org.robolectric.shadow.api.Shadow;
import java.util.concurrent.TimeUnit;

import java.io.File;
import java.io.FileOutputStream;
import java.nio.charset.StandardCharsets;
import java.time.Duration;

import dev.allofus.fusioncore.tools.Conta;
import dev.allofus.fusioncore.tools.Guia;

/**
 * CriCri Mods: people using the home screen, step by step, the way they would on the phone. Each test is one
 * person with one goal (first time, no game installed, signing in, signing out, a mod off, the quick icon).
 * Screens are saved as PNG in fusionApp/build/telas to look at the result without a phone.
 * Phone size like the CriCri's Xiaomi (720x1600 px, xhdpi).
 */
@RunWith(AndroidJUnit4.class)
@Config(sdk = 35, qualifiers = "w360dp-h800dp-port-xhdpi")
@GraphicsMode(GraphicsMode.Mode.NATIVE)
public class PessoaUsandoTest {
    private static final String JOGO = "com.innersloth.spacemafia";
    private static final File TELAS = new File("build/telas");

    private Context ctx;

    @Before
    public void preparar() {
        ctx = ApplicationProvider.getApplicationContext();
        TELAS.mkdirs();
        // the one-time shortcut tip has its own test; elsewhere it would sit over the screen being tested
        dev.allofus.fusioncore.tools.Atalho.marcarOferecido(ctx);
        // no network from the tests: the site check (ModsRemotos) waits 10 minutes after the "last" one
        ctx.getSharedPreferences("cricri_mods_remotos", Context.MODE_PRIVATE).edit()
                .putLong("ultima_verificacao", System.currentTimeMillis()).commit();
    }

    // ---------- people ----------

    /** First time: the guide walks through the four parts and never shows up again. */
    @Test
    public void primeiraVez_oGuiaExplicaATelaEDepoisSome() {
        instalarJogo("2026.11.3");
        ActivityController<SelectorActivity> app = abrirApp();
        Activity tela = app.get();
        assertTrue("o guia aparece na primeira vez", Guia.aberto(tela));

        String[] passos = {"guia-1-jogo", "guia-2-conta", "guia-3-mods", "guia-4-jogar"};
        for (int i = 0; i < passos.length; i++) {
            esperar(400);
            foto(tela.getWindow().getDecorView(), passos[i]);
            assertTrue("o guia diz em que passo está", temTexto(tela.getWindow().getDecorView(), (i + 1) + " de 4"));
            Button botao = acharBotao(tela.getWindow().getDecorView(), i < 3 ? "Próximo" : "Entendi");
            assertNotNull("o botão do passo " + (i + 1) + " existe", botao);
            botao.performClick();
        }
        esperar(100);
        assertFalse("o guia fecha no fim", Guia.aberto(tela));
        assertTrue(Guia.jaVisto(ctx));

        app.pause().stop().destroy();
        ActivityController<SelectorActivity> deNovo = abrirApp();
        assertFalse("na segunda vez o guia não volta", Guia.aberto(deNovo.get()));
    }

    /** Someone in a hurry skips the guide and plays right away. */
    @Test
    public void apressado_pulaOGuiaEJoga() {
        instalarJogo("2026.11.3");
        Activity tela = abrirApp().get();
        acharBotao(tela.getWindow().getDecorView(), "Pular").performClick();
        esperar(100);
        assertFalse(Guia.aberto(tela));
        tela.findViewById(R.id.play_button).performClick();
        esperar(100);
        Intent proxima = ((ShadowActivity) Shadow.extract(tela)).getNextStartedActivity();
        Dialog dialogo = ShadowDialog.getLatestDialog();
        assertTrue("JOGAR abre o jogo (ou pede o arquivo de metadados do FusionCore)",
                (proxima != null && proxima.getComponent() != null
                        && proxima.getComponent().getClassName().equals(BootstrapActivity.class.getName()))
                        || (dialogo != null && dialogo.isShowing()));
    }

    /** Without Among Us, the one action is installing it, and the screen says why. */
    @Test
    public void semAmongUs_oBotaoViraInstalar() {
        Guia.marcarVisto(ctx);
        Activity tela = abrirApp().get();
        foto(tela.getWindow().getDecorView(), "sem-jogo");
        assertEquals(View.GONE, tela.findViewById(R.id.play_button).getVisibility());
        assertEquals(View.VISIBLE, tela.findViewById(R.id.install_game_button).getVisibility());
        assertEquals(ctx.getString(R.string.game_not_installed), texto(tela, R.id.game_status));
        assertEquals(ctx.getString(R.string.resumo_sem_jogo), texto(tela, R.id.jogar_resumo));
    }

    /** A guest signs in: the app explains the steps, opens Google, receives the shared page and shows the name. */
    @Test
    public void convidado_entraComOGoogle_ePassaAJogarComOSeuNome() throws Exception {
        instalarJogo("2026.11.3");
        Guia.marcarVisto(ctx);
        ActivityController<SelectorActivity> app = abrirApp();
        Activity tela = app.get();
        foto(tela.getWindow().getDecorView(), "home-convidado");
        assertEquals("Convidado", texto(tela, R.id.conta_nome));
        assertEquals("Entrar", ((Button) tela.findViewById(R.id.conta_entrar)).getText().toString());
        assertTrue(texto(tela, R.id.jogar_resumo).startsWith("Entra como convidado"));
        assertTrue(texto(tela, R.id.game_status).startsWith("Pronto pra jogar"));

        // first "Entrar": the steps first (the step people miss is "Compartilhar")
        tela.findViewById(R.id.conta_entrar).performClick();
        esperar(100);
        Dialog como = ShadowDialog.getLatestDialog();
        assertNotNull(como);
        assertTrue(temTexto(como.getWindow().getDecorView(), "Compartilhar"));
        foto(como.getWindow().getDecorView(), "dialogo-como-entrar");
        ((androidx.appcompat.app.AlertDialog) como).getButton(android.content.DialogInterface.BUTTON_POSITIVE).performClick();
        esperar(100);
        Intent google = ((ShadowActivity) Shadow.extract(tela)).getNextStartedActivity();
        assertNotNull("abre o navegador", google);
        assertEquals(Intent.ACTION_VIEW, google.getAction());
        assertTrue(google.getDataString().startsWith("https://accounts.google.com/o/oauth2/v2/auth?client_id=229679017402-kjalm"));
        assertTrue("a primeira vez mostra a lista de contas", google.getDataString().contains("prompt=select_account"));

        // the browser shares Innersloth's page to the app
        Intent compartilhado = new Intent(Intent.ACTION_SEND).setType("text/plain")
                .putExtra(Intent.EXTRA_TEXT, "Among Us Account Management https://accounts.innersloth.com/?store=google&token="
                        + tokenFalso("Jogador Teste", 3600));
        ActivityController<ContaActivity> conta = Robolectric.buildActivity(ContaActivity.class, compartilhado).setup();
        esperar(100);
        assertTrue("a tela de compartilhar fecha sozinha", conta.get().isFinishing());
        Conta.Login login = Conta.carregar(ctx);
        assertNotNull(login);
        assertEquals("Jogador Teste", login.nome);

        // back on the home screen
        app.pause().resume();
        esperar(100);
        foto(tela.getWindow().getDecorView(), "home-logado");
        assertEquals("Jogador Teste", texto(tela, R.id.conta_nome));
        assertTrue(texto(tela, R.id.conta_status).startsWith("Login vale até"));
        assertTrue(texto(tela, R.id.jogar_resumo).startsWith("Entra como Jogador Teste"));

        // the next "Entrar de novo" goes straight to the same account (no list)
        assertTrue(Conta.linkLogin(ctx).contains("login_hint=123"));
        assertFalse(Conta.linkLogin(ctx).contains("prompt=select_account"));
    }

    /** Login expired, JOGAR: asks, opens the login tab with the ✓ button; one tap on it and the game starts. */
    @Test
    public void loginVencido_jogar_renovaComUmToqueEEntraNoJogo() throws Exception {
        instalarJogo("2026.11.3");
        Guia.marcarVisto(ctx);
        Conta.salvar(ctx, Conta.extrair("https://accounts.innersloth.com/?store=google&token=" + tokenFalso("Fulano", 3600)));
        Conta.arquivo(ctx).delete();   // expired: the account is remembered, the token is gone
        SelectorActivity tela = abrirApp().get();

        tela.findViewById(R.id.play_button).performClick();
        esperar(100);
        Dialog pergunta = ShadowDialog.getLatestDialog();
        assertTrue(temTexto(pergunta.getWindow().getDecorView(), "Fulano"));
        ((androidx.appcompat.app.AlertDialog) pergunta).getButton(android.content.DialogInterface.BUTTON_POSITIVE).performClick();
        esperar(100);

        // the login tab: the same account, with the ✓ button that comes back to ContaActivity
        Intent aba = ((ShadowActivity) Shadow.extract(tela)).getNextStartedActivity();
        assertNotNull("abre o login", aba);
        assertEquals(Intent.ACTION_VIEW, aba.getAction());
        assertTrue(aba.getDataString().contains("login_hint=123"));
        assertTrue("é uma aba do navegador (Custom Tab)", aba.getExtras().containsKey("android.support.customtabs.extra.SESSION"));
        android.os.Bundle botao = aba.getBundleExtra("android.support.customtabs.extra.ACTION_BUTTON_BUNDLE");
        assertNotNull("tem o botão ✓", botao);
        assertEquals("Usar este login", botao.getString("android.support.customtabs.customaction.DESCRIPTION"));
        assertNotNull(botao.getParcelable("android.support.customtabs.customaction.PENDING_INTENT"));

        // the ✓: the browser sends the page's address to ContaActivity
        Intent usar = new Intent(Conta.ACAO_USAR).setData(android.net.Uri.parse(
                "https://accounts.innersloth.com/?store=google&token=" + tokenFalso("Fulano", 3600)));
        ActivityController<ContaActivity> conta = Robolectric.buildActivity(ContaActivity.class, usar).setup();
        esperar(100);
        assertTrue(conta.get().isFinishing());
        assertTrue(Conta.carregar(ctx).valido());
        Intent depois = ((ShadowActivity) Shadow.extract(conta.get())).getNextStartedActivity();
        assertEquals(SelectorActivity.class.getName(), depois.getComponent().getClassName());
        assertTrue("fecha a aba (tarefa limpa)", (depois.getFlags() & Intent.FLAG_ACTIVITY_CLEAR_TASK) != 0);
        assertTrue("e já entra no jogo", depois.getBooleanExtra(SelectorActivity.EXTRA_JOGAR_AGORA, false));
    }

    /** Opens the plain game by habit: after the guide, the app offers the "Among Us mods" shortcut once; it opens the
     *  game with the mods and carries the MOD badge on the game's own icon. */
    @Test
    public void costume_ofereceOAtalhoComSeloMod() {
        instalarJogo("2026.11.3");
        Guia.marcarVisto(ctx);
        ctx.getSharedPreferences("cricri_atalho", Context.MODE_PRIVATE).edit().clear().commit();
        SelectorActivity tela = abrirApp().get();
        esperar(800);
        Dialog dica = ShadowDialog.getLatestDialog();
        assertNotNull(dica);
        assertTrue(temTexto(dica.getWindow().getDecorView(), "selo MOD"));
        foto(dica.getWindow().getDecorView(), "dica-atalho");
        ((androidx.appcompat.app.AlertDialog) dica).getButton(android.content.DialogInterface.BUTTON_POSITIVE).performClick();
        esperar(100);

        android.content.pm.ShortcutManager sm = ctx.getSystemService(android.content.pm.ShortcutManager.class);
        android.content.pm.ShortcutInfo atalho = null;
        for (android.content.pm.ShortcutInfo s : sm.getPinnedShortcuts()) if (s.getId().equals("among_us_mods")) atalho = s;
        assertNotNull("pediu o atalho na tela inicial", atalho);
        assertEquals("Among Us (mod)", atalho.getShortLabel().toString());
        assertEquals(EntradaRapidaActivity.class.getName(), atalho.getIntent().getComponent().getClassName());
        // only once
        assertTrue(dev.allofus.fusioncore.tools.Atalho.jaOferecido(ctx));
        assertEquals(View.VISIBLE, tela.findViewById(R.id.atalho_button).getVisibility());
    }

    /** Sharing the error page (Google account that never played) explains what is missing. */
    @Test
    public void paginaDeErro_explicaQueAContaNaoTemAmongUs() {
        Intent compartilhado = new Intent(Intent.ACTION_SEND).setType("text/plain")
                .putExtra(Intent.EXTRA_TEXT, "https://accounts.innersloth.com/merge-error");
        Robolectric.buildActivity(ContaActivity.class, compartilhado).setup();
        esperar(100);
        Dialog erro = ShadowDialog.getLatestDialog();
        assertNotNull(erro);
        assertTrue(temTexto(erro.getWindow().getDecorView(), "ainda não tem conta do Among Us"));
        foto(erro.getWindow().getDecorView(), "dialogo-erro-sem-among-us");
        assertNull(Conta.carregar(ctx));
    }

    /** Signing out from the account menu goes back to guest and forgets the account. */
    @Test
    public void sairPeloMenu_voltaAConvidado() throws Exception {
        instalarJogo("2026.11.3");
        Guia.marcarVisto(ctx);
        Conta.salvar(ctx, Conta.extrair("https://accounts.innersloth.com/?store=google&token=" + tokenFalso("Fulano", 3600)));
        Activity tela = abrirApp().get();
        assertEquals("Fulano", texto(tela, R.id.conta_nome));

        tela.findViewById(R.id.conta_mais).performClick();
        PopupMenu menu = ShadowPopupMenu.getLatestPopupMenu();
        assertNotNull(menu);
        ((ShadowPopupMenu) Shadow.extract(menu)).getOnMenuItemClickListener().onMenuItemClick(menu.getMenu().findItem(3));
        esperar(100);
        assertEquals("Convidado", texto(tela, R.id.conta_nome));
        assertNull(Conta.nomeLembrado(ctx));
        assertTrue(Conta.linkLogin(ctx).contains("prompt=select_account"));
    }

    /** Turning mods off shows up in the line above JOGAR (the ones with a switch; the app's own parts don't count). */
    @Test
    public void desligarOMod_aparecaNoResumo() {
        instalarJogo("2026.11.3");
        Guia.marcarVisto(ctx);
        Activity tela = abrirApp().get();
        LinearLayout lista = tela.findViewById(R.id.mods_list);
        java.util.List<MaterialSwitch> chaves = new java.util.ArrayList<>();
        for (int i = 0; i < lista.getChildCount(); i++) {
            MaterialSwitch s = lista.getChildAt(i).findViewById(R.id.mod_switch);
            if (s.getVisibility() == View.VISIBLE) chaves.add(s);
        }
        assertTrue("Replay e Servidor têm chave", chaves.size() >= 2);
        assertTrue(texto(tela, R.id.jogar_resumo).endsWith(resumo(chaves.size())));
        chaves.get(0).performClick();
        esperar(50);
        assertTrue(texto(tela, R.id.jogar_resumo).endsWith(resumo(chaves.size() - 1)));
        for (MaterialSwitch s : chaves) if (s.isChecked()) s.performClick();
        esperar(50);
        assertTrue(texto(tela, R.id.jogar_resumo).endsWith("sem mods"));
        foto(tela.getWindow().getDecorView(), "home-sem-mods");
    }

    private static String resumo(int ligados) {
        return ligados == 0 ? "sem mods" : ligados == 1 ? "1 mod ligado" : ligados + " mods ligados";
    }

    private static View linha(LinearLayout lista, String nome) {
        for (int i = 0; i < lista.getChildCount(); i++) {
            View v = lista.getChildAt(i);
            if (nome.equals(((TextView) v.findViewById(R.id.mod_name)).getText().toString())) return v;
        }
        throw new AssertionError("sem a linha " + nome);
    }

    /** Curious: taps a mod to see what it does, who made it and the version; turns it off right there. */
    @Test
    public void tocarNoMod_mostraOQueEleFaz() {
        instalarJogo("2026.11.3");
        Guia.marcarVisto(ctx);
        SelectorActivity tela = abrirApp().get();
        LinearLayout lista = tela.findViewById(R.id.mods_list);
        assertTrue(lista.getChildCount() >= 4);
        View replay = lista.getChildAt(0);
        assertEquals("CriCri Replay", ((TextView) replay.findViewById(R.id.mod_name)).getText().toString());
        assertEquals("CriCri · v1.7.3", ((TextView) replay.findViewById(R.id.mod_autor)).getText().toString());
        linha(lista, "Servidor CRICRI BR");
        // the ones that are part of the app: no switch, "Sempre ligado"
        View otimizacao = linha(lista, "Otimização");
        assertEquals(View.GONE, otimizacao.findViewById(R.id.mod_switch).getVisibility());
        assertEquals(View.VISIBLE, otimizacao.findViewById(R.id.mod_sempre).getVisibility());
        foto(tela.getWindow().getDecorView(), "home-mods");

        replay.performClick();
        esperar(300);
        Dialog folha = tela.ultimaFolha;
        assertNotNull(folha);
        assertTrue(folha.isShowing());
        assertEquals("por CriCri · versão 1.7.3", ((TextView) folha.findViewById(R.id.sheet_autor)).getText().toString());
        assertTrue(((LinearLayout) folha.findViewById(R.id.sheet_itens)).getChildCount() >= 3);
        assertTrue(temTexto(folha.getWindow().getDecorView(), "Replays"));
        assertTrue(temTexto(folha.getWindow().getDecorView(), "Conferido no Among Us 2026.11.3"));
        foto(folha.findViewById(R.id.sheet_nome).getRootView(), "detalhes-replay");

        // the sheet's switch is the row's: off here = off in the list and in the line above JOGAR
        MaterialSwitch chave = folha.findViewById(R.id.sheet_chave);
        assertTrue(chave.isChecked());
        String antes = texto(tela, R.id.jogar_resumo);
        chave.performClick();
        esperar(50);
        assertFalse(((MaterialSwitch) replay.findViewById(R.id.mod_switch)).isChecked());
        assertFalse("o resumo acompanha", antes.equals(texto(tela, R.id.jogar_resumo)));
        folha.dismiss();

        otimizacao.performClick();
        esperar(300);
        Dialog outra = tela.ultimaFolha;
        assertTrue(temTexto(outra.getWindow().getDecorView(), "Faz parte do app"));
        assertEquals(View.GONE, outra.findViewById(R.id.sheet_chave).getVisibility());
        foto(outra.findViewById(R.id.sheet_nome).getRootView(), "detalhes-otimizacao");
    }

    /** A game update the mod wasn't checked on: the status turns amber and says so. */
    @Test
    public void jogoAtualizado_avisaQueOModNaoFoiConferido() {
        instalarJogo("2027.1.1");
        Guia.marcarVisto(ctx);
        Activity tela = abrirApp().get();
        assertTrue(texto(tela, R.id.game_status).contains("ainda não foi conferido"));
        foto(tela.getWindow().getDecorView(), "home-jogo-atualizado");
    }

    /** "Jogar com mods" icon, nothing to ask: straight into the launch. */
    @Test
    public void iconeRapido_semNadaPraPerguntar_vaiDiretoProJogo() {
        instalarJogo("2026.11.3");
        Activity rapida = Robolectric.buildActivity(EntradaRapidaActivity.class).setup().get();
        Intent proxima = ((ShadowActivity) Shadow.extract(rapida)).getNextStartedActivity();
        assertNotNull(proxima);
        assertEquals(SelectorActivity.class.getName(), proxima.getComponent().getClassName());
        assertTrue(proxima.getBooleanExtra(SelectorActivity.EXTRA_JOGAR_AGORA, false));
    }

    /** "Jogar com mods" icon with an expired login of an account the person uses: asks first. */
    @Test
    public void iconeRapido_loginVencido_perguntaAntes() throws Exception {
        instalarJogo("2026.11.3");
        Conta.salvar(ctx, Conta.extrair("https://accounts.innersloth.com/?store=google&token=" + tokenFalso("Fulano", 3600)));
        Conta.arquivo(ctx).delete();   // the token is gone (expired), the account is remembered
        Robolectric.buildActivity(EntradaRapidaActivity.class).setup();
        esperar(100);
        Dialog pergunta = ShadowDialog.getLatestDialog();
        assertNotNull(pergunta);
        assertTrue(temTexto(pergunta.getWindow().getDecorView(), "Fulano"));
        foto(pergunta.getWindow().getDecorView(), "dialogo-icone-login-vencido");
    }

    /** Small phone: the main labels fit (no "..." on JOGAR or on the account button). */
    @Test
    @Config(qualifiers = "w320dp-h640dp-port-xhdpi")
    public void celularPequeno_osTextosCabem() throws Exception {
        instalarJogo("2026.11.3");
        Guia.marcarVisto(ctx);
        Conta.salvar(ctx, Conta.extrair("https://accounts.innersloth.com/?store=google&token=" + tokenFalso("Nome Bem Comprido Da Silva", 3600)));
        Conta.arquivo(ctx).delete();
        Activity tela = abrirApp().get();
        esperar(50);
        foto(tela.getWindow().getDecorView(), "celular-pequeno");
        assertCabe(tela.findViewById(R.id.play_button));
        assertCabe(tela.findViewById(R.id.conta_entrar));
        // the name/status column must keep a readable width (it was squeezed to one word per line by the button)
        float dp = tela.getResources().getDisplayMetrics().density;
        assertTrue("coluna da conta espremida", tela.findViewById(R.id.conta_textos).getWidth() >= 160 * dp);
        assertTrue("status da conta em linhas demais", ((TextView) tela.findViewById(R.id.conta_status)).getLineCount() <= 3);
    }

    // ---------- helpers ----------

    private ActivityController<SelectorActivity> abrirApp() {
        ActivityController<SelectorActivity> c = Robolectric.buildActivity(SelectorActivity.class).setup();
        esperar(600);   // the guide waits ~450 ms for the layout
        return c;
    }

    private void instalarJogo(String versao) {
        PackageInfo pi = new PackageInfo();
        pi.packageName = JOGO;
        pi.versionName = versao;
        ApplicationInfo ai = new ApplicationInfo();
        ai.packageName = JOGO;
        ai.sourceDir = "/nao/existe.apk";
        pi.applicationInfo = ai;
        ((ShadowPackageManager) Shadow.extract(ctx.getPackageManager())).installPackage(pi);
    }

    // A JWT with Google's claims for Among Us's client (signature is fake: only Epic checks it, not the app).
    private static String tokenFalso(String nome, long segundos) throws Exception {
        JSONObject cab = new JSONObject().put("alg", "RS256").put("typ", "JWT");
        JSONObject corpo = new JSONObject().put("iss", "https://accounts.google.com").put("aud", Conta.CLIENTE_GOOGLE)
                .put("sub", "123").put("name", nome).put("exp", System.currentTimeMillis() / 1000 + segundos);
        int f = Base64.URL_SAFE | Base64.NO_PADDING | Base64.NO_WRAP;
        return Base64.encodeToString(cab.toString().getBytes(StandardCharsets.UTF_8), f) + "."
                + Base64.encodeToString(corpo.toString().getBytes(StandardCharsets.UTF_8), f) + ".assinaturafalsa";
    }

    private static void esperar(long ms) {
        ShadowLooper.idleMainLooper(ms, TimeUnit.MILLISECONDS);
    }

    private static String texto(Activity a, int id) {
        return ((TextView) a.findViewById(id)).getText().toString();
    }

    private static boolean temTexto(View v, String parte) {
        if (v instanceof TextView && ((TextView) v).getText().toString().contains(parte)) return true;
        if (v instanceof ViewGroup) {
            ViewGroup g = (ViewGroup) v;
            for (int i = 0; i < g.getChildCount(); i++) if (temTexto(g.getChildAt(i), parte)) return true;
        }
        return false;
    }

    private static Button acharBotao(View v, String texto) {
        if (v instanceof Button && ((Button) v).getText().toString().equals(texto) && v.isShown()) return (Button) v;
        if (v instanceof ViewGroup) {
            ViewGroup g = (ViewGroup) v;
            for (int i = g.getChildCount() - 1; i >= 0; i--) {
                Button b = acharBotao(g.getChildAt(i), texto);
                if (b != null) return b;
            }
        }
        return null;
    }

    private static void assertCabe(Button b) {
        assertNotNull(b.getLayout());
        for (int i = 0; i < b.getLayout().getLineCount(); i++) {
            assertEquals("\"" + b.getText() + "\" cortado com ...", 0, b.getLayout().getEllipsisCount(i));
        }
        assertEquals("\"" + b.getText() + "\" quebrou em duas linhas", 1, b.getLayout().getLineCount());
    }

    // The view as the phone would draw it, saved as PNG.
    private static void foto(View raiz, String nome) {
        try {
            if (raiz.getWidth() == 0 || raiz.getHeight() == 0) {
                int largura = raiz.getResources().getDisplayMetrics().widthPixels;
                raiz.measure(View.MeasureSpec.makeMeasureSpec(largura, View.MeasureSpec.EXACTLY),
                        View.MeasureSpec.makeMeasureSpec(0, View.MeasureSpec.UNSPECIFIED));
                raiz.layout(0, 0, raiz.getMeasuredWidth(), raiz.getMeasuredHeight());
            }
            Bitmap b = Bitmap.createBitmap(raiz.getWidth(), raiz.getHeight(), Bitmap.Config.ARGB_8888);
            raiz.draw(new Canvas(b));
            try (FileOutputStream out = new FileOutputStream(new File(TELAS, nome + ".png"))) {
                b.compress(Bitmap.CompressFormat.PNG, 100, out);
            }
        } catch (Exception e) {
            System.out.println("foto " + nome + ": " + e);
        }
    }
}
