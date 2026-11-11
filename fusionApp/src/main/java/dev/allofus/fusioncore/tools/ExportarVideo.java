package dev.allofus.fusioncore.tools;

import android.Manifest;
import android.app.Activity;
import android.app.Fragment;
import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.media.projection.MediaProjectionConfig;
import android.media.projection.MediaProjectionManager;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.SystemClock;
import android.util.Log;

/**
 * CriCri Mods: the "EXPORTAR" tool of the CriCri Replay editor (09/11/2026). The mod calls these static methods from the
 * game thread through raw JNI (replay\src\Editor\SaidaDeVideo.cs) and polls estado() once per frame.
 *
 * Android 10 (API 29) or newer only: a public video needs MediaStore without storage permissions, and game audio needs
 * AudioPlaybackCapture; both start at 29.
 *
 * The two system dialogs are asked from a headless Fragment on the game's own Activity, so no Activity is added to the
 * manifest. Context-bound work (service, permissions) uses this app's real Application, the same way as ChaveBr.
 *
 * The int results mean nothing: the PC interop of the mod can't compile a call to CallStaticVoidMethod (see SaidaDeVideo.cs).
 *
 * NEVER RUN ON A PHONE when written: only compiled.
 */
public final class ExportarVideo {
    static final String TAG = "CriCriExportar";

    // Mirrored in SaidaDeVideo.cs.
    public static final int NADA = 0, PEDINDO = 1, ABRINDO = 2, PRONTA = 3, GRAVANDO = 4, FECHANDO = 5, SALVO = 6, FALHOU = 7;

    static final String EXTRA_CODIGO = "cricri.exportar.codigo";
    static final String EXTRA_DADOS = "cricri.exportar.dados";
    private static final String ETIQUETA = "cricri_exportar_consentimento";

    private static volatile int estado = NADA;
    private static volatile String recado = "";
    private static volatile Uri uriSalva;
    private static volatile long ultimoPulso;
    private static ExportarVideoService servico;

    static volatile int larguraTela, alturaTela;
    static volatile float segundos;
    static volatile String nomeDoArquivo;

    private ExportarVideo() {}

    private static boolean ativo(int e) {
        return e >= PEDINDO && e <= FECHANDO;
    }

    static Context app() throws Exception {
        return (Context) Class.forName("android.app.ActivityThread").getMethod("currentApplication").invoke(null);
    }

    /** Game thread. Asks for the permissions; the answer shows up in estado(). */
    public static int pedir(Activity atividade, int largura, int altura, float seg, String nome) {
        synchronized (ExportarVideo.class) {
            if (ativo(estado)) return 0;
            recado = "";
            uriSalva = null;
            larguraTela = largura;
            alturaTela = altura;
            segundos = seg;
            nomeDoArquivo = nome;
            estado = PEDINDO;
        }
        if (Build.VERSION.SDK_INT < 29) {
            falhar("Exportar vídeo precisa do Android 10 ou mais novo.");
            return 0;
        }
        if (atividade == null) {
            falhar("Não achei a tela do jogo.");
            return 0;
        }
        atividade.runOnUiThread(() -> {
            try {
                atividade.getFragmentManager().beginTransaction()
                        .add(new Consentimento(), ETIQUETA).commitAllowingStateLoss();
            } catch (Throwable t) {
                Log.w(TAG, "fragment: " + t.getClass().getSimpleName());
                falhar("Não consegui pedir a permissão de gravar a tela.");
            }
        });
        return 0;
    }

    /** Game thread: the first frame of the edit is on screen, the file starts at the next key frame. */
    public static int comecar() {
        ExportarVideoService s;
        synchronized (ExportarVideo.class) { s = servico; }
        if (s != null) s.pedirInicio();
        return 0;
    }

    /** Game thread. false = cancel and leave nothing behind. Never blocks: the outcome shows up in estado(). */
    public static int parar(boolean guardar) {
        ExportarVideoService s;
        synchronized (ExportarVideo.class) {
            s = servico;
            if (s == null) {
                if (estado == PEDINDO || estado == ABRINDO) estado = NADA;
                return 0;
            }
        }
        s.pedirFim(guardar);
        return 0;
    }

    /** Game thread, every frame. The call itself is the proof that the game is still on screen (see the service). */
    public static int estado() {
        ultimoPulso = SystemClock.uptimeMillis();
        return estado;
    }

    public static String recado() {
        return recado == null ? "" : recado;
    }

    /** Game thread: the outcome was read. A later cancel of a running export is not affected. */
    public static synchronized int esquecer() {
        if (!ativo(estado)) estado = NADA;
        return 0;
    }

    /** Game thread. true = the share sheet was asked for. */
    public static boolean compartilhar(Activity atividade, String titulo) {
        Uri uri = uriSalva;
        if (uri == null || atividade == null) return false;
        atividade.runOnUiThread(() -> {
            try {
                Intent enviar = new Intent(Intent.ACTION_SEND).setType("video/mp4")
                        .putExtra(Intent.EXTRA_STREAM, uri)
                        .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
                atividade.startActivity(Intent.createChooser(enviar, titulo));
            } catch (Throwable t) {
                Log.w(TAG, "compartilhar: " + t.getClass().getSimpleName());
            }
        });
        return true;
    }

    // ───────── what the Fragment and the service report ─────────

    static long ultimoPulso() {
        return ultimoPulso;
    }

    static int estadoAgora() {
        return estado;
    }

    static synchronized void falhar(String texto) {
        if (!ativo(estado)) return;
        recado = texto;
        estado = FALHOU;
    }

    static synchronized void cancelado() {
        estado = NADA;
    }

    static synchronized void avancar(int de, int para) {
        if (estado == de) estado = para;
    }

    static synchronized void fechando() {
        if (ativo(estado)) estado = FECHANDO;
    }

    static synchronized void salvo(Uri uri, String aviso) {
        uriSalva = uri;
        recado = aviso == null ? "" : aviso;
        estado = SALVO;
    }

    /** false = the person cancelled while the service was starting: it must close without touching anything. */
    static synchronized boolean servicoChegou(ExportarVideoService s) {
        if (estado != ABRINDO) return false;
        servico = s;
        return true;
    }

    static synchronized void servicoSaiu(ExportarVideoService s) {
        if (servico == s) servico = null;
    }

    private static void consentiu(int codigo, Intent dados) {
        synchronized (ExportarVideo.class) {
            if (estado != PEDINDO) return;
            estado = ABRINDO;
        }
        try {
            Context c = app();
            c.startForegroundService(new Intent(c, ExportarVideoService.class)
                    .putExtra(EXTRA_CODIGO, codigo).putExtra(EXTRA_DADOS, dados));
        } catch (Throwable t) {
            Log.w(TAG, "servico: " + t.getClass().getSimpleName());
            falhar("Não consegui começar a gravação.");
        }
    }

    /** Asks RECORD_AUDIO (needed by AudioPlaybackCapture; the microphone is never opened), then the screen capture. */
    public static final class Consentimento extends Fragment {
        private static final int PEDIDO_SOM = 7401, PEDIDO_TELA = 7402;
        private boolean respondeu;

        @Override public void onCreate(Bundle guardado) {
            super.onCreate(guardado);
            try {
                if (app().checkSelfPermission(Manifest.permission.RECORD_AUDIO) == PackageManager.PERMISSION_GRANTED) {
                    pedirTela();
                } else {
                    requestPermissions(new String[] {Manifest.permission.RECORD_AUDIO}, PEDIDO_SOM);
                }
            } catch (Throwable t) {
                desistir("Não consegui pedir a permissão de gravar a tela.", t);
            }
        }

        // Without audio the export still goes on: the service reports "sem som" at the end.
        @Override public void onRequestPermissionsResult(int codigo, String[] permissoes, int[] resultados) {
            if (codigo != PEDIDO_SOM) return;
            try {
                pedirTela();
            } catch (Throwable t) {
                desistir("Não consegui pedir a permissão de gravar a tela.", t);
            }
        }

        private void pedirTela() throws Exception {
            MediaProjectionManager m = (MediaProjectionManager) app().getSystemService(Context.MEDIA_PROJECTION_SERVICE);
            // Android 14+ also offers "a single app": the whole screen is what holds the game full screen.
            startActivityForResult(Build.VERSION.SDK_INT >= 34
                    ? m.createScreenCaptureIntent(MediaProjectionConfig.createConfigForDefaultDisplay())
                    : m.createScreenCaptureIntent(), PEDIDO_TELA);
        }

        @Override public void onActivityResult(int codigo, int resultado, Intent dados) {
            if (codigo != PEDIDO_TELA) return;
            respondeu = true;
            remover();
            if (resultado == Activity.RESULT_OK && dados != null) consentiu(resultado, dados);
            else falhar("Exportação cancelada: você não permitiu gravar a tela.");
        }

        @Override public void onDestroy() {
            super.onDestroy();
            if (!respondeu) falhar("A permissão de gravar a tela não respondeu.");
        }

        private void desistir(String texto, Throwable t) {
            Log.w(TAG, "consentimento: " + t.getClass().getSimpleName());
            respondeu = true;
            falhar(texto);
            remover();
        }

        private void remover() {
            try {
                getFragmentManager().beginTransaction().remove(this).commitAllowingStateLoss();
            } catch (Throwable ignorado) {
                // already gone with the Activity
            }
        }
    }
}
