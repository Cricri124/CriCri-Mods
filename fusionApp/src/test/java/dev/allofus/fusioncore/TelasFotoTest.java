package dev.allofus.fusioncore;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNotNull;
import static org.junit.Assert.assertTrue;

import android.app.Activity;
import android.app.Dialog;
import android.content.Context;
import android.content.pm.ApplicationInfo;
import android.content.pm.PackageInfo;
import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.util.Base64;
import android.view.ContextThemeWrapper;
import android.view.LayoutInflater;
import android.view.View;
import android.widget.Button;
import android.widget.PopupMenu;
import android.widget.ProgressBar;
import android.widget.TextView;

import androidx.test.core.app.ApplicationProvider;
import androidx.test.ext.junit.runners.AndroidJUnit4;

import org.json.JSONObject;
import org.junit.Before;
import org.junit.Test;
import org.junit.runner.RunWith;
import org.robolectric.Robolectric;
import org.robolectric.RuntimeEnvironment;
import org.robolectric.annotation.Config;
import org.robolectric.annotation.GraphicsMode;
import org.robolectric.shadow.api.Shadow;
import org.robolectric.shadows.ShadowDialog;
import org.robolectric.shadows.ShadowLooper;
import org.robolectric.shadows.ShadowPackageManager;
import org.robolectric.shadows.ShadowPopupMenu;

import java.io.File;
import java.io.FileOutputStream;
import java.nio.charset.StandardCharsets;
import java.util.concurrent.TimeUnit;

import dev.allofus.fusioncore.tools.Atalho;
import dev.allofus.fusioncore.tools.Conta;
import dev.allofus.fusioncore.tools.Guia;
import dev.allofus.fusioncore.tools.JniBridge;

/**
 * CriCri Mods, app-telas (11/11, ele: "design mais bonito e amigável, menos texto, menos complexidade, mais facilidade"):
 * the states of the home screen the PessoaUsandoTest does not photograph, and the rule each one must keep: only what the
 * person can do now, and one main action (JOGAR, or installing the game). The pictures go to fusionApp/build/telas too.
 */
@RunWith(AndroidJUnit4.class)
@Config(sdk = 35, qualifiers = "w360dp-h800dp-port-xhdpi")
@GraphicsMode(GraphicsMode.Mode.NATIVE)
public class TelasFotoTest {
    private static final String JOGO = "com.innersloth.spacemafia";
    private static final File TELAS = new File("build/telas");

    private Context ctx;

    @Before
    public void preparar() {
        ctx = ApplicationProvider.getApplicationContext();
        TELAS.mkdirs();
        Atalho.marcarOferecido(ctx);
        // no network from the tests (same as PessoaUsandoTest)
        ctx.getSharedPreferences("cricri_mods_remotos", Context.MODE_PRIVATE).edit()
                .putLong("ultima_verificacao", System.currentTimeMillis()).commit();
    }

    /** Logged in and with the CRICRI BR account linked: nothing to press in "Conta" but the two ⋮; the action is JOGAR. */
    @Test
    public void tudoLigado_naoSobraBotaoNaConta() throws Exception {
        instalarJogo("2026.11.3");
        Guia.marcarVisto(ctx);
        Conta.salvar(ctx, Conta.extrair("https://accounts.innersloth.com/?store=google&token=" + tokenFalso("Fulano", 3600)));
        chaveBrGuardada();
        Activity tela = abrirApp();
        foto(tela.getWindow().getDecorView(), "home-tudo-ligado");
        assertUmaAcaoPrincipal(tela);

        assertEquals("login valendo: sem botão (o Renovar foi pro menu)", View.GONE, tela.findViewById(R.id.conta_entrar).getVisibility());
        assertEquals("Ligada", texto(tela, R.id.chave_br_status));
        assertEquals(View.GONE, tela.findViewById(R.id.chave_br_botao).getVisibility());
        assertEquals(View.GONE, tela.findViewById(R.id.chave_br_colar_link).getVisibility());
        assertEquals(View.VISIBLE, tela.findViewById(R.id.chave_br_mais).getVisibility());
        assertEquals(View.VISIBLE, tela.findViewById(R.id.play_button).getVisibility());
        assertEquals(View.GONE, tela.findViewById(R.id.install_game_button).getVisibility());

        // "Renovar o login" is the first item of the account's menu while the login is valid
        tela.findViewById(R.id.conta_mais).performClick();
        PopupMenu menu = ShadowPopupMenu.getLatestPopupMenu();
        assertNotNull(menu.getMenu().findItem(4));
        assertEquals("Renovar o login", menu.getMenu().findItem(4).getTitle().toString());

        // erasing the key is in the ⋮ of the CRICRI BR row, and the row goes back to "liga sozinha"
        tela.findViewById(R.id.chave_br_mais).performClick();
        PopupMenu daChave = ShadowPopupMenu.getLatestPopupMenu();
        ((ShadowPopupMenu) Shadow.extract(daChave)).getOnMenuItemClickListener().onMenuItemClick(daChave.getMenu().getItem(0));
        esperar(50);
        assertFalse(arquivoDaChaveBr().isFile());
        assertEquals(View.GONE, tela.findViewById(R.id.chave_br_mais).getVisibility());
        assertEquals(View.VISIBLE, tela.findViewById(R.id.chave_br_colar_link).getVisibility());
    }

    /** Login expired: one line in amber and "Entrar de novo"; JOGAR is still the main action (it asks first). */
    @Test
    public void loginVencido_umaLinhaEUmBotao() throws Exception {
        instalarJogo("2026.11.3");
        Guia.marcarVisto(ctx);
        Conta.salvar(ctx, Conta.extrair("https://accounts.innersloth.com/?store=google&token=" + tokenFalso("Fulano", 3600)));
        Conta.arquivo(ctx).delete();   // expired: the account is remembered, the token is gone
        Activity tela = abrirApp();
        foto(tela.getWindow().getDecorView(), "home-login-vencido");
        assertUmaAcaoPrincipal(tela);
        assertEquals("O login venceu", texto(tela, R.id.conta_status));
        assertEquals(1, ((TextView) tela.findViewById(R.id.conta_status)).getLineCount());
        assertEquals(View.VISIBLE, tela.findViewById(R.id.conta_entrar).getVisibility());
        assertEquals("Entrar de novo", ((Button) tela.findViewById(R.id.conta_entrar)).getText().toString());

        tela.findViewById(R.id.play_button).performClick();
        esperar(100);
        Dialog pergunta = ShadowDialog.getLatestDialog();
        assertNotNull(pergunta);
        foto(pergunta.getWindow().getDecorView(), "dialogo-entrar-antes-de-jogar");
    }

    /** Without Among Us, first time: no account, no shortcut, no guide over the screen. The one button installs the game. */
    @Test
    public void semJogoNaPrimeiraVez_soInstalar() {
        Activity tela = abrirApp();
        assertFalse("sem o jogo o guia não aparece", Guia.aberto(tela));
        assertFalse("e fica para quando o jogo estiver instalado", Guia.jaVisto(ctx));
        assertEquals(View.GONE, tela.findViewById(R.id.secao_conta).getVisibility());
        assertEquals(View.GONE, tela.findViewById(R.id.atalho_button).getVisibility());
        assertEquals(View.GONE, tela.findViewById(R.id.tutorial_link).getVisibility());
        assertEquals(View.GONE, tela.findViewById(R.id.play_button).getVisibility());
        assertEquals(View.VISIBLE, tela.findViewById(R.id.install_game_button).getVisibility());
        foto(tela.getWindow().getDecorView(), "sem-jogo-primeira-vez");
        assertUmaAcaoPrincipal(tela);
    }

    /** The shortcut is already on the home screen: its button leaves the screen. */
    @Test
    public void atalhoJaNaTelaInicial_oBotaoSome() {
        instalarJogo("2026.11.3");
        Guia.marcarVisto(ctx);
        assertTrue(Atalho.pedir(ctx));   // Robolectric's launcher pins it right away
        Activity tela = abrirApp();
        assertEquals(View.GONE, tela.findViewById(R.id.atalho_button).getVisibility());
        foto(tela.getWindow().getDecorView(), "home-com-atalho");
        assertUmaAcaoPrincipal(tela);
    }

    /** System font at 130%: JOGAR and the account button stay on one line; the long shortcut label may wrap, never "...". */
    @Test
    public void fonteGrande_osBotoesCabem() {
        RuntimeEnvironment.setFontScale(1.3f);
        instalarJogo("2026.11.3");
        Guia.marcarVisto(ctx);
        Activity tela = abrirApp();
        esperar(50);
        foto(tela.getWindow().getDecorView(), "home-fonte-grande");
        assertUmaAcaoPrincipal(tela);
        assertCabe(tela.findViewById(R.id.play_button));
        assertCabe(tela.findViewById(R.id.conta_entrar));
        Button atalho = tela.findViewById(R.id.atalho_button);
        for (int i = 0; i < atalho.getLayout().getLineCount(); i++) {
            assertEquals("\"" + atalho.getText() + "\" cortado com ...", 0, atalho.getLayout().getEllipsisCount(i));
        }
        TextView status = tela.findViewById(R.id.game_status);
        assertEquals("o estado do jogo cabe em uma linha", 1, status.getLineCount());
    }

    /** Guest, and a game update a mod was not checked on: still one main action, and the warning fits one line. */
    @Test
    public void convidadoEJogoAtualizado_umaAcaoPrincipal() {
        instalarJogo("2027.1.1");
        Guia.marcarVisto(ctx);
        Activity tela = abrirApp();
        assertUmaAcaoPrincipal(tela);
        assertEquals("o aviso do jogo cabe em uma linha", 1, ((TextView) tela.findViewById(R.id.game_status)).getLineCount());
        assertEquals(View.VISIBLE, tela.findViewById(R.id.conta_entrar).getVisibility());
        // while the guide is up its own button is neutral: JOGAR is still the only orange one
        Guia.mostrar(tela, java.util.Collections.singletonList(
                new Guia.Passo(tela.findViewById(R.id.play_button), R.string.guia_jogar_titulo, R.string.guia_jogar_texto)));
        esperar(400);
        assertTrue(Guia.aberto(tela));
        assertUmaAcaoPrincipal(tela);
    }

    /** First time, "Voltar" on the phone: the guide closes (and counts as seen); the app stays open. */
    @Test
    public void voltarFechaOGuia_naoOApp() {
        instalarJogo("2026.11.3");
        SelectorActivity tela = (SelectorActivity) abrirApp();
        assertTrue(Guia.aberto(tela));
        tela.getOnBackPressedDispatcher().onBackPressed();
        esperar(100);
        assertFalse("o guia fecha", Guia.aberto(tela));
        assertTrue(Guia.jaVisto(ctx));
        assertFalse("o app continua aberto", tela.isFinishing());
    }

    /** "Sobre" (the i at the top): where the versions and the licenses live now. Same dialog style as the others. */
    @Test
    public void sobre_temAsVersoesDoAppEDoJogo() {
        instalarJogo("2026.11.3");
        Guia.marcarVisto(ctx);
        Activity tela = abrirApp();
        tela.findViewById(R.id.about_button).performClick();
        esperar(100);
        Dialog sobre = ShadowDialog.getLatestDialog();
        assertNotNull(sobre);
        TextView mensagem = sobre.findViewById(android.R.id.message);
        assertTrue(mensagem.getText().toString().startsWith("CriCri Mods " + BuildConfig.VERSION_NAME + " · Among Us 2026.11.3"));
        foto(sobre.getWindow().getDecorView(), "dialogo-sobre");

        // two buttons (three were stacked and the last fell off the screen); "Licenças" lists the pieces and leads to the GPL text
        androidx.appcompat.app.AlertDialog alerta = (androidx.appcompat.app.AlertDialog) sobre;
        assertFalse(alerta.getButton(android.content.DialogInterface.BUTTON_NEUTRAL).isShown());
        assertEquals("Licenças", alerta.getButton(android.content.DialogInterface.BUTTON_NEGATIVE).getText().toString());
        alerta.getButton(android.content.DialogInterface.BUTTON_NEGATIVE).performClick();
        esperar(100);
        androidx.appcompat.app.AlertDialog licencas = (androidx.appcompat.app.AlertDialog) ShadowDialog.getLatestDialog();
        assertTrue(licencas.isShowing());
        assertEquals("Ver licença", licencas.getButton(android.content.DialogInterface.BUTTON_NEGATIVE).getText().toString());
    }

    /** The end of the page: the two quiet links and Innersloth's notice end above the JOGAR bar, never under it. */
    @Test
    public void fimDaPagina_oRodapeFicaAcimaDaBarra() {
        instalarJogo("2026.11.3");
        Guia.marcarVisto(ctx);
        Activity tela = abrirApp();
        android.widget.ScrollView rolagem = tela.findViewById(R.id.scroll);
        rolagem.scrollTo(0, rolagem.getChildAt(0).getHeight());
        esperar(50);
        foto(tela.getWindow().getDecorView(), "home-rodape");
        View barra = tela.findViewById(R.id.barra_jogar);
        assertEquals("a página reserva a altura da barra no fim", barra.getHeight(), rolagem.getPaddingBottom());
        int[] daBarra = new int[2], doFim = new int[2];
        barra.getLocationOnScreen(daBarra);
        View conteudo = rolagem.getChildAt(0);
        conteudo.getLocationOnScreen(doFim);
        assertTrue("o fim do conteúdo passa por baixo da barra", doFim[1] + conteudo.getHeight() <= daBarra[1] + 1);
    }

    /** Every touch target of the home screen is at least 48 dp tall and wide. */
    @Test
    public void alvosDeToque_tem48dp() {
        instalarJogo("2026.11.3");
        Guia.marcarVisto(ctx);
        chaveBrGuardada();
        Activity tela = abrirApp();
        float dp = tela.getResources().getDisplayMetrics().density;
        int[] alvos = {R.id.about_button, R.id.atalho_button, R.id.conta_mais, R.id.conta_entrar, R.id.chave_br_mais,
                R.id.tutorial_link, R.id.about_link, R.id.play_button};
        for (int id : alvos) {
            View v = tela.findViewById(id);
            String nome = tela.getResources().getResourceEntryName(id);
            assertTrue(nome + " com menos de 48 dp de altura: " + v.getHeight() / dp, v.getHeight() >= 48 * dp - 1);
            assertTrue(nome + " com menos de 48 dp de largura: " + v.getWidth() / dp, v.getWidth() >= 48 * dp - 1);
        }
    }

    /** The wait between JOGAR and the game, as BootstrapActivity shows it (its theme), spinning and while it downloads a piece. */
    @Test
    public void telaDeEspera_comACorDoApp() throws Exception {
        // the wait and its "Não deu pra abrir" window wear the app's theme (it was the plain Material one: purple button)
        assertEquals(R.style.Theme_CriCriMods, ctx.getPackageManager()
                .getActivityInfo(new android.content.ComponentName(ctx, BootstrapActivity.class), 0).theme);
        Context tema = new ContextThemeWrapper(ctx, R.style.Theme_CriCriMods);
        View raiz = LayoutInflater.from(tema).inflate(R.layout.activity_bootstrap, null, false);
        // an ordinary opening: nothing to copy, the step only checks the game
        ((TextView) raiz.findViewById(R.id.bootstrap_status)).setText(R.string.bootstrap_status_detecting_version);
        fotoDoTamanhoDaTela(raiz, "espera");

        raiz.findViewById(R.id.bootstrap_progress).setVisibility(View.GONE);
        ProgressBar barra = raiz.findViewById(R.id.bootstrap_download_progress);
        barra.setVisibility(View.VISIBLE);
        barra.setProgress(42);
        ((TextView) raiz.findViewById(R.id.bootstrap_status)).setText(R.string.bootstrap_status_downloading_libunity);
        TextView numeros = raiz.findViewById(R.id.bootstrap_progress_details);
        numeros.setVisibility(View.VISIBLE);
        numeros.setText(ctx.getString(R.string.bootstrap_download_progress, 42, "9.1 MB", "21.7 MB"));
        assertEquals("42% · 9.1 MB de 21.7 MB", numeros.getText().toString());
        fotoDoTamanhoDaTela(raiz, "espera-baixando");
        assertTrue("o texto da etapa está em português", ctx.getString(R.string.bootstrap_status_downloading_libunity).startsWith("Baixando"));

        // the first time (and after Among Us updates): the copy, with the same bar. The phrase comes with the bar, never alone.
        ((TextView) raiz.findViewById(R.id.bootstrap_status)).setText(R.string.bootstrap_status_copy_assets);
        barra.setProgress(64);
        numeros.setText(ctx.getString(R.string.bootstrap_download_progress, 64, "192.0 MB", "300.0 MB"));
        fotoDoTamanhoDaTela(raiz, "espera-copiando");
        assertEquals("Copiando o jogo (só desta vez)…", ctx.getString(R.string.bootstrap_status_copy_assets));
    }

    /**
     * The wait drawn over the game while the mods load (the longest one): BepInEx's programmer English becomes three phrases in
     * Portuguese, and two BepInEx steps that are one phrase for the person keep the clock counting instead of going back to 00:00.
     */
    @Test
    public void esperaPorCimaDoJogo_emPortuguesEORelogioNaoVolta() {
        // the six messages BepInEx.Unity.IL2CPP sends (read from its IL) and what the person reads instead
        assertEquals(R.string.espera_mods, EsperaDoJogo.recurso("Running Preloader..."));
        assertEquals(R.string.espera_mods, EsperaDoJogo.recurso("Initialized Chainloader..."));
        assertEquals(R.string.espera_mods, EsperaDoJogo.recurso("Running Chainloader..."));
        assertEquals(R.string.bootstrap_status_downloading_libunity, EsperaDoJogo.recurso("Downloading Unity assemblies..."));
        assertEquals(R.string.espera_so_desta_vez, EsperaDoJogo.recurso("Running Cpp2IL..."));
        assertEquals(R.string.espera_so_desta_vez, EsperaDoJogo.recurso("Generating IL2CPP Interop files..."));
        assertEquals("texto que o app não conhece vira o Carregando de sempre", R.string.loading, EsperaDoJogo.recurso("Something new"));
        assertEquals(R.string.loading, EsperaDoJogo.recurso(null));

        // as hooks/GameRuntime does it: the notice on the window of another activity, inflated with the app's own context
        Activity jogo = Robolectric.buildActivity(Activity.class).setup().get();
        Context tema = new ContextThemeWrapper(ctx, androidx.appcompat.R.style.Theme_AppCompat);
        android.view.ViewGroup janela = (android.view.ViewGroup) jogo.getWindow().getDecorView();
        janela.addView(LayoutInflater.from(tema).inflate(R.layout.loading_view, janela, false));
        TextView aviso = jogo.findViewById(R.id.loadingText);
        assertEquals("Carregando…", aviso.getText().toString());

        JniBridge.SetLoadingText(jogo, "Running Preloader...");
        esperar(3100);
        assertTrue(aviso.getText().toString(), aviso.getText().toString().startsWith("Carregando os mods… (00:0"));
        int antes = segundosDoRelogio(aviso);
        JniBridge.SetLoadingText(jogo, "Running Chainloader...");
        esperar(1000);
        assertTrue(aviso.getText().toString(), aviso.getText().toString().startsWith("Carregando os mods… ("));
        assertTrue("o relógio voltou: " + aviso.getText(), segundosDoRelogio(aviso) > antes);

        // a phrase that IS new starts its own clock
        JniBridge.SetLoadingText(jogo, "Generating IL2CPP Interop files...");
        esperar(1100);
        assertTrue(aviso.getText().toString(), aviso.getText().toString().startsWith("Preparando os mods (só desta vez, pode demorar)… (00:0"));
        assertTrue(aviso.getText().toString(), segundosDoRelogio(aviso) <= 3);
        esperar(6000);
        assertTrue(aviso.getText().toString(), segundosDoRelogio(aviso) >= 7);

        // hidden (its clock is stopped) and shown again: the same phrase comes back with a clock of its own, not frozen at the old one
        JniBridge.SetLoadingState(jogo, false);
        esperar(50);
        assertEquals(View.GONE, jogo.findViewById(R.id.loader).getVisibility());
        JniBridge.SetLoadingState(jogo, true);
        JniBridge.SetLoadingText(jogo, "Running Cpp2IL...");
        esperar(2100);
        assertTrue(aviso.getText().toString(), segundosDoRelogio(aviso) >= 2 && segundosDoRelogio(aviso) <= 4);
        JniBridge.SetLoadingState(jogo, false);
        esperar(50);

        // the picture: the game's window is black and lying down; the long phrase with its clock, and the same on a narrow screen
        View caixa = LayoutInflater.from(tema).inflate(R.layout.loading_view, null, false);
        caixa.setBackgroundColor(android.graphics.Color.BLACK);
        TextView frase = caixa.findViewById(R.id.loadingText);
        frase.setText(ctx.getString(R.string.espera_so_desta_vez) + " (01:12)");
        float dp = ctx.getResources().getDisplayMetrics().density;
        fotoDoTamanho(caixa, Math.round(800 * dp), Math.round(360 * dp), "espera-no-jogo");
        assertEquals("deitado, a frase longa cabe em uma linha", 1, frase.getLineCount());
        fotoDoTamanho(caixa, Math.round(320 * dp), Math.round(640 * dp), "espera-no-jogo-estreita");
        assertTrue("em tela estreita a frase quebra, não sai da tela", frase.getLineCount() >= 2 && frase.getWidth() <= Math.round(320 * dp));
        for (int i = 0; i < frase.getLineCount(); i++) assertEquals(0, frase.getLayout().getEllipsisCount(i));
    }

    private static int segundosDoRelogio(TextView aviso) {
        String t = aviso.getText().toString();
        int abre = t.lastIndexOf('(');
        assertTrue("sem relógio: " + t, abre >= 0 && t.endsWith(")"));
        String[] partes = t.substring(abre + 1, t.length() - 1).split(":");
        return Integer.parseInt(partes[0]) * 60 + Integer.parseInt(partes[1]);
    }

    private static void fotoDoTamanho(View raiz, int largura, int altura, String nome) {
        raiz.measure(View.MeasureSpec.makeMeasureSpec(largura, View.MeasureSpec.EXACTLY),
                View.MeasureSpec.makeMeasureSpec(altura, View.MeasureSpec.EXACTLY));
        raiz.layout(0, 0, largura, altura);
        foto(raiz, nome);
    }

    // ---------- helpers (the same as PessoaUsandoTest's, which are private there) ----------

    private Activity abrirApp() {
        Activity a = Robolectric.buildActivity(SelectorActivity.class).setup().get();
        esperar(600);
        return a;
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

    // The home screen only asks "is there a key?" (the file exists); what is inside is ChaveBrTest's business.
    private File arquivoDaChaveBr() {
        return new File(ctx.getNoBackupFilesDir(), "cricri-br-chave.bin");
    }

    private void chaveBrGuardada() {
        try (FileOutputStream out = new FileOutputStream(arquivoDaChaveBr())) {
            out.write(1);
        } catch (Exception e) {
            throw new AssertionError(e);
        }
    }

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

    // "Nenhum estado da tela inicial com mais de uma ação principal": on the whole window, exactly one button that is
    // showing wears the accent color (JOGAR, or "Instalar o Among Us" without the game).
    private void assertUmaAcaoPrincipal(Activity tela) {
        java.util.List<String> laranjas = new java.util.ArrayList<>();
        botoesLaranja(tela.getWindow().getDecorView(), androidx.core.content.ContextCompat.getColor(ctx, R.color.cricri_accent), laranjas);
        assertEquals("botões com a cor de ação na tela: " + laranjas, 1, laranjas.size());
    }

    private static void botoesLaranja(View v, int laranja, java.util.List<String> achados) {
        if (!v.isShown()) return;
        if (v instanceof com.google.android.material.button.MaterialButton) {
            android.content.res.ColorStateList fundo = ((com.google.android.material.button.MaterialButton) v).getBackgroundTintList();
            if (fundo != null && fundo.getDefaultColor() == laranja) achados.add(((Button) v).getText().toString());
        }
        if (v instanceof android.view.ViewGroup) {
            android.view.ViewGroup g = (android.view.ViewGroup) v;
            for (int i = 0; i < g.getChildCount(); i++) botoesLaranja(g.getChildAt(i), laranja, achados);
        }
    }

    private static void assertCabe(Button b) {
        assertNotNull(b.getLayout());
        for (int i = 0; i < b.getLayout().getLineCount(); i++) {
            assertEquals("\"" + b.getText() + "\" cortado com ...", 0, b.getLayout().getEllipsisCount(i));
        }
        assertEquals("\"" + b.getText() + "\" quebrou em duas linhas", 1, b.getLayout().getLineCount());
    }

    private void fotoDoTamanhoDaTela(View raiz, String nome) {
        int largura = ctx.getResources().getDisplayMetrics().widthPixels;
        int altura = ctx.getResources().getDisplayMetrics().heightPixels;
        raiz.measure(View.MeasureSpec.makeMeasureSpec(largura, View.MeasureSpec.EXACTLY),
                View.MeasureSpec.makeMeasureSpec(altura, View.MeasureSpec.EXACTLY));
        raiz.layout(0, 0, largura, altura);
        foto(raiz, nome);
    }

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
