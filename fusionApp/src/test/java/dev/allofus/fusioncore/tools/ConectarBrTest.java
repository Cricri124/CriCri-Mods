package dev.allofus.fusioncore.tools;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertNotNull;
import static org.junit.Assert.assertNull;

import androidx.test.ext.junit.runners.AndroidJUnit4;

import org.junit.Test;
import org.junit.runner.RunWith;
import org.robolectric.annotation.Config;

@RunWith(AndroidJUnit4.class)
@Config(sdk = 35)
public class ConectarBrTest {
    private static final String BASE = "https://cricribr.com.br";
    private static final String ID = "abcdefghijklmnopqrstuvwxyz0123456789";

    private static String corpo(String id, String url) {
        return "{\"id\":\"" + id + "\",\"url\":\"" + url + "\",\"expiraEmSeg\":600,\"intervaloSeg\":3}";
    }

    @Test
    public void aceitaOLinkDoProprioSite() {
        ConectarBr.Inicio i = ConectarBr.lerInicio(corpo(ID, BASE + "/conta/ligar?c=AB12"), BASE);
        assertNotNull(i);
        assertEquals(600, i.expiraSeg);
        assertEquals(3, i.intervaloSeg);
    }

    @Test
    public void recusaLinkDeOutroLugar() {
        assertNull(ConectarBr.lerInicio(corpo(ID, "https://golpe.example/conta/ligar?c=AB12"), BASE));
        assertNull(ConectarBr.lerInicio(corpo(ID, BASE + ".golpe.example/conta/ligar?c=AB12"), BASE));
        assertNull(ConectarBr.lerInicio(corpo(ID, BASE + "/outra-pagina?c=AB12"), BASE));
    }

    @Test
    public void recusaIdCurtoOuRespostaQuebrada() {
        assertNull(ConectarBr.lerInicio(corpo("curto", BASE + "/conta/ligar?c=AB12"), BASE));
        assertNull(ConectarBr.lerInicio("nao e json", BASE));
        assertNull(ConectarBr.lerInicio("{}", BASE));
    }

    @Test
    public void intervaloETempoFicamDentroDosLimites() {
        String c = "{\"id\":\"" + ID + "\",\"url\":\"" + BASE + "/conta/ligar?c=A\",\"expiraEmSeg\":99999,\"intervaloSeg\":0}";
        ConectarBr.Inicio i = ConectarBr.lerInicio(c, BASE);
        assertNotNull(i);
        assertEquals(900, i.expiraSeg);
        assertEquals(2, i.intervaloSeg);
    }
}
