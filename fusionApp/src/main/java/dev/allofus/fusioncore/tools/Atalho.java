package dev.allofus.fusioncore.tools;

import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Paint;
import android.graphics.RectF;
import android.graphics.Typeface;
import android.graphics.drawable.Drawable;

import androidx.core.content.pm.ShortcutInfoCompat;
import androidx.core.content.pm.ShortcutManagerCompat;
import androidx.core.graphics.drawable.IconCompat;

import dev.allofus.fusioncore.BuildConfig;
import dev.allofus.fusioncore.EntradaRapidaActivity;
import dev.allofus.fusioncore.R;

/**
 * CriCri Mods: the habit problem (the CriCri, 08/11: "eu sempre tenho o problema de confundir e abrir o among us real
 * por causa de costume... e sei que mais pessoas vão ter esse problema"). Android lets no app take over another app's
 * icon, so the finger gets the same target instead: a home-screen shortcut drawn from the Among Us installed on this
 * phone (read at run time; the app ships none of Innersloth's art) with an orange MOD badge, opening the game WITH
 * the mods (the "Jogar com mods" flow). The person puts it where the plain game's icon was.
 */
public final class Atalho {
    public static final String ID = "among_us_mods";
    private static final String PREFS = "cricri_atalho";

    private Atalho() {}

    public static boolean suportado(Context context) {
        return ShortcutManagerCompat.isRequestPinShortcutSupported(context);
    }

    /** Asks the launcher to pin it (the system shows its own "add to home screen" confirmation). */
    public static boolean pedir(Context context) {
        Intent abrir = new Intent(context, EntradaRapidaActivity.class)
                .setAction(Intent.ACTION_MAIN)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        ShortcutInfoCompat atalho = new ShortcutInfoCompat.Builder(context, ID)
                .setShortLabel(context.getString(R.string.atalho_nome))
                .setLongLabel(context.getString(R.string.atalho_nome_longo))
                .setIcon(IconCompat.createWithBitmap(icone(context)))
                .setIntent(abrir)
                .build();
        marcarOferecido(context);
        try {
            return ShortcutManagerCompat.requestPinShortcut(context, atalho, null);
        } catch (Exception e) {
            return false;
        }
    }

    /** The game's own icon from this phone, with the MOD badge (or the app's play icon if the game is missing). */
    static Bitmap icone(Context context) {
        int lado = 192;
        Bitmap b = Bitmap.createBitmap(lado, lado, Bitmap.Config.ARGB_8888);
        Canvas c = new Canvas(b);
        Drawable jogo;
        try {
            jogo = context.getPackageManager().getApplicationIcon(BuildConfig.TARGET_PACKAGE);
        } catch (PackageManager.NameNotFoundException e) {
            jogo = androidx.core.content.ContextCompat.getDrawable(context, R.mipmap.jogar_icon);
        }
        if (jogo != null) {
            jogo.setBounds(0, 0, lado, lado);
            jogo.draw(c);
        }
        // the badge: an orange pill with "MOD" across the bottom right, a dark outline so it reads on any icon
        Paint fundo = new Paint(Paint.ANTI_ALIAS_FLAG);
        RectF pilula = new RectF(lado * 0.36f, lado * 0.66f, lado * 0.98f, lado * 0.94f);
        float raio = pilula.height() / 2f;
        fundo.setColor(Color.parseColor("#05070F"));
        c.drawRoundRect(new RectF(pilula.left - 5, pilula.top - 5, pilula.right + 5, pilula.bottom + 5), raio + 5, raio + 5, fundo);
        fundo.setColor(Color.parseColor("#FF5A36"));
        c.drawRoundRect(pilula, raio, raio, fundo);
        Paint letra = new Paint(Paint.ANTI_ALIAS_FLAG);
        letra.setColor(Color.WHITE);
        letra.setTypeface(Typeface.create(Typeface.DEFAULT, Typeface.BOLD));
        letra.setTextSize(pilula.height() * 0.62f);
        letra.setTextAlign(Paint.Align.CENTER);
        Paint.FontMetrics fm = letra.getFontMetrics();
        c.drawText("MOD", pilula.centerX(), pilula.centerY() - (fm.ascent + fm.descent) / 2f, letra);
        return b;
    }

    /** The first-run tip is shown once. */
    public static boolean jaOferecido(Context context) {
        return context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).getBoolean("oferecido", false);
    }

    public static void marcarOferecido(Context context) {
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit().putBoolean("oferecido", true).apply();
    }
}
