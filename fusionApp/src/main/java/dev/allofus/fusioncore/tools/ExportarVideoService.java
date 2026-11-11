package dev.allofus.fusioncore.tools;

import android.Manifest;
import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.Service;
import android.content.ContentResolver;
import android.content.ContentValues;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.content.pm.ServiceInfo;
import android.hardware.display.DisplayManager;
import android.hardware.display.VirtualDisplay;
import android.media.AudioFormat;
import android.media.AudioPlaybackCaptureConfiguration;
import android.media.AudioRecord;
import android.media.MediaCodec;
import android.media.MediaCodecInfo;
import android.media.MediaFormat;
import android.media.MediaMuxer;
import android.media.projection.MediaProjection;
import android.media.projection.MediaProjectionManager;
import android.net.Uri;
import android.os.Bundle;
import android.os.Environment;
import android.os.Handler;
import android.os.HandlerThread;
import android.os.IBinder;
import android.os.ParcelFileDescriptor;
import android.os.Process;
import android.os.StatFs;
import android.os.SystemClock;
import android.provider.MediaStore;
import android.util.Log;
import android.view.Surface;

import androidx.annotation.RequiresApi;

import java.nio.ByteBuffer;

/**
 * CriCri Mods: records the screen and the game's sound into a public MP4 (see ExportarVideo). MediaRecorder can't take
 * AudioPlaybackCapture as a source, so it is H.264 from a VirtualDisplay and AAC from an AudioRecord, into a MediaMuxer.
 *
 * Only this app's own UID is captured (the game runs inside it): no Discord, music or notifications in the video.
 *
 * The encoders run from the moment the consent is given, but nothing reaches the file until the mod says the first frame
 * of the edit is on screen (pedirInicio): the dialog closing never shows in the video.
 *
 * MediaProjection mirrors the whole display, so if the game leaves the screen the recording is cancelled and the file
 * deleted: no "pulse" from the game (ExportarVideo.estado) for PULSO_MAXIMO_MS.
 */
@RequiresApi(29)
public final class ExportarVideoService extends Service {
    private static final String TAG = ExportarVideo.TAG;
    private static final String CANAL = "cricri_exportar";
    private static final int ID_NOTIFICACAO = 7401;
    private static final String TIPO_ARQUIVO = "video/mp4";
    private static final int ALTURA = 720, ALTURA_RESERVA = 480, LARGURA_MAXIMA = 1920;
    private static final int KBPS_VIDEO = 4500, KBPS_SOM = 128, FPS = 60, SEGUNDOS_ENTRE_CHAVES = 2;
    private static final int TAXA_SOM = 48000, CANAIS_SOM = 2, BYTES_POR_AMOSTRA = 4, PEDACO_SOM = 3840;
    private static final long ESPERA_US = 10_000L;
    private static final long PULSO_MAXIMO_MS = 8_000L, ESPERA_INICIO_MS = 30_000L, ESPERA_IMAGEM_MS = 8_000L, ESPERA_SOM_MS = 3_000L;
    private static final long ESPERA_FIM_MS = 2_500L;

    private HandlerThread fio;
    private Handler mao;
    private MediaProjection projecao;
    private VirtualDisplay tela;
    private Surface superficie;
    private MediaCodec video, som;
    private AudioRecord gravadorDeSom;
    private MediaMuxer mux;
    private ParcelFileDescriptor descritor;
    private Uri uri;
    private Thread fioVideo, fioSom;

    private final Object travaMux = new Object();
    private volatile MediaFormat formatoVideo, formatoSom;
    private volatile boolean muxLigado, desistiuDoSom;
    private volatile long marcaPronta;
    private int trilhaVideo = -1, trilhaSom = -1;
    private long quadros, ultimoPtsSom = -1L;

    private volatile boolean pedidoDeInicio, fimDoSom, fimDoVideo, encerrando, ouviuAlgo;
    private volatile long inicioUs = -1L, fimUs = -1L;
    private volatile String avisoDoSom;
    private long proximoPtsSom = -1L;

    private final MediaProjection.Callback aoParar = new MediaProjection.Callback() {
        @Override public void onStop() {
            encerrar(false, "O Android parou a gravação.");
        }
    };

    @Override public IBinder onBind(Intent intent) {
        return null;
    }

    @Override public int onStartCommand(Intent intent, int flags, int startId) {
        // Foreground first, always: a service started with startForegroundService that doesn't do this kills the process.
        try {
            startForeground(ID_NOTIFICACAO, notificacao(), ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PROJECTION);
        } catch (Throwable t) {
            Log.w(TAG, "startForeground: " + t.getClass().getSimpleName());
            ExportarVideo.falhar("O Android não deixou começar a gravação.");
            stopSelf();
            return START_NOT_STICKY;
        }
        if (fio != null) return START_NOT_STICKY;
        fio = new HandlerThread("CriCriExportar");
        fio.start();
        mao = new Handler(fio.getLooper());
        if (intent == null || !ExportarVideo.servicoChegou(this)) {
            sair();
            return START_NOT_STICKY;
        }
        final int codigo = intent.getIntExtra(ExportarVideo.EXTRA_CODIGO, 0);
        final Intent dados = intent.getParcelableExtra(ExportarVideo.EXTRA_DADOS);
        mao.post(() -> abrir(codigo, dados));
        return START_NOT_STICKY;
    }

    private Notification notificacao() {
        NotificationManager nm = getSystemService(NotificationManager.class);
        nm.createNotificationChannel(new NotificationChannel(CANAL, "Exportar vídeo", NotificationManager.IMPORTANCE_LOW));
        return new Notification.Builder(this, CANAL)
                .setSmallIcon(android.R.drawable.presence_video_online)
                .setContentTitle("Gravando o vídeo do replay")
                .setOngoing(true)
                .build();
    }

    // ───────── what the game asks ─────────

    void pedirInicio() {
        pedidoDeInicio = true;
    }

    void pedirFim(boolean guardar) {
        encerrar(guardar, null);
    }

    // ───────── opening ─────────

    private void abrir(int codigo, Intent dados) {
        try {
            if (encerrando) return;
            String semEspaco = conferirEspaco();
            if (semEspaco != null) { encerrar(false, semEspaco); return; }
            MediaProjectionManager m = getSystemService(MediaProjectionManager.class);
            projecao = dados == null ? null : m.getMediaProjection(codigo, dados);
            if (projecao == null) { encerrar(false, "O Android não liberou a gravação da tela."); return; }
            projecao.registerCallback(aoParar, mao);   // Android 14+: required before createVirtualDisplay
            criarArquivo();
            int[] tamanho = criarVideo();
            criarSom();
            int dpi = getResources().getDisplayMetrics().densityDpi;
            tela = projecao.createVirtualDisplay("CriCriExportar", tamanho[0], tamanho[1], dpi,
                    DisplayManager.VIRTUAL_DISPLAY_FLAG_AUTO_MIRROR, superficie, null, mao);
            fioVideo = new Thread(this::lacoDoVideo, "CriCriExportarVideo");
            fioVideo.start();
            if (som != null) {
                fioSom = new Thread(this::lacoDoSom, "CriCriExportarSom");
                fioSom.start();
            }
            Log.i(TAG, "gravando " + tamanho[0] + "x" + tamanho[1] + (som != null ? " com som" : " sem som"));
        } catch (Throwable t) {
            Log.w(TAG, "abrir: " + t.getClass().getSimpleName() + ": " + t.getMessage());
            encerrar(false, "Não consegui começar a gravação.");
        }
    }

    private String conferirEspaco() {
        try {
            long porSegundo = (KBPS_VIDEO + KBPS_SOM) * 1000L / 8L;
            long precisa = (long) (porSegundo * ExportarVideo.segundos * 1.25f) + 64L * 1048576L;
            long livre = new StatFs(Environment.getExternalStorageDirectory().getPath()).getAvailableBytes();
            if (livre >= precisa) return null;
            return "Falta espaço no aparelho: o vídeo precisa de uns " + (precisa / 1048576L) + " MB.";
        } catch (Exception e) {
            return null;   // a phone that doesn't answer the size is no reason to refuse
        }
    }

    private void criarArquivo() throws Exception {
        ContentValues v = new ContentValues();
        v.put(MediaStore.Video.Media.DISPLAY_NAME, ExportarVideo.nomeDoArquivo);
        v.put(MediaStore.Video.Media.MIME_TYPE, TIPO_ARQUIVO);
        v.put(MediaStore.Video.Media.RELATIVE_PATH, Environment.DIRECTORY_MOVIES + "/CriCri Replay");
        v.put(MediaStore.Video.Media.IS_PENDING, 1);
        ContentResolver cr = getContentResolver();
        uri = cr.insert(MediaStore.Video.Media.getContentUri(MediaStore.VOLUME_EXTERNAL_PRIMARY), v);
        if (uri == null) throw new IllegalStateException("sem linha no MediaStore");
        descritor = cr.openFileDescriptor(uri, "rw");
        if (descritor == null) throw new IllegalStateException("sem descritor");
        mux = new MediaMuxer(descritor.getFileDescriptor(), MediaMuxer.OutputFormat.MUXER_OUTPUT_MPEG_4);
    }

    /** Height 720 keeps the game's aspect; a phone whose encoder refuses it gets 480. Returns {width, height}. */
    private int[] criarVideo() throws Exception {
        Exception ultimo = null;
        int larguraTela = Math.max(1, ExportarVideo.larguraTela), alturaTela = Math.max(1, ExportarVideo.alturaTela);
        for (int altura : new int[] {ALTURA, ALTURA_RESERVA}) {
            int alt = Math.min(altura, alturaTela & ~1);
            int larg = Math.min(LARGURA_MAXIMA, Math.round(alt * (float) larguraTela / alturaTela / 16f) * 16);
            MediaCodec c = null;
            try {
                MediaFormat f = MediaFormat.createVideoFormat(MediaFormat.MIMETYPE_VIDEO_AVC, larg, alt);
                f.setInteger(MediaFormat.KEY_COLOR_FORMAT, MediaCodecInfo.CodecCapabilities.COLOR_FormatSurface);
                f.setInteger(MediaFormat.KEY_BIT_RATE, KBPS_VIDEO * 1000);
                f.setInteger(MediaFormat.KEY_FRAME_RATE, FPS);
                f.setInteger(MediaFormat.KEY_I_FRAME_INTERVAL, SEGUNDOS_ENTRE_CHAVES);
                // A still screen produces no frames on its own, and the start of the file waits for one.
                f.setLong(MediaFormat.KEY_REPEAT_PREVIOUS_FRAME_AFTER, 1_000_000L / 30L);
                c = MediaCodec.createEncoderByType(MediaFormat.MIMETYPE_VIDEO_AVC);
                c.configure(f, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE);
                superficie = c.createInputSurface();
                c.start();
                video = c;
                return new int[] {larg, alt};
            } catch (Exception e) {
                ultimo = e;
                if (c != null) try { c.release(); } catch (Exception ignorado) { /* nothing to free */ }
            }
        }
        throw ultimo != null ? ultimo : new IllegalStateException("sem codificador de video");
    }

    /** Any problem here means a video with no sound, never a failed export. */
    private void criarSom() {
        if (checkSelfPermission(Manifest.permission.RECORD_AUDIO) != PackageManager.PERMISSION_GRANTED) {
            avisoDoSom = "O vídeo saiu sem som: a permissão de áudio foi negada.";
            return;
        }
        AudioRecord r = null;
        MediaCodec c = null;
        try {
            AudioPlaybackCaptureConfiguration captura = new AudioPlaybackCaptureConfiguration.Builder(projecao)
                    .addMatchingUid(Process.myUid()).build();
            AudioFormat formato = new AudioFormat.Builder().setEncoding(AudioFormat.ENCODING_PCM_16BIT)
                    .setSampleRate(TAXA_SOM).setChannelMask(AudioFormat.CHANNEL_IN_STEREO).build();
            int minimo = AudioRecord.getMinBufferSize(TAXA_SOM, AudioFormat.CHANNEL_IN_STEREO, AudioFormat.ENCODING_PCM_16BIT);
            r = new AudioRecord.Builder().setAudioPlaybackCaptureConfig(captura).setAudioFormat(formato)
                    .setBufferSizeInBytes(Math.max(minimo, PEDACO_SOM) * 4).build();
            if (r.getState() != AudioRecord.STATE_INITIALIZED) throw new IllegalStateException("AudioRecord sem iniciar");
            MediaFormat f = MediaFormat.createAudioFormat(MediaFormat.MIMETYPE_AUDIO_AAC, TAXA_SOM, CANAIS_SOM);
            f.setInteger(MediaFormat.KEY_AAC_PROFILE, MediaCodecInfo.CodecProfileLevel.AACObjectLC);
            f.setInteger(MediaFormat.KEY_BIT_RATE, KBPS_SOM * 1000);
            f.setInteger(MediaFormat.KEY_MAX_INPUT_SIZE, PEDACO_SOM * 4);
            c = MediaCodec.createEncoderByType(MediaFormat.MIMETYPE_AUDIO_AAC);
            c.configure(f, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE);
            c.start();
            gravadorDeSom = r;
            som = c;
        } catch (Throwable t) {
            Log.w(TAG, "som: " + t.getClass().getSimpleName() + ": " + t.getMessage());
            avisoDoSom = "O vídeo saiu sem som: este aparelho não deixou capturar o áudio do jogo.";
            if (r != null) try { r.release(); } catch (Throwable ignorado) { /* nothing to free */ }
            if (c != null) try { c.release(); } catch (Throwable ignorado) { /* nothing to free */ }
        }
    }

    // ───────── the file ─────────

    private void ligarMux() {
        synchronized (travaMux) {
            if (muxLigado || formatoVideo == null) return;
            if (som != null && formatoSom == null && !desistiuDoSom) return;
            trilhaVideo = mux.addTrack(formatoVideo);
            if (formatoSom != null) trilhaSom = mux.addTrack(formatoSom);
            mux.start();
            muxLigado = true;
        }
        ExportarVideo.avancar(ExportarVideo.ABRINDO, ExportarVideo.PRONTA);
        marcaPronta = SystemClock.uptimeMillis();
    }

    private void escrever(int trilha, ByteBuffer dados, MediaCodec.BufferInfo info) {
        synchronized (travaMux) {
            long inicio = inicioUs, fim = fimUs;
            if (!muxLigado || trilha < 0 || inicio < 0L || info.presentationTimeUs < inicio) return;
            if (fim > 0L && info.presentationTimeUs > fim) return;
            long pts = info.presentationTimeUs - inicio;
            if (trilha == trilhaSom) {
                if (pts <= ultimoPtsSom) return;   // MediaMuxer refuses audio that goes back in time
                ultimoPtsSom = pts;
            }
            dados.position(info.offset);
            dados.limit(info.offset + info.size);
            long original = info.presentationTimeUs;
            info.presentationTimeUs = pts;
            mux.writeSampleData(trilha, dados, info);
            info.presentationTimeUs = original;
            if (trilha == trilhaVideo) quadros++;
        }
    }

    // ───────── video thread ─────────

    private void lacoDoVideo() {
        MediaCodec.BufferInfo info = new MediaCodec.BufferInfo();
        long comeco = SystemClock.uptimeMillis(), formatoEm = -1L, eosEm = -1L;
        boolean chavePedida = false;
        try {
            while (true) {
                long agora = SystemClock.uptimeMillis();
                if (fimDoVideo && eosEm < 0L) {
                    video.signalEndOfInputStream();
                    eosEm = agora;
                }
                if (eosEm >= 0L && agora - eosEm > ESPERA_FIM_MS) return;   // some encoders never send the end
                if (!encerrando) {
                    if (formatoVideo == null && agora - comeco > ESPERA_IMAGEM_MS) {
                        encerrar(false, "O aparelho não entregou a imagem da tela.");
                    } else if (formatoVideo != null && formatoEm >= 0L && !muxLigado && agora - formatoEm > ESPERA_SOM_MS) {
                        synchronized (travaMux) { desistiuDoSom = true; }
                        avisoDoSom = "O vídeo saiu sem som: o áudio do jogo não chegou.";
                        ligarMux();
                    }
                    int e = ExportarVideo.estadoAgora();
                    if (e == ExportarVideo.PRONTA && marcaPronta > 0L && agora - marcaPronta > ESPERA_INICIO_MS) {
                        encerrar(false, "A gravação não começou a tempo.");
                    } else if (e == ExportarVideo.GRAVANDO && agora - ExportarVideo.ultimoPulso() > PULSO_MAXIMO_MS) {
                        encerrar(false, "Exportação cancelada: o jogo saiu da tela.");
                    }
                }
                if (pedidoDeInicio && !chavePedida && muxLigado) {
                    Bundle b = new Bundle();
                    b.putInt(MediaCodec.PARAMETER_KEY_REQUEST_SYNC_FRAME, 0);
                    video.setParameters(b);
                    chavePedida = true;
                }
                int saida = video.dequeueOutputBuffer(info, ESPERA_US);
                if (saida == MediaCodec.INFO_TRY_AGAIN_LATER) continue;
                if (saida == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED) {
                    synchronized (travaMux) { formatoVideo = video.getOutputFormat(); }
                    formatoEm = agora;
                    ligarMux();
                    continue;
                }
                if (saida < 0) continue;
                if ((info.flags & MediaCodec.BUFFER_FLAG_CODEC_CONFIG) == 0 && info.size > 0) {
                    // The file has to open on a key frame, and the game only starts playing after this one.
                    if (inicioUs < 0L && chavePedida && (info.flags & MediaCodec.BUFFER_FLAG_KEY_FRAME) != 0) {
                        inicioUs = info.presentationTimeUs;
                        ExportarVideo.avancar(ExportarVideo.PRONTA, ExportarVideo.GRAVANDO);
                    }
                    escrever(trilhaVideo, video.getOutputBuffer(saida), info);
                }
                boolean acabou = (info.flags & MediaCodec.BUFFER_FLAG_END_OF_STREAM) != 0;
                video.releaseOutputBuffer(saida, false);
                if (acabou) return;
            }
        } catch (Throwable t) {
            Log.w(TAG, "video: " + t.getClass().getSimpleName() + ": " + t.getMessage());
            encerrar(false, "A gravação do vídeo falhou.");
        }
    }

    // ───────── sound thread ─────────

    private long ptsDoSom(int bytes) {
        long agora = System.nanoTime() / 1000L;
        long duracao = bytes * 1_000_000L / (TAXA_SOM * BYTES_POR_AMOSTRA);
        // Same clock as the frames of the VirtualDisplay (CLOCK_MONOTONIC). Only jumps forward, when data was lost.
        if (proximoPtsSom < 0L || agora - duracao - proximoPtsSom > 200_000L) proximoPtsSom = agora - duracao;
        long pts = proximoPtsSom;
        proximoPtsSom += duracao;
        return pts;
    }

    private void lacoDoSom() {
        MediaCodec.BufferInfo info = new MediaCodec.BufferInfo();
        boolean calado = false;
        try {
            gravadorDeSom.startRecording();
            while (true) {
                int entrada = som.dequeueInputBuffer(ESPERA_US);
                if (entrada >= 0) {
                    ByteBuffer b = som.getInputBuffer(entrada);
                    b.clear();
                    int pedaco = Math.min(b.remaining(), PEDACO_SOM);
                    int lidos = 0;
                    if (!fimDoSom && !calado) lidos = gravadorDeSom.read(b, pedaco, AudioRecord.READ_BLOCKING);
                    if (fimDoSom) {
                        som.queueInputBuffer(entrada, 0, 0, ptsDoSom(0), MediaCodec.BUFFER_FLAG_END_OF_STREAM);
                        long ate = SystemClock.uptimeMillis() + ESPERA_FIM_MS;
                        while (!drenarSom(info) && SystemClock.uptimeMillis() < ate) { /* until the end marker */ }
                        return;
                    }
                    if (lidos < 0) calado = true;
                    if (lidos <= 0) {
                        // A track with no samples makes MediaMuxer.stop fail and lose the whole file: silence keeps it valid.
                        for (int i = 0; i < pedaco; i++) b.put(i, (byte) 0);
                        Thread.sleep(pedaco * 1000L / (TAXA_SOM * BYTES_POR_AMOSTRA));
                        lidos = pedaco;
                    } else if (!ouviuAlgo) {
                        for (int i = 0; i + 1 < lidos; i += 64) {
                            if (b.getShort(i) != 0) { ouviuAlgo = true; break; }
                        }
                    }
                    som.queueInputBuffer(entrada, 0, lidos, ptsDoSom(lidos), 0);
                }
                drenarSom(info);
            }
        } catch (Throwable t) {
            Log.w(TAG, "som: " + t.getClass().getSimpleName() + ": " + t.getMessage());
            avisoDoSom = "O som do vídeo parou no meio.";
        }
    }

    /** true = the encoder delivered its end marker. */
    private boolean drenarSom(MediaCodec.BufferInfo info) {
        while (true) {
            int saida = som.dequeueOutputBuffer(info, ESPERA_US);
            if (saida == MediaCodec.INFO_TRY_AGAIN_LATER) return false;
            if (saida == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED) {
                synchronized (travaMux) { formatoSom = som.getOutputFormat(); }
                ligarMux();
                continue;
            }
            if (saida < 0) continue;
            if ((info.flags & MediaCodec.BUFFER_FLAG_CODEC_CONFIG) == 0 && info.size > 0) {
                escrever(trilhaSom, som.getOutputBuffer(saida), info);
            }
            boolean acabou = (info.flags & MediaCodec.BUFFER_FLAG_END_OF_STREAM) != 0;
            som.releaseOutputBuffer(saida, false);
            if (acabou) return true;
        }
    }

    // ───────── closing ─────────

    private void encerrar(boolean guardar, String erro) {
        synchronized (this) {
            if (encerrando) return;
            encerrando = true;
        }
        fimUs = System.nanoTime() / 1000L;   // what is drawn after this instant ("salvando...") stays out of the video
        ExportarVideo.fechando();
        mao.post(() -> fechar(guardar, erro));
    }

    private void fechar(boolean guardar, String erro) {
        fimDoSom = true;
        try { if (gravadorDeSom != null) gravadorDeSom.stop(); } catch (Throwable ignorado) { /* already stopped */ }
        fimDoVideo = true;
        juntar(fioSom, 3000L);
        juntar(fioVideo, 5000L);
        boolean ok = guardar && erro == null && fecharMux();
        soltar();
        if (ok) ok = publicar();
        else apagar();
        String semImagem = guardar && erro == null && !ok ? "Não consegui salvar o vídeo." : null;
        if (ok) {
            if (avisoDoSom == null && som != null && !ouviuAlgo) avisoDoSom = "O vídeo saiu sem som: o Android não entregou o áudio do jogo.";
            ExportarVideo.salvo(uri, avisoDoSom);
            Log.i(TAG, "salvo: " + quadros + " quadros");
        } else if (erro != null || semImagem != null) {
            ExportarVideo.falhar(erro != null ? erro : semImagem);
        } else {
            ExportarVideo.cancelado();
        }
        sair();
    }

    private static void juntar(Thread t, long ms) {
        if (t == null) return;
        try {
            t.join(ms);
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
        }
    }

    private boolean fecharMux() {
        synchronized (travaMux) {
            if (mux == null || !muxLigado || quadros == 0L) return false;
            try {
                mux.stop();
                return true;
            } catch (Throwable t) {
                Log.w(TAG, "mux: " + t.getClass().getSimpleName() + ": " + t.getMessage());
                return false;
            }
        }
    }

    private void soltar() {
        synchronized (travaMux) {
            try { if (mux != null) mux.release(); } catch (Throwable ignorado) { /* never started */ }
            mux = null;
            muxLigado = false;
        }
        try { if (tela != null) tela.release(); } catch (Throwable ignorado) { /* already gone */ }
        try { if (projecao != null) projecao.stop(); } catch (Throwable ignorado) { /* already gone */ }
        try { if (video != null) { video.stop(); video.release(); } } catch (Throwable ignorado) { /* already gone */ }
        try { if (superficie != null) superficie.release(); } catch (Throwable ignorado) { /* already gone */ }
        try { if (som != null) { som.stop(); som.release(); } } catch (Throwable ignorado) { /* already gone */ }
        try { if (gravadorDeSom != null) gravadorDeSom.release(); } catch (Throwable ignorado) { /* already gone */ }
        try { if (descritor != null) descritor.close(); } catch (Throwable ignorado) { /* already gone */ }
    }

    private boolean publicar() {
        try {
            ContentValues v = new ContentValues();
            v.put(MediaStore.Video.Media.IS_PENDING, 0);
            getContentResolver().update(uri, v, null, null);
            return true;
        } catch (Throwable t) {
            Log.w(TAG, "publicar: " + t.getClass().getSimpleName());
            apagar();
            return false;
        }
    }

    private void apagar() {
        try {
            if (uri != null) getContentResolver().delete(uri, null, null);
        } catch (Throwable t) {
            Log.w(TAG, "apagar: " + t.getClass().getSimpleName());
        }
    }

    private void sair() {
        ExportarVideo.servicoSaiu(this);
        try { stopForeground(STOP_FOREGROUND_REMOVE); } catch (Throwable ignorado) { /* not foreground */ }
        stopSelf();
    }

    @Override public void onDestroy() {
        ExportarVideo.servicoSaiu(this);
        if (fio != null) fio.quitSafely();
        super.onDestroy();
    }
}
