package dev.allofus.fusioncore;

import android.util.Log;
import android.view.View;
import android.widget.TextView;

import java.util.Locale;

/**
 * CriCri Mods, app-telas (11/11, ele: "menos texto" e "tão falando que demora muito pra carregar"): a frase que a pessoa lê por cima do
 * jogo enquanto os mods carregam (res/layout/loading_view.xml). É a espera mais longa da abertura (uns 10 s em toda abertura, medidos em
 * 08/11; bem mais na primeira vez) e o texto vinha direto do BepInEx, em inglês de programador, com o relógio voltando para 00:00 a cada
 * troca: "Running Preloader...", "Initialized Chainloader...", "Running Chainloader...", "Downloading Unity assemblies...",
 * "Running Cpp2IL...", "Generating IL2CPP Interop files...". São os seis que o BepInEx.Unity.IL2CPP.dll do app manda (lista tirada do IL
 * dele, do BepInEx-arm64.zip). Aqui viram três frases em português; texto que não conheço vira o "Carregando…" de sempre. O texto original
 * continua no log, para quem for ler um relatório.
 */
public final class EsperaDoJogo {
    private static final String TAG = "EsperaDoJogo";

    private EsperaDoJogo() {}

    /** A frase (recurso) para um texto do BepInEx. Por palavra, não pelo texto inteiro: uma versão nova do BepInEx muda a pontuação. */
    static int recurso(String original) {
        String t = original == null ? "" : original.toLowerCase(Locale.ROOT);
        // gerar o interop: só na primeira abertura e depois de o Among Us atualizar; é a parte que demora de verdade
        if (t.contains("cpp2il") || t.contains("interop")) return R.string.espera_so_desta_vez;
        if (t.contains("download")) return R.string.bootstrap_status_downloading_libunity;
        if (t.contains("preloader") || t.contains("chainloader")) return R.string.espera_mods;
        return R.string.loading;
    }

    /**
     * Para o tools/JniBridge: a frase em português para pôr no aviso, ou null quando ela já é a que está na tela (duas etapas do BepInEx
     * que para a pessoa são uma só: o relógio segue contando em vez de voltar para 00:00).
     */
    public static String frase(TextView aviso, String original) {
        Log.i(TAG, "BepInEx: " + original);
        // os textos saem do contexto do AVISO, que é o do app; a tela onde ele está é a do jogo, com os recursos do jogo
        String frase = aviso.getContext().getString(recurso(original));
        if (frase.equals(aviso.getTag())) return null;
        aviso.setTag(frase);
        return frase;
    }

    /**
     * Para o tools/JniBridge: o aviso saiu da tela e o relógio dele foi parado. Se ele voltar, a próxima frase entra como nova (com um
     * relógio novo), mesmo sendo a mesma de antes.
     */
    public static void sumiu(View caixa) {
        View aviso = caixa == null ? null : caixa.findViewById(R.id.loadingText);
        if (aviso != null) aviso.setTag(null);
    }
}
