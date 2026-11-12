// Copyright (c) 2026 XtraCube
#include <unistd.h>
#include <fcntl.h>
#include <jni.h>
#include <ctime>
#include <string>
#include <filesystem>
#include <logger.h>
#include <libmain.h>
#include <fusion_config.h>
#include <hooking/il2cpp.h>
#include <hooking/safehook.h>
#include <hooking/allocator.h>
#include <hooking/libunity.h>
#include <dotnet.h>
#include <external/dobby.h>
#include <utilities/java.h>
#include <dlfcn.h>

#define TAG "FusionCore"

namespace fs = std::filesystem;

FusionConfig runtime_config;

// CriCri Mods (11/11/2026, o CriCri: "tão falando que demora muito pra carregar"): as marcas de tempo do lado nativo da abertura.
// Cada linha de <pasta de dados>/abertura-nativo.txt é "nome=milissegundos desde que o celular ligou" (CLOCK_BOOTTIME, o mesmo relógio
// do SystemClock.elapsedRealtime() do Java). tools/Abertura.java apaga o arquivo no JOGAR e soma estas marcas ao resumo da abertura,
// que vai para o log e para o relatório de erro. Só nomes de etapa e números.
static void marcar_abertura(const std::string &pasta, const char *nome)
{
    if (pasta.empty()) return;
    timespec agora{};
    clock_gettime(CLOCK_BOOTTIME, &agora);
    const long long ms = static_cast<long long>(agora.tv_sec) * 1000LL + agora.tv_nsec / 1000000;
    const std::string caminho = pasta + "/abertura-nativo.txt";
    int arquivo = open(caminho.c_str(), O_WRONLY | O_CREAT | O_APPEND | O_CLOEXEC, 0600);
    if (arquivo < 0) return;
    const std::string linha = std::string(nome) + "=" + std::to_string(ms) + "\n";
    if (write(arquivo, linha.c_str(), linha.size()) < 0)
    {
        log(LogLevel::WARN, TAG, "Could not write the opening mark.");
    }
    close(arquivo);
}

// CriCri Mods: fdsan (Android 10+) mata o processo quando um descritor de arquivo é fechado duas vezes. Das 9 quedas vistas nos
// relatórios do celular (08/11), 8 eram isso, vindas do lado gerenciado. Avisa uma vez em vez de matar o jogo.
static void fdsan_so_avisa()
{
    using FdsanSetLevel = int (*)(int);
    if (auto setLevel = reinterpret_cast<FdsanSetLevel>(dlsym(RTLD_DEFAULT, "android_fdsan_set_error_level"))) setLevel(1 /* WARN_ONCE */);
}

static bool execute_fusion_config(const FusionConfig &config)
{
    fusion_print_config(config);

    fs::path gameLibsPath(config.gameLibraryDirectory);
    fs::path codeCache(config.codeCacheDirectory);
    fs::path gamePath = fs::path(config.bepInExDirectory).parent_path();

    fs::path libUnity;
    fs::path patchedLibIl2Cpp;
    if(config.isIl2Cpp2Mono){
        fs::path MonoPath(config.dotnetDirectory);

        fs::path monoLib = MonoPath / "libmonosgen-2.0.so";
        void* handle = dlopen(monoLib.c_str(), RTLD_GLOBAL | RTLD_NOW);
        if (!handle) {
            log_format(LogLevel::ERROR, TAG, "Failed to dlopen libmonosgen-2.0.so: {}", dlerror());
            return false;
        }
        patchedLibIl2Cpp = MonoPath / "libil2cpp.so";
        handle = dlopen(patchedLibIl2Cpp.c_str(), RTLD_GLOBAL | RTLD_NOW);
        if (!handle) {
            log_format(LogLevel::ERROR, TAG, "Failed to dlopen {}: {}", patchedLibIl2Cpp.string(), dlerror());
            return false;
        }
        using SetOverrideDirs = void (*)(const char*, const char*);
        auto set_override_dirs =
                reinterpret_cast<SetOverrideDirs>(
                        dlsym(handle, "il2cpp2mono_set_override_dirs")
                );
        fs::path dllpath = gamePath / "PersistentData/mono";
        fs::path monopath = MonoPath / "mono";
        log_format(LogLevel::INFO, TAG, "setting il2cpp2mono paths: {}, {}", dllpath.c_str(), monopath.c_str());
        set_override_dirs(dllpath.c_str(), monopath.c_str());
    }
    else{
        patchedLibIl2Cpp = codeCache / "libil2cpp.so";
        fs::path libIl2Cpp = gameLibsPath / "libil2cpp.so";
        allocate_setup_injected(libIl2Cpp.c_str(), patchedLibIl2Cpp.c_str(), 1024 * 1024);
    }
    if (config.useOriginalLibUnity)
    {
        libUnity = gameLibsPath / "libunity.so";
    }
    else
    {
        libUnity = codeCache / "libunity.so";
    }

    std::string libUnityPath = libUnity.string();
    try_hook_libunity(libUnityPath, (gameLibsPath / "libunity.so").string());

    std::string patchedPath = patchedLibIl2Cpp.string();
    libmain_set_override_il2cpp_path(patchedPath.c_str());
    libmain_set_override_unity_path(libUnityPath.c_str());

    log(LogLevel::INFO, TAG, "FusionCore bootstrap finished successfully.");
    return true;
}

int il2cpp_init_hook(char *domain_name)
{
    const char *d_name = domain_name ? domain_name : "Unknown Domain Name";
    log_format(LogLevel::INFO, TAG, "il2cpp_init called with domain: {}", d_name);
    il2cpp_destroy_init_hook();

    marcar_abertura(runtime_config.appDataDirectory, "il2cpp_init");
    // call the original il2cpp_init function
    int result = il2cpp_init(domain_name);
    marcar_abertura(runtime_config.appDataDirectory, "il2cpp_pronto");

    if (runtime_config.initialized)
    {
        // setup environment variables
        setenv("BEPINEX_GAME_ASSEMBLY_PATH", libmain_get_override_il2cpp_path(), 1);
        setenv("FUSION_BEPINEX_PATH", runtime_config.bepInExDirectory.c_str(), 1);
        setenv("FUSION_GAME_BINARY", libmain_get_override_il2cpp_path(), 1);
        setenv("FUSION_GAME_DATA_DIR", runtime_config.unityDataDirectory.c_str(), 1);
        setenv("FUSION_APP_DATA_DIR", runtime_config.appDataDirectory.c_str(), 1);
        setenv("FUSION_UNITY_VERSION", runtime_config.unityVersion.c_str(), 1);

        const char *ssl_cert_path = "/apex/com.android.conscrypt/cacerts";
        const char *backup_cert_path = "/system/etc/security/cacerts";
        if (access(ssl_cert_path, R_OK) == 0) {
            setenv("SSL_CERT_DIR", ssl_cert_path, 1);
        } else if (access(backup_cert_path, R_OK) == 0) {
            setenv("SSL_CERT_DIR", backup_cert_path, 1);
        } else {
            log(LogLevel::WARN, TAG, "No readable SSL cert file found; HTTPS requests may fail.");
        }
        const char *ssl_path = getenv("SSL_CERT_DIR");
        const char *safe_ssl_path = ssl_path ? ssl_path : "(null)";
        log_format(LogLevel::INFO, TAG, "Using {} for SSL certificates", safe_ssl_path);

        fs::path bepInExCoreDirectory = fs::path(runtime_config.bepInExDirectory) / "core";

        DotNetConfig dotNetConfig;
        dotNetConfig.runtimeDir = runtime_config.dotnetDirectory;
        dotNetConfig.managedLibsDir = bepInExCoreDirectory.string();
        dotNetConfig.entryPointAssembly = "BepInEx.Unity.IL2CPP";
        dotNetConfig.entryPointType = "BepInEx.Unity.IL2CPP.FusionCoreEntrypoint";
        dotNetConfig.entryPointMethod = "Start";
        dotNetConfig.IsMono = runtime_config.isIl2Cpp2Mono;
        // CriCri Mods: something in the managed side closes a file descriptor twice, sometimes while the game opens. On Android that is a
        // fatal "fdsan" abort (phone logs 08/11: in UnityMain and, with the number reused, in RenderThread). Warn once instead of killing the
        // game; the first warning still goes to logcat so the culprit can be found. Looked up by name: the function needs Android 10.
        // (11/11) agora também é ligado quando a biblioteca carrega (fusion_bootstrap_from_libmain); repetir aqui não custa nada.
        fdsan_so_avisa();

        // CriCri Mods: an unhandled .NET exception ends the process with only "Aborting process." in the crash report (09/11); its text
        // goes to stderr, and Android throws stderr away. Keep it in a file, and keep the file of the last launch that wrote something.
        {
            fs::path erros = fs::path(runtime_config.appDataDirectory) / "dotnet-erro.txt";
            std::error_code erroDoArquivo;
            auto tamanho = fs::file_size(erros, erroDoArquivo);
            if (!erroDoArquivo && tamanho > 0) fs::rename(erros, fs::path(erros.string() + ".anterior"), erroDoArquivo);
            int arquivo = open(erros.c_str(), O_WRONLY | O_CREAT | O_TRUNC | O_CLOEXEC, 0644);
            if (arquivo >= 0)
            {
                dup2(arquivo, STDERR_FILENO);
                close(arquivo);
            }
        }

        // set TMPDIR for MonoMod lib drops
        setenv("TMPDIR", runtime_config.codeCacheDirectory.c_str(), 1);
        // CriCri Mods: MonoMod.Core drops its native helper in $XDG_CACHE_HOME or $HOME/.cache. In an app
        // process HOME is not writable (on a Xiaomi/Android 16 it resolved to /data/.cache), and every Harmony
        // patch failed with "Access to the path '/data/.cache' is denied". Point both at the app's code cache.
        setenv("HOME", runtime_config.codeCacheDirectory.c_str(), 1);
        fs::path xdgCache = fs::path(runtime_config.codeCacheDirectory) / ".cache";
        std::error_code cacheError;
        fs::create_directories(xdgCache, cacheError);
        setenv("XDG_CACHE_HOME", xdgCache.string().c_str(), 1);

        // create AuxFolderPluginList
        auto aux = runtime_config.auxiliaryPluginFolders;
        int size = static_cast<int>(aux.size());
        const char** pointerArray = new const char*[size];

        for (int i = 0; i < size; i++) {
            char* strCopy = new char[aux[i].length() + 1];
            std::copy(aux[i].begin(), aux[i].end(), strCopy);
            strCopy[aux[i].length()] = '\0';

            pointerArray[i] = strCopy;
        }

        AuxPluginFolderList list{};
        list.count = size;
        list.folders = pointerArray;

        // change working directory to fusion's scoped data directory
        chdir(runtime_config.appDataDirectory.c_str());
        dotnet_execute_assembly(dotNetConfig, &list);
        marcar_abertura(runtime_config.appDataDirectory, "bepinex_pronto");
    }
    else
    {
        log(LogLevel::WARN, TAG, "FusionConfig not initialized. Skipping modloader initialization.");
    }

    log_format(LogLevel::INFO, TAG, "il2cpp_init returned: {}", result);
    return result;
}

extern "C" [[maybe_unused]] bool fusion_bootstrap_from_libmain(JNIEnv *env)
{
    (void) env;

    log(LogLevel::INFO, TAG, "FusionCore bootstrap starting...");
    // CriCri Mods (11/11): antes só era ligado dentro do il2cpp_init; uma dupla de close() entre a carga das bibliotecas e esse ponto
    // ainda derrubava o processo.
    fdsan_so_avisa();
    jobject javaConfig = get_fusion_config(env);
    FusionConfig config = fusion_parse_config(env, javaConfig);
    env->DeleteLocalRef(javaConfig);
    marcar_abertura(config.appDataDirectory, "biblioteca");

    log(LogLevel::INFO, TAG, "Executing Fusion bootstrap from libmain namespace...");
    if (!execute_fusion_config(config)) {
        log(LogLevel::ERROR, TAG, "Failed to execute Fusion bootstrap.");
        return false;
    }

    runtime_config = config;
    auto il2cpp_path = libmain_get_override_il2cpp_path();
    if (!il2cpp_initialize(il2cpp_path))
    {
        log_format(LogLevel::ERROR, TAG, "Failed to initialize il2cpp with path: {}", il2cpp_path);
        return false;
    }
    if(runtime_config.isIl2Cpp2Mono){
        setLoadingState(false);
        log(LogLevel::WARN, TAG, "BepInEx is currently disabled in il2cpp2mono.");
        return true;
    }
    // Pool placement and library bounds must be trustworthy before any hooking.
    const uintptr_t pool_base = get_injected_pool_base();
    const uintptr_t il2cpp_base = il2cpp_get_library_base();
    if (pool_base == 0 || il2cpp_base == 0 || pool_base <= il2cpp_base)
    {
        log_format(LogLevel::ERROR, TAG,
                   "Invalid pool/library base relationship! pool=0x{:X}, il2cpp=0x{:X}",
                   pool_base, il2cpp_base);
        return false;
    }

    // plain integer subtraction (pool sits directly above the library image)
    const size_t library_size = pool_base - il2cpp_base;
    constexpr size_t kMaxPlausibleLibrarySize = 512ull * 1024 * 1024;
    if (library_size > kMaxPlausibleLibrarySize)
    {
        log_format(LogLevel::ERROR, TAG,
                   "Implausible library size 0x{:X}; refusing to initialize SafeHook",
                   library_size);
        return false;
    }

    if (!safehook_initialize(il2cpp_get_handle(), il2cpp_base, library_size, allocate_injected))
    {
        log(LogLevel::ERROR, TAG, "Failed to initialize SafeHook");
        return false;
    }

    log(LogLevel::INFO, TAG, "Installing il2cpp hooks...");
    il2cpp_install_init_hook(il2cpp_init_hook);
    marcar_abertura(runtime_config.appDataDirectory, "motor_pronto");
    log(LogLevel::INFO, TAG, "il2cpp hooks installed successfully!");
    log(LogLevel::INFO, TAG, "FusionCore bootstrap finished successfully.");
    return true;
}

