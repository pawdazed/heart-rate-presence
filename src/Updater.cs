// Automatic updates: asks GitHub for the latest release, downloads HeartRatePresence.exe,
// checks it against the SHA-256 GitHub publishes for it, then swaps it in for the installed copy.
// Windows lets a running exe be renamed (just not deleted or overwritten), so the swap needs no helper program.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

namespace HeartRatePresence
{
    static class Updater
    {
        public const string AssetName = "HeartRatePresence.exe";

        /// <summary>Overridable only so the self-test can point it somewhere else.</summary>
        public static string FeedUrl = "https://api.github.com/repos/" + AppInfo.RepoSlug + "/releases/latest";

        public class Info
        {
            public string Tag, Url, Sha256;
            public long Size;
            public bool Newer;
        }

        static Version Parse(string s)
        {
            s = (s ?? "").Trim().TrimStart('v', 'V');
            int dash = s.IndexOfAny(new[] { '-', '+' });
            if (dash >= 0) s = s.Substring(0, dash);
            Version v;
            return Version.TryParse(s, out v) ? v : null;
        }

        public static bool IsNewer(string latestTag, string currentVersion)
        {
            var a = Parse(latestTag); var b = Parse(currentVersion);
            return a != null && b != null && a > b;
        }

        static HttpWebRequest Request(string url)
        {
            var r = (HttpWebRequest)WebRequest.Create(url);
            r.UserAgent = "HeartRatePresence/" + AppInfo.Version;
            r.Accept = "application/vnd.github+json";
            r.Timeout = 20000;
            r.ReadWriteTimeout = 30000;
            return r;
        }

        /// <summary>Asks GitHub about the latest release. Throws with a readable message if it can't.</summary>
        public static Info Check()
        {
            try
            {
                string json;
                using (var resp = Request(FeedUrl).GetResponse())
                using (var rd = new StreamReader(resp.GetResponseStream()))
                    json = rd.ReadToEnd();
                var d = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
                if (d == null) throw new Exception("unexpected reply from GitHub");
                var info = new Info { Tag = Convert.ToString(Json.Get(d, "tag_name")) };
                var assets = Json.Get(d, "assets") as IEnumerable;
                if (assets != null)
                    foreach (var o in assets)
                    {
                        var a = o as Dictionary<string, object>;
                        if (a == null || Convert.ToString(Json.Get(a, "name")) != AssetName) continue;
                        info.Url = Convert.ToString(Json.Get(a, "browser_download_url"));
                        info.Size = Convert.ToInt64(Json.Get(a, "size") ?? 0);
                        var digest = Convert.ToString(Json.Get(a, "digest")) ?? "";
                        if (digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) info.Sha256 = digest.Substring(7).ToLowerInvariant();
                    }
                if (info.Url == null) throw new Exception("the latest release has no " + AssetName);
                info.Newer = IsNewer(info.Tag, AppInfo.Version);
                return info;
            }
            catch (WebException ex)
            {
                var http = ex.Response as HttpWebResponse;
                if (http != null && http.StatusCode == HttpStatusCode.NotFound) throw new Exception("no release found yet");
                if (http != null && (int)http.StatusCode == 403) throw new Exception("GitHub is rate-limiting this PC, try again later");
                throw new Exception("couldn't reach GitHub");
            }
        }

        /// <summary>Downloads the release file next to the target and verifies it. Returns the path of the verified file.</summary>
        public static string Download(Info info, string dir)
        {
            // Only ever fetch from this app's own GitHub releases.
            string allowed = "https://github.com/" + AppInfo.RepoSlug + "/releases/download/";
            if (!info.Url.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new Exception("unexpected download address");

            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "HeartRatePresence.new");
            try { if (File.Exists(path)) File.Delete(path); } catch { }
            try
            {
                using (var resp = Request(info.Url).GetResponse())
                using (var src = resp.GetResponseStream())
                using (var dst = File.Create(path))
                    src.CopyTo(dst);

                var fi = new FileInfo(path);
                if (info.Size > 0 && fi.Length != info.Size) throw new Exception("the download was cut short");
                using (var fs = File.OpenRead(path))
                {
                    if (fs.ReadByte() != 'M' || fs.ReadByte() != 'Z') throw new Exception("the download isn't a program");
                    if (!string.IsNullOrEmpty(info.Sha256))
                    {
                        fs.Position = 0;
                        using (var sha = SHA256.Create())
                        {
                            var hex = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
                            if (hex != info.Sha256) throw new Exception("the download didn't match GitHub's checksum");
                        }
                    }
                }
                return path;
            }
            catch
            {
                try { File.Delete(path); } catch { }
                throw;
            }
        }

        /// <summary>Puts newFile in place of target. The running exe is renamed aside, then the new one is moved in.</summary>
        public static void Swap(string newFile, string target)
        {
            string old = target + ".old";
            try { if (File.Exists(old)) File.Delete(old); } catch { }
            if (File.Exists(old)) old = target + "." + DateTime.UtcNow.Ticks + ".old";
            File.Move(target, old);
            try { File.Move(newFile, target); }
            catch { File.Move(old, target); throw; }
        }

        /// <summary>Removes leftovers from earlier swaps (they can't be deleted while that copy was still running).</summary>
        public static void CleanOld(string installDir)
        {
            try
            {
                foreach (var f in Directory.GetFiles(installDir, "HeartRatePresence.exe*.old"))
                    try { File.Delete(f); } catch { }
                foreach (var f in Directory.GetFiles(installDir, "HeartRatePresence.new"))
                    try { File.Delete(f); } catch { }
            }
            catch { }
        }
    }
}
