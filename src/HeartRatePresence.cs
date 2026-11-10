// Heart Rate Presence
// Shows your live heart rate (Apple Watch -> Pulsoid free app, or any app that can POST BPM)
// as a Discord Rich Presence activity, with text and icons that change per BPM range.
// Builds with the C# compiler that ships with Windows (.NET Framework 4.x), no installs needed.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

[assembly: System.Reflection.AssemblyTitle("Heart Rate Presence")]
[assembly: System.Reflection.AssemblyProduct("Heart Rate Presence")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]

namespace HeartRatePresence
{
    // ------------------------------------------------------------------ config

    public class Tier
    {
        public int MinBpm { get; set; }
        public string Name { get; set; }
        public string Details { get; set; }
        public string State { get; set; }
        public string LargeImage { get; set; }
        public string LargeText { get; set; }
        public string SmallImage { get; set; }
        public string SmallText { get; set; }
    }

    public class AppConfig
    {
        public string DiscordClientId { get; set; }
        public string Source { get; set; }          // "Bluetooth", "HypeRate", "Pulsoid" or "LocalHttp"
        public string BleNameFilter { get; set; }
        public string HypeRateKey { get; set; }
        public string HypeRateId { get; set; }
        public string PulsoidToken { get; set; }
        public int HttpPort { get; set; }
        public int ActivityType { get; set; }       // 0 Playing, 2 Listening, 3 Watching, 5 Competing
        public int StatusDisplay { get; set; }      // 0 app name, 1 state, 2 details
        public bool ShowElapsed { get; set; }
        public int UpdateIntervalSec { get; set; }
        public int StaleTimeoutSec { get; set; }
        public bool AutoStart { get; set; }
        public bool MinimizeToTray { get; set; }
        public string Button1Label { get; set; }
        public string Button1Url { get; set; }
        public string Button2Label { get; set; }
        public string Button2Url { get; set; }
        public List<Tier> Tiers { get; set; }

        public static string FilePath
        {
            get { return Path.Combine(AppInfo.DataDir, "config.json"); }
        }

        // where 1.0 pre-releases kept it (next to the exe)
        static string LegacyPath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json"); } }

        public static bool Exists { get { return File.Exists(FilePath) || File.Exists(LegacyPath); } }

        public static string Img(string name) { return AppInfo.AssetBase + name + ".png"; }

        public static AppConfig CreateDefault()
        {
            var c = new AppConfig();
            c.DiscordClientId = "";
            c.Source = "Bluetooth";
            c.BleNameFilter = "";
            c.HypeRateKey = "";
            c.HypeRateId = "";
            c.PulsoidToken = "";
            c.HttpPort = 8765;
            c.ActivityType = 0;
            c.StatusDisplay = 2;
            c.ShowElapsed = true;
            c.UpdateIntervalSec = 5;
            c.StaleTimeoutSec = 30;
            c.AutoStart = true;
            c.MinimizeToTray = true;
            c.Button1Label = ""; c.Button1Url = "";
            c.Button2Label = ""; c.Button2Url = "";
            c.Tiers = DefaultTiers();
            return c;
        }

        public static List<Tier> DefaultTiers()
        {
            return new List<Tier>
            {
                T(0,   "Resting",  "❤️ {bpm} BPM", "basically asleep",      Img("hr_resting"),  "{bpm} BPM · resting"),
                T(60,  "Normal",   "❤️ {bpm} BPM", "vibing",                Img("hr_normal"),   "{bpm} BPM · normal"),
                T(90,  "Elevated", "❤️ {bpm} BPM", "something's going on",  Img("hr_elevated"), "{bpm} BPM · elevated"),
                T(120, "High",     "❤️ {bpm} BPM", "heart going brrr",      Img("hr_high"),     "{bpm} BPM · high"),
                T(150, "Max",      "❤️ {bpm} BPM", "send help",             Img("hr_max"),      "{bpm} BPM · way too high"),
            };
        }

        static Tier T(int min, string name, string details, string state, string img, string imgText)
        {
            var t = new Tier();
            t.MinBpm = min; t.Name = name; t.Details = details; t.State = state;
            t.LargeImage = img; t.LargeText = imgText; t.SmallImage = ""; t.SmallText = "";
            return t;
        }

        public static AppConfig Load(out string warning)
        {
            warning = null;
            var def = CreateDefault();
            string path = File.Exists(FilePath) ? FilePath : File.Exists(LegacyPath) ? LegacyPath : null;
            if (path == null) return def;
            try
            {
                var c = new JavaScriptSerializer().Deserialize<AppConfig>(File.ReadAllText(path, Encoding.UTF8));
                if (c == null) return def;
                if (c.DiscordClientId == null) c.DiscordClientId = "";
                if (c.Source != "LocalHttp" && c.Source != "Pulsoid" && c.Source != "HypeRate") c.Source = "Bluetooth";
                if (c.BleNameFilter == null) c.BleNameFilter = "";
                if (c.HypeRateKey == null) c.HypeRateKey = "";
                if (c.HypeRateId == null) c.HypeRateId = "";
                if (c.PulsoidToken == null) c.PulsoidToken = "";
                if (c.HttpPort <= 0 || c.HttpPort > 65535) c.HttpPort = def.HttpPort;
                if (c.UpdateIntervalSec < 4) c.UpdateIntervalSec = 4;
                if (c.StaleTimeoutSec < 5) c.StaleTimeoutSec = def.StaleTimeoutSec;
                if (c.Button1Label == null) c.Button1Label = "";
                if (c.Button1Url == null) c.Button1Url = "";
                if (c.Button2Label == null) c.Button2Label = "";
                if (c.Button2Url == null) c.Button2Url = "";
                if (c.Tiers == null || c.Tiers.Count == 0) c.Tiers = DefaultTiers();
                foreach (var t in c.Tiers) Normalize(t);
                return c;
            }
            catch (Exception ex)
            {
                warning = "config.json could not be read (" + ex.Message + "). Using defaults; saving will overwrite it.";
                return def;
            }
        }

        public static void Normalize(Tier t)
        {
            if (t.Name == null) t.Name = "";
            if (t.Details == null) t.Details = "";
            if (t.State == null) t.State = "";
            if (t.LargeImage == null) t.LargeImage = "";
            if (t.LargeText == null) t.LargeText = "";
            if (t.SmallImage == null) t.SmallImage = "";
            if (t.SmallText == null) t.SmallText = "";
        }

        public void Save()
        {
            Tiers = Tiers.OrderBy(t => t.MinBpm).ToList();
            var json = Json.Pretty(new JavaScriptSerializer().Serialize(this));
            Directory.CreateDirectory(AppInfo.DataDir);
            File.WriteAllText(FilePath, json, new UTF8Encoding(false));
        }

        public AppConfig Clone()
        {
            var s = new JavaScriptSerializer();
            return s.Deserialize<AppConfig>(s.Serialize(this));
        }
    }

    static class Json
    {
        public static Dictionary<string, object> Obj(string json)
        {
            return new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
        }

        public static object Get(Dictionary<string, object> d, params string[] path)
        {
            object cur = d;
            foreach (var p in path)
            {
                var dd = cur as Dictionary<string, object>;
                if (dd == null || !dd.ContainsKey(p)) return null;
                cur = dd[p];
            }
            return cur;
        }

        public static string Pretty(string json)
        {
            var sb = new StringBuilder();
            int indent = 0; bool inStr = false; bool esc = false;
            foreach (char ch in json)
            {
                if (inStr)
                {
                    sb.Append(ch);
                    if (esc) esc = false;
                    else if (ch == '\\') esc = true;
                    else if (ch == '"') inStr = false;
                    continue;
                }
                switch (ch)
                {
                    case '"': inStr = true; sb.Append(ch); break;
                    case '{': case '[':
                        sb.Append(ch); indent++; sb.Append('\n').Append(new string(' ', indent * 2)); break;
                    case '}': case ']':
                        indent--; sb.Append('\n').Append(new string(' ', indent * 2)).Append(ch); break;
                    case ',':
                        sb.Append(ch).Append('\n').Append(new string(' ', indent * 2)); break;
                    case ':': sb.Append(": "); break;
                    default: sb.Append(ch); break;
                }
            }
            // collapse empty [] / {}
            return Regex.Replace(sb.ToString(), @"([\[{])\s+([\]}])", "$1$2");
        }
    }

    // ------------------------------------------------------------------ presence building

    public class Resolved
    {
        public int Index;
        public Tier Tier;
        public string Details, State, LargeImage, LargeText, SmallImage, SmallText;
    }

    static class Presence
    {
        public static readonly int[] TypeValues = { 0, 2, 3, 5 };
        public static readonly string[] TypeNames = { "Playing", "Listening to", "Watching", "Competing in" };

        public static List<Tier> Sorted(AppConfig cfg)
        {
            return (cfg.Tiers ?? new List<Tier>()).Where(t => t != null).OrderBy(t => t.MinBpm).ToList();
        }

        public static Resolved Resolve(AppConfig cfg, int bpm)
        {
            var tiers = Sorted(cfg);
            if (tiers.Count == 0) return null;
            int idx = 0;
            for (int i = 0; i < tiers.Count; i++) if (bpm >= tiers[i].MinBpm) idx = i;
            var t = tiers[idx];
            AppConfig.Normalize(t);
            string max = idx + 1 < tiers.Count ? (tiers[idx + 1].MinBpm - 1).ToString() : "∞";
            var r = new Resolved();
            r.Index = idx; r.Tier = t;
            r.Details = Fill(t.Details, bpm, t, max);
            r.State = Fill(t.State, bpm, t, max);
            r.LargeImage = t.LargeImage.Trim();
            r.LargeText = Fill(t.LargeText, bpm, t, max);
            r.SmallImage = t.SmallImage.Trim();
            r.SmallText = Fill(t.SmallText, bpm, t, max);
            return r;
        }

        static string Fill(string template, int bpm, Tier t, string max)
        {
            if (string.IsNullOrEmpty(template)) return "";
            return template.Replace("{bpm}", bpm.ToString())
                           .Replace("{name}", t.Name ?? "")
                           .Replace("{min}", t.MinBpm.ToString())
                           .Replace("{max}", max)
                           .Trim();
        }

        // Discord wants 2..128 chars for text fields.
        static string Txt(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            if (s.Length > 128) s = s.Substring(0, 127) + "…";
            if (s.Length < 2) s = s + "​";
            return s;
        }

        static string Img(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim();
            if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return s;
            return s.ToLowerInvariant();
        }

        public static Dictionary<string, object> BuildActivity(AppConfig cfg, int bpm, long? startUnix)
        {
            var r = Resolve(cfg, bpm);
            if (r == null) return null;
            var a = new Dictionary<string, object>();
            a["type"] = TypeValues.Contains(cfg.ActivityType) ? cfg.ActivityType : 0;
            a["status_display_type"] = Math.Max(0, Math.Min(2, cfg.StatusDisplay));
            var d = Txt(r.Details); if (d != null) a["details"] = d;
            var s = Txt(r.State); if (s != null) a["state"] = s;

            var assets = new Dictionary<string, object>();
            var li = Img(r.LargeImage);
            if (li != null)
            {
                assets["large_image"] = li;
                var lt = Txt(r.LargeText); if (lt != null) assets["large_text"] = lt;
            }
            var si = Img(r.SmallImage);
            if (si != null)
            {
                assets["small_image"] = si;
                var st = Txt(r.SmallText); if (st != null) assets["small_text"] = st;
            }
            if (assets.Count > 0) a["assets"] = assets;

            if (startUnix.HasValue)
                a["timestamps"] = new Dictionary<string, object> { { "start", startUnix.Value } };

            var buttons = new List<object>();
            AddButton(buttons, cfg.Button1Label, cfg.Button1Url);
            AddButton(buttons, cfg.Button2Label, cfg.Button2Url);
            if (buttons.Count > 0) a["buttons"] = buttons;
            return a;
        }

        static void AddButton(List<object> list, string label, string url)
        {
            if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(url)) return;
            url = url.Trim();
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) url = "https://" + url;
            label = label.Trim();
            if (label.Length > 32) label = label.Substring(0, 32);
            list.Add(new Dictionary<string, object> { { "label", label }, { "url", url } });
        }
    }

    // ------------------------------------------------------------------ Discord local RPC (named pipe)

    class DiscordIpc : IDisposable
    {
        NamedPipeClientStream pipe;
        readonly JavaScriptSerializer ser = new JavaScriptSerializer();
        public string UserName = "";

        public void Connect(string clientId)
        {
            for (int i = 0; i < 10 && pipe == null; i++)
            {
                NamedPipeClientStream p = null;
                try
                {
                    p = new NamedPipeClientStream(".", "discord-ipc-" + i, PipeDirection.InOut, PipeOptions.None);
                    p.Connect(150);
                    pipe = p;
                }
                catch { if (p != null) p.Dispose(); }
            }
            if (pipe == null) throw new Exception("Discord desktop app isn't running.");

            Write(0, ser.Serialize(new Dictionary<string, object> { { "v", 1 }, { "client_id", clientId } }));
            var frame = Read();
            var d = Json.Obj(frame.Value);
            if (frame.Key == 2)
            {
                var msg = d != null ? Convert.ToString(Json.Get(d, "message")) : "closed";
                throw new Exception("Discord refused the connection (" + msg + "). Check your Application ID.");
            }
            if (d != null && Convert.ToString(Json.Get(d, "evt")) == "ERROR")
                throw new Exception("Discord error: " + Convert.ToString(Json.Get(d, "data", "message")));
            var gn = d == null ? null : Json.Get(d, "data", "user", "global_name") as string;
            var un = d == null ? null : Json.Get(d, "data", "user", "username") as string;
            UserName = !string.IsNullOrEmpty(gn) ? gn : (un ?? "you");
        }

        /// <summary>Returns null on success, or Discord's error message.</summary>
        public string SetActivity(Dictionary<string, object> activity)
        {
            var args = new Dictionary<string, object> { { "pid", Process.GetCurrentProcess().Id } };
            if (activity != null) args["activity"] = activity;
            var nonce = Guid.NewGuid().ToString();
            Write(1, ser.Serialize(new Dictionary<string, object> { { "cmd", "SET_ACTIVITY" }, { "args", args }, { "nonce", nonce } }));
            for (int guard = 0; guard < 20; guard++)
            {
                var f = Read();
                if (f.Key == 3) { Write(4, f.Value); continue; }
                if (f.Key == 2) throw new Exception("Discord closed the connection.");
                var d = Json.Obj(f.Value);
                if (d == null) continue;
                if (Convert.ToString(Json.Get(d, "nonce")) != nonce) continue;
                if (Convert.ToString(Json.Get(d, "evt")) == "ERROR")
                    return Convert.ToString(Json.Get(d, "data", "message"));
                return null;
            }
            return null;
        }

        void Write(int op, string json)
        {
            var body = Encoding.UTF8.GetBytes(json);
            var buf = new byte[8 + body.Length];
            BitConverter.GetBytes(op).CopyTo(buf, 0);
            BitConverter.GetBytes(body.Length).CopyTo(buf, 4);
            body.CopyTo(buf, 8);
            pipe.Write(buf, 0, buf.Length);
            pipe.Flush();
        }

        KeyValuePair<int, string> Read()
        {
            var head = ReadExact(8);
            int op = BitConverter.ToInt32(head, 0);
            int len = BitConverter.ToInt32(head, 4);
            if (len < 0 || len > 1 << 20) throw new Exception("Bad data from Discord.");
            return new KeyValuePair<int, string>(op, Encoding.UTF8.GetString(ReadExact(len)));
        }

        byte[] ReadExact(int n)
        {
            var b = new byte[n]; int got = 0;
            while (got < n)
            {
                int r = pipe.Read(b, got, n - got);
                if (r <= 0) throw new Exception("Lost connection to Discord.");
                got += r;
            }
            return b;
        }

        public void Dispose()
        {
            try { if (pipe != null) pipe.Dispose(); } catch { }
            pipe = null;
        }
    }

    // ------------------------------------------------------------------ tiny WinRT helpers
    // (instead of System.Runtime.WindowsRuntime, which needs the Windows SDK to compile against)

    static class WinRt
    {
        public static Task<T> Await<T>(Windows.Foundation.IAsyncOperation<T> op, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<T>();
            op.Completed = (o, status) =>
            {
                if (status == Windows.Foundation.AsyncStatus.Completed)
                {
                    try { tcs.TrySetResult(o.GetResults()); } catch (Exception e) { tcs.TrySetException(e); }
                }
                else if (status == Windows.Foundation.AsyncStatus.Canceled) tcs.TrySetCanceled();
                else tcs.TrySetException(o.ErrorCode ?? new Exception("Windows Bluetooth call failed."));
            };
            if (ct.CanBeCanceled)
            {
                var reg = ct.Register(() => { try { op.Cancel(); } catch { } tcs.TrySetCanceled(); });
                tcs.Task.ContinueWith(_ => reg.Dispose(), TaskContinuationOptions.ExecuteSynchronously);
            }
            return tcs.Task;
        }

        public static byte[] Bytes(Windows.Storage.Streams.IBuffer buf)
        {
            if (buf == null) return new byte[0];
            var reader = Windows.Storage.Streams.DataReader.FromBuffer(buf);
            var b = new byte[reader.UnconsumedBufferLength];
            reader.ReadBytes(b);
            return b;
        }
    }

    // ------------------------------------------------------------------ engine (sources + Discord worker)

    public class Engine
    {
        public volatile AppConfig Config;
        public volatile int TestBpm;
        public volatile string SourceStatus = "Stopped";
        public volatile string DiscordStatus = "Stopped";
        public volatile string AppName;

        readonly object sync = new object();
        int bpm; DateTime bpmAt = DateTime.MinValue;
        CancellationTokenSource cts; Thread worker; DateTime sessionStart;

        public bool Running { get { return cts != null; } }

        public void PushBpm(int v)
        {
            if (v < 20 || v > 260) return;
            lock (sync) { bpm = v; bpmAt = DateTime.UtcNow; }
        }

        public int LiveBpm()
        {
            lock (sync)
            {
                var cfg = Config;
                if (bpm > 0 && (DateTime.UtcNow - bpmAt).TotalSeconds <= Math.Max(5, cfg.StaleTimeoutSec)) return bpm;
                return 0;
            }
        }

        public int CurrentBpm()
        {
            int t = TestBpm;
            return t > 0 ? t : LiveBpm();
        }

        public void Start()
        {
            if (cts != null) return;
            cts = new CancellationTokenSource();
            var ct = cts.Token;
            var cfg = Config;
            sessionStart = DateTime.UtcNow;
            lock (sync) { bpm = 0; }
            if (cfg.Source == "LocalHttp") Task.Run(() => HttpLoop(cfg.HttpPort, ct));
            else if (cfg.Source == "Pulsoid") Task.Run(() => PulsoidLoop((cfg.PulsoidToken ?? "").Trim(), ct));
            else if (cfg.Source == "Bluetooth") Task.Run(() => BleLoop(cfg.BleNameFilter, ct));
            else Task.Run(() => HypeRateLoop((cfg.HypeRateKey ?? "").Trim(), (cfg.HypeRateId ?? "").Trim(), ct));
            var id = AppInfo.EffectiveClientId(cfg.DiscordClientId);
            worker = new Thread(() => DiscordLoop(id, ct));
            worker.IsBackground = true;
            worker.Start();
            Task.Run(() => FetchAppName(id));
        }

        public void Stop()
        {
            if (cts == null) return;
            cts.Cancel();
            if (worker != null) worker.Join(3000);
            worker = null; cts = null;
            SourceStatus = "Stopped";
            DiscordStatus = "Stopped";
        }

        void SetSource(CancellationToken ct, string s) { if (!ct.IsCancellationRequested) SourceStatus = s; }
        void SetDiscord(CancellationToken ct, string s) { if (!ct.IsCancellationRequested) DiscordStatus = s; }

        static long ToUnix(DateTime utc) { return (long)(utc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds; }

        void DiscordLoop(string clientId, CancellationToken ct)
        {
            if (!Regex.IsMatch(clientId, @"^\d{15,22}$"))
            {
                DiscordStatus = clientId.Length == 0
                    ? "Add your Discord Application ID in Settings, then press Start again."
                    : "That Application ID doesn't look right (it should be a long number).";
                return;
            }
            var ser = new JavaScriptSerializer();
            DiscordIpc ipc = null;
            string lastKey = null;
            DateTime lastSend = DateTime.MinValue;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (ipc == null)
                    {
                        SetDiscord(ct, "Connecting to Discord…");
                        ipc = new DiscordIpc();
                        ipc.Connect(clientId);
                        lastKey = null;
                        SetDiscord(ct, "Connected as " + ipc.UserName + ". Waiting for heart rate…");
                    }
                    var cfg = Config;
                    int b = CurrentBpm();
                    var act = b > 0 ? Presence.BuildActivity(cfg, b, cfg.ShowElapsed ? (long?)ToUnix(sessionStart) : null) : null;
                    string key = act == null ? "null" : ser.Serialize(act);
                    if (key != lastKey && (DateTime.UtcNow - lastSend).TotalSeconds >= Math.Max(4, cfg.UpdateIntervalSec))
                    {
                        string err = ipc.SetActivity(act);
                        lastSend = DateTime.UtcNow;
                        lastKey = key;
                        if (err != null) SetDiscord(ct, "Discord rejected the update: " + err);
                        else if (act == null) SetDiscord(ct, "Connected as " + ipc.UserName + ". No heart rate yet, so your status is hidden.");
                        else SetDiscord(ct, "Live on " + ipc.UserName + "'s profile: " + b + " BPM (sent " + DateTime.Now.ToString("HH:mm:ss") + ")");
                    }
                }
                catch (Exception ex)
                {
                    if (ipc != null) { ipc.Dispose(); ipc = null; }
                    SetDiscord(ct, ex.Message + " Retrying in 5 s…");
                    ct.WaitHandle.WaitOne(5000);
                    continue;
                }
                ct.WaitHandle.WaitOne(500);
            }
            if (ipc != null)
            {
                try { ipc.SetActivity(null); } catch { }
                ipc.Dispose();
            }
        }

        void FetchAppName(string id)
        {
            AppName = null;
            if (!Regex.IsMatch(id ?? "", @"^\d{15,22}$")) return;
            try
            {
                var req = (HttpWebRequest)WebRequest.Create("https://discord.com/api/v10/applications/" + id + "/rpc");
                req.UserAgent = "HeartRatePresence/1.0";
                req.Timeout = 8000;
                using (var resp = req.GetResponse())
                using (var rd = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    var d = Json.Obj(rd.ReadToEnd());
                    AppName = d == null ? null : Json.Get(d, "name") as string;
                }
            }
            catch { }
        }

        // ---- Pulsoid (free): wss://dev.pulsoid.net/api/v1/data/real_time?access_token=...
        async Task PulsoidLoop(string token, CancellationToken ct)
        {
            if (token.Length == 0)
            {
                SetSource(ct, "Add your Pulsoid token in Settings, then press Start again.");
                return;
            }
            int backoff = 2;
            while (!ct.IsCancellationRequested)
            {
                using (var ws = new ClientWebSocket())
                {
                    try
                    {
                        SetSource(ct, "Connecting to Pulsoid…");
                        var uri = new Uri("wss://dev.pulsoid.net/api/v1/data/real_time?access_token=" + Uri.EscapeDataString(token));
                        await ws.ConnectAsync(uri, ct);
                        SetSource(ct, "Connected to Pulsoid. Waiting for your watch (open Pulsoid on it and start)…");
                        backoff = 2;
                        var buf = new byte[8192];
                        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                        {
                            var ms = new MemoryStream();
                            WebSocketReceiveResult r;
                            do
                            {
                                r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                                if (r.MessageType == WebSocketMessageType.Close) break;
                                ms.Write(buf, 0, r.Count);
                            } while (!r.EndOfMessage);
                            if (r.MessageType == WebSocketMessageType.Close) break;
                            int hr = ParsePulsoid(Encoding.UTF8.GetString(ms.ToArray()));
                            if (hr > 0)
                            {
                                PushBpm(hr);
                                SetSource(ct, "Receiving from Pulsoid: " + hr + " BPM (" + DateTime.Now.ToString("HH:mm:ss") + ")");
                            }
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        if (ct.IsCancellationRequested) break;
                        int code = HttpStatusOf(ex);
                        if (code == 401 || code == 403)
                        {
                            SetSource(ct, "Pulsoid rejected the token (HTTP " + code + "). Make a new one at pulsoid.net/ui/keys.");
                            return;
                        }
                        SetSource(ct, "Pulsoid connection problem: " + Innermost(ex).Message);
                    }
                }
                if (ct.IsCancellationRequested) break;
                var msg = SourceStatus;
                SetSource(ct, (msg.StartsWith("Pulsoid connection problem") ? msg + " " : "Pulsoid disconnected. ") + "Retrying in " + backoff + " s…");
                try { await Task.Delay(backoff * 1000, ct); } catch { break; }
                backoff = Math.Min(backoff * 2, 30);
            }
        }

        // ---- Bluetooth LE: the standard Heart Rate service (0x180D) that chest straps and
        //      watch "broadcast" apps (e.g. HeartCast) send. Local, instant, no account.
        public static readonly Guid HrService = new Guid("0000180d-0000-1000-8000-00805f9b34fb");
        public static readonly Guid HrMeasurement = new Guid("00002a37-0000-1000-8000-00805f9b34fb");

        public static int ParseHrMeasurement(byte[] b)
        {
            if (b == null || b.Length < 2) return 0;
            bool wide = (b[0] & 0x01) != 0;
            if (wide) return b.Length >= 3 ? (b[1] | (b[2] << 8)) : 0;
            return b[1];
        }

        async Task BleLoop(string filter, CancellationToken ct)
        {
            filter = (filter ?? "").Trim();
            while (!ct.IsCancellationRequested)
            {
                BluetoothLEDevice dev = null;
                GattDeviceService svc = null;
                GattCharacteristic ch = null;
                Windows.Foundation.TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs> onValue = null;
                try
                {
                    SetSource(ct, "Looking for a Bluetooth heart rate monitor" + (filter.Length > 0 ? " named “" + filter + "”" : "") +
                                  "… Start HeartCast on your watch and keep HeartCast open on your iPhone.");
                    var found = await FindHrDevice(filter, ct);
                    string devName = found.Item2;
                    SetSource(ct, "Found " + devName + ". Connecting…");
                    dev = await WinRt.Await(BluetoothLEDevice.FromBluetoothAddressAsync(found.Item1), ct);
                    if (dev == null) throw new Exception("Windows couldn't open " + devName + ".");
                    var sr = await WinRt.Await(dev.GetGattServicesForUuidAsync(HrService, BluetoothCacheMode.Uncached), ct);
                    if (sr.Status != GattCommunicationStatus.Success || sr.Services.Count == 0)
                        throw new Exception(devName + " didn't offer heart rate (" + sr.Status + ").");
                    svc = sr.Services[0];
                    var cr = await WinRt.Await(svc.GetCharacteristicsForUuidAsync(HrMeasurement, BluetoothCacheMode.Uncached), ct);
                    if (cr.Status != GattCommunicationStatus.Success || cr.Characteristics.Count == 0)
                        throw new Exception(devName + " didn't offer heart rate readings (" + cr.Status + ").");
                    ch = cr.Characteristics[0];

                    long lastTicks = DateTime.UtcNow.Ticks;
                    onValue = (s, a) =>
                    {
                        int hr = ParseHrMeasurement(WinRt.Bytes(a.CharacteristicValue));
                        if (hr <= 0) return;
                        Interlocked.Exchange(ref lastTicks, DateTime.UtcNow.Ticks);
                        PushBpm(hr);
                        SetSource(ct, "Receiving from " + devName + " over Bluetooth: " + hr + " BPM (" + DateTime.Now.ToString("HH:mm:ss") + ")");
                    };
                    ch.ValueChanged += onValue;
                    var st = await WinRt.Await(ch.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify), ct);
                    if (st != GattCommunicationStatus.Success) throw new Exception("Couldn't subscribe to " + devName + " (" + st + ").");
                    SetSource(ct, "Connected to " + devName + " over Bluetooth. Waiting for heart rate…");
                    Interlocked.Exchange(ref lastTicks, DateTime.UtcNow.Ticks);

                    while (!ct.IsCancellationRequested)
                    {
                        await Task.Delay(1000, ct);
                        if ((DateTime.UtcNow - new DateTime(Interlocked.Read(ref lastTicks))).TotalSeconds > 20)
                        {
                            SetSource(ct, devName + " stopped sending. Looking for it again…");
                            break;
                        }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    if (ct.IsCancellationRequested) break;
                    SetSource(ct, "Bluetooth: " + Innermost(ex).Message + " Retrying in 3 s…");
                    try { await Task.Delay(3000, ct); } catch { break; }
                }
                finally
                {
                    try { if (ch != null && onValue != null) ch.ValueChanged -= onValue; } catch { }
                    try { if (svc != null) svc.Dispose(); } catch { }
                    try { if (dev != null) dev.Dispose(); } catch { }
                }
            }
        }

        /// <summary>Scans until a device advertising the heart rate service (or matching the name filter) shows up.</summary>
        public static async Task<Tuple<ulong, string>> FindHrDevice(string filter, CancellationToken ct)
        {
            var names = new Dictionary<ulong, string>();
            var hasHr = new HashSet<ulong>();
            var tcs = new TaskCompletionSource<Tuple<ulong, string>>();
            var watcher = new BluetoothLEAdvertisementWatcher();
            watcher.ScanningMode = BluetoothLEScanningMode.Active;
            watcher.Received += (w, a) =>
            {
                lock (names)
                {
                    ulong addr = a.BluetoothAddress;
                    string nm = a.Advertisement.LocalName;
                    if (!string.IsNullOrEmpty(nm)) names[addr] = nm;
                    if (a.Advertisement.ServiceUuids.Contains(HrService)) hasHr.Add(addr);
                    string known;
                    names.TryGetValue(addr, out known);
                    bool nameOk = known != null && filter.Length > 0 && known.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
                    // HR service advertised: take it (if a filter is set, the name must match).
                    // No HR service visible (e.g. iPhone app in background): accept only on a name match.
                    bool take = hasHr.Contains(addr) ? (filter.Length == 0 || nameOk) : nameOk;
                    if (take) tcs.TrySetResult(Tuple.Create(addr, known ?? ("heart rate monitor " + addr.ToString("X12"))));
                }
            };
            watcher.Stopped += (w, a) =>
            {
                if (a.Error == BluetoothError.RadioNotAvailable) tcs.TrySetException(new Exception("Bluetooth is turned off on this PC. Turn it on in Windows Settings."));
                else if (a.Error != BluetoothError.Success) tcs.TrySetException(new Exception("Bluetooth scan stopped (" + a.Error + ")."));
            };
            watcher.Start();
            try
            {
                using (ct.Register(() => tcs.TrySetCanceled()))
                    return await tcs.Task;
            }
            finally
            {
                try { watcher.Stop(); } catch { }
            }
        }

        // ---- HypeRate (free Apple Watch app + free "Research & Indie" API key)
        // Phoenix channels over wss://app.hyperate.io/socket/websocket?token=KEY, join topic "hr:<ID>",
        // keep-alive every 10 s, updates arrive as event "hr_update" with payload {"hr": 79}.
        async Task HypeRateLoop(string key, string id, CancellationToken ct)
        {
            if (key.Length == 0 || id.Length == 0)
            {
                SetSource(ct, "Add your HypeRate API key and your HypeRate ID in Settings, then press Start again.");
                return;
            }
            var ser = new JavaScriptSerializer();
            string topic = "hr:" + id;
            int backoff = 2;
            while (!ct.IsCancellationRequested)
            {
                using (var ws = new ClientWebSocket())
                using (var hbCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    var sendLock = new SemaphoreSlim(1, 1);
                    try
                    {
                        SetSource(ct, "Connecting to HypeRate…");
                        await ws.ConnectAsync(new Uri("wss://app.hyperate.io/socket/websocket?token=" + Uri.EscapeDataString(key)), ct);
                        int refNo = 1;
                        await SendText(ws, sendLock, "{\"topic\":" + ser.Serialize(topic) + ",\"event\":\"phx_join\",\"payload\":{},\"ref\":1}", ct);
                        var hbToken = hbCts.Token;
                        var heartbeat = Task.Run(async () =>
                        {
                            while (!hbToken.IsCancellationRequested && ws.State == WebSocketState.Open)
                            {
                                await Task.Delay(10000, hbToken);
                                await SendText(ws, sendLock, "{\"topic\":\"phoenix\",\"event\":\"heartbeat\",\"payload\":{},\"ref\":" + Interlocked.Increment(ref refNo) + "}", hbToken);
                            }
                        });
                        backoff = 2;
                        bool gotData = false;
                        var buf = new byte[8192];
                        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                        {
                            var ms = new MemoryStream();
                            WebSocketReceiveResult r;
                            do
                            {
                                r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                                if (r.MessageType == WebSocketMessageType.Close) break;
                                ms.Write(buf, 0, r.Count);
                            } while (!r.EndOfMessage);
                            if (r.MessageType == WebSocketMessageType.Close) break;

                            var d = Json.Obj(Encoding.UTF8.GetString(ms.ToArray()));
                            if (d == null) continue;
                            string ev = Convert.ToString(Json.Get(d, "event"));
                            string tp = Convert.ToString(Json.Get(d, "topic"));
                            if (ev == "hr_update")
                            {
                                int hr = 0;
                                try { hr = Convert.ToInt32(Json.Get(d, "payload", "hr")); } catch { }
                                if (hr > 0)
                                {
                                    gotData = true;
                                    PushBpm(hr);
                                    SetSource(ct, "Receiving from HypeRate: " + hr + " BPM (" + DateTime.Now.ToString("HH:mm:ss") + ")");
                                }
                            }
                            else if (ev == "phx_reply" && tp == topic)
                            {
                                string status = Convert.ToString(Json.Get(d, "payload", "status"));
                                if (status == "ok")
                                {
                                    if (!gotData) SetSource(ct, "Connected to HypeRate (ID " + id + "). Waiting for your watch: open HypeRate on it and start…");
                                }
                                else
                                {
                                    var resp = Json.Get(d, "payload", "response");
                                    SetSource(ct, "HypeRate wouldn't open ID \"" + id + "\": " + (resp == null ? status : ser.Serialize(resp)));
                                }
                            }
                            else if (ev == "phx_error" || ev == "phx_close")
                            {
                                if (tp == topic) break;
                            }
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        if (ct.IsCancellationRequested) break;
                        int code = HttpStatusOf(ex);
                        if (code == 401 || code == 403)
                        {
                            SetSource(ct, "HypeRate rejected the API key (HTTP " + code + "). Check the key from HypeRate's email, or request a new one.");
                            return;
                        }
                        SetSource(ct, "HypeRate connection problem: " + Innermost(ex).Message);
                    }
                    finally { try { hbCts.Cancel(); } catch { } }
                }
                if (ct.IsCancellationRequested) break;
                var msg = SourceStatus;
                SetSource(ct, (msg.StartsWith("HypeRate connection problem") ? msg + " " : "HypeRate disconnected. ") + "Retrying in " + backoff + " s…");
                try { await Task.Delay(backoff * 1000, ct); } catch { break; }
                backoff = Math.Min(backoff * 2, 30);
            }
        }

        static async Task SendText(ClientWebSocket ws, SemaphoreSlim gate, string text, CancellationToken ct)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            await gate.WaitAsync(ct);
            try { await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct); }
            finally { gate.Release(); }
        }

        static int ParsePulsoid(string text)
        {
            try
            {
                var d = Json.Obj(text);
                var v = d == null ? null : Json.Get(d, "data", "heart_rate");
                if (v != null) return Convert.ToInt32(v);
            }
            catch { }
            int n;
            return int.TryParse(text.Trim(), out n) ? n : 0;
        }

        static Exception Innermost(Exception ex) { while (ex.InnerException != null) ex = ex.InnerException; return ex; }

        static int HttpStatusOf(Exception ex)
        {
            for (var e = ex; e != null; e = e.InnerException)
            {
                var we = e as WebException;
                if (we != null && we.Response is HttpWebResponse) return (int)((HttpWebResponse)we.Response).StatusCode;
                var m = Regex.Match(e.Message, @"\b(401|403)\b");
                if (m.Success) return int.Parse(m.Value);
            }
            return 0;
        }

        // ---- Local HTTP: any app/shortcut can send  GET /?bpm=80  or POST a body like "80", "heartRate:80", {"heart_rate":80}
        async Task HttpLoop(int port, CancellationToken ct)
        {
            TcpListener listener;
            try
            {
                listener = new TcpListener(IPAddress.Any, port);
                listener.Start();
            }
            catch (Exception ex)
            {
                SetSource(ct, "Can't listen on port " + port + ": " + ex.Message);
                return;
            }
            ct.Register(() => { try { listener.Stop(); } catch { } });
            SetSource(ct, "Listening on " + string.Join(", ", LocalUrls(port)) + ". Waiting for heart rate…");
            while (!ct.IsCancellationRequested)
            {
                TcpClient c;
                try { c = await listener.AcceptTcpClientAsync(); }
                catch { break; }
                var client = c;
                var _ = Task.Run(() => HandleHttp(client, ct));
            }
        }

        public static List<string> LocalUrls(int port)
        {
            var list = new List<string>();
            try
            {
                foreach (var a in Dns.GetHostAddresses(Dns.GetHostName()))
                    if (a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
                        list.Add("http://" + a + ":" + port + "/");
            }
            catch { }
            if (list.Count == 0) list.Add("http://localhost:" + port + "/");
            return list;
        }

        void HandleHttp(TcpClient c, CancellationToken ct)
        {
            using (c)
            {
                try
                {
                    c.ReceiveTimeout = 3000; c.SendTimeout = 3000;
                    var s = c.GetStream();
                    var data = new MemoryStream();
                    var buf = new byte[4096];
                    int headerEnd = -1, contentLength = 0;
                    while (true)
                    {
                        int n = s.Read(buf, 0, buf.Length);
                        if (n <= 0) break;
                        data.Write(buf, 0, n);
                        var text = Encoding.UTF8.GetString(data.ToArray());
                        if (headerEnd < 0)
                        {
                            headerEnd = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                            if (headerEnd >= 0)
                            {
                                var m = Regex.Match(text.Substring(0, headerEnd), @"(?im)^content-length:\s*(\d+)");
                                if (m.Success) contentLength = int.Parse(m.Groups[1].Value);
                            }
                        }
                        if (headerEnd >= 0 && data.Length >= headerEnd + 4 + contentLength) break;
                        if (data.Length > 65536) break;
                    }
                    var req = Encoding.UTF8.GetString(data.ToArray());
                    string firstLine = req.Split('\n')[0].Trim();
                    string body = headerEnd >= 0 ? req.Substring(Math.Min(req.Length, headerEnd + 4)) : "";
                    string path = firstLine.Split(' ').Length > 1 ? firstLine.Split(' ')[1] : "/";

                    int hr = ParseHttpBpm(path, body);
                    string reply;
                    if (hr > 0)
                    {
                        PushBpm(hr);
                        SetSource(ct, "Receiving over local HTTP: " + hr + " BPM (" + DateTime.Now.ToString("HH:mm:ss") + ")");
                        reply = "ok " + hr;
                    }
                    else
                    {
                        int cur = LiveBpm();
                        reply = "Heart Rate Presence is running. Current BPM: " + (cur > 0 ? cur.ToString() : "none yet") +
                                "\nSend a heart rate with  GET /?bpm=80  or POST a body like  80  or  {\"heart_rate\":80}";
                    }
                    var bytes = Encoding.UTF8.GetBytes(reply);
                    var head = "HTTP/1.1 200 OK\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: " + bytes.Length +
                               "\r\nAccess-Control-Allow-Origin: *\r\nConnection: close\r\n\r\n";
                    var hb = Encoding.ASCII.GetBytes(head);
                    s.Write(hb, 0, hb.Length);
                    s.Write(bytes, 0, bytes.Length);
                }
                catch { }
            }
        }

        public static int ParseHttpBpm(string path, string body)
        {
            var m = Regex.Match(path ?? "", @"(?i)[?&](?:bpm|hr|heart_?rate|heartrate)=(\d{2,3})");
            if (m.Success) return int.Parse(m.Groups[1].Value);
            body = (body ?? "").Trim();
            int n;
            if (int.TryParse(body, out n)) return n;
            m = Regex.Match(body, @"(?i)(?:bpm|hr|heart_?rate|heartrate)""?\s*[:=]\s*""?(\d{2,3})");
            if (m.Success) return int.Parse(m.Groups[1].Value);
            return 0;
        }
    }

    // ------------------------------------------------------------------ art

    static class Art
    {
        public static readonly string[] DefaultKeys = { "hr_resting", "hr_normal", "hr_elevated", "hr_high", "hr_max" };
        static readonly Color[] Colors =
        {
            Color.FromArgb(0x4F, 0x8B, 0xF0), Color.FromArgb(0x2F, 0xB3, 0x6D), Color.FromArgb(0xF2, 0xB1, 0x2E),
            Color.FromArgb(0xF2, 0x73, 0x2A), Color.FromArgb(0xE3, 0x2F, 0x3C),
        };

        public static Color ColorFor(int idx) { return Colors[Math.Max(0, Math.Min(Colors.Length - 1, idx))]; }

        public static int KeyIndex(string key)
        {
            // accepts "hr_high" or a link ending in ".../hr_high.png"
            var k = (key ?? "").Trim().ToLowerInvariant();
            int slash = k.LastIndexOf('/');
            if (slash >= 0) k = k.Substring(slash + 1);
            if (k.EndsWith(".png")) k = k.Substring(0, k.Length - 4);
            return Array.IndexOf(DefaultKeys, k);
        }

        public static GraphicsPath Heart(RectangleF r)
        {
            Func<float, float, PointF> P = (x, y) => new PointF(r.X + x / 100f * r.Width, r.Y + y / 100f * r.Height);
            var g = new GraphicsPath();
            g.AddBezier(P(50, 28), P(42, 8), P(8, 6), P(6, 34));
            g.AddBezier(P(6, 34), P(5, 58), P(32, 74), P(50, 92));
            g.AddBezier(P(50, 92), P(68, 74), P(95, 58), P(94, 34));
            g.AddBezier(P(94, 34), P(92, 6), P(58, 8), P(50, 28));
            g.CloseFigure();
            return g;
        }

        static Color Lighten(Color c, float f)
        {
            return Color.FromArgb(c.A, (int)(c.R + (255 - c.R) * f), (int)(c.G + (255 - c.G) * f), (int)(c.B + (255 - c.B) * f));
        }

        public static void DrawHeart(Graphics g, RectangleF r, Color c, int ecgLevel)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Heart(r))
            {
                using (var br = new LinearGradientBrush(new RectangleF(r.X, r.Y, r.Width, r.Height + 1), Lighten(c, 0.22f), c, 90f))
                    g.FillPath(br, path);
                // soft gloss on the left lobe
                using (var gloss = new GraphicsPath())
                {
                    gloss.AddEllipse(r.X + r.Width * 0.17f, r.Y + r.Height * 0.16f, r.Width * 0.2f, r.Height * 0.13f);
                    using (var gb = new SolidBrush(Color.FromArgb(70, 255, 255, 255))) g.FillPath(gb, gloss);
                }
            }
            if (ecgLevel < 0) return;
            float amp = 7 + ecgLevel * 5.5f;      // taller spike for higher ranges
            float y0 = 50;
            Func<float, float, PointF> P = (x, y) => new PointF(r.X + x / 100f * r.Width, r.Y + y / 100f * r.Height);
            var pts = new[]
            {
                P(16, y0), P(33, y0), P(37, y0 - amp * 0.22f), P(41, y0), P(45, y0),
                P(49, y0 - amp), P(54, y0 + amp * 0.55f), P(58, y0), P(63, y0),
                P(67, y0 - amp * 0.3f), P(71, y0), P(84, y0),
            };
            using (var pen = new Pen(Color.White, Math.Max(1.5f, r.Width * 0.045f)))
            {
                pen.LineJoin = LineJoin.Round; pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                g.DrawLines(pen, pts);
            }
        }

        public static Bitmap Render(int idx, int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                float pad = size * 0.06f;
                DrawHeart(g, new RectangleF(pad, pad, size - 2 * pad, size - 2 * pad), ColorFor(idx), idx);
            }
            return bmp;
        }

        public static void ExportAll(string dir)
        {
            Directory.CreateDirectory(dir);
            for (int i = 0; i < DefaultKeys.Length; i++)
                using (var b = Render(i, 1024)) b.Save(Path.Combine(dir, DefaultKeys[i] + ".png"), ImageFormat.Png);
        }

        public static Icon MakeIcon()
        {
            using (var b = Render(4, 64)) return Icon.FromHandle(b.GetHicon());
        }

        // .ico with embedded PNGs (Vista+), used for the .exe icon
        public static void WriteIco(string file)
        {
            int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
            var pngs = sizes.Select(s => { using (var b = Render(4, s)) using (var ms = new MemoryStream()) { b.Save(ms, ImageFormat.Png); return ms.ToArray(); } }).ToList();
            using (var fs = File.Create(file))
            using (var w = new BinaryWriter(fs))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
                    w.Write(pngs[i].Length); w.Write(offset);
                    offset += pngs[i].Length;
                }
                foreach (var p in pngs) w.Write(p);
            }
        }
    }

    // ------------------------------------------------------------------ UI controls

    class HeartView : Control
    {
        public int Bpm;
        public bool Stale;
        public Color HeartColor = Color.Gray;
        public int Level = -1;
        double phase; DateTime last = DateTime.UtcNow;

        public HeartView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        }

        public void Tick()
        {
            var now = DateTime.UtcNow;
            double dt = (now - last).TotalSeconds; last = now;
            if (Bpm > 0 && !Stale) phase += dt * Bpm / 60.0;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            double f = phase - Math.Floor(phase);
            double pulse = (Bpm > 0 && !Stale) ? Math.Exp(-f * 7) : 0;
            float baseSize = Math.Min(Width * 0.86f, Height * 0.95f);
            float size = baseSize * (float)(0.92 + 0.08 * pulse);
            var r = new RectangleF((Width - size) / 2f, (Height - size) / 2f, size, size);
            DrawHeartNoEcg(g, r);

            string num = Bpm > 0 ? Bpm.ToString() : "--";
            using (var big = new Font("Segoe UI Semibold", Math.Max(8f, baseSize * 0.2f), FontStyle.Bold, GraphicsUnit.Pixel))
            using (var small = new Font("Segoe UI", Math.Max(6f, baseSize * 0.075f), FontStyle.Bold, GraphicsUnit.Pixel))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                float cy = r.Y + r.Height * 0.47f;
                g.DrawString(num, big, Brushes.White, new PointF(Width / 2f, cy), sf);
                g.DrawString(Stale ? "NO SIGNAL" : "BPM", small, new SolidBrush(Color.FromArgb(220, 255, 255, 255)), new PointF(Width / 2f, cy + baseSize * 0.15f), sf);
            }
        }

        void DrawHeartNoEcg(Graphics g, RectangleF r)
        {
            var c = (Bpm > 0 && !Stale) ? HeartColor : Color.FromArgb(0x9A, 0x9A, 0xA2);
            Art.DrawHeart(g, r, c, -1);
        }
    }

    class PreviewCard : Control
    {
        public string Header = "PLAYING";
        public string AppName = "Your app name";
        public string Details = "", State = "", LargeImage = "", SmallImage = "";
        public string Elapsed = "";
        public bool Empty = true;
        readonly Dictionary<int, Bitmap> cache = new Dictionary<int, Bitmap>();

        public PreviewCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        }

        Bitmap ArtFor(int idx, int size)
        {
            Bitmap b;
            if (!cache.TryGetValue(idx, out b) || b.Width != size)
            {
                if (b != null) b.Dispose();
                b = Art.Render(idx, size);
                cache[idx] = b;
            }
            return b;
        }

        static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath(); float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : SystemColors.Control);
            float s = DeviceDpi / 96f;
            var card = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var p = Round(card, 10 * s))
            using (var bg = new SolidBrush(Color.FromArgb(0x23, 0x24, 0x28)))
                g.FillPath(bg, p);

            int pad = (int)(14 * s);
            var gray = Color.FromArgb(0xB5, 0xBA, 0xC1);
            using (var hf = new Font("Segoe UI", 8f, FontStyle.Bold))
                TextRenderer.DrawText(g, Header, hf, new Point(pad, pad), gray);

            if (Empty)
            {
                using (var f = new Font("Segoe UI", 9.5f))
                    TextRenderer.DrawText(g, "Nothing is showing on Discord yet (no heart rate).", f,
                        new Rectangle(pad, (int)(40 * s), Width - 2 * pad, Height - (int)(50 * s)), gray, TextFormatFlags.WordBreak);
                return;
            }

            int img = (int)(84 * s);
            int top = pad + (int)(24 * s);
            int x = pad;
            if (!string.IsNullOrWhiteSpace(LargeImage))
            {
                var ir = new Rectangle(pad, top, img, img);
                int idx = Art.KeyIndex(LargeImage);
                if (idx >= 0) g.DrawImage(ArtFor(idx, img), ir);
                else
                {
                    using (var p = Round(ir, 8 * s))
                    using (var b = new SolidBrush(Color.FromArgb(0x3A, 0x3C, 0x43))) g.FillPath(b, p);
                    using (var f = new Font("Segoe UI", 7.5f))
                        TextRenderer.DrawText(g, "your image", f, ir, gray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                }
                if (!string.IsNullOrWhiteSpace(SmallImage))
                {
                    int sm = (int)(28 * s);
                    var sr = new Rectangle(ir.Right - sm + (int)(4 * s), ir.Bottom - sm + (int)(4 * s), sm, sm);
                    using (var b = new SolidBrush(Color.FromArgb(0x23, 0x24, 0x28))) g.FillEllipse(b, Rectangle.Inflate(sr, (int)(3 * s), (int)(3 * s)));
                    int sidx = Art.KeyIndex(SmallImage);
                    if (sidx >= 0) g.DrawImage(ArtFor(sidx, sm), sr);
                    else using (var b = new SolidBrush(Color.FromArgb(0x5A, 0x5C, 0x63))) g.FillEllipse(b, sr);
                }
                x = pad + img + (int)(12 * s);
            }

            int w = Width - x - pad;
            int lh = (int)(20 * s);
            int y = top + (int)(2 * s);
            var flags = TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            using (var bold = new Font("Segoe UI", 10f, FontStyle.Bold))
            using (var reg = new Font("Segoe UI", 9.5f))
            {
                TextRenderer.DrawText(g, AppName, bold, new Rectangle(x, y, w, lh), Color.White, flags); y += lh;
                if (!string.IsNullOrEmpty(Details)) { TextRenderer.DrawText(g, Details, reg, new Rectangle(x, y, w, lh), Color.FromArgb(0xDB, 0xDE, 0xE1), flags); y += lh; }
                if (!string.IsNullOrEmpty(State)) { TextRenderer.DrawText(g, State, reg, new Rectangle(x, y, w, lh), Color.FromArgb(0xDB, 0xDE, 0xE1), flags); y += lh; }
                if (!string.IsNullOrEmpty(Elapsed)) TextRenderer.DrawText(g, Elapsed, reg, new Rectangle(x, y, w, lh), Color.FromArgb(0x23, 0xA5, 0x5A), flags);
            }
        }
    }

    // ------------------------------------------------------------------ main window

    class MainForm : Form
    {
        readonly Engine engine = new Engine();
        AppConfig cfg;
        bool dirty;
        DateTime previewStart = DateTime.UtcNow;

        TabControl tabs;
        HeartView heart;
        PreviewCard preview;
        Label lblTier, lblSource, lblDiscord, lblTestVal, lblSaved;
        Button btnStart, btnSave;
        CheckBox chkTest;
        TrackBar trkTest;

        DataGridView grid;
        BindingList<Tier> tiers;

        TextBox txtHrKey, txtHrId, txtBleName;
        static readonly string[] SourceKeys = { "Bluetooth", "HypeRate", "Pulsoid", "LocalHttp" };
        CheckBox chkShowHrKey;
        TextBox txtClientId, txtToken, txtB1L, txtB1U, txtB2L, txtB2U;
        ComboBox cmbSource, cmbType, cmbDisplay;
        NumericUpDown numPort, numInterval, numStale;
        CheckBox chkElapsed, chkAutoStart, chkTray, chkShowToken;
        Label lblHttpUrls;
        NotifyIcon tray;
        System.Windows.Forms.Timer uiTimer;

        int S(int px) { return (int)Math.Round(px * DeviceDpi / 96.0); }

        readonly bool startHidden;
        bool startupDone;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        protected override void SetVisibleCore(bool value)
        {
            // Launched by Windows at sign-in: go straight to the tray without flashing the window.
            if (startHidden && !IsHandleCreated)
            {
                CreateHandle();
                value = false;
                BeginInvoke(new Action(StartupActions));
            }
            base.SetVisibleCore(value);
        }

        void StartupActions()
        {
            if (startupDone) return;
            startupDone = true;
            RefreshLive();
            if (cfg.AutoStart && !engine.Running) ToggleRun();
        }

        public MainForm(AppConfig config, string loadWarning, bool hidden = false)
        {
            startHidden = hidden;
            cfg = config;
            engine.Config = cfg.Clone();
            Text = AppInfo.Name + " " + AppInfo.Version;
            Font = new Font("Segoe UI", 9f);
            AutoScaleMode = AutoScaleMode.Dpi;
            Icon = Art.MakeIcon();
            ClientSize = new Size(S(780), S(560));
            MinimumSize = new Size(S(640), S(480));
            StartPosition = FormStartPosition.CenterScreen;

            tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(S(14), S(5)) };
            tabs.TabPages.Add(BuildLiveTab());
            tabs.TabPages.Add(BuildRangesTab());
            tabs.TabPages.Add(BuildSettingsTab());

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(S(8)) };
            btnSave = new Button { Text = "Save", AutoSize = true, Padding = new Padding(S(10), S(2), S(10), S(2)) };
            btnSave.Click += (s, e) => SaveAll(true);
            lblSaved = new Label { AutoSize = true, Margin = new Padding(S(4), S(9), S(10), 0), ForeColor = Color.DimGray };
            bottom.Controls.Add(btnSave);
            bottom.Controls.Add(lblSaved);

            Controls.Add(bottom);
            Controls.Add(tabs);
            tabs.BringToFront();
            AcceptButton = null;

            tray = new NotifyIcon { Icon = Icon, Text = "Heart Rate Presence", Visible = true };
            var menu = new ContextMenuStrip();
            menu.Items.Add("Open", null, (s, e) => RestoreFromTray());
            menu.Items.Add("Start / Stop", null, (s, e) => ToggleRun());
            menu.Items.Add("Quit", null, (s, e) => { quitting = true; Close(); });
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += (s, e) => RestoreFromTray();

            LoadIntoUi();
            PullFromUi();
            savedSnapshot = Snapshot();
            loading = false;
            dirty = false;
            UpdateSavedLabel();
            if (loadWarning != null) lblSaved.Text = loadWarning;

            uiTimer = new System.Windows.Forms.Timer { Interval = 33 };
            int frame = 0;
            uiTimer.Tick += (s, e) => { heart.Tick(); if (++frame % 6 == 0) RefreshLive(); };
            uiTimer.Start();

            Shown += (s, e) => StartupActions();
            Resize += (s, e) =>
            {
                if (WindowState == FormWindowState.Minimized && chkTray.Checked)
                {
                    Hide();
                    tray.ShowBalloonTip(2000, "Heart Rate Presence", "Still running in the tray.", ToolTipIcon.None);
                }
            };
        }

        bool quitting;

        void RestoreFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        // ---------------- Live tab

        TabPage BuildLiveTab()
        {
            var page = new TabPage("Live") { Padding = new Padding(S(10)), UseVisualStyleBackColor = false, BackColor = Color.White };
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(250)));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            heart = new HeartView { Dock = DockStyle.Fill, BackColor = Color.White };
            lblTier = new Label { Dock = DockStyle.Fill, AutoSize = true, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 11f, FontStyle.Bold), Padding = new Padding(0, S(4), 0, S(4)) };
            left.Controls.Add(heart, 0, 0);
            left.Controls.Add(lblTier, 0, 1);

            var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(S(10), 0, 0, 0) };
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, S(150)));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            right.Controls.Add(new Label { Text = "How it looks on your Discord profile", AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold), Margin = new Padding(0, 0, 0, S(6)) });
            preview = new PreviewCard { Dock = DockStyle.Fill };
            right.Controls.Add(preview);

            right.Controls.Add(new Label { Text = "Heart rate source", AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold), Margin = new Padding(0, S(14), 0, S(2)) });
            lblSource = new Label { AutoSize = true, MaximumSize = new Size(S(470), 0) };
            right.Controls.Add(lblSource);
            var dl = new Label { Text = "Discord", AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold), Margin = new Padding(0, S(10), 0, S(2)) };
            lblDiscord = new Label { AutoSize = true, MaximumSize = new Size(S(470), 0) };
            var dpanel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            dpanel.Controls.Add(dl); dpanel.Controls.Add(lblDiscord);
            right.Controls.Add(dpanel);
            right.SizeChanged += (s, e) =>
            {
                var w = Math.Max(S(200), right.ClientSize.Width - S(20));
                lblSource.MaximumSize = new Size(w, 0);
                lblDiscord.MaximumSize = new Size(w, 0);
            };

            var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Padding = new Padding(0, S(8), 0, 0) };
            btnStart = new Button { Text = "Start", AutoSize = true, Padding = new Padding(S(18), S(4), S(18), S(4)), Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
            btnStart.Click += (s, e) => ToggleRun();
            chkTest = new CheckBox { Text = "Test with a fake BPM:", AutoSize = true, Margin = new Padding(S(24), S(9), 0, 0) };
            trkTest = new TrackBar { Minimum = 40, Maximum = 200, Value = 75, TickFrequency = 10, Width = S(220), Enabled = false, AutoSize = false, Height = S(32), Margin = new Padding(0, S(5), 0, 0) };
            lblTestVal = new Label { AutoSize = true, Margin = new Padding(0, S(9), 0, 0), Text = "75", Enabled = false };
            chkTest.CheckedChanged += (s, e) => { trkTest.Enabled = lblTestVal.Enabled = chkTest.Checked; ApplyTest(); };
            trkTest.ValueChanged += (s, e) => { lblTestVal.Text = trkTest.Value.ToString(); ApplyTest(); };
            bar.Controls.Add(btnStart);
            bar.Controls.Add(chkTest);
            bar.Controls.Add(trkTest);
            bar.Controls.Add(lblTestVal);

            root.Controls.Add(left, 0, 0);
            root.Controls.Add(right, 1, 0);
            root.Controls.Add(bar, 0, 1);
            root.SetColumnSpan(bar, 2);
            page.Controls.Add(root);
            return page;
        }

        void ApplyTest() { engine.TestBpm = chkTest.Checked ? trkTest.Value : 0; RefreshLive(); }

        void ToggleRun()
        {
            if (engine.Running)
            {
                engine.Stop();
                btnStart.Text = "Start";
            }
            else
            {
                PullFromUi();
                engine.Config = cfg.Clone();
                if (dirty) SaveAll(false);
                previewStart = DateTime.UtcNow;
                engine.Start();
                btnStart.Text = "Stop";
            }
            RefreshLive();
        }

        void RefreshLive()
        {
            var ec = engine.Config;
            int bpm = engine.CurrentBpm();
            int shown = bpm > 0 ? bpm : 0;
            heart.Bpm = shown;
            heart.Stale = engine.Running && bpm == 0;
            var r = shown > 0 ? Presence.Resolve(ec, shown) : null;
            if (r != null)
            {
                heart.HeartColor = Art.ColorFor(r.Index);
                var sorted = Presence.Sorted(ec);
                string max = r.Index + 1 < sorted.Count ? "–" + (sorted[r.Index + 1].MinBpm - 1) : "+";
                lblTier.Text = (string.IsNullOrEmpty(r.Tier.Name) ? "Range" : r.Tier.Name) + "  (" + r.Tier.MinBpm + max + " BPM)";
                lblTier.ForeColor = Art.ColorFor(r.Index);
            }
            else
            {
                lblTier.Text = engine.Running ? "waiting for heart rate…" : "not running";
                lblTier.ForeColor = Color.Gray;
            }

            preview.Empty = r == null;
            int ti = Array.IndexOf(Presence.TypeValues, ec.ActivityType);
            preview.Header = (ti >= 0 ? Presence.TypeNames[ti] : "Playing").ToUpperInvariant();
            preview.AppName = !string.IsNullOrEmpty(engine.AppName) ? engine.AppName : "Heart Rate";
            if (r != null)
            {
                preview.Details = r.Details; preview.State = r.State;
                preview.LargeImage = r.LargeImage; preview.SmallImage = r.SmallImage;
                if (ec.ShowElapsed)
                {
                    var el = DateTime.UtcNow - previewStart;
                    preview.Elapsed = (el.TotalHours >= 1 ? ((int)el.TotalHours) + ":" : "") + el.Minutes.ToString("00") + ":" + el.Seconds.ToString("00") + " elapsed";
                }
                else preview.Elapsed = "";
            }
            preview.Invalidate();

            lblSource.Text = engine.TestBpm > 0 && engine.Running
                ? "Test BPM is on, so the real heart rate is ignored. (" + engine.SourceStatus + ")"
                : engine.SourceStatus;
            lblDiscord.Text = engine.DiscordStatus;
            btnStart.Text = engine.Running ? "Stop" : "Start";

            string tip = engine.Running ? (bpm > 0 ? "♥ " + bpm + " BPM" : "♥ waiting for heart rate") : "Heart Rate Presence (stopped)";
            if (tray.Text != tip) tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
        }

        // ---------------- Ranges tab

        TabPage BuildRangesTab()
        {
            var page = new TabPage("BPM ranges") { Padding = new Padding(S(10)), UseVisualStyleBackColor = true };
            var help = new Label
            {
                Dock = DockStyle.Top, AutoSize = true, MaximumSize = new Size(S(740), 0), Padding = new Padding(0, 0, 0, S(8)),
                Text = "Each range starts at its Min BPM and lasts until the next range starts. Edit any cell; the Live preview updates right away.\n" +
                       "In any text you can write {bpm} (current heart rate), {name}, {min} or {max}.\n" +
                       "Image: paste a direct https:// link to any PNG or GIF (animated GIFs work, e.g. from imgur). Works with the built-in Discord app.\n" +
                       "Using your own Discord app (Settings)? You can also type the name of an Art Asset you uploaded to it."
            };
            grid = new DataGridView
            {
                Dock = DockStyle.Fill, AutoGenerateColumns = false, AllowUserToAddRows = false, RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect, BackgroundColor = SystemColors.Window, BorderStyle = BorderStyle.FixedSingle,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            };
            grid.RowTemplate.Height = S(26);
            AddCol("MinBpm", "Min BPM", 55);
            AddCol("Name", "Name", 70);
            AddCol("Details", "Line 1 (details)", 110);
            AddCol("State", "Line 2 (state)", 120);
            AddCol("LargeImage", "Image", 85);
            AddCol("LargeText", "Image hover text", 110);
            AddCol("SmallImage", "Small image", 75);
            AddCol("SmallText", "Small hover text", 90);
            grid.DataError += (s, e) => { e.ThrowException = false; MessageBox.Show(this, "Min BPM must be a whole number.", Text); };
            grid.CellValueChanged += (s, e) => MarkDirty();
            grid.RowsRemoved += (s, e) => MarkDirty();

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, S(8), 0, 0) };
            var add = new Button { Text = "Add range", AutoSize = true };
            add.Click += (s, e) =>
            {
                int next = tiers.Count == 0 ? 0 : tiers.Max(t => t.MinBpm) + 20;
                var nt = new Tier { MinBpm = next, Name = "New", Details = "❤️ {bpm} BPM", State = "", LargeImage = AppConfig.Img("hr_max"), LargeText = "{bpm} BPM", SmallImage = "", SmallText = "" };
                tiers.Add(nt);
                MarkDirty();
            };
            var remove = new Button { Text = "Remove selected", AutoSize = true };
            remove.Click += (s, e) =>
            {
                if (grid.CurrentRow == null) return;
                if (tiers.Count <= 1) { MessageBox.Show(this, "You need at least one range.", Text); return; }
                tiers.RemoveAt(grid.CurrentRow.Index);
                MarkDirty();
            };
            var sort = new Button { Text = "Sort by BPM", AutoSize = true };
            sort.Click += (s, e) => { grid.EndEdit(); SetTiers(tiers.OrderBy(t => t.MinBpm).ToList()); MarkDirty(); };
            var reset = new Button { Text = "Reset to defaults", AutoSize = true };
            reset.Click += (s, e) =>
            {
                if (MessageBox.Show(this, "Replace your ranges with the default five?", Text, MessageBoxButtons.YesNo) == DialogResult.Yes)
                { SetTiers(AppConfig.DefaultTiers()); MarkDirty(); }
            };
            var export = new Button { Text = "Save the heart icons as PNGs…", AutoSize = true, Margin = new Padding(S(20), S(3), S(3), S(3)) };
            export.Click += (s, e) => ExportIcons();
            buttons.Controls.AddRange(new Control[] { add, remove, sort, reset, export });

            page.Controls.Add(grid);
            page.Controls.Add(buttons);
            page.Controls.Add(help);
            grid.BringToFront();
            return page;
        }

        void AddCol(string prop, string header, int weight)
        {
            var c = new DataGridViewTextBoxColumn { DataPropertyName = prop, HeaderText = header, FillWeight = weight, SortMode = DataGridViewColumnSortMode.NotSortable };
            grid.Columns.Add(c);
        }

        void SetTiers(List<Tier> list)
        {
            tiers = new BindingList<Tier>(list.Select(t => { AppConfig.Normalize(t); return t; }).ToList());
            tiers.ListChanged += (s, e) => MarkDirty();
            grid.DataSource = tiers;
        }

        void ExportIcons()
        {
            using (var dlg = new FolderBrowserDialog { Description = "Pick a folder for hr_resting.png … hr_max.png (1024×1024)" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    Art.ExportAll(dlg.SelectedPath);
                    Process.Start("explorer.exe", "\"" + dlg.SelectedPath + "\"");
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, Text); }
            }
        }

        // ---------------- Settings tab

        TableLayoutPanel form;

        TabPage BuildSettingsTab()
        {
            var page = new TabPage("Settings") { Padding = new Padding(S(10)), UseVisualStyleBackColor = true, AutoScroll = true };
            form = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3 };
            form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            Section("Discord");
            txtClientId = new TextBox { Dock = DockStyle.Fill };
            txtClientId.HandleCreated += (s, e) => SendMessage(txtClientId.Handle, 0x1501 /* EM_SETCUEBANNER */, (IntPtr)1, "optional: blank uses the built-in app");
            Row("Own Application ID", txtClientId, LinkButton("Open Developer Portal", "https://discord.com/developers/applications"));
            cmbType = Combo(Presence.TypeNames);
            Row("Activity type", cmbType, null);
            cmbDisplay = Combo(new[] { "App name (“Playing Heart Rate”)", "Line 2 (state)", "Line 1 (details, e.g. “❤ 82 BPM”)" });
            Row("Member list shows", cmbDisplay, null);
            chkElapsed = new CheckBox { Text = "Show the elapsed-time timer", AutoSize = true };
            Row("", chkElapsed, null);
            numInterval = Num(4, 60);
            Row("Update every (seconds)", numInterval, Hint("Discord's limit: 4 s"));
            numStale = Num(5, 600);
            Row("Hide status after no data for (s)", numStale, null);

            Section("Heart rate source");
            cmbSource = Combo(new[]
            {
                "Bluetooth (free + instant: HeartCast app or a chest strap)",
                "HypeRate (free app, but the API key takes ~2 days)",
                "Pulsoid (needs Pulsoid's paid BRO plan)",
                "Local HTTP (any app or shortcut that sends BPM)",
            });
            cmbSource.DropDownWidth = S(420);
            Row("Source", cmbSource, null);
            txtBleName = new TextBox { Dock = DockStyle.Fill };
            Row("Bluetooth device name", txtBleName, Hint("optional, blank = first one found"));
            txtHrKey = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
            chkShowHrKey = new CheckBox { Text = "show", AutoSize = true, Margin = new Padding(S(6), S(5), 0, 0) };
            chkShowHrKey.CheckedChanged += (s, e) => txtHrKey.UseSystemPasswordChar = !chkShowHrKey.Checked;
            var hrKeyRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            hrKeyRow.Controls.Add(chkShowHrKey);
            hrKeyRow.Controls.Add(LinkButton("Request free key", "https://www.hyperate.io/api-free-request.html"));
            Row("HypeRate API key", txtHrKey, hrKeyRow);
            txtHrId = new TextBox { Dock = DockStyle.Fill };
            Row("HypeRate ID", txtHrId, Hint("from the HypeRate app"));
            txtToken = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
            chkShowToken = new CheckBox { Text = "show", AutoSize = true, Margin = new Padding(S(6), S(5), 0, 0) };
            chkShowToken.CheckedChanged += (s, e) => txtToken.UseSystemPasswordChar = !chkShowToken.Checked;
            var tokRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            tokRow.Controls.Add(chkShowToken);
            tokRow.Controls.Add(LinkButton("Get token", "https://pulsoid.net/ui/keys"));
            Row("Pulsoid token", txtToken, tokRow);
            numPort = Num(1024, 65535);
            lblHttpUrls = new Label { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(S(6), S(6), 0, 0) };
            Row("Local HTTP port", numPort, lblHttpUrls);
            numPort.ValueChanged += (s, e) => UpdateHttpHint();
            cmbSource.SelectedIndexChanged += (s, e) => UpdateSourceEnabled();

            Section("Profile buttons (optional, only other people see them)");
            txtB1L = new TextBox { Dock = DockStyle.Fill }; txtB1U = new TextBox { Dock = DockStyle.Fill };
            txtB2L = new TextBox { Dock = DockStyle.Fill }; txtB2U = new TextBox { Dock = DockStyle.Fill };
            Row("Button 1 label", txtB1L, null); Row("Button 1 link", txtB1U, null);
            Row("Button 2 label", txtB2L, null); Row("Button 2 link", txtB2U, null);

            Section("App");
            chkAutoStart = new CheckBox { Text = "Start sending as soon as the app opens", AutoSize = true };
            chkTray = new CheckBox { Text = "Minimizing hides the app in the system tray", AutoSize = true };
            Row("", chkAutoStart, null);
            Row("", chkTray, null);
            var chkStartup = new CheckBox { Text = "Open when Windows starts (in the tray)", AutoSize = true };
            try { chkStartup.Checked = Setup.StartupEnabled; } catch { }
            chkStartup.CheckedChanged += (s, e) =>
            {
                try { Setup.SetStartup(chkStartup.Checked); }
                catch (Exception ex) { MessageBox.Show(this, "Couldn't change the startup setting: " + ex.Message, Text); }
            };
            Row("", chkStartup, null);
            var appButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, S(6), 0, 0) };
            appButtons.Controls.Add(LinkButton("Updates and help (GitHub)", AppInfo.RepoUrl));
            var uninstall = new Button { Text = "Uninstall…", AutoSize = true, Margin = new Padding(S(6), S(1), 0, S(1)) };
            uninstall.Click += (s, e) =>
            {
                if (MessageBox.Show(this, "Remove Heart Rate Presence from this PC?\n\nThis deletes the app, its Start menu shortcut, the startup entry and your settings. Your Discord status will be cleared.",
                        Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                engine.Stop();
                Setup.Uninstall();
                quitting = true;
                dirty = false;
                Close();
            };
            appButtons.Controls.Add(uninstall);
            Row("", appButtons, null);

            foreach (Control c in form.Controls)
            {
                if (c is TextBox) ((TextBox)c).TextChanged += (s, e) => MarkDirty();
                if (c is ComboBox) ((ComboBox)c).SelectedIndexChanged += (s, e) => MarkDirty();
                if (c is NumericUpDown) ((NumericUpDown)c).ValueChanged += (s, e) => MarkDirty();
                if (c is CheckBox) ((CheckBox)c).CheckedChanged += (s, e) => MarkDirty();
            }
            page.Controls.Add(form);
            return page;
        }

        void Section(string title)
        {
            var l = new Label { Text = title, AutoSize = true, Font = new Font("Segoe UI", 10f, FontStyle.Bold), Margin = new Padding(0, form.RowCount == 0 ? 0 : S(14), 0, S(4)) };
            form.Controls.Add(l, 0, form.RowCount);
            form.SetColumnSpan(l, 3);
            form.RowCount++;
        }

        void Row(string label, Control c, Control extra)
        {
            int r = form.RowCount;
            form.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(S(8), S(6), S(10), S(3)) }, 0, r);
            c.Margin = new Padding(0, S(3), 0, S(3));
            form.Controls.Add(c, 1, r);
            if (extra != null) form.Controls.Add(extra, 2, r);
            form.RowCount++;
        }

        Control LinkButton(string text, string url)
        {
            var b = new Button { Text = text, AutoSize = true, Margin = new Padding(S(6), S(1), 0, S(1)) };
            b.Click += (s, e) => { try { Process.Start(url); } catch { } };
            return b;
        }

        Label Hint(string text) { return new Label { Text = text, AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(S(6), S(6), 0, 0) }; }

        ComboBox Combo(string[] items)
        {
            var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            c.Items.AddRange(items);
            return c;
        }

        NumericUpDown Num(int min, int max) { return new NumericUpDown { Minimum = min, Maximum = max, Width = S(90) }; }

        void UpdateSourceEnabled()
        {
            int src = cmbSource.SelectedIndex;
            txtBleName.Enabled = src == 0;
            txtHrKey.Enabled = chkShowHrKey.Enabled = txtHrId.Enabled = src == 1;
            txtToken.Enabled = chkShowToken.Enabled = src == 2;
            numPort.Enabled = src == 3;
            UpdateHttpHint();
        }

        void UpdateHttpHint()
        {
            lblHttpUrls.Text = cmbSource.SelectedIndex == 3 ? "send to " + Engine.LocalUrls((int)numPort.Value)[0] + "?bpm=80" : "";
        }

        // ---------------- config <-> UI

        void LoadIntoUi()
        {
            txtClientId.Text = cfg.DiscordClientId;
            int ti = Array.IndexOf(Presence.TypeValues, cfg.ActivityType);
            cmbType.SelectedIndex = ti < 0 ? 0 : ti;
            cmbDisplay.SelectedIndex = Math.Max(0, Math.Min(2, cfg.StatusDisplay));
            chkElapsed.Checked = cfg.ShowElapsed;
            numInterval.Value = Math.Max(4, Math.Min(60, cfg.UpdateIntervalSec));
            numStale.Value = Math.Max(5, Math.Min(600, cfg.StaleTimeoutSec));
            cmbSource.SelectedIndex = Math.Max(0, Array.IndexOf(SourceKeys, cfg.Source));
            txtBleName.Text = cfg.BleNameFilter;
            txtHrKey.Text = cfg.HypeRateKey;
            txtHrId.Text = cfg.HypeRateId;
            txtToken.Text = cfg.PulsoidToken;
            numPort.Value = Math.Max(1024, Math.Min(65535, cfg.HttpPort));
            txtB1L.Text = cfg.Button1Label; txtB1U.Text = cfg.Button1Url;
            txtB2L.Text = cfg.Button2Label; txtB2U.Text = cfg.Button2Url;
            chkAutoStart.Checked = cfg.AutoStart;
            chkTray.Checked = cfg.MinimizeToTray;
            SetTiers(cfg.Tiers.Select(t => t).ToList());
            UpdateSourceEnabled();
        }

        void PullFromUi()
        {
            if (grid.IsCurrentCellInEditMode) grid.EndEdit();
            cfg.DiscordClientId = txtClientId.Text.Trim();
            cfg.ActivityType = Presence.TypeValues[Math.Max(0, cmbType.SelectedIndex)];
            cfg.StatusDisplay = Math.Max(0, cmbDisplay.SelectedIndex);
            cfg.ShowElapsed = chkElapsed.Checked;
            cfg.UpdateIntervalSec = (int)numInterval.Value;
            cfg.StaleTimeoutSec = (int)numStale.Value;
            cfg.Source = SourceKeys[Math.Max(0, cmbSource.SelectedIndex)];
            cfg.BleNameFilter = txtBleName.Text.Trim();
            cfg.HypeRateKey = txtHrKey.Text.Trim();
            cfg.HypeRateId = txtHrId.Text.Trim();
            cfg.PulsoidToken = txtToken.Text.Trim();
            cfg.HttpPort = (int)numPort.Value;
            cfg.Button1Label = txtB1L.Text; cfg.Button1Url = txtB1U.Text;
            cfg.Button2Label = txtB2L.Text; cfg.Button2Url = txtB2U.Text;
            cfg.AutoStart = chkAutoStart.Checked;
            cfg.MinimizeToTray = chkTray.Checked;
            cfg.Tiers = tiers.ToList();
        }

        bool loading = true;
        string savedSnapshot = "";

        string Snapshot() { return new JavaScriptSerializer().Serialize(cfg); }

        void MarkDirty()
        {
            if (loading || tiers == null || txtClientId == null) return;
            // Ranges and look apply live; source/ID changes need Start again.
            var oldId = engine.Config.DiscordClientId; var oldSrc = engine.Config.Source; var oldTok = engine.Config.PulsoidToken; var oldPort = engine.Config.HttpPort;
            var oldHrKey = engine.Config.HypeRateKey; var oldHrId = engine.Config.HypeRateId; var oldBle = engine.Config.BleNameFilter;
            PullFromUi();
            dirty = Snapshot() != savedSnapshot;
            var next = cfg.Clone();
            next.DiscordClientId = oldId; next.Source = oldSrc; next.PulsoidToken = oldTok; next.HttpPort = oldPort;
            next.HypeRateKey = oldHrKey; next.HypeRateId = oldHrId; next.BleNameFilter = oldBle;
            engine.Config = next;
            UpdateSavedLabel();
        }

        void UpdateSavedLabel()
        {
            if (lblSaved == null) return;
            lblSaved.Text = dirty ? "Unsaved changes" : "";
            lblSaved.ForeColor = dirty ? Color.DarkOrange : Color.DimGray;
        }

        void SaveAll(bool restartIfNeeded)
        {
            PullFromUi();
            var before = engine.Config;
            bool connChanged = before.DiscordClientId != cfg.DiscordClientId || before.Source != cfg.Source ||
                               before.PulsoidToken != cfg.PulsoidToken || before.HttpPort != cfg.HttpPort ||
                               before.HypeRateKey != cfg.HypeRateKey || before.HypeRateId != cfg.HypeRateId ||
                               before.BleNameFilter != cfg.BleNameFilter;
            try { cfg.Save(); }
            catch (Exception ex) { MessageBox.Show(this, "Couldn't save config.json: " + ex.Message, Text); return; }
            engine.Config = cfg.Clone();
            savedSnapshot = Snapshot();
            dirty = false;
            UpdateSavedLabel();
            lblSaved.Text = "Saved " + DateTime.Now.ToString("HH:mm:ss");
            if (restartIfNeeded && connChanged && engine.Running)
            {
                engine.Stop();
                engine.Start();
            }
            RefreshLive();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (dirty && !quitting && e.CloseReason == CloseReason.UserClosing)
            {
                var r = MessageBox.Show(this, "Save your changes before closing?", Text, MessageBoxButtons.YesNoCancel);
                if (r == DialogResult.Cancel) { e.Cancel = true; return; }
                if (r == DialogResult.Yes) SaveAll(false);
            }
            engine.Stop();
            tray.Visible = false;
            base.OnFormClosing(e);
        }

        // used by --screenshot to render each tab without showing on screen
        public void CaptureTabs(string dir, int testBpm)
        {
            chkTest.Checked = true;
            trkTest.Value = testBpm;
            for (int i = 0; i < tabs.TabCount; i++)
            {
                tabs.SelectedIndex = i;
                Application.DoEvents();
                RefreshLive();
                heart.Tick();
                Application.DoEvents();
                using (var b = new Bitmap(Width, Height))
                {
                    DrawToBitmap(b, new Rectangle(0, 0, Width, Height));
                    b.Save(Path.Combine(dir, "tab" + i + ".png"), ImageFormat.Png);
                }
            }
        }
    }

    // ------------------------------------------------------------------ entry

    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        [STAThread]
        static int Main(string[] args)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            try { SetProcessDPIAware(); } catch { }

            if (args.Length >= 2 && args[0] == "--export-art") { Art.ExportAll(args[1]); return 0; }
            if (args.Length >= 2 && args[0] == "--export-ico") { Art.WriteIco(args[1]); return 0; }
            if (args.Length >= 2 && args[0] == "--selftest") return SelfTest.Run(args[1]);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args.Length >= 2 && args[0] == "--screenshot-welcome")
            {
                using (var wf = new WelcomeForm { StartPosition = FormStartPosition.Manual, Location = new Point(-4000, -4000), ShowInTaskbar = false })
                {
                    wf.Show();
                    Application.DoEvents();
                    using (var b = new Bitmap(wf.Width, wf.Height))
                    {
                        wf.DrawToBitmap(b, new Rectangle(0, 0, wf.Width, wf.Height));
                        b.Save(Path.Combine(args[1], "welcome.png"), ImageFormat.Png);
                    }
                }
                return 0;
            }

            if (args.Length >= 2 && args[0] == "--screenshot")
            {
                string w;
                var f = new MainForm(AppConfig.Load(out w), null) { StartPosition = FormStartPosition.Manual, Location = new Point(-4000, -4000), ShowInTaskbar = false };
                f.Show();
                Application.DoEvents();
                f.CaptureTabs(args[1], args.Length >= 3 ? int.Parse(args[2]) : 128);
                f.Close();
                return 0;
            }

            bool minimized = args.Contains("--minimized");
            bool afterSetup = args.Contains("--after-setup");
            bool created;
            using (var mutex = new Mutex(true, "HeartRatePresence_SingleInstance", out created))
            {
                if (!created && afterSetup)
                {
                    // the copy that just installed us is still closing
                    try { created = mutex.WaitOne(10000); } catch (AbandonedMutexException) { created = true; }
                }
                if (!created)
                {
                    if (!minimized) MessageBox.Show("Heart Rate Presence is already running (look for the heart in the system tray).", AppInfo.Name);
                    return 0;
                }

                bool firstRun = !AppConfig.Exists;
                if (!Setup.IsInstalledCopy && (firstRun || Setup.IsInstalled))
                {
                    if (firstRun)
                    {
                        // First launch of a download: one click installs it for this Windows user.
                        bool startMenu, startup;
                        using (var welcome = new WelcomeForm())
                        {
                            if (welcome.ShowDialog() != DialogResult.OK) return 0;
                            startMenu = welcome.AddStartMenu;
                            startup = welcome.StartWithWindows;
                        }
                        try { AppConfig.CreateDefault().Save(); } catch { }
                        Setup.CopySelfToInstallDir();
                        try { if (startMenu) Setup.CreateStartMenuShortcut(); } catch { }
                        try { Setup.SetStartup(startup); } catch { }
                    }
                    else
                    {
                        // Running a newer download while installed: update the installed copy.
                        Setup.CopySelfToInstallDir();
                    }
                    if (Setup.IsInstalled)
                    {
                        try
                        {
                            mutex.ReleaseMutex();
                            Process.Start(AppInfo.InstalledExe, "--after-setup");
                            return 0;
                        }
                        catch { /* fall through and run from here */ }
                    }
                }
                else if (firstRun)
                {
                    try { AppConfig.CreateDefault().Save(); } catch { }
                }

                string warning;
                var cfg = AppConfig.Load(out warning);
                Application.Run(new MainForm(cfg, warning, minimized));
            }
            return 0;
        }
    }

    // ------------------------------------------------------------------ headless checks (used during development)

    static class SelfTest
    {
        public static int Run(string outFile)
        {
            var log = new StringBuilder();
            int fails = 0;
            Action<bool, string> check = (ok, name) => { log.AppendLine((ok ? "PASS " : "FAIL ") + name); if (!ok) fails++; };

            // range resolution
            var cfg = AppConfig.CreateDefault();
            check(Presence.Resolve(cfg, 45).Tier.Name == "Resting", "45 -> Resting");
            check(Presence.Resolve(cfg, 60).Tier.Name == "Normal", "60 -> Normal");
            check(Presence.Resolve(cfg, 119).Tier.Name == "Elevated", "119 -> Elevated");
            check(Presence.Resolve(cfg, 150).Tier.Name == "Max", "150 -> Max");
            var a = Presence.BuildActivity(cfg, 131, 1700000000);
            var json = new JavaScriptSerializer().Serialize(a);
            log.AppendLine("activity@131: " + json);
            check(json.Contains(" 131 BPM\",\"state\"") && json.Contains("hr_high") && json.Contains("heart going brrr"), "activity text for 131");

            // HTTP body parsing
            check(Engine.ParseHttpBpm("/?bpm=88", "") == 88, "GET ?bpm=88");
            check(Engine.ParseHttpBpm("/", "77") == 77, "POST 77");
            check(Engine.ParseHttpBpm("/", "heartRate:91") == 91, "POST heartRate:91");
            check(Engine.ParseHttpBpm("/", "{\"heart_rate\": 102}") == 102, "POST json");

            // live HTTP listener round trip
            var eng = new Engine();
            var c2 = AppConfig.CreateDefault(); c2.Source = "LocalHttp"; c2.HttpPort = 18765; c2.DiscordClientId = "";
            eng.Config = c2;
            eng.Start();
            Thread.Sleep(500);
            try
            {
                using (var wc = new WebClient())
                {
                    var r1 = wc.DownloadString("http://127.0.0.1:18765/?bpm=97");
                    log.AppendLine("http reply: " + r1);
                    var r2 = wc.UploadString("http://127.0.0.1:18765/", "heartRate:123");
                    log.AppendLine("http reply: " + r2);
                }
                Thread.Sleep(200);
                check(eng.LiveBpm() == 123, "HTTP listener delivered 123 (got " + eng.LiveBpm() + ")");
            }
            catch (Exception ex) { check(false, "HTTP listener: " + ex.Message); }
            Thread.Sleep(300);
            log.AppendLine("discord status w/o id: " + eng.DiscordStatus);
            eng.Stop();

            // Discord IPC framing: a deliberately invalid client id should be refused by Discord, not crash.
            try
            {
                using (var ipc = new DiscordIpc())
                {
                    ipc.Connect("1");
                    log.AppendLine("discord handshake unexpectedly accepted id 1 (user " + ipc.UserName + ")");
                }
            }
            catch (Exception ex) { log.AppendLine("discord handshake with bogus id -> " + ex.Message); }

            // Pulsoid with a bogus token should be rejected (proves websocket/TLS path works)
            var e3 = new Engine();
            var c3 = AppConfig.CreateDefault(); c3.Source = "Pulsoid"; c3.PulsoidToken = "00000000-0000-0000-0000-000000000000"; c3.DiscordClientId = "";
            e3.Config = c3;
            e3.Start();
            for (int i = 0; i < 40 && !e3.SourceStatus.Contains("rejected") && !e3.SourceStatus.Contains("problem"); i++) Thread.Sleep(250);
            log.AppendLine("pulsoid with bogus token -> " + e3.SourceStatus);
            e3.Stop();

            // Bluetooth: heart rate packet parsing + a short real scan to prove the Windows Bluetooth API works here
            check(Engine.ParseHrMeasurement(new byte[] { 0x00, 72 }) == 72, "BLE 8-bit HR");
            check(Engine.ParseHrMeasurement(new byte[] { 0x01, 0x2C, 0x01 }) == 300, "BLE 16-bit HR");
            check(Engine.ParseHrMeasurement(new byte[] { 0x16, 88, 0x10, 0x03 }) == 88, "BLE HR with RR intervals");
            try
            {
                var seen = new HashSet<ulong>();
                var w = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
                string err = null;
                w.Received += (s, ev) => { lock (seen) seen.Add(ev.BluetoothAddress); };
                w.Stopped += (s, ev) => { if (ev.Error != BluetoothError.Success) err = ev.Error.ToString(); };
                w.Start();
                Thread.Sleep(6000);
                w.Stop();
                log.AppendLine("bluetooth scan (6 s): " + seen.Count + " nearby devices advertising" + (err != null ? ", error " + err : ""));
                using (var cts = new CancellationTokenSource(6000))
                {
                    try
                    {
                        var hr = Engine.FindHrDevice("", cts.Token).Result;
                        log.AppendLine("heart rate monitor found nearby: " + hr.Item2);
                    }
                    catch { log.AppendLine("no heart rate monitor broadcasting nearby right now (expected unless HeartCast is running)"); }
                }
            }
            catch (Exception ex) { log.AppendLine("bluetooth scan unavailable here: " + ex.Message + " (not a failure; build servers have no Bluetooth)"); }

            // HypeRate with a bogus key: proves the websocket/TLS path to app.hyperate.io works and errors are readable
            var e4 = new Engine();
            var c4 = AppConfig.CreateDefault(); c4.Source = "HypeRate"; c4.HypeRateKey = "not-a-real-key"; c4.HypeRateId = "internal-testing"; c4.DiscordClientId = "";
            e4.Config = c4;
            e4.Start();
            for (int i = 0; i < 40 && !e4.SourceStatus.Contains("rejected") && !e4.SourceStatus.Contains("problem") && !e4.SourceStatus.Contains("Receiving") && !e4.SourceStatus.Contains("wouldn't"); i++) Thread.Sleep(250);
            log.AppendLine("hyperate with bogus key -> " + e4.SourceStatus);
            e4.Stop();

            log.AppendLine(fails == 0 ? "ALL PASS" : fails + " FAILED");
            File.WriteAllText(outFile, log.ToString(), Encoding.UTF8);
            return fails;
        }
    }
}
