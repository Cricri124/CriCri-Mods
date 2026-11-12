package dev.allofus.fusioncore.tools;

import android.content.Context;
import android.content.pm.ApplicationInfo;
import android.content.pm.PackageManager;
import android.os.Build;

import java.io.File;
import java.util.Locale;

/**
 * CriCri Mods (11/11, ele: "erros de compatibilidade, tão reclamando muito"): a conferência do aparelho ANTES de preparar qualquer coisa.
 * Um celular que não roda o jogo com mods recebia um aviso em inglês que some em 3 segundos ("Failed to create package context...") ou só via
 * o app fechar. Agora cada caso conhecido tem UMA frase que a pessoa entende, e o motivo vai para o relatório de erro.
 *
 * O que se sabe (cricriqol\docs\CRICRI-MODS-32BITS.md): o Among Us atual é só 64 bits (arm64-v8a) e pede Android 8; o app instala no Android 7.
 * Os textos ficam aqui (e não no strings.xml) porque só existem em português e para não disputar o arquivo das telas; quem cuida das telas pode
 * mudar para recurso quando quiser.
 */
public final class Aparelho {
    public static final String SEM_JOGO = "sem_jogo", ANDROID_ANTIGO = "android_antigo", SEM_64_BITS = "sem_64_bits",
            JOGO_32_BITS = "jogo_32_bits", JOGO_OUTRO_PROCESSADOR = "jogo_outro_processador", JOGO_SEM_BIBLIOTECAS = "jogo_sem_bibliotecas",
            SEM_ESPACO = "sem_espaco";

    /** Android 8.0: o mínimo do Among Us desde a versão 2026.6.5. */
    static final int ANDROID_DO_JOGO = 26;
    /** Folga que sobra depois de preparar tudo: o jogo ainda grava configuração, replays e o interop (uns 50 MB). */
    static final long FOLGA = 150L * 1024 * 1024;
    /** Margem quando só falta regravar por cima a diferença de tamanho da biblioteca do jogo (depois de o Among Us atualizar). */
    static final long MARGEM_DE_REGRAVAR = 24L * 1024 * 1024;
    /** O que os dois zips do app ocupam descompactados (BepInEx 8 MB + .NET 67 MB). */
    static final long PECAS_DESCOMPACTADAS = 80L * 1024 * 1024;

    private Aparelho() {}

    public static final class Problema {
        public final String codigo;
        /** Uma frase, em português, pronta para a tela. */
        public final String mensagem;

        Problema(String codigo, String mensagem) {
            this.codigo = codigo;
            this.mensagem = mensagem;
        }

        @Override public String toString() {
            return codigo;
        }
    }

    /** null = pode abrir. pastaDoMotor = a pasta interna onde ficam a cópia da libil2cpp e a libunity (app_fusion_runtime/<jogo>). */
    public static Problema conferir(Context context, String pacoteDoJogo, File pastaDoMotor) {
        ApplicationInfo jogo;
        try {
            jogo = context.getPackageManager().getApplicationInfo(pacoteDoJogo, 0);
        } catch (PackageManager.NameNotFoundException e) {
            return conferirPuro(Build.VERSION.SDK_INT, Build.SUPPORTED_64_BIT_ABIS, null, false, false, 0, 0);
        }
        String libs = jogo.nativeLibraryDir;
        File pastaLibs = libs == null ? null : new File(libs);
        File il2cpp = pastaLibs == null ? null : new File(pastaLibs, "libil2cpp.so");
        boolean temIl2cpp = il2cpp != null && il2cpp.isFile();
        boolean temUnity = pastaLibs != null && new File(pastaLibs, "libunity.so").isFile();

        // espaço interno que ESTA abertura ainda vai gastar: a cópia da libil2cpp (uma vez por versão do jogo) e os zips (uma vez por versão do app)
        long preciso = 0;
        if (temIl2cpp) {
            File copia = new File(pastaDoMotor, "libil2cpp.so");
            // (11/11, noite) A cópia nova é gravada POR CIMA da antiga (o nativo abre com O_TRUNC: o espaço da antiga volta antes de a nova
            // ser escrita). Depois de o Among Us atualizar só falta a DIFERENÇA de tamanho, não a biblioteca inteira: pedir a inteira barrava
            // com "Falta espaço" quem tinha o celular quase cheio e abria o jogo sem problema antes desta conferência existir.
            if (!copia.isFile()) preciso += il2cpp.length();
            else if (copia.length() < il2cpp.length()) preciso += il2cpp.length() - copia.length();
        }
        boolean primeiraVez = temIl2cpp && !new File(pastaDoMotor, "libil2cpp.so").isFile();
        File noBackup = context.getNoBackupFilesDir();
        if (!new File(noBackup, "extraido-dotnet-arm64.zip.txt").isFile()) {
            preciso += PECAS_DESCOMPACTADAS;
            primeiraVez = true;
        }
        long livre = context.getFilesDir().getUsableSpace();
        // (11/11, revisão) A folga de 150 MB é para a PRIMEIRA vez (o interop ainda vai ser gerado, as peças descompactadas). Depois de
        // o Among Us atualizar, quando só falta a diferença de tamanho da biblioteca, pedir a diferença mais 150 MB barrava quem tinha
        // 100 MB livres e abria o jogo sem problema na versão anterior do app. Para regravar por cima basta a diferença e uma margem.
        if (!primeiraVez && preciso > 0 && livre >= preciso + MARGEM_DE_REGRAVAR) preciso = 0;

        return conferirPuro(Build.VERSION.SDK_INT, Build.SUPPORTED_64_BIT_ABIS, pastaLibs == null ? "" : pastaLibs.getName(),
                temIl2cpp, temUnity, preciso, livre);
    }

    /**
     * As regras, sem tocar no aparelho (para os testes). pastaDasLibs = o nome da pasta de bibliotecas do jogo ("arm64", "arm", "x86_64"...),
     * null = jogo não instalado. A ordem importa: a primeira causa é a que a pessoa consegue resolver.
     */
    static Problema conferirPuro(int android, String[] abis64, String pastaDasLibs, boolean temIl2cpp, boolean temUnity,
                                 long precisoBytes, long livreBytes) {
        boolean arm64 = false;
        if (abis64 != null) for (String abi : abis64) if ("arm64-v8a".equals(abi)) arm64 = true;
        if (!arm64) {
            return new Problema(SEM_64_BITS, "Este celular não é de 64 bits (ARM). O Among Us atual só existe para 64 bits, então os mods não rodam aqui.");
        }
        if (android < ANDROID_DO_JOGO) {
            return new Problema(ANDROID_ANTIGO, "O Among Us atual pede Android 8 ou mais novo. Neste Android os mods não abrem.");
        }
        if (pastaDasLibs == null) {
            return new Problema(SEM_JOGO, "O Among Us não está instalado. Instale pela Play Store e toque em JOGAR de novo.");
        }
        String pasta = pastaDasLibs.toLowerCase(Locale.ROOT);
        if (pasta.equals("arm") || pasta.equals("armeabi-v7a") || pasta.equals("armeabi")) {
            return new Problema(JOGO_32_BITS, "Este Among Us é a versão antiga de 32 bits. Atualize o Among Us pela Play Store.");
        }
        if (pasta.startsWith("x86") || pasta.startsWith("mips") || pasta.startsWith("riscv")) {
            return new Problema(JOGO_OUTRO_PROCESSADOR, "O Among Us deste aparelho não é o de celular ARM de 64 bits. Os mods só rodam nele.");
        }
        if (!temIl2cpp || !temUnity) {
            return new Problema(JOGO_SEM_BIBLIOTECAS, "Não achei as peças do Among Us neste celular. Instale o Among Us pela Play Store (não por APK de outro site).");
        }
        if (precisoBytes > 0 && livreBytes < precisoBytes + FOLGA) {
            return new Problema(SEM_ESPACO, faltaEspaco(precisoBytes + FOLGA - livreBytes));
        }
        return null;
    }

    /** A frase de falta de espaço, com o tamanho arredondado para cima em pedaços de 50 MB (um número que dá para lembrar). */
    public static String faltaEspaco(long faltamBytes) {
        long mb = Math.max(1, (faltamBytes + 1024 * 1024 - 1) / (1024 * 1024));
        long redondo = ((mb + 49) / 50) * 50;
        return "Falta espaço no celular. Libere uns " + redondo + " MB e toque em JOGAR de novo.";
    }
}
