package dev.allofus.fusioncore;

import android.content.ComponentName;
import android.content.Context;
import android.content.Intent;
import android.content.pm.ActivityInfo;
import android.content.pm.PackageInfo;
import android.content.pm.PackageManager.NameNotFoundException;
import android.database.Cursor;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Looper;
import android.provider.OpenableColumns;
import android.util.Log;
import android.view.View;
import android.webkit.MimeTypeMap;
import android.widget.ProgressBar;
import android.widget.TextView;
import android.widget.Toast;

import androidx.activity.result.ActivityResultLauncher;
import androidx.activity.result.contract.ActivityResultContracts;
import androidx.annotation.Nullable;
import androidx.appcompat.app.AlertDialog;
import androidx.appcompat.app.AppCompatActivity;

import java.io.BufferedReader;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.FileReader;
import java.io.IOException;
import java.io.InputStream;
import java.util.HashMap;
import java.util.Locale;
import java.util.Map;
import java.util.Objects;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicReference;

import dev.allofus.fusioncore.hooks.InstrumentationHooks;
import dev.allofus.fusioncore.hooks.ActivityManagerHooks;
import dev.allofus.fusioncore.hooks.NotificationManagerHooks;
import dev.allofus.fusioncore.hooks.PackageManagerHooks;
import dev.allofus.fusioncore.hooks.GameRuntime;
import dev.allofus.fusioncore.tools.Abertura;
import dev.allofus.fusioncore.tools.Aparelho;
import dev.allofus.fusioncore.tools.FusionConfig;
import dev.allofus.fusioncore.tools.GlobalMetadata;
import dev.allofus.fusioncore.tools.Il2CppApiMapper;
import dev.allofus.fusioncore.tools.UnityBaseLibraries;
import dev.allofus.fusioncore.tools.LibUnityDownloader;
import dev.allofus.fusioncore.tools.Conta;
import dev.allofus.fusioncore.tools.ModsManager;
import dev.allofus.fusioncore.tools.Otimizacoes;
import dev.allofus.fusioncore.tools.NativeLibraryManager;
import dev.allofus.fusioncore.tools.Utilities;
import dev.allofus.fusioncore.tools.UnityUtils;

public class BootstrapActivity extends AppCompatActivity {

    private static final String TAG = "FusionCore";

    public static final String EXTRA_TARGET_PACKAGE = "target_package";
    public static final String EXTRA_USE_ORIGINAL_LIBUNITY = "og_libunity";
    public static final String EXTRA_USE_IL2CPP2MONO = "use_il2cpp2mono";
    public static final String BACKUP_UNITY_VERSION = "2017.0.0";
    public static final String IL2CPP_API_MAP = "il2cpp-api.map";

    // CriCri Mods (11/11): o que a pessoa lê quando não abre. Uma frase cada; ficam aqui, junto de quem as mostra (ver Aparelho).
    private static final String FALHA_GERAL = "Algo deu errado ao preparar o jogo. Abra o app de novo: ele oferece o relatório para você mandar.";
    private static final String FALHA_DADOS = "Não consegui copiar os dados do Among Us. Confira o espaço do celular e toque em JOGAR de novo.";
    private static final String FALHA_PECAS = "Não consegui preparar as peças dos mods. Confira o espaço do celular e toque em JOGAR de novo.";
    private static final String FALHA_REDE = "Não consegui baixar uma peça do jogo (é só na primeira vez). Se estiver no Wi-Fi, tente pelos dados móveis e toque em JOGAR de novo.";
    private static final String FALHA_ESTRANHOS ="Achei arquivos estranhos na pasta dos mods e não consegui apagar. Reinicie o celular e tente de novo.";

    /**
     * CriCri Mods (11/11, "segundo plano no meio da preparação"): a preparação roda numa linha própria e a primeira vez demora; a pessoa sai
     * do app e volta. Então:
     *  - só UMA preparação por vez no processo (uma tela recriada pelo Android se junta à que já está rodando, não começa outra);
     *  - quem atualiza a tela é sempre a tela ATUAL, não a que começou a preparação;
     *  - o jogo só é aberto com o app na frente (o Android 10+ recusa calado abrir uma tela a partir do fundo: a pessoa voltava e não havia
     *    nada). Se ficou pronto no fundo, abre quando ela voltar.
     */
    private static final AtomicBoolean emAndamento = new AtomicBoolean(false);
    // (12/11, pendência da revisão de 11/11) A preparação começa num post da tela; se a tela for destruída antes do post rodar (o
    // Android fecha a tela logo ao abrir), o post nunca roda e a marca acima ficava presa: a próxima abertura no mesmo processo
    // "se juntava" a uma preparação que não existia e parava na tela de carregamento. Quem pôs a marca e não começou, solta.
    private static final AtomicBoolean fluxoComecou = new AtomicBoolean(false);
    private boolean poasAMarca;
    private static volatile BootstrapActivity atual;
    private static volatile Lancar lancamentoPendente;
    private static volatile String ultimoStatus;

    private interface Lancar {
        void com(BootstrapActivity tela);
    }

    private volatile boolean visivel;
    private TextView statusView;
    private TextView progressDetailsView;
    private ProgressBar spinnerProgress;
    private ProgressBar downloadProgress;
    private final AtomicReference<Uri> selectedFileUri = new AtomicReference<>(null);
    private volatile CountDownLatch filePickerLatch;
    private final ActivityResultLauncher<Intent> selectFileLauncher =
            registerForActivityResult(new ActivityResultContracts.StartActivityForResult(), result -> {
                try {
                    if (result.getResultCode() == RESULT_OK && result.getData() != null && result.getData().getData() != null) {
                        Uri uri = result.getData().getData();
                        selectedFileUri.set(uri);
                    } else {
                        selectedFileUri.set(null);
                    }
                } finally {
                    if (filePickerLatch != null) {
                        filePickerLatch.countDown();
                    }
                }
            });

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        Utilities.init(this);
        // (12/11) O gravador de erros tambem liga aqui: se o Android recriar o processo direto nesta tela (sem a tela inicial), um erro
        // de Java na abertura ainda deixa o arquivo com a pilha para o relatorio. Uma vez por processo (CrashDetector).
        try { dev.allofus.fusioncore.tools.CrashDetector.init(this); } catch (Exception ignored) { }
        setContentView(R.layout.activity_bootstrap);
        statusView = findViewById(R.id.bootstrap_status);
        progressDetailsView = findViewById(R.id.bootstrap_progress_details);
        spinnerProgress = findViewById(R.id.bootstrap_progress);
        downloadProgress = findViewById(R.id.bootstrap_download_progress);
        atual = this;

        String targetPackage = getIntent().getStringExtra(EXTRA_TARGET_PACKAGE);
        if (targetPackage == null || targetPackage.isEmpty()) {
            failAndFinish("No target package specified in intent extras!", null);
            return;
        }

        if (GameRuntime.jogoAberto()) {
            // (11/11, "queda na volta do login") O jogo já está aberto neste processo e alguém tocou em JOGAR de novo (a tela inicial pode
            // aparecer por cima do jogo na volta do login). Preparar tudo outra vez por cima do jogo vivo (ganchos, bibliotecas, arquivos
            // que ele está usando) é pedir queda: esta tela só sai da frente e o jogo, que está logo atrás, volta.
            Log.i(TAG, "The game is already open in this process: going back to it");
            finish();
            return;
        }

        if (!emAndamento.compareAndSet(false, true)) {
            // uma preparação já está rodando neste processo (esta tela foi recriada): só mostra por onde ela anda
            Log.i(TAG, "Bootstrap already running: this screen joins it");
            String status = ultimoStatus;
            if (status != null && statusView != null) statusView.setText(status);
            return;
        }
        poasAMarca = true;
        fluxoComecou.set(false);
        setPhaseStatus(getString(R.string.bootstrap_status_preparing));

        // Let the loading screen render first, then perform initialization work.
        statusView.post(() -> new Thread(() -> {
            fluxoComecou.set(true);
            try {
                runBootstrapFlow(targetPackage);
            } catch (Throwable t) {
                falhar(FALHA_GERAL, "Unexpected failure in the bootstrap flow", t, true);
            }
        }, "bootstrap-flow").start());
    }

    @Override
    protected void onStart() {
        super.onStart();
        visivel = true;
        atual = this;
        Lancar pendente = lancamentoPendente;
        if (pendente != null) {
            lancamentoPendente = null;
            Log.i(TAG, "The app is back in front: opening the game that was ready");
            pendente.com(this);
        }
    }

    @Override
    protected void onStop() {
        visivel = false;
        super.onStop();
    }

    @Override
    protected void onDestroy() {
        if (atual == this) atual = null;
        // a preparação desta tela nunca começou (o post não rodou): solta a marca para a próxima abertura
        if (poasAMarca && !fluxoComecou.get()) {
            emAndamento.set(false);
            Log.i(TAG, "Bootstrap screen closed before the flow started: mark released");
        }
        super.onDestroy();
    }

    private void runBootstrapFlow(String targetPackage) {
        Context appContext = getApplicationContext();
        File appDataDir = new File(appContext.getFilesDir(), targetPackage);
        Abertura.ouvir(new Abertura.Ouvinte() {
            @Override public void etapa(String nome, int indice, int total) {
            }

            @Override public void progresso(long feito, long total) {
                // (11/11, app-telas) "Copiando o jogo…" só aparece quando há mesmo o que copiar (o andamento só vem nesse caso); antes a
                // frase "(só na primeira vez)" piscava em toda abertura
                setBytesStatus(getString(R.string.bootstrap_status_copy_assets), feito, total);
            }
        });
        Abertura.comecar(appContext, appDataDir);

        // CriCri Mods (11/11, "erros de compatibilidade"): o aparelho e o jogo servem? Antes de copiar, descompactar ou baixar qualquer coisa.
        Abertura.etapa(Abertura.CONFERIR);
        Aparelho.Problema problema = Aparelho.conferir(appContext, targetPackage, new File(getRuntimeDir(appContext), targetPackage));
        if (problema != null) {
            falhar(problema.mensagem, "Device check: " + problema.codigo, null, false);
            return;
        }

        Context gameContext;
        try {
            gameContext = createPackageContext(targetPackage, CONTEXT_IGNORE_SECURITY | CONTEXT_INCLUDE_CODE);
        } catch (Exception e) {
            falhar(FALHA_GERAL, "Failed to create package context for target package: " + targetPackage, e, true);
            return;
        }

        try {
            gameContext = GameRuntime.prepareGameContext(gameContext, getClassLoader());
        } catch (Exception e) {
            falhar(FALHA_GERAL, "Failed to configure classes.", e, true);
            return;
        }

        Intent launchIntent = getPackageManager().getLaunchIntentForPackage(targetPackage);
        if (launchIntent == null) {
            falhar(FALHA_GERAL, "No launch intent for target package: " + targetPackage, null, true);
            return;
        }

        ComponentName launcherComponent = launchIntent.getComponent();
        if (launcherComponent == null) {
            launcherComponent = launchIntent.resolveActivity(getPackageManager());
        }

        var overrideActivity = FusionSettings.getActivityOverrideForGame(this, targetPackage);
        try {
            if (!overrideActivity.equals(getString(R.string.settings_automatic))) {
                var overrideClass = gameContext.getClassLoader().loadClass(overrideActivity);
                if (overrideClass != null) {
                    launcherComponent = new ComponentName(targetPackage, overrideActivity);
                    Log.i(TAG, "Using override activity " + overrideActivity);
                    runOnUiThread(() -> Toast.makeText(this, "Using override activity " + overrideActivity, Toast.LENGTH_LONG).show());
                } else {
                    Log.i(TAG, "Failed to find override activity " + overrideActivity);
                    runOnUiThread(()-> Toast.makeText(this, "Failed to find override activity.", Toast.LENGTH_LONG).show());
                }
            }
        } catch (Exception e) {
            runOnUiThread(()-> Toast.makeText(this, "Exception when finding override activity.", Toast.LENGTH_LONG).show());
            Log.e(TAG, "Failed to get override activity "+ overrideActivity, e);
        }

        if (launcherComponent == null) {
            falhar(FALHA_GERAL, "Failed to resolve launcher activity for target package: " + targetPackage, null, true);
            return;
        }

        final int targetOrientation = resolveTargetOrientation(launcherComponent);
        // CriCri Mods (11/11, CriCri: "essa tela de carregamento também vira de cabeça pra baixo do nada"): the loading screen was
        // "locked" to whatever side the phone had when it opened, and the game then picked its own landscape side, so the picture
        // turned 180° at the switch. The loading screen now follows the same orientation rule as the game.
        runOnUiThread(() -> {
            try {
                BootstrapActivity tela = atual;
                (tela != null ? tela : this).setRequestedOrientation(targetOrientation);
            } catch (Exception ignored) {}
        });

        boolean useOriginalLibUnity = getIntent().getBooleanExtra(EXTRA_USE_ORIGINAL_LIBUNITY, false);
        boolean useIl2Cpp2Mono = getIntent().getBooleanExtra(EXTRA_USE_IL2CPP2MONO, false);
        FusionConfig config;

        try {
            config = prepareConfig(
                    appContext,
                    gameContext,
                    launcherComponent,
                    targetPackage,
                    appDataDir,
                    useOriginalLibUnity,
                    useIl2Cpp2Mono
            );
        } catch (Falha f) {
            falhar(f.paraAPessoa, f.getMessage(), f.getCause(), f.guardar);
            return;
        } catch (Throwable t) {
            falhar(FALHA_GERAL, "Failed while preparing Fusion runtime.", t, true);
            return;
        }

        Class<?> launcherClass;
        try {
            launcherClass = gameContext.getClassLoader().loadClass(launcherComponent.getClassName());
        } catch (ClassNotFoundException e) {
            // (11/11) antes só escrevia no log e a tela de espera ficava para sempre
            falhar(FALHA_GERAL, "Failed to get class for launcher activity: " + launcherComponent.getClassName(), e, true);
            return;
        }

        Abertura.etapa(Abertura.GANCHOS);
        setPhaseStatus(getString(R.string.bootstrap_status_installing_hooks));
        try {
            PackageManagerHooks.install(appContext, gameContext);
            ActivityManagerHooks.install(appContext, gameContext);
            NotificationManagerHooks.install(appContext, gameContext);
            GameRuntime.install(gameContext);
        } catch (Exception e) {
            falhar(FALHA_GERAL, "Failed to install base hooks", e, true);
            return;
        }

        var className = launcherComponent.getClassName();
        final Context preparedGameContext = gameContext;

        try {
            Abertura.etapa(Abertura.ABRIR);
            setPhaseStatus(getString(R.string.bootstrap_status_launching));
            initializeFusion(config);
            Lancar lancar = tela -> {
                try {
                    GameRuntime.initializeApplication(preparedGameContext, appContext);
                    var intent = new Intent(tela, launcherClass);

                    // Using the stub activity intent here avoids one extra layer of hooks running.
                    // Its not necessary but could be more performant.
                    var intentWrapped = new Intent(tela, StubActivity.class);
                    intentWrapped.putExtra(InstrumentationHooks.EXTRA_IS_DYNAMIC_ACTIVITY, true);
                    intentWrapped.putExtra(InstrumentationHooks.EXTRA_ORIGINAL_INTENT, intent);
                    intentWrapped.putExtra(InstrumentationHooks.EXTRA_FUSION_CONFIG, config);
                    intentWrapped.putExtra(InstrumentationHooks.EXTRA_TARGET_ORIENTATION, targetOrientation);
                    tela.startActivity(intentWrapped);
                    tela.finish();
                    Abertura.fim(true, null);
                    emAndamento.set(false);
                } catch (Throwable t) {
                    falhar(FALHA_GERAL, "Failed to launch target app's launcher activity: " + className, t, true);
                }
            };
            runOnMainThread(() -> {
                BootstrapActivity tela = atual;
                if (tela != null && tela.visivel && !tela.isFinishing()) {
                    lancar.com(tela);
                } else {
                    Log.i(TAG, "The game is ready but the app is not in front: it opens when the player comes back");
                    lancamentoPendente = lancar;
                }
            });
        } catch (Exception e) {
            falhar(FALHA_GERAL, "Failed to launch target app's launcher activity: " + className, e, true);
        }
    }

    /** Uma falha da preparação que já sabe o que dizer à pessoa. guardar = vira relatório oferecido na próxima abertura do app. */
    private static final class Falha extends RuntimeException {
        final String paraAPessoa;
        final boolean guardar;

        Falha(String paraAPessoa, String tecnico, Throwable causa, boolean guardar) {
            super(tecnico, causa);
            this.paraAPessoa = paraAPessoa;
            this.guardar = guardar;
        }
    }

    private void setPhaseStatus(String status) {
        ultimoStatus = status;
        runOnMainThread(() -> {
            BootstrapActivity tela = atual;
            if (tela == null) return;
            if (tela.statusView != null) {
                tela.statusView.setText(status);
            }
            if (tela.spinnerProgress != null) {
                tela.spinnerProgress.setVisibility(View.VISIBLE);
            }
            if (tela.downloadProgress != null) {
                tela.downloadProgress.setVisibility(View.GONE);
                tela.downloadProgress.setIndeterminate(false);
                tela.downloadProgress.setProgress(0);
            }
            if (tela.progressDetailsView != null) {
                tela.progressDetailsView.setVisibility(View.GONE);
                tela.progressDetailsView.setText("");
            }
        });
    }

    private void setDownloadStatus(long downloadedBytes, long totalBytes) {
        setBytesStatus(getString(R.string.bootstrap_status_downloading_libunity), downloadedBytes, totalBytes);
    }

    /** A barra com os números de uma etapa longa (baixar a peça, copiar os dados). status null = o texto da etapa fica como está. */
    private void setBytesStatus(@Nullable String status, long doneBytes, long totalBytes) {
        if (status != null) ultimoStatus = status;
        runOnMainThread(() -> {
            BootstrapActivity tela = atual;
            if (tela == null) return;
            if (tela.spinnerProgress != null) {
                tela.spinnerProgress.setVisibility(View.GONE);
            }
            // (11/11) totalBytes vem 0 ou -1 quando o servidor não diz o tamanho: a divisão por zero fechava o app no meio do download
            boolean hasTotal = totalBytes > 0L;
            int percent = hasTotal ? (int) Math.max(0L, Math.min(100L, (doneBytes * 100L) / totalBytes)) : 0;
            if (tela.downloadProgress != null) {
                tela.downloadProgress.setVisibility(View.VISIBLE);
                tela.downloadProgress.setIndeterminate(!hasTotal);
                if (hasTotal) {
                    tela.downloadProgress.setProgress(percent);
                }
            }
            if (status != null && tela.statusView != null) {
                tela.statusView.setText(status);
            }
            if (tela.progressDetailsView != null) {
                tela.progressDetailsView.setVisibility(View.VISIBLE);
                tela.progressDetailsView.setText(tela.getString(
                        R.string.bootstrap_download_progress,
                        percent,
                        formatBytes(doneBytes),
                        hasTotal ? formatBytes(totalBytes) : "?"
                ));
            }
        });
    }

    private String formatBytes(long bytes) {
        if (bytes < 1024L) {
            return bytes + " B";
        }
        double value = bytes;
        String[] units = new String[]{"B", "KB", "MB", "GB"};
        int unitIndex = 0;
        while (value >= 1024.0 && unitIndex < units.length - 1) {
            value /= 1024.0;
            unitIndex++;
        }
        return String.format(Locale.US, "%.1f %s", value, units[unitIndex]);
    }

    private void failAndFinish(String message, Throwable error) {
        falhar(FALHA_GERAL, message, error, error != null);
    }

    /**
     * CriCri Mods (11/11, ele: "tem gente conseguindo e outras não"): não abriu. Antes era um aviso em inglês que some em 3 segundos e o app
     * fechava. Agora: uma frase em português numa janela que espera a pessoa ler, o motivo técnico no log e na medida da abertura, e, quando
     * não é uma causa conhecida do aparelho, um arquivo que a tela inicial oferece como relatório na próxima vez.
     */
    private void falhar(String paraAPessoa, String tecnico, @Nullable Throwable erro, boolean guardarRelatorio) {
        if (erro != null) {
            Log.e(TAG, tecnico, erro);
        } else {
            Log.e(TAG, tecnico);
        }
        String etapa = Abertura.etapaAtual();
        Abertura.fim(false, tecnico + (erro == null ? "" : " (" + erro.getClass().getSimpleName() + ")"));
        if (guardarRelatorio) Abertura.guardarFalha(etapa, tecnico, erro);
        emAndamento.set(false);
        lancamentoPendente = null;
        runOnMainThread(() -> {
            BootstrapActivity tela = atual;
            if (tela == null) tela = this;
            final BootstrapActivity onde = tela;
            if (onde.statusView != null) {
                onde.statusView.setText(onde.getString(R.string.bootstrap_status_error));
            }
            if (onde.spinnerProgress != null) onde.spinnerProgress.setVisibility(View.GONE);
            if (onde.downloadProgress != null) onde.downloadProgress.setVisibility(View.GONE);
            if (onde.progressDetailsView != null) onde.progressDetailsView.setVisibility(View.GONE);
            if (onde.isFinishing() || onde.isDestroyed()) {
                Toast.makeText(onde.getApplicationContext(), paraAPessoa, Toast.LENGTH_LONG).show();
                return;
            }
            try {
                new AlertDialog.Builder(onde)
                        .setTitle(R.string.bootstrap_status_error)
                        .setMessage(paraAPessoa)
                        .setPositiveButton(android.R.string.ok, null)
                        .setOnDismissListener(d -> onde.finish())
                        .show();
            } catch (Exception e) {
                // a janela não pôde ser mostrada (tela indo embora): o aviso curto ainda aparece
                Toast.makeText(onde.getApplicationContext(), paraAPessoa, Toast.LENGTH_LONG).show();
                onde.finish();
            }
        });
    }

    private void runOnMainThread(Runnable runnable) {
        if (Looper.myLooper() == Looper.getMainLooper()) {
            runnable.run();
        } else {
            runOnUiThread(runnable);
        }
    }

    private void initializeFusion(FusionConfig config) {
        Log.i(TAG, "Initializing Fusion for " + config.gamePackageId + " via " + config.gameLauncherName);

        try {
            NativeLibraryManager.addFusionLibrary("main");
            NativeLibraryManager.addFusionLibrary("fusion");
            if(!config.isIl2Cpp2Mono) {
                NativeLibraryManager.addCacheLibrary("il2cpp");
            }
            else {
                NativeLibraryManager.addDotnetLibrary("il2cpp");
                NativeLibraryManager.addDotnetLibrary("monosgen-2.0");
            }
            NativeLibraryManager.addCacheLibrary("unity");
            NativeLibraryManager.setupLibraryHooks(config);
        } catch (Throwable t) {
            throw new IllegalStateException("Failed to initialize Fusion library routing", t);
        }
    }

    private Map<String, String> resolveIl2CppApiMap(File gameLibDir, File codeCacheScoped,
                                                    boolean useOriginalLibUnity, File DeobfuscationDir) {
        File gameLibIl2Cpp = new File(gameLibDir, "libil2cpp.so");
        File cachedLibUnity = new File(codeCacheScoped, "libunity.so");

        if (!useOriginalLibUnity) {
            try {
                Map<String, String> map = Il2CppApiMapper.prepare(
                        new File(gameLibDir, "libunity.so"), gameLibIl2Cpp, cachedLibUnity);
                if (!map.isEmpty()) {
                    return map;
                }
            } catch (IOException e) {
                Log.e(TAG, "Failed to map obfuscated il2cpp exports automatically", e);
            }
        }

        try {
            if (!Il2CppApiMapper.hasObfuscatedExports(gameLibIl2Cpp)) {
                return new HashMap<>();
            }
        } catch (IOException e) {
            Log.e(TAG, "Failed to read il2cpp exports", e);
            return new HashMap<>();
        }

        File mapFile = new File(DeobfuscationDir, IL2CPP_API_MAP);
        if (!mapFile.isFile()) {
            Uri selected = promptForFile(R.string.boostrap_file_request_il2cpp_map, "map");
            if (selected == null) {
                runOnMainThread(() -> Toast.makeText(this, "WARNING: no il2cpp api deobfuscation map selected. are you sure you know what you are doing?", Toast.LENGTH_LONG).show());
                return new HashMap<>();
            }
            try {
                copyUriToFile(selected, mapFile);
            } catch (IOException e) {
                Log.e(TAG, "Couldn't copy the deobfuscated il2cpp api map!", e);
                return new HashMap<>();
            }
        }

        Map<String, String> map = GetIL2CPPMap(DeobfuscationDir);
        if (!useOriginalLibUnity && !map.isEmpty()) {
            try {
                Il2CppApiMapper.patchLibUnity(cachedLibUnity, map);
            } catch (IOException e) {
                Log.e(TAG, "Failed to apply il2cpp api map to libunity", e);
            }
        }
        return map;
    }

    /**
     * Troca o global-metadata.dat da cópia por um que a pessoa escolheu (recurso do Fusion Core para jogos com o arquivo CIFRADO).
     *
     * CriCri Mods (11/11, revisão de segurança): o arquivo de troca fica na pasta externa do app, onde um cabo, um gerenciador de arquivos
     * ou (até o Android 10) outro app escrevem. Um arquivo plantado ali era copiado por cima do oficial A CADA JOGAR e usado para gerar o
     * interop, sem conferência nenhuma. O Among Us não cifra esse arquivo: a troca só vale quando o do jogo é mesmo cifrado; senão é ignorada.
     */
    private void applyGlobalMetadataOverride(String targetPackage, File copiedData, android.content.pm.ApplicationInfo gameInfo) {
        if (!GlobalMetadata.hasValidOverride(targetPackage)) {
            return;
        }
        if (!GlobalMetadata.isEncrypted(gameInfo)) {
            Log.w(TAG, "Ignoring the global-metadata override: this game's metadata is not encrypted");
            return;
        }
        File target = new File(copiedData, "Managed/Metadata/" + GlobalMetadata.FILE_NAME);
        try (InputStream in = new FileInputStream(GlobalMetadata.getOverrideFile(targetPackage));
             FileOutputStream out = new FileOutputStream(target, false)) {
            byte[] buffer = new byte[64 * 1024];
            int count;
            while ((count = in.read(buffer)) != -1) {
                out.write(buffer, 0, count);
            }
            Log.i(TAG, "Applied global-metadata override to " + target.getAbsolutePath());
        } catch (IOException e) {
            Log.e(TAG, "Failed to apply global-metadata override", e);
        }
    }

    private static File getRuntimeDir(Context context) {
        return context.getDir("fusion_runtime", Context.MODE_PRIVATE);
    }

    HashMap<String, String> GetIL2CPPMap(File DeobfuscationDir){
        File MapFile = new File(DeobfuscationDir, IL2CPP_API_MAP);
        HashMap<String, String> map = new HashMap<>();
        if (!MapFile.exists() || !MapFile.isFile()) {
            return map;
        }
        try (BufferedReader reader = new BufferedReader(new FileReader(MapFile))) {
            String line;
            while ((line = reader.readLine()) != null) {
                line = line.trim();
                if (line.isEmpty() || line.startsWith("#") || line.startsWith("//")) {
                    continue;
                }
                int colonIndex = line.indexOf(':');
                if (colonIndex > 0) {
                    String normal = line.substring(0, colonIndex).trim();
                    String deobfuscated = line.substring(colonIndex + 1).trim();
                    if (!normal.isEmpty() && !deobfuscated.isEmpty()) {
                        map.put(normal, deobfuscated);
                    }
                }
            }
        } catch (IOException e) {
            Log.e(TAG, "Failed to parse IL2CPP API map file: " + MapFile.getAbsolutePath(), e);
        }
        return map;
    }

    /**
     * A preparação, etapa por etapa (cada uma medida pela Abertura). O que cada uma custa por natureza:
     *  - dados: copiar assets/bin/Data do APK do jogo para a pasta do app. Só na primeira vez e quando o Among Us atualiza (centenas de MB).
     *  - unity: achar a versão da Unity (lê 32 bytes) e ter a libunity sem cortes: baixada uma vez (dezenas de MB, rede), depois só conferida.
     *  - pecas: descompactar o BepInEx (8 MB) e o .NET (67 MB) de dentro do APK. Só quando o app é instalado/atualizado; roda EM PARALELO com
     *    "dados" e "unity" desde 11/11 (são discos e pastas diferentes; na primeira abertura eram 4,6 s parados na fila, medidos em 08/11).
     *  - mods: pôr os DLLs ligados em BepInEx/plugins (sha256 de poucos MB).
     *  - seguranca: Integridade, a cada JOGAR: sha256 do núcleo do BepInEx (8 MB) e do interop (50 MB), agora em paralelo.
     *  - conta: opções de tela, login do Google e chave do CRICRI BR (Keystore) para o processo do jogo.
     *  - mapa: o jogo é ofuscado? (resposta guardada; antes lia duas libunity inteiras a cada JOGAR)
     */
    private FusionConfig prepareConfig(Context appContext,
                                       Context gameContext,
                                       ComponentName launcherComponent,
                                       String targetPackage,
                                       File appDataDir,
                                       boolean useOriginalLibUnity,
                                       boolean useIl2Cpp2Mono) {

        String gameLibDir = gameContext.getApplicationInfo().nativeLibraryDir;
        String appLibDir = appContext.getApplicationInfo().nativeLibraryDir;

        String targetGameAbi = resolveTargetGameAbi(gameLibDir);

        File dataOnSdCard = Utilities.getExternalFusionCoreDirectory(targetPackage, null);
        File bepInExDir = new File(dataOnSdCard, "BepInEx");
        File codeCacheScoped = new File(getRuntimeDir(appContext), targetPackage);

        // As peças do app (BepInEx + .NET) não dependem do jogo: começam já, enquanto os dados do jogo são copiados e a libunity baixada.
        final File dotnetDirNormal = new File(getRuntimeDir(appContext), "dotnet");
        final boolean[] pecasOk = { true, true };
        Thread pecas = null;
        if (!useIl2Cpp2Mono) {
            pecas = new Thread(() -> {
                pecasOk[0] = Utilities.extractZipFromAssetsIfChanged(appContext, "BepInEx-arm64.zip", bepInExDir, "config/");
                pecasOk[1] = Utilities.extractZipFromAssetsIfChanged(appContext, "dotnet-arm64.zip", dotnetDirNormal, null);
            }, "bootstrap-pecas");
            pecas.start();
        }

        Abertura.etapa(Abertura.DADOS);
        setPhaseStatus(getString(R.string.bootstrap_status_detecting_version));
        File Assets = new File(dataOnSdCard, "Assets");
        File PersistentData = new File(dataOnSdCard, "PersistentData");
        File copiedData = new File(Assets, "bin/Data");
        String carimboDoJogo = null;
        long jogoAtualizadoEm = 0L;
        try {
            PackageInfo jogo = getPackageManager().getPackageInfo(targetPackage, 0);
            long codigo = Build.VERSION.SDK_INT >= Build.VERSION_CODES.P ? jogo.getLongVersionCode() : jogo.versionCode;
            carimboDoJogo = codigo + "|" + jogo.lastUpdateTime;
            jogoAtualizadoEm = jogo.lastUpdateTime;
        } catch (Exception e) {
            Log.w(TAG, "Could not read the game's version: the copy of its data is kept as it is");
        }
        boolean copied;
        try {
            copied = Utilities.copyAssets(gameContext.getApplicationInfo(), Assets, carimboDoJogo, jogoAtualizadoEm, Abertura::progresso);
        } catch (Utilities.SemEspaco e) {
            esperar(pecas);
            throw new Falha(Aparelho.faltaEspaco(e.faltam), "No space to copy the game's data", e, false);
        }
        if (!copied) {
            Log.e(TAG, "Failed to copy Unity Data assets! BepInEx may not work correctly.");
            // sem o global-metadata.dat o BepInEx não gera o interop e nenhum mod carrega: melhor dizer agora do que abrir quebrado
            if (!new File(copiedData, "Managed/Metadata/" + GlobalMetadata.FILE_NAME).isFile()) {
                esperar(pecas);
                throw new Falha(FALHA_DADOS, "Failed to copy the Unity Data assets", null, true);
            }
        }
        applyGlobalMetadataOverride(targetPackage, copiedData, gameContext.getApplicationInfo());
        File DeobfuscationDir = new File(bepInExDir, "Deobfuscation");
        Abertura.etapa(Abertura.UNITY);
        setPhaseStatus(getString(R.string.bootstrap_status_detecting_version));
        String version = UnityUtils.TryGetVersion(copiedData);
        if (!BuildConfig.ALLOW_RUNTIME_DOWNLOADS) {
            useOriginalLibUnity = true;
        }
        if (version == null) {
            Log.e(TAG, "Failed to determine Unity version! BepInEx may not work correctly.");
            version = BACKUP_UNITY_VERSION;
            useOriginalLibUnity = true;
        } else if (useOriginalLibUnity) {
            Log.i(TAG, "Skipping libunity download");
        } else {
            Log.i(TAG, "Determined Unity version: " + version);
            if (LibUnityDownloader.downloadAndCacheSafely(codeCacheScoped, version, targetGameAbi, new LibUnityDownloader.DownloadProgressListener() {
                @Override
                public void onDownloadStarted(String url, long totalBytes) {
                    Abertura.marcarPrimeiraVez();
                    setDownloadStatus(0L, totalBytes);
                }

                @Override
                public void onDownloadProgress(long downloadedBytes, long totalBytes) {
                    setDownloadStatus(downloadedBytes, totalBytes);
                }

                @Override
                public void onDownloadFinished(boolean success, boolean usedCache) {
                    // No-op: next phase will handle this.
                }
            })) {
                Log.i(TAG, "Successfully downloaded libunity for version " + version + " and ABI " + targetGameAbi);
            } else {
                Log.e(TAG, "Failed to download libunity for version " + version + " and ABI " + targetGameAbi + ", falling back to original.");
                useOriginalLibUnity = true;
            }
        }

        Abertura.etapa(Abertura.PECAS);
        setPhaseStatus(getString(R.string.bootstrap_status_extracting_runtime));
        File dotnetDir;
        if(!useIl2Cpp2Mono) {
            dotnetDir = dotnetDirNormal;
            esperar(pecas);
            if (!pecasOk[0] || !pecasOk[1]) {
                throw new Falha(FALHA_PECAS, "Failed to unpack " + (pecasOk[0] ? "dotnet-arm64.zip" : "BepInEx-arm64.zip"), null, true);
            }
            // (12/11) a primeira vez mais curta: duas opções do BepInEx que nenhum mod usa (ver Utilities.setFirstLaunchOptions)
            Utilities.setFirstLaunchOptions(bepInExDir);
            if (!BuildConfig.ALLOW_RUNTIME_DOWNLOADS) {
                Utilities.setUnityBaseLibrariesSource(bepInExDir, "");
            } else {
                Utilities.setUnityBaseLibrariesSource(bepInExDir, Utilities.DEFAULT_UNITY_BASE_LIBRARIES_SOURCE);
                if (!BACKUP_UNITY_VERSION.equals(version)) {
                    // (12/11, instalação do zero do CriCri) a peça que o BepInEx baixa sozinho na primeira vez: sem ela o jogo fecha
                    // logo ao abrir. Agora o app a deixa no lugar antes; se não der, avisa em vez de abrir o jogo quebrado.
                    final String baixando = getString(R.string.bootstrap_status_downloading_libunity);
                    if (!UnityBaseLibraries.resolve(bepInExDir, version, () -> {
                        Abertura.marcarPrimeiraVez();
                        setPhaseStatus(baixando);
                    })) {
                        throw new Falha(FALHA_REDE, "Unity base libraries for " + version + " unavailable and no interop yet", null, false);
                    }
                }
            }
            Abertura.etapa(Abertura.MODS);
            setPhaseStatus(getString(R.string.bootstrap_status_installing_mods));
            java.util.Set<String> oficiais = ModsManager.install(appContext, bepInExDir);
            Abertura.etapa(Abertura.SEGURANCA);
            if (!dev.allofus.fusioncore.tools.Integridade.conferir(appContext, bepInExDir, oficiais, targetPackage)) {
                throw new Falha(FALHA_ESTRANHOS, "Arquivos nao oficiais na pasta de mods que nao deu para remover", null, true);
            }
            Abertura.etapa(Abertura.CONTA);
            Otimizacoes.prepararJogo(appContext, getWindowManager().getDefaultDisplay(), bepInExDir);
            Conta.prepararJogo(appContext);
            dev.allofus.fusioncore.tools.ChaveBr.prepararJogo(appContext);
        } //we do something else
        else{
            dotnetDir = new File(getRuntimeDir(appContext), "mono");
            Utilities.extractZipFromAssets(appContext, "il2cpp2mono-arm64.zip", dotnetDir);
            ensureManagedDllsForMono(PersistentData);
        }

        Abertura.etapa(Abertura.MAPA);
        Map<String, String> il2cppMap = useIl2Cpp2Mono
                ? GetIL2CPPMap(DeobfuscationDir)
                : resolveIl2CppApiMap(new File(gameLibDir), codeCacheScoped, useOriginalLibUnity, DeobfuscationDir);

        return new FusionConfig(
                targetPackage,
                launcherComponent.flattenToString(),
                gameLibDir,
                appLibDir,
                appDataDir.getAbsolutePath(),
                codeCacheScoped.getAbsolutePath(),
                bepInExDir.getAbsolutePath(),
                dotnetDir.getAbsolutePath(),
                copiedData.getAbsolutePath(),
                version,
                useOriginalLibUnity,
                useIl2Cpp2Mono,
                new String[]{},
                new String[]{},
                il2cppMap
        );
    }

    /** Espera a linha das peças terminar (nunca deixa ela escrevendo sozinha depois de uma falha). */
    private static void esperar(@Nullable Thread linha) {
        if (linha == null) return;
        try {
            linha.join();
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
        }
    }

    private void copyUriToFile(Uri sourceUri, File target) throws IOException {
        File parent = target.getParentFile();
        if (parent != null && !parent.exists() && !parent.mkdirs()) {
            throw new IOException("Failed to create parent directory: " + parent.getAbsolutePath());
        }

        try (InputStream in = getContentResolver().openInputStream(sourceUri);
             FileOutputStream out = new FileOutputStream(target, false)) {
            if (in == null) {
                throw new IOException("Unable to open input stream for URI: " + sourceUri);
            }
            byte[] buffer = new byte[8192];
            int count;
            while ((count = in.read(buffer)) != -1) {
                out.write(buffer, 0, count);
            }
        }
    }

    private void ensureManagedDllsForMono(File GameDir) {
        File managedDir = new File(GameDir, "mono");
        File[] existingFiles = managedDir.listFiles();
        if (managedDir.exists() && existingFiles != null && existingFiles.length > 0) {
            Log.i(TAG, "Managed DLLs already present in " + managedDir.getAbsolutePath() + ", skipping selection prompt.");
            return;
        }

        Uri zipUri = promptForFile(R.string.bootstrap_request_dlls, "zip");
        if (zipUri == null) {
            throw new IllegalStateException("No Managed Zip file selected!");
        }

        setPhaseStatus(getString(R.string.bootstrap_status_extracting_mono_dlls));
        if (!extractZipFile(zipUri, managedDir)) {
            throw new IllegalStateException("Invalid or corrupted Managed Zip file provided!");
        }
    }

    public Uri promptForFile(int messageResId, @Nullable String extension) {
        filePickerLatch = new CountDownLatch(1);
        selectedFileUri.set(null);

        final String cleanExt = extension != null ? extension.trim().toLowerCase(Locale.ROOT).replace(".", "") : "";

        runOnMainThread(() -> {
            if (isFinishing() || isDestroyed()) {
                if (filePickerLatch != null) {
                    filePickerLatch.countDown();
                }
                return;
            }
            Toast.makeText(this, messageResId, Toast.LENGTH_LONG).show();
            Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT);
            intent.addCategory(Intent.CATEGORY_OPENABLE);

            if (!cleanExt.isEmpty()) {
                String mimeType = MimeTypeMap.getSingleton().getMimeTypeFromExtension(cleanExt);
                intent.setType(Objects.requireNonNullElse(mimeType, "*/*"));
            } else {
                intent.setType("*/*");
            }
            try {
                selectFileLauncher.launch(intent);
            } catch (Exception e) {
                Log.e(TAG, "Failed to launch file picker for extension: " + extension, e);
                if (filePickerLatch != null) {
                    filePickerLatch.countDown();
                }
            }
        });

        try {
            filePickerLatch.await();
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            Log.e(TAG, "Interrupted while waiting for file picker result", e);
            return null;
        }

        Uri resultUri = selectedFileUri.get();
        if (resultUri != null && !cleanExt.isEmpty()) {
            String fileName = getFileNameFromUri(resultUri);
            if (fileName != null && !fileName.toLowerCase(Locale.ROOT).endsWith("." + cleanExt)) {
                Log.w(TAG, "Selected file '" + fileName + "' does not match required extension: ." + cleanExt);
                runOnMainThread(() -> Toast.makeText(this, "Selected file must be a ." + cleanExt + " file", Toast.LENGTH_LONG).show());
                return null;
            }
        }

        return resultUri;
    }

    private String getFileNameFromUri(Uri uri) {
        if (uri == null) return null;
        if ("content".equals(uri.getScheme())) {
            try (Cursor cursor = getContentResolver().query(uri, new String[]{OpenableColumns.DISPLAY_NAME}, null, null, null)) {
                if (cursor != null && cursor.moveToFirst()) {
                    int index = cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME);
                    if (index != -1) {
                        String name = cursor.getString(index);
                        if (name != null && !name.isEmpty()) {
                            return name;
                        }
                    }
                }
            } catch (Exception e) {
                Log.w(TAG, "Failed to query display name for URI: " + uri, e);
            }
        }
        String path = uri.getPath();
        if (path != null) {
            int cut = path.lastIndexOf('/');
            if (cut != -1) {
                return path.substring(cut + 1);
            }
            return path;
        }
        return null;
    }

    private boolean extractZipFile(Uri uri, File Target) {
        try (InputStream is = getContentResolver().openInputStream(uri)) {
            if (is == null) {
                Log.e(TAG, "openInputStream returned null for URI: " + uri);
                return false;
            }
            Utilities.extractZipFromInputStream(is, Target);
            return true;
        } catch (Exception e) {
            Log.e(TAG, "Failed to extract zip from URI: " + uri, e);
            return false;
        }
    }

    private int resolveTargetOrientation(ComponentName launcher) {
        try {
            ActivityInfo info = getPackageManager().getActivityInfo(launcher, 0);
            if (info.screenOrientation == ActivityInfo.SCREEN_ORIENTATION_UNSPECIFIED) {
                Log.i(TAG, "Target orientation unspecified; defaulting to landscape");
                return ActivityInfo.SCREEN_ORIENTATION_LANDSCAPE;
            }
            return info.screenOrientation;
        } catch (NameNotFoundException e) {
            return ActivityInfo.SCREEN_ORIENTATION_LANDSCAPE;
        }
    }

    private String resolveTargetGameAbi(String gameLibDir) {
        if (gameLibDir == null || gameLibDir.isEmpty()) {
            return null;
        }

        String abi = new File(gameLibDir).getName();
        if (abi.isEmpty()) {
            return null;
        }

        return abi;
    }
}
