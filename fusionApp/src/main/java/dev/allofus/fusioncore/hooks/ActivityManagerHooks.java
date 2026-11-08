package dev.allofus.fusioncore.hooks;

import android.content.Context;
import android.os.Build;
import android.util.Log;

import java.lang.reflect.Field;
import java.lang.reflect.InvocationTargetException;
import java.lang.reflect.Method;
import java.lang.reflect.Proxy;

public final class ActivityManagerHooks {
    private ActivityManagerHooks() {}

    public static void install(Context fusionContext, Context gameContext)
            throws ReflectiveOperationException {
        Class<?> manager = Class.forName(Build.VERSION.SDK_INT >= 26
                ? "android.app.ActivityManager" : "android.app.ActivityManagerNative");
        Field singletonField = manager.getDeclaredField(Build.VERSION.SDK_INT >= 26
                ? "IActivityManagerSingleton" : "gDefault");
        singletonField.setAccessible(true);
        Object singleton = singletonField.get(null);
        Class<?> singletonClass = Class.forName("android.util.Singleton");
        Method get = singletonClass.getDeclaredMethod("get");
        get.setAccessible(true);
        Object original = get.invoke(singleton);
        Class<?> service = Class.forName("android.app.IActivityManager");
        String hostPackage = fusionContext.getPackageName();
        String gamePackage = gameContext.getPackageName();
        Object proxy = Proxy.newProxyInstance(ActivityManagerHooks.class.getClassLoader(),
                new Class<?>[] {service}, (object, method, args) -> {
                    String name = method.getName();
                    if ((name.equals("getIntentSender") || name.equals("getIntentSenderWithFeature"))
                            && args != null && args.length > 1 && gamePackage.equals(args[1])) {
                        args = args.clone();
                        args[1] = hostPackage;
                        Log.d("ActivityManagerHooks", "PendingIntent owner: " + gamePackage
                                + " -> " + hostPackage);
                    }
                    if (name.equals("getHistoricalProcessExitReasons")
                            && args != null && args.length > 0 && gamePackage.equals(args[0])) {
                        args = args.clone();
                        args[0] = hostPackage;
                        Log.d("ActivityManagerHooks", "Process exit history owner: "
                                + gamePackage + " -> " + hostPackage);
                    }
                    // CriCri Mods (08/11, the game stuck at 0% on the opening screen): the game's Play Asset Delivery
                    // asks the Play Store's AssetModuleService for its asset packs. From this app the Play Store
                    // never serves them; usually the binding fails at once and the game reads the installed split
                    // APK itself. But with the Play Store process killed (low memory), the request STARTS it and it
                    // never answers: "Waiting to bind to the service" forever, the addressables catalog never loads.
                    // Refused here, it fails at once every time, like in the launches that worked.
                    if (pedidoDePacotesDaPlayStore(name, args)) {
                        Log.i("ActivityManagerHooks", "Play Store asset pack service refused (the game reads its installed packs)");
                        Class<?> tipo = method.getReturnType();
                        if (tipo == boolean.class) return false;
                        if (tipo == int.class) return 0;
                        return null;
                    }
                    try {
                        return method.invoke(original, args);
                    } catch (InvocationTargetException error) {
                        throw error.getCause();
                    }
                });
        Field instance = singletonClass.getDeclaredField("mInstance");
        instance.setAccessible(true);
        instance.set(singleton, proxy);
    }

    /** A service binding aimed at the Play Store's asset module service (Play Asset Delivery). */
    static boolean pedidoDePacotesDaPlayStore(String metodo, Object[] args) {
        if (args == null || !(metodo.startsWith("bindService") || metodo.equals("bindIsolatedService"))) return false;
        for (Object a : args) {
            if (!(a instanceof android.content.Intent)) continue;
            android.content.Intent i = (android.content.Intent) a;
            String acao = i.getAction();
            android.content.ComponentName c = i.getComponent();
            if (acao != null && acao.toLowerCase(java.util.Locale.ROOT).contains("assetmoduleservice")) return true;
            if (c != null && c.getClassName() != null && c.getClassName().toLowerCase(java.util.Locale.ROOT).contains("assetmoduleservice")) return true;
        }
        return false;
    }
}