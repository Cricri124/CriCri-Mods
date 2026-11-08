package dev.allofus.fusioncore.tools;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNull;
import static org.junit.Assert.assertTrue;

import android.content.Context;

import androidx.test.core.app.ApplicationProvider;
import androidx.test.ext.junit.runners.AndroidJUnit4;

import org.junit.After;
import org.junit.Before;
import org.junit.Test;
import org.junit.runner.RunWith;
import org.robolectric.annotation.Config;

import java.nio.charset.StandardCharsets;
import java.nio.file.Files;

import javax.crypto.KeyGenerator;

/** CriCri Mods: the CRICRI BR mod key is checked when pasted, kept encrypted (never in clear on disk) and erasable. */
@RunWith(AndroidJUnit4.class)
@Config(sdk = 35)
public class ChaveBrTest {
    private Context ctx;

    @Before
    public void preparar() throws Exception {
        ctx = ApplicationProvider.getApplicationContext();
        KeyGenerator g = KeyGenerator.getInstance("AES");
        g.init(256);
        ChaveBr.chaveParaTeste = g.generateKey();   // stands in for the Android Keystore
    }

    @After
    public void limpar() {
        ChaveBr.chaveParaTeste = null;
    }

    @Test
    public void colado_soAceitaOQuePodeSerChave() {
        assertEquals("cbr_ABCdef123456", ChaveBr.limpar("  cbr_ABCdef123456\n"));
        assertNull(ChaveBr.limpar(null));
        assertNull(ChaveBr.limpar("curta"));
        assertNull(ChaveBr.limpar("tem espaco no meio 123456"));
        assertNull(ChaveBr.limpar("x".repeat(65)));
        assertNull(ChaveBr.limpar("chave-com-acento-é-1234"));
    }

    @Test
    public void guardada_cifrada_eApagavel() throws Exception {
        assertFalse(ChaveBr.temChave(ctx));
        ChaveBr.salvar(ctx, "cbr_SegredoMuitoSecreto42");
        assertTrue(ChaveBr.temChave(ctx));
        byte[] noDisco = Files.readAllBytes(ChaveBr.arquivo(ctx).toPath());
        assertFalse("nunca em claro no disco",
                new String(noDisco, StandardCharsets.ISO_8859_1).contains("SegredoMuitoSecreto"));
        assertEquals("cbr_SegredoMuitoSecreto42", ChaveBr.ler(ctx));
        ChaveBr.apagar(ctx);
        assertFalse(ChaveBr.temChave(ctx));
        assertNull(ChaveBr.ler(ctx));
    }

    @Test
    public void arquivoEstragado_eApagado() throws Exception {
        ChaveBr.salvar(ctx, "cbr_SegredoMuitoSecreto42");
        byte[] b = Files.readAllBytes(ChaveBr.arquivo(ctx).toPath());
        b[b.length - 1] ^= 1;
        Files.write(ChaveBr.arquivo(ctx).toPath(), b);
        assertNull(ChaveBr.ler(ctx));
        assertFalse(ChaveBr.temChave(ctx));
    }
}
