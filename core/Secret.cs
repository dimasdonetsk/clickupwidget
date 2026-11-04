using System;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace ClickUpWidget
{
    /// <summary>
    /// Де живе токен ClickUp. Windows — DPAPI (зашифровано під обліковий запис, поле TokenEnc у settings.json).
    /// Mac — Keychain (у settings.json токена немає взагалі).
    /// </summary>
    internal static class Secret
    {
        public static string? LoadToken(Settings s)
        {
            if (OperatingSystem.IsWindows()) return Unprotect(s.TokenEnc);
            if (OperatingSystem.IsMacOS()) return Keychain.Token;
            return null;
        }

        public static void SaveToken(Settings s, string? token)
        {
            if (OperatingSystem.IsWindows())
                s.TokenEnc = Protect(token);
            else if (OperatingSystem.IsMacOS())
            {
                Keychain.Token = token;
                s.TokenEnc = null;
            }
        }

        public static bool HasToken(Settings s) =>
            OperatingSystem.IsMacOS() ? Keychain.Token != null : !string.IsNullOrEmpty(s.TokenEnc);

        [SupportedOSPlatform("windows")]
        public static string? Protect(string? plain)
        {
            if (string.IsNullOrEmpty(plain)) return null;
            try
            {
                byte[] data = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(data);
            }
            catch (Exception ex) { Log.Write("Protect: " + ex.Message); return null; }
        }

        [SupportedOSPlatform("windows")]
        public static string? Unprotect(string? enc)
        {
            if (string.IsNullOrEmpty(enc)) return null;
            try
            {
                byte[] data = ProtectedData.Unprotect(Convert.FromBase64String(enc), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(data);
            }
            catch (Exception ex) { Log.Write("Unprotect: " + ex.Message); return null; }
        }
    }

    /// <summary>
    /// Keychain на Mac через системну утиліту /usr/bin/security.
    /// Запис робить сама утиліта (вона й «власник» запису), тож після оновлення віджета
    /// macOS не перепитує дозволу — як і DPAPI, токен доступний лише цьому користувачу.
    /// </summary>
    internal static class Keychain
    {
        public const string Service = "ClickUpWidget";
        public const string Account = "clickup-api-token";

        private static string? _cached;
        private static bool _loaded;

        /// <summary>Токен (читається з Keychain один раз за запуск). null — видалити.</summary>
        public static string? Token
        {
            get
            {
                if (!_loaded)
                {
                    _cached = Read(Service, Account);
                    _loaded = true;
                }
                return _cached;
            }
            set
            {
                if (string.IsNullOrEmpty(value)) Delete(Service, Account);
                else if (!Write(Service, Account, value)) { Log.Write("Keychain: не вдалося зберегти токен"); return; }
                _cached = string.IsNullOrEmpty(value) ? null : value;
                _loaded = true;
            }
        }

        public static string? Read(string service, string account)
        {
            (int code, string output) = Run(null, "find-generic-password", "-s", service, "-a", account, "-w");
            if (code != 0) return null;   // 44 — запису немає
            string value = output.TrimEnd('\r', '\n');
            return value.Length > 0 ? value : null;
        }

        /// <summary>Зберегти або замінити. Пароль іде через stdin у шістнадцятковому вигляді — не видно в списку процесів.</summary>
        public static bool Write(string service, string account, string secret)
        {
            if (!Safe(service) || !Safe(account)) return false;
            string hex = Convert.ToHexString(Encoding.UTF8.GetBytes(secret));
            string command = "add-generic-password -U -s \"" + service + "\" -a \"" + account +
                             "\" -l \"ClickUp Widget\" -X " + hex + "\n";
            Run(command, "-i");
            return Read(service, account) == secret;
        }

        public static void Delete(string service, string account) =>
            Run(null, "delete-generic-password", "-s", service, "-a", account);

        private static bool Safe(string s)
        {
            foreach (char c in s)
                if (!(char.IsLetterOrDigit(c) || c is '-' or '_' or '.')) return false;
            return s.Length > 0;
        }

        private static (int Code, string Output) Run(string? stdin, params string[] args)
        {
            try
            {
                var psi = new ProcessStartInfo("/usr/bin/security")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = stdin != null,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                foreach (string a in args) psi.ArgumentList.Add(a);
                using Process p = Process.Start(psi)!;
                if (stdin != null)
                {
                    p.StandardInput.Write(stdin);
                    p.StandardInput.Close();
                }
                string output = p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                if (!p.WaitForExit(15000)) { try { p.Kill(); } catch { } return (-1, ""); }
                return (p.ExitCode, output);
            }
            catch (Exception ex)
            {
                Log.Write("Keychain: " + ex.Message);
                return (-1, "");
            }
        }
    }
}
