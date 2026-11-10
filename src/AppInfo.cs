// App identity and install locations. The two constants at the top are the only things
// a fork needs to change: the Discord application everyone shares, and the GitHub repo
// that hosts the icon images and releases.

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace HeartRatePresence
{
    static class AppInfo
    {
        /// <summary>Shared Discord application. Its name is what shows after "Playing".</summary>
        public const string BuiltInClientId = "1569006866174976351";

        /// <summary>GitHub "owner/repo". The heart icons are served from its assets folder.</summary>
        public const string RepoSlug = "pawdazed/heart-rate-presence";

        public const string Version = "1.0.0";
        public const string Name = "Heart Rate Presence";

        public static string RepoUrl { get { return "https://github.com/" + RepoSlug; } }
        public static string ReleasesUrl { get { return RepoUrl + "/releases/latest"; } }
        public static string AssetBase { get { return "https://raw.githubusercontent.com/" + RepoSlug + "/main/assets/"; } }
        public const string HeartCastUrl = "https://apps.apple.com/us/app/heartcast-heart-rate-monitor/id1499771124";

        public static string DataDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HeartRatePresence"); } }
        public static string InstallDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "HeartRatePresence"); } }
        public static string InstalledExe { get { return Path.Combine(InstallDir, "HeartRatePresence.exe"); } }
        public static string StartMenuLink
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Name + ".lnk"); }
        }

        public static string EffectiveClientId(string configured)
        {
            var c = (configured ?? "").Trim();
            return c.Length > 0 ? c : BuiltInClientId;
        }
    }

    /// <summary>Per-user install: no admin rights, nothing outside the user's own folders and HKCU.</summary>
    static class Setup
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunValue = "HeartRatePresence";

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool DeleteFile(string path);

        public static string SelfPath { get { return Path.GetFullPath(Application.ExecutablePath); } }

        public static bool IsInstalledCopy
        {
            get { return string.Equals(SelfPath, Path.GetFullPath(AppInfo.InstalledExe), StringComparison.OrdinalIgnoreCase); }
        }

        public static bool IsInstalled { get { return File.Exists(AppInfo.InstalledExe); } }

        /// <summary>Copies this exe into the install folder (overwriting an older copy). Returns false if it couldn't.</summary>
        public static bool CopySelfToInstallDir()
        {
            if (IsInstalledCopy) return true;
            try
            {
                Directory.CreateDirectory(AppInfo.InstallDir);
                File.Copy(SelfPath, AppInfo.InstalledExe, true);
                // The user already chose to run this download; drop the "downloaded from the internet"
                // marker on our own installed copy so Windows doesn't ask again on every start.
                DeleteFile(AppInfo.InstalledExe + ":Zone.Identifier");
                return true;
            }
            catch { return false; }
        }

        public static string LaunchTarget { get { return IsInstalled ? AppInfo.InstalledExe : SelfPath; } }

        public static void CreateStartMenuShortcut()
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(t);
            object sc = null;
            try
            {
                sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { AppInfo.StartMenuLink });
                var st = sc.GetType();
                st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { LaunchTarget });
                st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { Path.GetDirectoryName(LaunchTarget) });
                st.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { "Show your heart rate on your Discord profile" });
                st.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { LaunchTarget + ",0" });
                st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
            }
            finally
            {
                if (sc != null) Marshal.FinalReleaseComObject(sc);
                Marshal.FinalReleaseComObject(shell);
            }
        }

        public static bool StartupEnabled
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue(RunValue) != null;
            }
        }

        public static void SetStartup(bool on)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue(RunValue, "\"" + LaunchTarget + "\" --minimized");
                else k.DeleteValue(RunValue, false);
            }
        }

        /// <summary>Removes the shortcut, the startup entry, settings and the installed exe.</summary>
        public static void Uninstall()
        {
            try { SetStartup(false); } catch { }
            try { if (File.Exists(AppInfo.StartMenuLink)) File.Delete(AppInfo.StartMenuLink); } catch { }
            try { if (Directory.Exists(AppInfo.DataDir)) Directory.Delete(AppInfo.DataDir, true); } catch { }
            if (Directory.Exists(AppInfo.InstallDir))
            {
                // The running exe can't delete itself, so a short-lived cmd removes the folder after we exit.
                var psi = new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"" + AppInfo.InstallDir + "\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden,
                };
                try { Process.Start(psi); } catch { }
            }
        }
    }
}
