#if CRICRI_PC
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace CriCriMods.Servidor
{
    // PC only (built with -p:DefineConstants=CRICRI_PC; the phone build never sees this file). On the phone the app keeps the
    // CRICRI BR key; on the PC there is no app, so the key lives here: DPAPI of the current Windows user (another user of the
    // PC, or the file copied to another PC, cannot open it), in %LOCALAPPDATA%\CriCri Mods, outside the game folder so it can
    // never travel in a zip of the mods or of the logs.
    // Plain Win32 on purpose: System.Security.Cryptography.ProtectedData is not in the runtime BepInEx ships, IMGUI's
    // GUIUtility would need another reference, and this class can be tested without the game.
    // Rules: the key is never logged and never shown; nothing here throws at the caller.
    internal static class PcCofre
    {
        const string NomeDoArquivo = "chave-cricri-br.dpapi";
        const int CryptProtectUiForbidden = 1;
        const uint CfUnicodeText = 13;
        const int MaxCaracteresColados = 4096;

        // Not a secret: it only keeps this blob from being opened by another program that calls DPAPI without saying what it wants.
        static readonly byte[] Proposito = Encoding.ASCII.GetBytes("CriCriMods.Servidor.ChaveBr.v1");

        internal static string PastaDeTeste;

        static string Pasta
        {
            get
            {
                if (PastaDeTeste != null) return PastaDeTeste;
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return string.IsNullOrEmpty(local) ? null : Path.Combine(local, "CriCri Mods");
            }
        }

        static string Arquivo => Pasta == null ? null : Path.Combine(Pasta, NomeDoArquivo);

        // The same rule as the phone's ChaveBr.limpar: trimmed, 8 to 64 printable ASCII, no spaces. null = cannot be a key.
        internal static string Limpar(string colado)
        {
            if (colado == null) return null;
            string s = colado.Trim();
            if (s.Length < 8 || s.Length > 64) return null;
            foreach (char c in s)
                if (c <= ' ' || c > '~') return null;
            return s;
        }

        internal static bool Salvar(string chave)
        {
            string destino = Arquivo;
            if (destino == null || chave == null) return false;
            string temporario = destino + ".tmp";
            byte[] claro = Encoding.ASCII.GetBytes(chave);
            try
            {
                byte[] cifrado = Transformar(claro, true);
                if (cifrado == null) return false;
                Directory.CreateDirectory(Pasta);
                File.WriteAllBytes(temporario, cifrado);
                File.Move(temporario, destino, true);
                return true;
            }
            catch
            {
                try { if (File.Exists(temporario)) File.Delete(temporario); } catch { }
                return false;
            }
            finally { Array.Clear(claro, 0, claro.Length); }
        }

        // The key, or null (none yet, or a file this Windows user cannot open: it stays where it is and the next paste replaces it).
        internal static string Ler()
        {
            byte[] claro = null;
            try
            {
                string origem = Arquivo;
                if (origem == null || !File.Exists(origem)) return null;
                claro = Transformar(File.ReadAllBytes(origem), false);
                return claro == null ? null : Limpar(Encoding.ASCII.GetString(claro));
            }
            catch { return null; }
            finally { if (claro != null) Array.Clear(claro, 0, claro.Length); }
        }

        internal static bool Apagar()
        {
            try
            {
                string alvo = Arquivo;
                if (alvo != null && File.Exists(alvo)) File.Delete(alvo);
                return true;
            }
            catch { return false; }
        }

        // ---------------- DPAPI ----------------

        [StructLayout(LayoutKind.Sequential)]
        struct Blob
        {
            public int Tamanho;
            public IntPtr Dados;
        }

        [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool CryptProtectData(ref Blob dados, string descricao, ref Blob entropia, IntPtr reservado, IntPtr aviso, int bandeiras, out Blob saida);

        [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool CryptUnprotectData(ref Blob dados, IntPtr descricao, ref Blob entropia, IntPtr reservado, IntPtr aviso, int bandeiras, out Blob saida);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr LocalFree(IntPtr memoria);

        static byte[] Transformar(byte[] entrada, bool cifrar)
        {
            Blob dados = Alocar(entrada), entropia = Alocar(Proposito), saida = default;
            try
            {
                bool certo = cifrar
                    ? CryptProtectData(ref dados, "CriCri Mods", ref entropia, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out saida)
                    : CryptUnprotectData(ref dados, IntPtr.Zero, ref entropia, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out saida);
                if (!certo || saida.Dados == IntPtr.Zero) return null;
                var resultado = new byte[saida.Tamanho];
                Marshal.Copy(saida.Dados, resultado, 0, saida.Tamanho);
                return resultado;
            }
            finally
            {
                Liberar(dados);
                Liberar(entropia);
                if (saida.Dados != IntPtr.Zero)
                {
                    Zerar(saida);
                    LocalFree(saida.Dados);
                }
            }
        }

        static Blob Alocar(byte[] bytes)
        {
            var b = new Blob { Tamanho = bytes.Length, Dados = Marshal.AllocHGlobal(Math.Max(bytes.Length, 1)) };
            Marshal.Copy(bytes, 0, b.Dados, bytes.Length);
            return b;
        }

        static void Liberar(Blob b)
        {
            Zerar(b);
            Marshal.FreeHGlobal(b.Dados);
        }

        static void Zerar(Blob b)
        {
            if (b.Tamanho > 0) Marshal.Copy(new byte[b.Tamanho], 0, b.Dados, b.Tamanho);
        }

        // ---------------- clipboard (read only when the player clicks COLAR) ----------------

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool OpenClipboard(IntPtr janela);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr GetClipboardData(uint formato);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr GlobalLock(IntPtr memoria);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GlobalUnlock(IntPtr memoria);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern UIntPtr GlobalSize(IntPtr memoria);

        // Another program may be holding the clipboard for a moment.
        static bool AbrirAreaDeTransferencia()
        {
            for (int i = 0; i < 5; i++)
            {
                if (OpenClipboard(IntPtr.Zero)) return true;
                Thread.Sleep(10);
            }
            return false;
        }

        // The clipboard's text; "" = no text there; null = another program would not let go of it.
        internal static string LerAreaDeTransferencia()
        {
            try
            {
                if (!AbrirAreaDeTransferencia()) return null;
                try
                {
                    IntPtr alca = GetClipboardData(CfUnicodeText);
                    if (alca == IntPtr.Zero) return "";
                    IntPtr ponteiro = GlobalLock(alca);
                    if (ponteiro == IntPtr.Zero) return "";
                    try
                    {
                        int caracteres = (int)Math.Min(GlobalSize(alca).ToUInt64() / 2, MaxCaracteresColados);
                        string texto = Marshal.PtrToStringUni(ponteiro, caracteres);
                        int fim = texto.IndexOf('\0');
                        return fim >= 0 ? texto.Substring(0, fim) : texto;
                    }
                    finally { GlobalUnlock(alca); }
                }
                finally { CloseClipboard(); }
            }
            catch { return null; }
        }

        // After the key is safe: a streamer's Ctrl+V in the chat must not paste it on screen.
        internal static void EsvaziarAreaDeTransferencia()
        {
            try
            {
                if (!AbrirAreaDeTransferencia()) return;
                try { EmptyClipboard(); }
                finally { CloseClipboard(); }
            }
            catch { }
        }
    }
}
#endif
