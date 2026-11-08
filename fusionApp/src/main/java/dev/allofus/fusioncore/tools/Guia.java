package dev.allofus.fusioncore.tools;

import android.annotation.SuppressLint;
import android.app.Activity;
import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.Rect;
import android.graphics.RectF;
import android.graphics.drawable.GradientDrawable;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.MotionEvent;
import android.view.View;
import android.view.ViewGroup;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.TextView;

import androidx.core.content.ContextCompat;

import com.google.android.material.button.MaterialButton;

import java.util.List;

import dev.allofus.fusioncore.R;

/**
 * CriCri Mods: first-run guide. Dims the screen, leaves a rounded window over one part of the home screen at a
 * time and explains it in a small panel ("1 de 4", title, one or two lines, Pular / Próximo). While it is up,
 * touches don't reach the screen below (nobody launches the game by accident in the middle of it).
 */
public final class Guia {
    private static final String PREFS = "cricri_guia";
    private static final String VISTO = "visto_v1";

    public static final class Passo {
        final View alvo;
        final int titulo;
        final int texto;

        public Passo(View alvo, int titulo, int texto) {
            this.alvo = alvo;
            this.titulo = titulo;
            this.texto = texto;
        }
    }

    private Guia() {}

    public static boolean jaVisto(Context context) {
        return context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).getBoolean(VISTO, false);
    }

    public static void marcarVisto(Context context) {
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit().putBoolean(VISTO, true).apply();
    }

    /** Shows the guide over the activity; when it ends (finished or skipped) it is marked as seen. */
    public static void mostrar(Activity activity, List<Passo> passos) {
        ViewGroup decor = (ViewGroup) activity.getWindow().getDecorView();
        if (decor.findViewWithTag(GuiaView.TAG) != null || passos.isEmpty()) return;
        GuiaView view = new GuiaView(activity, passos, () -> marcarVisto(activity));
        view.setTag(GuiaView.TAG);
        decor.addView(view, new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        view.irPara(0);
    }

    /** True while the guide is on screen (tests use it). */
    public static boolean aberto(Activity activity) {
        return ((ViewGroup) activity.getWindow().getDecorView()).findViewWithTag(GuiaView.TAG) != null;
    }

    @SuppressLint("ViewConstructor")
    static final class GuiaView extends FrameLayout {
        static final String TAG = "cricri_guia";

        private final List<Passo> passos;
        private final Runnable aoTerminar;
        private final Paint escuro = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final Paint borda = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final Path furo = new Path();
        private final RectF janela = new RectF();
        private final LinearLayout painel;
        private final TextView contador, titulo, texto;
        private final MaterialButton proximo;
        private int atual;

        GuiaView(Activity activity, List<Passo> passos, Runnable aoTerminar) {
            super(activity);
            this.passos = passos;
            this.aoTerminar = aoTerminar;
            setWillNotDraw(false);
            setClickable(true);
            setFocusable(true);
            escuro.setColor(Color.argb(200, 6, 4, 8));
            borda.setStyle(Paint.Style.STROKE);
            borda.setStrokeWidth(dp(2));
            borda.setColor(ContextCompat.getColor(activity, R.color.cricri_accent));

            painel = new LinearLayout(activity);
            painel.setOrientation(LinearLayout.VERTICAL);
            int p = dp(18);
            painel.setPadding(p, dp(16), p, dp(10));
            GradientDrawable fundo = new GradientDrawable();
            fundo.setColor(ContextCompat.getColor(activity, R.color.cricri_barra));
            fundo.setCornerRadius(dp(18));
            fundo.setStroke(dp(1), ContextCompat.getColor(activity, R.color.cricri_linha));
            painel.setBackground(fundo);
            painel.setElevation(dp(6));

            contador = textoComo(activity, 12, false);
            contador.setTextColor(ContextCompat.getColor(activity, R.color.cricri_accent));
            titulo = textoComo(activity, 18, true);
            titulo.setPadding(0, dp(4), 0, 0);
            texto = textoComo(activity, 14, false);
            texto.setPadding(0, dp(6), 0, dp(8));
            texto.setLineSpacing(0, 1.15f);
            painel.addView(contador);
            painel.addView(titulo);
            painel.addView(texto);

            LinearLayout botoes = new LinearLayout(activity);
            botoes.setOrientation(LinearLayout.HORIZONTAL);
            botoes.setGravity(Gravity.END | Gravity.CENTER_VERTICAL);
            // a quiet text button next to the filled "Próximo"
            MaterialButton pular = new MaterialButton(activity);
            pular.setBackgroundTintList(android.content.res.ColorStateList.valueOf(Color.TRANSPARENT));
            pular.setTextColor(Color.argb(200, 255, 255, 255));
            pular.setElevation(0);
            pular.setStateListAnimator(null);
            pular.setText(R.string.guia_pular);
            pular.setOnClickListener(v -> terminar());
            proximo = new MaterialButton(activity);
            proximo.setTextColor(Color.WHITE);
            proximo.setBackgroundTintList(ContextCompat.getColorStateList(activity, R.color.cricri_accent));
            proximo.setOnClickListener(v -> {
                if (atual + 1 < passos.size()) irPara(atual + 1); else terminar();
            });
            botoes.addView(pular);
            LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
            lp.setMarginStart(dp(8));
            botoes.addView(proximo, lp);
            painel.addView(botoes);

            FrameLayout.LayoutParams plp = new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
            plp.leftMargin = plp.rightMargin = dp(16);
            addView(painel, plp);
        }

        private TextView textoComo(Context c, int sp, boolean negrito) {
            TextView t = new TextView(c);
            t.setTextSize(TypedValue.COMPLEX_UNIT_SP, sp);
            t.setTextColor(Color.WHITE);
            if (negrito) t.setTypeface(t.getTypeface(), android.graphics.Typeface.BOLD);
            return t;
        }

        private int dp(float v) {
            return Math.round(v * getResources().getDisplayMetrics().density);
        }

        void irPara(int indice) {
            atual = indice;
            Passo passo = passos.get(indice);
            contador.setText(getContext().getString(R.string.guia_contador_format, indice + 1, passos.size()));
            titulo.setText(passo.titulo);
            texto.setText(passo.texto);
            proximo.setText(indice + 1 < passos.size() ? R.string.guia_proximo : R.string.guia_entendi);
            // bring the highlighted part on screen (it may be below the fold), then measure where it ended up
            Rect r = new Rect(0, 0, passo.alvo.getWidth(), passo.alvo.getHeight());
            passo.alvo.requestRectangleOnScreen(r, true);
            post(this::posicionar);
            postDelayed(this::posicionar, 250);
        }

        private void posicionar() {
            View alvo = passos.get(atual).alvo;
            int[] eu = new int[2], ele = new int[2];
            getLocationOnScreen(eu);
            alvo.getLocationOnScreen(ele);
            float folga = dp(8);
            janela.set(ele[0] - eu[0] - folga, ele[1] - eu[1] - folga,
                    ele[0] - eu[0] + alvo.getWidth() + folga, ele[1] - eu[1] + alvo.getHeight() + folga);
            furo.reset();
            furo.setFillType(Path.FillType.EVEN_ODD);
            furo.addRect(0, 0, getWidth(), getHeight(), Path.Direction.CW);
            furo.addRoundRect(janela, dp(16), dp(16), Path.Direction.CW);

            // the panel goes below the window when it fits, otherwise above
            painel.measure(MeasureSpec.makeMeasureSpec(getWidth() - dp(32), MeasureSpec.EXACTLY), MeasureSpec.UNSPECIFIED);
            int altura = painel.getMeasuredHeight();
            FrameLayout.LayoutParams lp = (FrameLayout.LayoutParams) painel.getLayoutParams();
            float abaixo = janela.bottom + dp(14);
            lp.topMargin = abaixo + altura < getHeight() - dp(24)
                    ? Math.round(abaixo)
                    : Math.max(dp(24), Math.round(janela.top - dp(14) - altura));
            painel.setLayoutParams(lp);
            invalidate();
        }

        private void terminar() {
            aoTerminar.run();
            ViewGroup pai = (ViewGroup) getParent();
            if (pai != null) pai.removeView(this);
        }

        @Override
        protected void onDraw(Canvas canvas) {
            super.onDraw(canvas);
            canvas.drawPath(furo, escuro);
            canvas.drawRoundRect(janela, dp(16), dp(16), borda);
        }

        @SuppressLint("ClickableViewAccessibility")
        @Override
        public boolean onTouchEvent(MotionEvent event) {
            return true;   // nothing below reacts while the guide is up
        }
    }
}
