using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Web.Script.Serialization;

[assembly: AssemblyTitle("DavPilot")]
[assembly: AssemblyProduct("DavPilot")]
[assembly: AssemblyDescription("Windows WebDAV drive manager powered by rclone and WinFsp")]
[assembly: AssemblyCompany("DavPilot contributors")]
[assembly: AssemblyCopyright("Copyright (C) 2026 DavPilot contributors")]

namespace DavPilotNative
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            DpiAwareness.Enable();
            NetworkDefaults.Apply();
            AppPaths.Initialize(GetArgValue(args, "--data-dir"));
            EmbeddedRclone.EnsureAvailable();

            if (HasArg(args, "--service"))
            {
                if (Environment.UserInteractive)
                {
                    ServiceRunner.RunInteractive();
                }
                else
                {
                    ServiceBase.Run(new DavPilotService());
                }
                return;
            }

            bool background = HasArg(args, "--background");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(background));
        }

        static bool HasArg(string[] args, string value)
        {
            foreach (string arg in args)
            {
                if (string.Equals(arg, value, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        static string GetArgValue(string[] args, string name)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    return args[i + 1];
                }
            }
            return null;
        }
    }

    static class DpiAwareness
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        public static void Enable()
        {
            try { SetProcessDPIAware(); } catch { }
        }
    }

    static class NetworkDefaults
    {
        public static void Apply()
        {
            try
            {
                ServicePointManager.SecurityProtocol =
                    ServicePointManager.SecurityProtocol |
                    (SecurityProtocolType)768 |
                    (SecurityProtocolType)3072;
                ServicePointManager.Expect100Continue = false;
            }
            catch { }
        }
    }

    static class DpiUtil
    {
        static float scale = 0F;

        [DllImport("user32.dll")]
        static extern uint GetDpiForSystem();

        public static float ScaleFactor
        {
            get
            {
                if (scale <= 0F)
                {
                    try
                    {
                        scale = GetDpiForSystem() / 96F;
                    }
                    catch
                    {
                        try
                        {
                            using (Graphics graphics = Graphics.FromHwnd(IntPtr.Zero))
                            {
                                scale = graphics.DpiX / 96F;
                            }
                        }
                        catch { scale = 1F; }
                    }
                    if (scale < 1F) scale = 1F;
                }
                return scale;
            }
        }

        public static int Scale(int value)
        {
            return (int)Math.Round(value * ScaleFactor);
        }

        public static Size ScaleSize(int width, int height)
        {
            return new Size(Scale(width), Scale(height));
        }

        public static void Apply(Control root)
        {
            float factor = ScaleFactor;
            if (factor <= 1.01F) return;
            root.Scale(new SizeF(factor, factor));
            ScaleSpecialControls(root, factor);
        }

        static void ScaleSpecialControls(Control control, float factor)
        {
            ListBox list = control as ListBox;
            if (list != null) list.ItemHeight = Math.Min(255, Math.Max(1, (int)Math.Round(list.ItemHeight * factor)));

            StyledPanel panel = control as StyledPanel;
            if (panel != null) panel.Radius = Math.Max(1, (int)Math.Round(panel.Radius * factor));

            StyledButton button = control as StyledButton;
            if (button != null) button.Radius = Math.Max(1, (int)Math.Round(button.Radius * factor));

            ComboBox combo = control as ComboBox;
            if (combo != null && combo.DrawMode != DrawMode.Normal)
            {
                combo.ItemHeight = Math.Max(1, (int)Math.Round(combo.ItemHeight * factor));
            }

            foreach (Control child in control.Controls)
            {
                ScaleSpecialControls(child, factor);
            }
        }
    }

    static class AppPaths
    {
        public static string AppDir;
        public static string DataDir;
        public static string ToolsDir;
        public static string RcloneConfigPath;
        public static string CacheDir;
        public static string LogDir;

        public static void Initialize(string dataDirOverride)
        {
            AppDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            DataDir = string.IsNullOrWhiteSpace(dataDirOverride)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DavPilot")
                : Path.GetFullPath(dataDirOverride);
            ToolsDir = Path.Combine(DataDir, "tools");
            RcloneConfigPath = Path.Combine(DataDir, "rclone", "rclone.conf");
            CacheDir = Path.Combine(DataDir, "cache");
            LogDir = Path.Combine(DataDir, "logs");
            Directory.CreateDirectory(DataDir);
        }

        public static string AppExePath()
        {
            return Process.GetCurrentProcess().MainModule.FileName;
        }
    }

    static class EmbeddedRclone
    {
        const string ResourceName = "DavPilot.rclone.exe";

        public static void EnsureAvailable()
        {
            try
            {
                string target = Path.Combine(AppPaths.ToolsDir, "rclone.exe");
                if (File.Exists(target)) return;

                using (Stream input = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
                {
                    if (input == null) return;

                    Directory.CreateDirectory(AppPaths.ToolsDir);
                    string temp = target + "." + Process.GetCurrentProcess().Id + ".tmp";
                    using (FileStream output = File.Create(temp))
                    {
                        input.CopyTo(output);
                    }

                    if (File.Exists(target)) File.Delete(temp);
                    else File.Move(temp, target);
                }
            }
            catch { }
        }
    }

    class Settings
    {
        public bool autoStartApp = false;
        public bool autoStartBackground = false;
        public bool closeToTray = false;
        public bool startMinimized = false;
    }

    class Profile
    {
        public string vendor = "other";
        public string authType = "basic";
        public string username = "";
        public string passwordObscured = "";
        public string mountPoint = "Z:";
        public bool networkMode = true;
        public bool skipTlsVerify = false;
        public bool readOnly = false;
        public bool autoMount = false;
        public string cacheMode = "writes";
        public string cacheMaxSize = "10G";
        public string dirCacheTime = "30s";
        public string id = "";
        public string name = "我的 WebDAV";
        public string url = "";
        public string remoteName = "";
        public string createdAt = "";
        public string updatedAt = "";
    }

    class ConfigData
    {
        public int version = 1;
        public Settings settings = new Settings();
        public List<Profile> profiles = new List<Profile>();
    }

    static class ConfigStore
    {
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        public static string ConfigPath { get { return Path.Combine(AppPaths.DataDir, "config.json"); } }

        public static ConfigData Load()
        {
            if (!File.Exists(ConfigPath))
            {
                ConfigData fresh = new ConfigData();
                Save(fresh);
                return fresh;
            }

            try
            {
                ConfigData data = Json.Deserialize<ConfigData>(File.ReadAllText(ConfigPath, Encoding.UTF8));
                if (data == null) data = new ConfigData();
                if (data.settings == null) data.settings = new Settings();
                if (data.profiles == null) data.profiles = new List<Profile>();
                foreach (Profile profile in data.profiles)
                {
                    NormalizeLoadedProfile(profile);
                }
                return data;
            }
            catch
            {
                return new ConfigData();
            }
        }

        public static void Save(ConfigData data)
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            string json = Json.Serialize(data);
            File.WriteAllText(ConfigPath, PrettyJson(json) + Environment.NewLine, Encoding.UTF8);
        }

        static void NormalizeLoadedProfile(Profile profile)
        {
            if (profile.id == null || profile.id.Length == 0) profile.id = Guid.NewGuid().ToString();
            if (profile.remoteName == null || profile.remoteName.Length == 0)
            {
                profile.remoteName = "davpilot_webdav_" + profile.id.Replace("-", "").Substring(0, 8);
            }
            if (profile.name == null || profile.name.Length == 0) profile.name = "我的 WebDAV";
            if (profile.mountPoint == null || profile.mountPoint.Length == 0) profile.mountPoint = "Z:";
            if (profile.vendor == null || profile.vendor.Length == 0) profile.vendor = "other";
            if (profile.authType == null || profile.authType.Length == 0) profile.authType = "basic";
            if (profile.cacheMode == null || profile.cacheMode.Length == 0) profile.cacheMode = "writes";
            if (profile.cacheMaxSize == null || profile.cacheMaxSize.Length == 0) profile.cacheMaxSize = "10G";
            try { profile.cacheMaxSize = NormalizeCacheMaxSize(profile.cacheMaxSize); }
            catch { profile.cacheMaxSize = "10G"; }
            if (profile.dirCacheTime == null || profile.dirCacheTime.Length == 0) profile.dirCacheTime = "30s";
        }

        public static Profile NormalizeProfile(Profile input, Profile existing)
        {
            string now = DateTime.UtcNow.ToString("o");
            Profile profile = new Profile();
            if (existing != null) CopyProfile(existing, profile);
            CopyProfile(input, profile);

            if (string.IsNullOrWhiteSpace(profile.id)) profile.id = Guid.NewGuid().ToString();
            profile.name = CleanSingleLine(profile.name, "我的 WebDAV");
            profile.url = CleanSingleLine(profile.url, "");
            if (!Regex.IsMatch(profile.url, "^https?://", RegexOptions.IgnoreCase))
            {
                throw new Exception("WebDAV 地址必须以 http:// 或 https:// 开头");
            }

            profile.mountPoint = NormalizeMountPoint(profile.mountPoint);
            profile.cacheMaxSize = NormalizeCacheMaxSize(profile.cacheMaxSize);
            profile.authType = profile.authType == "anonymous" ? "anonymous" : "basic";
            if (profile.authType == "anonymous")
            {
                profile.username = "";
                profile.passwordObscured = "";
            }

            if (string.IsNullOrWhiteSpace(profile.remoteName))
            {
                profile.remoteName = SanitizeRemoteName(profile.name) + "_" + profile.id.Replace("-", "").Substring(0, 8);
            }
            if (string.IsNullOrWhiteSpace(profile.createdAt)) profile.createdAt = now;
            profile.updatedAt = now;
            return profile;
        }

        static void CopyProfile(Profile source, Profile target)
        {
            target.vendor = source.vendor;
            target.authType = source.authType;
            target.username = source.username;
            target.passwordObscured = source.passwordObscured;
            target.mountPoint = source.mountPoint;
            target.networkMode = source.networkMode;
            target.skipTlsVerify = source.skipTlsVerify;
            target.readOnly = source.readOnly;
            target.autoMount = source.autoMount;
            target.cacheMode = source.cacheMode;
            target.cacheMaxSize = source.cacheMaxSize;
            target.dirCacheTime = source.dirCacheTime;
            target.id = source.id;
            target.name = source.name;
            target.url = source.url;
            target.remoteName = source.remoteName;
            target.createdAt = source.createdAt;
            target.updatedAt = source.updatedAt;
        }

        static string CleanSingleLine(string value, string fallback)
        {
            string text = (value ?? fallback).Trim();
            text = text.Replace("\r", "").Replace("\n", "");
            return text.Length == 0 ? fallback : text;
        }

        static string NormalizeMountPoint(string value)
        {
            string raw = (value ?? "Z:").Trim().ToUpperInvariant();
            Match match = Regex.Match(raw, "^([A-Z])(:)?(\\\\)?$");
            if (!match.Success) throw new Exception("盘符格式需要类似 X:");
            return match.Groups[1].Value + ":";
        }

        static string NormalizeCacheMaxSize(string value)
        {
            Match match = Regex.Match(value ?? "", @"^\s*(\d{1,4})\s*([MGT])B?\s*$", RegexOptions.IgnoreCase);
            if (!match.Success) throw new Exception("缓存上限必须是数字加单位，例如 512M、10G 或 1T。");
            int number = int.Parse(match.Groups[1].Value);
            if (number < 1 || number > 1024) throw new Exception("缓存上限数字必须在 1 到 1024 之间。");
            return number.ToString() + match.Groups[2].Value.ToUpperInvariant();
        }

        static string SanitizeRemoteName(string value)
        {
            string lower = (value ?? "webdav").ToLowerInvariant();
            lower = Regex.Replace(lower, "[^a-z0-9_-]+", "_").Trim('_');
            if (lower.Length > 30) lower = lower.Substring(0, 30);
            if (lower.Length == 0) lower = "webdav";
            return "davpilot_" + lower;
        }

        static string PrettyJson(string json)
        {
            StringBuilder output = new StringBuilder();
            int indent = 0;
            bool quoted = false;
            for (int i = 0; i < json.Length; i++)
            {
                char ch = json[i];
                if (ch == '"' && (i == 0 || json[i - 1] != '\\')) quoted = !quoted;
                if (!quoted && (ch == '{' || ch == '['))
                {
                    output.Append(ch).AppendLine();
                    indent++;
                    output.Append(new string(' ', indent * 2));
                }
                else if (!quoted && (ch == '}' || ch == ']'))
                {
                    output.AppendLine();
                    indent--;
                    output.Append(new string(' ', indent * 2)).Append(ch);
                }
                else if (!quoted && ch == ',')
                {
                    output.Append(ch).AppendLine();
                    output.Append(new string(' ', indent * 2));
                }
                else if (!quoted && ch == ':')
                {
                    output.Append(": ");
                }
                else
                {
                    output.Append(ch);
                }
            }
            return output.ToString();
        }
    }

    class ToolStatus
    {
        public bool Available;
        public string Path;
        public string Version;
        public string Message;
    }

    class MountInfo
    {
        public string ProfileId;
        public string MountPoint;
        public string LogPath;
        public DateTime StartedAt;
        public Process Process;
    }

    class RcloneManager
    {
        public Dictionary<string, MountInfo> Mounts = new Dictionary<string, MountInfo>();
        const string RcloneDownloadUrl = "https://downloads.rclone.org/rclone-current-windows-amd64.zip";

        public ToolStatus CheckRclone()
        {
            string path = FindRclonePath();
            if (path == null)
            {
                return new ToolStatus { Available = false, Message = "尚未安装 rclone" };
            }
            ExecResult version = Exec(path, "version", 10000);
            return new ToolStatus
            {
                Available = version.Ok,
                Path = path,
                Version = FirstLine(version.Stdout),
                Message = version.Ok ? "" : version.ErrorText
            };
        }

        public ToolStatus CheckWinFsp()
        {
            ExecResult reg = Exec("reg.exe", "query HKLM\\SOFTWARE\\WinFsp /v InstallDir", 5000);
            if (reg.Ok) return new ToolStatus { Available = true };
            ExecResult regWow = Exec("reg.exe", "query HKLM\\SOFTWARE\\WOW6432Node\\WinFsp /v InstallDir", 5000);
            if (regWow.Ok) return new ToolStatus { Available = true };
            ExecResult service = Exec("sc.exe", "query WinFsp.Launcher", 5000);
            return new ToolStatus
            {
                Available = service.Ok,
                Message = service.Ok ? "" : "Windows 挂载盘符需要先安装 WinFsp"
            };
        }

        public string FindRclonePath()
        {
            string[] candidates = new string[]
            {
                Path.Combine(AppPaths.ToolsDir, "rclone.exe"),
                Path.Combine(AppPaths.AppDir, "tools", "rclone.exe")
            };
            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate)) return candidate;
            }

            ExecResult where = Exec("where.exe", "rclone.exe", 5000);
            if (where.Ok) return FirstLine(where.Stdout);
            return null;
        }

        public void DownloadRclone()
        {
            Directory.CreateDirectory(AppPaths.ToolsDir);
            string tempRoot = Path.Combine(Path.GetTempPath(), "davpilot-rclone-" + DateTime.Now.Ticks);
            string zipPath = Path.Combine(tempRoot, "rclone.zip");
            string extractPath = Path.Combine(tempRoot, "extract");
            Directory.CreateDirectory(tempRoot);
            try
            {
                using (WebClient client = new WebClient())
                {
                    client.Proxy = WebRequest.GetSystemWebProxy();
                    if (client.Proxy != null) client.Proxy.Credentials = CredentialCache.DefaultCredentials;
                    client.Headers.Add("User-Agent", "DavPilot-native");
                    client.DownloadFile(RcloneDownloadUrl, zipPath);
                }
            }
            catch (WebException ex)
            {
                throw new Exception("下载 rclone 失败：" + ex.Message + "\n\n如果正在使用虚拟网卡/TUN 代理，请确认代理的 HTTPS 证书已被 Windows 信任，或临时关闭 HTTPS 解密/切换为系统代理模式后重试。");
            }
            ZipFile.ExtractToDirectory(zipPath, extractPath);
            string rclone = FindFile(extractPath, "rclone.exe");
            if (rclone == null) throw new Exception("rclone 压缩包里没有 rclone.exe");
            File.Copy(rclone, Path.Combine(AppPaths.ToolsDir, "rclone.exe"), true);
            Directory.Delete(tempRoot, true);
        }

        public string ObscurePassword(string password)
        {
            ToolStatus rclone = CheckRclone();
            if (!rclone.Available) throw new Exception("保存密码前需要先安装或下载 rclone");
            ExecResult result = Exec(rclone.Path, "obscure " + QuoteArg(password ?? ""), 10000);
            if (!result.Ok) throw new Exception(result.ErrorText);
            return result.Stdout.Trim();
        }

        public void SyncConfig(List<Profile> profiles)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.RcloneConfigPath));
            StringBuilder lines = new StringBuilder();
            lines.AppendLine("# 由 DavPilot 管理，手动修改可能会被覆盖。");
            lines.AppendLine();
            foreach (Profile profile in profiles)
            {
                lines.AppendLine("[" + SafeConfig(profile.remoteName) + "]");
                lines.AppendLine("type = webdav");
                lines.AppendLine("url = " + SafeConfig(profile.url));
                lines.AppendLine("vendor = " + SafeConfig(profile.vendor ?? "other"));
                if (profile.authType != "anonymous")
                {
                    lines.AppendLine("user = " + SafeConfig(profile.username));
                    if (!string.IsNullOrEmpty(profile.passwordObscured))
                    {
                        lines.AppendLine("pass = " + SafeConfig(profile.passwordObscured));
                    }
                }
                lines.AppendLine();
            }
            File.WriteAllText(AppPaths.RcloneConfigPath, lines.ToString(), Encoding.UTF8);
        }

        public string TestProfile(Profile profile, List<Profile> profiles)
        {
            ToolStatus rclone = CheckRclone();
            if (!rclone.Available) throw new Exception("测试连接前需要先安装 rclone");
            SyncConfig(profiles);
            string args = "--config " + QuoteArg(AppPaths.RcloneConfigPath) + " ";
            if (profile.skipTlsVerify) args += "--no-check-certificate ";
            args += "lsjson " + QuoteArg(profile.remoteName + ":") + " --max-depth 1";
            ExecResult result = Exec(rclone.Path, args, 45000);
            if (!result.Ok) throw new Exception(result.ErrorText);
            return result.Stdout.Length > 4000 ? result.Stdout.Substring(0, 4000) : result.Stdout;
        }

        public MountInfo MountProfile(Profile profile, List<Profile> profiles)
        {
            if (Mounts.ContainsKey(profile.id)) return Mounts[profile.id];
            ToolStatus rclone = CheckRclone();
            ToolStatus winfsp = CheckWinFsp();
            if (!rclone.Available) throw new Exception("挂载前需要先安装 rclone");
            if (!winfsp.Available) throw new Exception("Windows 挂载盘符前需要先安装 WinFsp");

            TestProfile(profile, profiles);
            SyncConfig(profiles);
            Directory.CreateDirectory(AppPaths.CacheDir);
            Directory.CreateDirectory(AppPaths.LogDir);
            string logPath = Path.Combine(AppPaths.LogDir, profile.id + ".log");

            StringBuilder args = new StringBuilder();
            args.Append("--config ").Append(QuoteArg(AppPaths.RcloneConfigPath)).Append(" ");
            if (profile.skipTlsVerify) args.Append("--no-check-certificate ");
            args.Append("mount ").Append(QuoteArg(profile.remoteName + ":")).Append(" ");
            args.Append(QuoteArg(profile.mountPoint)).Append(" ");
            args.Append("--vfs-cache-mode ").Append(QuoteArg(profile.cacheMode ?? "writes")).Append(" ");
            args.Append("--cache-dir ").Append(QuoteArg(AppPaths.CacheDir)).Append(" ");
            args.Append("--dir-cache-time ").Append(QuoteArg(profile.dirCacheTime ?? "30s")).Append(" ");
            args.Append("--log-file ").Append(QuoteArg(logPath)).Append(" ");
            args.Append("--log-level INFO ");
            args.Append("--volname ").Append(QuoteArg(profile.name)).Append(" ");
            if (profile.networkMode) args.Append("--network-mode ");
            if (profile.readOnly) args.Append("--read-only ");
            args.Append("--links ");
            if (!string.IsNullOrWhiteSpace(profile.cacheMaxSize))
            {
                args.Append("--vfs-cache-max-size ").Append(QuoteArg(profile.cacheMaxSize)).Append(" ");
            }

            ProcessStartInfo start = new ProcessStartInfo();
            start.FileName = rclone.Path;
            start.Arguments = args.ToString();
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            Process process = new Process();
            process.StartInfo = start;
            StringBuilder output = new StringBuilder();
            process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) output.AppendLine(e.Data); };
            process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) output.AppendLine(e.Data); };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (process.WaitForExit(1800))
            {
                throw new Exception(output.Length > 0 ? output.ToString() : "rclone 挂载进程已退出");
            }

            MountInfo mount = new MountInfo();
            mount.ProfileId = profile.id;
            mount.MountPoint = profile.mountPoint;
            mount.LogPath = logPath;
            mount.StartedAt = DateTime.Now;
            mount.Process = process;
            Mounts[profile.id] = mount;
            return mount;
        }

        public void UnmountProfile(string profileId)
        {
            if (!Mounts.ContainsKey(profileId)) return;
            MountInfo mount = Mounts[profileId];
            try
            {
                if (!mount.Process.HasExited)
                {
                    mount.Process.Kill();
                    mount.Process.WaitForExit(2500);
                }
            }
            catch
            {
                try { Exec("taskkill.exe", "/PID " + mount.Process.Id + " /T /F", 10000); } catch { }
            }
            Mounts.Remove(profileId);
        }

        public void UnmountAll()
        {
            List<string> ids = new List<string>(Mounts.Keys);
            foreach (string id in ids) UnmountProfile(id);
        }

        public string ReadLog(string profileId)
        {
            string path = Path.Combine(AppPaths.LogDir, profileId + ".log");
            if (!File.Exists(path)) return "";
            try
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    int size = (int)Math.Min(stream.Length, 12000);
                    byte[] buffer = new byte[size];
                    stream.Seek(-size, SeekOrigin.End);
                    int read = stream.Read(buffer, 0, size);
                    return Encoding.UTF8.GetString(buffer, 0, read);
                }
            }
            catch (IOException)
            {
                return "日志正在写入，稍后刷新。";
            }
            catch (UnauthorizedAccessException)
            {
                return "当前没有权限读取日志。";
            }
        }

        static string FindFile(string root, string fileName)
        {
            foreach (string file in Directory.GetFiles(root, fileName, SearchOption.AllDirectories))
            {
                return file;
            }
            return null;
        }

        static string SafeConfig(string value)
        {
            return (value ?? "").Replace("\r", "").Replace("\n", "").Trim();
        }

        static string FirstLine(string value)
        {
            if (value == null) return "";
            string[] lines = value.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return lines.Length == 0 ? "" : lines[0].Trim();
        }

        public static string QuoteArg(string value)
        {
            return "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";
        }

        public static ExecResult Exec(string file, string args, int timeout)
        {
            ProcessStartInfo start = new ProcessStartInfo();
            start.FileName = file;
            start.Arguments = args;
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            using (Process process = Process.Start(start))
            {
                StringBuilder stdout = new StringBuilder();
                StringBuilder stderr = new StringBuilder();
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) stdout.AppendLine(e.Data); };
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) stderr.AppendLine(e.Data); };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                bool exited = process.WaitForExit(timeout);
                if (!exited)
                {
                    try { process.Kill(); } catch { }
                    return new ExecResult { Ok = false, Stdout = stdout.ToString(), Stderr = stderr.ToString(), ErrorText = "命令超时" };
                }
                process.WaitForExit();
                return new ExecResult
                {
                    Ok = process.ExitCode == 0,
                    Stdout = stdout.ToString(),
                    Stderr = stderr.ToString(),
                    ErrorText = stderr.Length > 0 ? stderr.ToString() : stdout.ToString()
                };
            }
        }
    }

    class ExecResult
    {
        public bool Ok;
        public string Stdout;
        public string Stderr;
        public string ErrorText;
    }

    class ServiceStatus
    {
        public bool Available;
        public bool Installed;
        public bool Running;
        public bool Pending;
        public string State;
        public string Label;
        public string Message;
    }

    static class WindowsServiceManager
    {
        public const string ServiceId = "DavPilotWebDavService";

        public static string ScriptPath { get { return Path.Combine(AppPaths.DataDir, "DavPilotService-admin.ps1"); } }

        public static ServiceStatus GetStatus()
        {
            ExecResult result = RcloneManager.Exec("sc.exe", "query " + ServiceId, 8000);
            string output = (result.Stdout ?? "") + "\n" + (result.Stderr ?? "") + "\n" + (result.ErrorText ?? "");
            if (!result.Ok)
            {
                if (output.Contains("1060") || output.IndexOf("does not exist", StringComparison.OrdinalIgnoreCase) >= 0 || output.IndexOf("未安装", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return new ServiceStatus { Available = true, Installed = false, Running = false, Pending = false, State = "", Label = "未安装", Message = "" };
                }
                return new ServiceStatus { Available = true, Installed = false, Running = false, Pending = false, State = "", Label = "无法读取", Message = output.Trim() };
            }

            Match match = Regex.Match(output, @"STATE\s*:\s*\d+\s+([A-Z_]+)", RegexOptions.IgnoreCase);
            string state = match.Success ? match.Groups[1].Value.ToUpperInvariant() : "UNKNOWN";
            return new ServiceStatus { Available = true, Installed = true, Running = state == "RUNNING", Pending = state.EndsWith("_PENDING"), State = state, Label = StateLabel(state), Message = "" };
        }

        public static ServiceStatus Install()
        {
            RunElevated("install");
            return GetStatus();
        }

        public static ServiceStatus Start()
        {
            RunElevated("start");
            return GetStatus();
        }

        public static ServiceStatus Stop()
        {
            RunElevated("stop");
            return GetStatus();
        }

        public static ServiceStatus Uninstall()
        {
            RunElevated("uninstall");
            return GetStatus();
        }

        static void RunElevated(string action)
        {
            string[] lines;
            if (action == "install")
            {
                string binPath = RcloneManager.QuoteArg(AppPaths.AppExePath()) + " --service --data-dir " + RcloneManager.QuoteArg(AppPaths.DataDir);
                lines = new string[]
                {
                    "$existing = & sc.exe query " + ServiceId + " 2>$null",
                    "if ($LASTEXITCODE -eq 0) { $svc = Get-Service -Name " + Ps(ServiceId) + " -ErrorAction SilentlyContinue; if ($svc -and $svc.Status -ne 'Stopped') { & sc.exe stop " + ServiceId + " | Out-Null; $svc.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30)) }; & sc.exe delete " + ServiceId + " | Out-Null; Start-Sleep -Seconds 1 }",
                    "$out = & sc.exe create " + ServiceId + " binPath= " + Ps(binPath) + " start= auto DisplayName= " + Ps("DavPilot WebDAV Service") + " 2>&1; if ($LASTEXITCODE -ne 0) { throw ($out | Out-String) }",
                    "$out = & sc.exe description " + ServiceId + " " + Ps("DavPilot background WebDAV mounting service powered by rclone and WinFsp.") + " 2>&1; if ($LASTEXITCODE -ne 0) { throw ($out | Out-String) }",
                    "StartDavPilotService"
                };
            }
            else if (action == "uninstall")
            {
                lines = new string[]
                {
                    "$svc = Get-Service -Name " + Ps(ServiceId) + " -ErrorAction SilentlyContinue",
                    "if ($svc -and $svc.Status -ne 'Stopped') { & sc.exe stop " + ServiceId + " | Out-Null; $svc.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30)) }",
                    "& sc.exe delete " + ServiceId,
                    "exit 0"
                };
            }
            else if (action == "start")
            {
                lines = new string[] { "StartDavPilotService" };
            }
            else if (action == "stop")
            {
                lines = new string[]
                {
                    "$svc = Get-Service -Name " + Ps(ServiceId) + " -ErrorAction Stop",
                    "if ($svc.Status -eq 'StopPending') { $svc.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30)) } elseif ($svc.Status -ne 'Stopped') { $out = & sc.exe stop " + ServiceId + " 2>&1; if ($LASTEXITCODE -ne 0) { throw ($out | Out-String) }; $svc.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30)) }"
                };
            }
            else
            {
                throw new Exception("未知系统服务操作：" + action);
            }
            Directory.CreateDirectory(AppPaths.DataDir);
            string logPath = ScriptPath + ".log";
            List<string> script = new List<string>();
            script.Add("$ErrorActionPreference = 'Stop'");
            script.Add("$log = " + Ps(logPath));
            script.Add("Remove-Item -LiteralPath $log -ErrorAction SilentlyContinue");
            script.Add("function StartDavPilotService {");
            script.Add("    $svc = Get-Service -Name " + Ps(ServiceId) + " -ErrorAction Stop");
            script.Add("    if ($svc.Status -eq 'StopPending') { $svc.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30)); $svc.Refresh() }");
            script.Add("    if ($svc.Status -ne 'Running') { $out = & sc.exe start " + ServiceId + " 2>&1; if ($LASTEXITCODE -ne 0) { throw ($out | Out-String) }; $svc.Refresh(); $svc.WaitForStatus('Running', [TimeSpan]::FromSeconds(30)) }");
            script.Add("}");
            script.Add("try {");
            foreach (string line in lines) script.Add("    " + line);
            script.Add("} catch {");
            script.Add("    $_.Exception.Message | Set-Content -LiteralPath $log -Encoding UTF8");
            script.Add("    exit 1");
            script.Add("}");
            File.WriteAllText(ScriptPath, string.Join("\r\n", script) + "\r\n", Encoding.UTF8);

            ProcessStartInfo start = new ProcessStartInfo();
            start.FileName = "powershell.exe";
            start.Arguments = "-NoProfile -ExecutionPolicy Bypass -File " + RcloneManager.QuoteArg(ScriptPath);
            start.Verb = "runas";
            start.UseShellExecute = true;
            using (Process process = Process.Start(start))
            {
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    string detail = "";
                    try { if (File.Exists(logPath)) detail = File.ReadAllText(logPath, Encoding.UTF8).Trim(); } catch { }
                    throw new Exception("系统服务操作未完成。" + (detail.Length > 0 ? "\n\n" + detail : "\n\n可能已取消管理员权限确认。"));
                }
            }
        }

        static string StateLabel(string state)
        {
            if (state == "RUNNING") return "运行中";
            if (state == "STOPPED") return "已停止";
            if (state == "START_PENDING") return "正在启动";
            if (state == "STOP_PENDING") return "正在停止";
            return state;
        }

        static string Ps(string value)
        {
            return "'" + (value ?? "").Replace("'", "''") + "'";
        }
    }

    class DavPilotService : ServiceBase
    {
        Thread worker;

        public DavPilotService()
        {
            ServiceName = WindowsServiceManager.ServiceId;
            CanStop = true;
            CanShutdown = true;
        }

        protected override void OnStart(string[] args)
        {
            ServiceRunner.Reset();
            worker = new Thread(ServiceRunner.RunLoop);
            worker.IsBackground = true;
            worker.Start();
        }

        protected override void OnStop()
        {
            ServiceRunner.Stop();
            if (worker != null && worker.IsAlive)
            {
                worker.Join(10000);
            }
        }

        protected override void OnShutdown()
        {
            OnStop();
            base.OnShutdown();
        }
    }

    static class ServiceRunner
    {
        static readonly RcloneManager Rclone = new RcloneManager();
        static bool stopping = false;

        public static void Reset()
        {
            stopping = false;
        }

        public static void RunInteractive()
        {
            Reset();
            AppDomain.CurrentDomain.ProcessExit += delegate { Stop(); };
            RunLoop();
        }

        public static void RunLoop()
        {
            Log("DavPilot service mode started.");
            while (!stopping)
            {
                try { EnsureMounts(); }
                catch (Exception ex) { Log("Service loop failed: " + ex.Message); }
                for (int i = 0; i < 30 && !stopping; i++)
                {
                    Thread.Sleep(1000);
                }
            }
            Stop();
        }

        static void EnsureMounts()
        {
            ConfigData config = ConfigStore.Load();
            Rclone.SyncConfig(config.profiles);
            Dictionary<string, Profile> autoProfiles = new Dictionary<string, Profile>();
            foreach (Profile profile in config.profiles)
            {
                if (profile.autoMount) autoProfiles[profile.id] = profile;
            }

            List<string> mounted = new List<string>(Rclone.Mounts.Keys);
            foreach (string id in mounted)
            {
                if (!autoProfiles.ContainsKey(id))
                {
                    Log("Unmounting disabled profile " + id);
                    Rclone.UnmountProfile(id);
                }
            }

            foreach (Profile profile in autoProfiles.Values)
            {
                if (Rclone.Mounts.ContainsKey(profile.id)) continue;
                try
                {
                    Log("Mounting " + profile.name + " at " + profile.mountPoint);
                    Rclone.MountProfile(profile, config.profiles);
                    Log("Mounted " + profile.name + " at " + profile.mountPoint);
                }
                catch (Exception ex)
                {
                    Log("Mount failed for " + profile.name + ": " + ex.Message);
                }
            }

            if (autoProfiles.Count == 0) Log("No profiles marked for auto mount.");
        }

        public static void Stop()
        {
            if (stopping) return;
            stopping = true;
            try { Rclone.UnmountAll(); } catch { }
            Log("DavPilot service mode stopped.");
        }

        static void Log(string message)
        {
            Directory.CreateDirectory(AppPaths.LogDir);
            File.AppendAllText(Path.Combine(AppPaths.LogDir, "service.log"), "[" + DateTime.UtcNow.ToString("o") + "] " + message + Environment.NewLine, Encoding.UTF8);
        }
    }

    class MainForm : Form
    {
        const int LayoutGap = 20;
        const int FormLeft = 20;
        const int FormLabelWidth = 132;
        const int FormLabelGap = 14;
        const int FieldX = FormLeft + FormLabelWidth + FormLabelGap;
        const int FieldHeight = 32;
        const int FieldRowHeight = 46;
        const int FieldRightPadding = 15;
        const string SavedPasswordMask = "********";

        ConfigData config;
        RcloneManager rclone = new RcloneManager();
        NotifyIcon tray;
        bool isQuitting = false;

        ListBox profileList;
        Label titleLabel;
        Label subtitleLabel;
        Label rcloneStatus;
        Label winfspStatus;
        Label mountStateLabel;
        Label mountBadgeLabel;
        Label serviceStatusLabel;
        Label serviceBadgeLabel;
        TextBox nameInput;
        TextBox urlInput;
        ComboBox mountPointInput;
        ComboBox vendorInput;
        ComboBox authTypeInput;
        TextBox usernameInput;
        TextBox passwordInput;
        ComboBox cacheModeInput;
        NumericUpDown cacheSizeValueInput;
        ComboBox cacheSizeUnitInput;
        CheckBox networkModeInput;
        CheckBox skipTlsVerifyInput;
        CheckBox readOnlyInput;
        CheckBox autoMountInput;
        TextBox logOutput;
        Button testButton;
        Button saveButton;
        Button deleteButton;
        Button mountButton;
        Button unmountButton;
        Button openDriveButton;
        Button installServiceButton;
        Button startServiceButton;
        Button stopServiceButton;
        Button uninstallServiceButton;
        Button refreshLogButton;
        ToolTip toolTips = new ToolTip();
        Action relayoutAfterScale;
        Action relayoutOperations;
        Action relayoutForm;
        Control formRightBoundary;
        Control formSwitches;
        List<Control> formWidthControls = new List<Control>();

        string selectedId = null;
        bool rendering = false;

        static Font UiFont(float pixels)
        {
            return UiFont(pixels, FontStyle.Regular);
        }

        static Font UiFont(float pixels, FontStyle style)
        {
            return new Font("Segoe UI", pixels * 72F / 96F, style, GraphicsUnit.Point);
        }

        public MainForm(bool background)
        {
            Text = "DavPilot";
            AutoScaleMode = AutoScaleMode.None;
            Font = UiFont(13);
            ClientSize = new Size(1320, 800);
            MinimumSize = SizeFromClientSize(ClientSize);
            Icon = LoadIcon();
            BuildUi();
            DpiUtil.Apply(this);
            NormalizeButtonMargins(this);
            ClientSize = DpiUtil.ScaleSize(1320, 800);
            MinimumSize = SizeFromClientSize(ClientSize);
            if (relayoutAfterScale != null) relayoutAfterScale();
            Shown += delegate
            {
                if (relayoutAfterScale != null) relayoutAfterScale();
            };
            SizeChanged += delegate
            {
                if (WindowState != FormWindowState.Minimized) RelayoutSoon();
            };
            CreateTray();
            LoadState();
            if (background || config.settings.startMinimized)
            {
                WindowState = FormWindowState.Minimized;
                ShowInTaskbar = false;
                BeginInvoke(new Action(Hide));
            }
        }

        void NormalizeButtonMargins(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                StyledButton button = control as StyledButton;
                if (button != null)
                {
                    int gap = DpiUtil.Scale(LayoutGap);
                    button.Margin = new Padding(
                        button.Margin.Left,
                        button.Margin.Top,
                        button.Margin.Right > 0 ? gap : 0,
                        button.Margin.Bottom > 0 ? gap : 0
                    );
                }
                NormalizeButtonMargins(control);
            }
        }

        Icon LoadIcon()
        {
            Icon icon = Icon.ExtractAssociatedIcon(AppPaths.AppExePath());
            return icon ?? SystemIcons.Application;
        }

        void BuildUi()
        {
            BackColor = Color.FromArgb(244, 246, 248);
            Panel root = new Panel();
            root.Dock = DockStyle.Fill;
            Controls.Add(root);

            Panel sidebar = new Panel();
            sidebar.Dock = DockStyle.Left;
            sidebar.Width = 240;
            sidebar.BackColor = Color.FromArgb(23, 32, 42);
            sidebar.Padding = new Padding(22);
            root.Controls.Add(sidebar);

            Panel brand = new Panel();
            brand.Location = new Point(18, 18);
            brand.Size = new Size(204, 52);
            brand.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            PictureBox brandMark = new PictureBox();
            brandMark.Image = Icon.ToBitmap();
            brandMark.SizeMode = PictureBoxSizeMode.Zoom;
            brandMark.BackColor = Color.Transparent;
            brandMark.Location = new Point(0, 4);
            brandMark.Size = new Size(42, 42);
            brand.Controls.Add(brandMark);
            Label brandTitle = new Label();
            brandTitle.Text = "DavPilot";
            brandTitle.ForeColor = Color.White;
            brandTitle.Font = UiFont(17, FontStyle.Bold);
            brandTitle.Location = new Point(54, 2);
            brandTitle.Size = new Size(165, 26);
            brand.Controls.Add(brandTitle);
            Label brandSub = new Label();
            brandSub.Text = "WebDAV 磁盘";
            brandSub.ForeColor = Color.FromArgb(174, 189, 202);
            brandSub.Font = UiFont(12);
            brandSub.Location = new Point(54, 28);
            brandSub.Size = new Size(165, 22);
            brand.Controls.Add(brandSub);
            sidebar.Controls.Add(brand);

            Button newButton = NewButton("+ 新建 WebDAV", true);
            newButton.Location = new Point(18, 86);
            newButton.Size = new Size(204, 40);
            newButton.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            newButton.Click += delegate { selectedId = null; RenderAll(); };
            sidebar.Controls.Add(newButton);

            profileList = new ListBox();
            profileList.Location = new Point(14, 144);
            profileList.Size = new Size(212, 480);
            profileList.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            profileList.BackColor = Color.FromArgb(23, 32, 42);
            profileList.ForeColor = Color.White;
            profileList.BorderStyle = BorderStyle.None;
            profileList.Font = UiFont(13, FontStyle.Bold);
            profileList.DrawMode = DrawMode.OwnerDrawFixed;
            profileList.ItemHeight = 124;
            profileList.IntegralHeight = false;
            profileList.DrawItem += DrawProfileItem;
            profileList.SelectedIndexChanged += delegate
            {
                if (rendering) return;
                ProfileItem item = profileList.SelectedItem as ProfileItem;
                if (item != null)
                {
                    selectedId = item.Id;
                    RenderAll();
                }
            };
            sidebar.Controls.Add(profileList);

            Panel operationsShell = new Panel();
            operationsShell.Dock = DockStyle.Right;
            operationsShell.Width = 360;
            operationsShell.Padding = new Padding(0, 16, 18, 18);
            operationsShell.BackColor = Color.FromArgb(244, 246, 248);
            root.Controls.Add(operationsShell);
            formRightBoundary = operationsShell;

            Panel operationsPanel = CardPanel();
            operationsPanel.Dock = DockStyle.Fill;
            operationsPanel.Margin = new Padding(0);
            operationsPanel.AutoScroll = false;
            operationsShell.Controls.Add(operationsPanel);
            BuildOperations(operationsPanel);

            Panel workspace = new Panel();
            workspace.Dock = DockStyle.Fill;
            workspace.Padding = new Padding(18, 16, 18, 18);
            root.Controls.Add(workspace);
            workspace.BringToFront();
            operationsShell.BringToFront();

            Panel rightLayout = new Panel();
            rightLayout.Dock = DockStyle.Fill;
            workspace.Controls.Add(rightLayout);

            Panel header = new Panel();
            header.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            header.Location = new Point(0, 0);
            header.Height = 112;
            rightLayout.Controls.Add(header);

            titleLabel = new Label();
            titleLabel.AutoSize = false;
            titleLabel.AutoEllipsis = true;
            titleLabel.Font = UiFont(24, FontStyle.Bold);
            titleLabel.Location = new Point(0, 0);
            titleLabel.Size = new Size(480, 44);
            titleLabel.TextAlign = ContentAlignment.MiddleLeft;
            header.Controls.Add(titleLabel);
            subtitleLabel = new Label();
            subtitleLabel.AutoSize = false;
            subtitleLabel.AutoEllipsis = true;
            subtitleLabel.Font = UiFont(16);
            subtitleLabel.ForeColor = Color.FromArgb(99, 112, 131);
            subtitleLabel.Location = new Point(0, 46);
            subtitleLabel.Size = new Size(520, 30);
            subtitleLabel.TextAlign = ContentAlignment.MiddleLeft;
            header.Controls.Add(subtitleLabel);

            FlowLayoutPanel headerActions = new FlowLayoutPanel();
            headerActions.FlowDirection = FlowDirection.LeftToRight;
            headerActions.WrapContents = false;
            headerActions.AutoSize = false;
            headerActions.Width = 284;
            headerActions.Height = 46;
            headerActions.Padding = new Padding(0, 6, 0, 0);
            headerActions.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            header.Controls.Add(headerActions);

            Button downloadRclone = NewButton("下载 rclone", false);
            downloadRclone.Size = new Size(116, 36);
            SpaceButton(downloadRclone);
            downloadRclone.Click += delegate { RunBusy("正在下载 rclone...", delegate { rclone.DownloadRclone(); }, "rclone 已下载"); };
            headerActions.Controls.Add(downloadRclone);
            Button installWinFsp = NewButton("安装 WinFsp", false);
            installWinFsp.Size = new Size(126, 36);
            SpaceButton(installWinFsp, false);
            toolTips.SetToolTip(installWinFsp, "WinFsp Copyright (C) Bill Zissimopoulos. Source: https://github.com/winfsp/winfsp");
            installWinFsp.Click += delegate
            {
                string[] installers = Directory.GetFiles(AppPaths.AppDir, "winfsp-*.msi");
                Process.Start(installers.Length > 0 ? installers[0] : "https://winfsp.dev/rel/");
            };
            headerActions.Controls.Add(installWinFsp);

            rcloneStatus = StatusLabel();
            rcloneStatus.Location = new Point(0, 82);
            rcloneStatus.Size = new Size(240, 34);
            header.Controls.Add(rcloneStatus);
            winfspStatus = StatusLabel();
            winfspStatus.Location = new Point(252, 82);
            winfspStatus.Size = new Size(170, 34);
            header.Controls.Add(winfspStatus);
            header.Resize += delegate
            {
                LayoutHeader(header, headerActions);
            };
            LayoutHeader(header, headerActions);

            Panel content = new Panel();
            content.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            content.Padding = new Padding(0, 8, 0, 0);
            rightLayout.Controls.Add(content);
            rightLayout.Resize += delegate { LayoutMainArea(rightLayout, header, content); };
            LayoutMainArea(rightLayout, header, content);

            Panel formPanel = CardPanel();
            formPanel.Dock = DockStyle.Fill;
            formPanel.Margin = new Padding(0);
            content.Controls.Add(formPanel);
            BuildForm(formPanel);
            relayoutAfterScale = delegate
            {
                LayoutMainArea(rightLayout, header, content);
                LayoutHeader(header, headerActions);
                if (relayoutForm != null) relayoutForm();
                if (relayoutOperations != null) relayoutOperations();
            };
        }

        void LayoutMainArea(Panel host, Panel header, Panel content)
        {
            header.Location = new Point(0, 0);
            header.Size = new Size(Math.Max(1, host.ClientSize.Width), DpiUtil.Scale(112));
            content.Location = new Point(0, header.Bottom);
            content.Size = new Size(Math.Max(1, host.ClientSize.Width), Math.Max(1, host.ClientSize.Height - header.Bottom));
        }

        void LayoutHeader(Panel header, FlowLayoutPanel headerActions)
        {
            int gap = DpiUtil.Scale(12);
            int actionW = DpiUtil.Scale(284);
            int visibleRight = header.ClientSize.Width;
            if (formRightBoundary != null && !formRightBoundary.IsDisposed)
            {
                int boundaryX = header.PointToClient(formRightBoundary.PointToScreen(Point.Empty)).X;
                if (boundaryX > DpiUtil.Scale(360))
                {
                    visibleRight = Math.Min(visibleRight, boundaryX - DpiUtil.Scale(18));
                }
            }
            headerActions.Size = new Size(actionW, DpiUtil.Scale(46));
            headerActions.Location = new Point(Math.Max(0, visibleRight - actionW), DpiUtil.Scale(0));
            headerActions.BringToFront();

            int textW = Math.Max(DpiUtil.Scale(360), headerActions.Left - DpiUtil.Scale(20));
            titleLabel.Size = new Size(textW, DpiUtil.Scale(44));
            subtitleLabel.Location = new Point(0, DpiUtil.Scale(46));
            subtitleLabel.Size = new Size(textW, DpiUtil.Scale(30));

            int statusW = Math.Min(DpiUtil.Scale(210), Math.Max(DpiUtil.Scale(160), (textW - gap) / 2));
            rcloneStatus.Location = new Point(0, DpiUtil.Scale(78));
            rcloneStatus.Size = new Size(statusW, DpiUtil.Scale(34));
            winfspStatus.Location = new Point(statusW + gap, DpiUtil.Scale(78));
            winfspStatus.Size = new Size(statusW, DpiUtil.Scale(34));
        }

        void BuildForm(Panel panel)
        {
            formWidthControls.Clear();
            panel.Padding = new Padding(20);
            int y = 20;
            nameInput = AddInlineText(panel, "连接名称", y);
            y += FieldRowHeight;
            urlInput = AddInlineText(panel, "WebDAV 地址", y);
            y += FieldRowHeight;
            mountPointInput = AddInlineCombo(panel, "盘符", y, DriveLetters());
            y += FieldRowHeight;
            vendorInput = AddInlineCombo(panel, "服务类型", y, new string[] { "other|通用 WebDAV", "nextcloud|Nextcloud", "owncloud|ownCloud", "sharepoint|SharePoint", "sharepoint-ntlm|SharePoint NTLM", "fastmail|Fastmail" });
            y += FieldRowHeight;
            authTypeInput = AddInlineCombo(panel, "认证方式", y, new string[] { "basic|用户名 / 密码", "anonymous|匿名访问" });
            y += FieldRowHeight;
            usernameInput = AddInlineText(panel, "用户名", y);
            y += FieldRowHeight;
            passwordInput = AddInlineText(panel, "密码", y);
            passwordInput.UseSystemPasswordChar = true;
            passwordInput.PlaceholderTextCompat("留空则保持原密码");
            passwordInput.GotFocus += delegate { if (passwordInput.Text == SavedPasswordMask) passwordInput.Text = ""; };
            y += FieldRowHeight;
            cacheModeInput = AddInlineCombo(panel, "缓存模式", y, new string[] { "off|关闭", "minimal|最小", "writes|写入缓存", "full|完整缓存" });
            y += FieldRowHeight;
            AddCacheSizeInput(panel, y);
            y += FieldRowHeight + 6;

            TableLayoutPanel switches = new TableLayoutPanel();
            switches.Location = new Point(FieldX, y);
            switches.Size = new Size(430, 100);
            switches.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            switches.ColumnCount = 2;
            switches.RowCount = 2;
            switches.Padding = new Padding(0);
            switches.Margin = new Padding(0);
            switches.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            switches.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            switches.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            switches.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            networkModeInput = AddGridCheck(switches, "网络驱动器", 0, 0);
            skipTlsVerifyInput = AddGridCheck(switches, "跳过证书校验", 1, 0);
            readOnlyInput = AddGridCheck(switches, "只读", 0, 1);
            autoMountInput = AddGridCheck(switches, "系统服务启动后自动挂载", 1, 1);
            panel.Controls.Add(switches);
            formSwitches = switches;
            y += 108;

            Panel actions = new Panel();
            actions.Location = new Point(FormLeft, y);
            actions.Size = new Size(Math.Max(260, panel.ClientSize.Width - FormLeft * 2), 46);
            actions.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            actions.Height = 50;
            actions.Padding = new Padding(0);
            actions.Margin = new Padding(0);
            panel.Controls.Add(actions);
            saveButton = NewButton("保存", true);
            saveButton.Size = new Size(70, 40);
            saveButton.Click += delegate { SaveProfile(); };
            actions.Controls.Add(saveButton);
            testButton = NewButton("测试连接", false);
            testButton.Size = new Size(104, 40);
            testButton.Click += delegate { TestSelectedProfile(); };
            actions.Controls.Add(testButton);
            deleteButton = NewButton("删除", false);
            deleteButton.Size = new Size(72, 40);
            deleteButton.ForeColor = Color.FromArgb(180, 35, 24);
            deleteButton.Click += delegate { DeleteSelectedProfile(); };
            actions.Controls.Add(deleteButton);
            actions.Resize += delegate { LayoutActionButtons(actions); };
            LayoutActionButtons(actions);

            relayoutForm = delegate { LayoutFormFields(panel); };
            panel.Resize += delegate { LayoutFormFields(panel); };
        }

        int FormFieldWidth(Panel parent, int left)
        {
            int right = parent.ClientSize.Width;
            if (formRightBoundary != null && formRightBoundary.Parent != null)
            {
                Point boundaryOnScreen = formRightBoundary.Parent.PointToScreen(new Point(formRightBoundary.Left, 0));
                right = parent.PointToClient(boundaryOnScreen).X;
            }
            int available = right - left - DpiUtil.Scale(FieldRightPadding);
            int min = DpiUtil.Scale(140);
            return Math.Max(min, available);
        }

        void LayoutFormFields(Panel parent)
        {
            foreach (Control control in formWidthControls)
            {
                control.Width = FormFieldWidth(parent, control.Left);
            }
            if (formSwitches != null)
            {
                formSwitches.Width = Math.Min(FormFieldWidth(parent, formSwitches.Left), DpiUtil.Scale(430));
            }
        }

        TextBox AddInlineText(Panel parent, string label, int y)
        {
            AddInlineLabel(parent, label, y);
            TextBox input = new TextBox();
            input.AutoSize = false;
            input.Location = new Point(FieldX, y);
            input.Size = new Size(Math.Max(140, parent.ClientSize.Width - FieldX - FormLeft), FieldHeight);
            input.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            input.Margin = new Padding(0);
            parent.Controls.Add(input);
            formWidthControls.Add(input);
            return input;
        }

        ComboBox AddInlineCombo(Panel parent, string label, int y, string[] items)
        {
            AddInlineLabel(parent, label, y);
            ComboBox combo = CreateCombo(items);
            combo.Location = new Point(FieldX, y);
            combo.Size = new Size(Math.Max(140, parent.ClientSize.Width - FieldX - FormLeft), FieldHeight);
            combo.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            combo.Margin = new Padding(0);
            parent.Controls.Add(combo);
            formWidthControls.Add(combo);
            return combo;
        }

        void AddCacheSizeInput(Panel parent, int y)
        {
            AddInlineLabel(parent, "缓存上限", y);
            Panel cacheHost = new Panel();
            cacheHost.Location = new Point(FieldX, y);
            cacheHost.Size = new Size(Math.Max(180, parent.ClientSize.Width - FieldX - FormLeft), FieldHeight);
            cacheHost.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            cacheHost.Margin = new Padding(0);
            parent.Controls.Add(cacheHost);
            formWidthControls.Add(cacheHost);

            cacheSizeUnitInput = CreateNativeCombo(new string[] { "M|MB", "G|GB", "T|TB" });
            cacheSizeUnitInput.Width = 74;
            cacheSizeUnitInput.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            cacheHost.Controls.Add(cacheSizeUnitInput);

            cacheSizeValueInput = new NumericUpDown();
            cacheSizeValueInput.AutoSize = false;
            cacheSizeValueInput.Minimum = 1;
            cacheSizeValueInput.Maximum = 1024;
            cacheSizeValueInput.Value = 10;
            cacheSizeValueInput.Location = new Point(0, 0);
            cacheSizeValueInput.Size = new Size(Math.Max(80, cacheHost.ClientSize.Width - 86), FieldHeight);
            cacheSizeValueInput.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            cacheHost.Controls.Add(cacheSizeValueInput);
            cacheHost.Resize += delegate { LayoutCacheSizeInput(cacheHost); };
            LayoutCacheSizeInput(cacheHost);
            SetComboValue(cacheSizeUnitInput, "G");
        }

        void LayoutCacheSizeInput(Panel cacheHost)
        {
            int unitW = Math.Max(74, cacheSizeUnitInput.Width);
            int gap = Math.Max(12, unitW / 6);
            int fieldH = Math.Max(1, cacheHost.ClientSize.Height);
            int valueW = Math.Max(80, cacheHost.ClientSize.Width - unitW - gap);
            cacheSizeValueInput.Size = new Size(valueW, fieldH);
            int actualH = Math.Min(fieldH, cacheSizeValueInput.Height);
            int top = Math.Max(0, (fieldH - actualH) / 2);
            cacheSizeValueInput.Location = new Point(0, top);
            cacheSizeValueInput.Size = new Size(valueW, actualH);
            cacheSizeUnitInput.Location = new Point(Math.Max(0, cacheHost.ClientSize.Width - unitW), top);
            cacheSizeUnitInput.Size = new Size(unitW, actualH);
        }

        void AddInlineLabel(Panel parent, string text, int y)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = false;
            label.AutoEllipsis = false;
            label.Location = new Point(FormLeft, y);
            label.Size = new Size(FormLabelWidth, FieldHeight);
            label.ForeColor = Color.FromArgb(80, 92, 110);
            label.Font = UiFont(13, FontStyle.Bold);
            label.TextAlign = ContentAlignment.MiddleLeft;
            parent.Controls.Add(label);
        }

        ComboBox CreateCombo(string[] items)
        {
            ComboBox combo = new ComboBox();
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.DrawMode = DrawMode.OwnerDrawFixed;
            combo.ItemHeight = 27;
            combo.Height = 30;
            combo.DrawItem += DrawComboItem;
            foreach (string item in items) combo.Items.Add(new ComboItem(item));
            if (combo.Items.Count > 0) combo.SelectedIndex = 0;
            return combo;
        }

        ComboBox CreateNativeCombo(string[] items)
        {
            ComboBox combo = new ComboBox();
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.Margin = new Padding(0);
            foreach (string item in items) combo.Items.Add(new ComboItem(item));
            if (combo.Items.Count > 0) combo.SelectedIndex = 0;
            return combo;
        }

        void LayoutActionButtons(Panel actions)
        {
            if (saveButton == null || testButton == null || deleteButton == null) return;
            int y = 3;
            deleteButton.Location = new Point(0, y);
            saveButton.Location = new Point(deleteButton.Right + LayoutGap, y);
            testButton.Location = new Point(saveButton.Right + LayoutGap, y);
        }

        void BuildOperations(Panel panel)
        {
            panel.Padding = new Padding(20);
            int x = 20;
            int y = 20;
            int innerW = 320;
            int statusInset = 12;
            int buttonGap = LayoutGap;
            int smallButtonW = (innerW - buttonGap * 2) / 3;
            int wideButtonW = (innerW - buttonGap) / 2;

            Label diskTitle = new Label();
            diskTitle.Text = "磁盘操作";
            diskTitle.AutoSize = false;
            diskTitle.AutoEllipsis = true;
            diskTitle.Font = UiFont(17, FontStyle.Bold);
            diskTitle.Location = new Point(x, y);
            diskTitle.Size = new Size(innerW - 94 - statusInset, 26);
            panel.Controls.Add(diskTitle);
            mountStateLabel = new Label();
            mountStateLabel.AutoSize = false;
            mountStateLabel.Location = new Point(x, y + 28);
            mountStateLabel.Size = new Size(innerW, 22);
            mountStateLabel.AutoEllipsis = true;
            mountStateLabel.ForeColor = Color.FromArgb(99, 112, 131);
            panel.Controls.Add(mountStateLabel);
            mountBadgeLabel = StatusLabel();
            mountBadgeLabel.AutoSize = false;
            mountBadgeLabel.Size = new Size(84, 30);
            mountBadgeLabel.Text = "空闲";
            mountBadgeLabel.Location = new Point(x + innerW - statusInset - mountBadgeLabel.Width, y - 2);
            mountBadgeLabel.ForeColor = Color.FromArgb(99, 112, 131);
            panel.Controls.Add(mountBadgeLabel);

            y += 64;
            mountButton = NewButton("挂载", true);
            mountButton.Location = new Point(x, y);
            mountButton.Size = new Size(smallButtonW, 38);
            mountButton.Click += delegate { MountSelectedProfile(); };
            panel.Controls.Add(mountButton);
            unmountButton = NewButton("卸载", false);
            unmountButton.Location = new Point(x + smallButtonW + buttonGap, y);
            unmountButton.Size = new Size(smallButtonW, 38);
            unmountButton.Click += delegate { UnmountSelectedProfile(); };
            panel.Controls.Add(unmountButton);
            openDriveButton = NewButton("打开", false);
            openDriveButton.Location = new Point(x + (smallButtonW + buttonGap) * 2, y);
            openDriveButton.Size = new Size(smallButtonW, 38);
            openDriveButton.Click += delegate { OpenSelectedDrive(); };
            panel.Controls.Add(openDriveButton);

            y += 38 + buttonGap;
            Button configButton = NewButton("rclone.conf", false);
            configButton.Location = new Point(x, y);
            configButton.Size = new Size(smallButtonW, 36);
            configButton.Click += delegate { OpenPath(AppPaths.RcloneConfigPath); };
            panel.Controls.Add(configButton);
            Button cacheButton = NewButton("缓存目录", false);
            cacheButton.Location = new Point(x + smallButtonW + buttonGap, y);
            cacheButton.Size = new Size(smallButtonW, 36);
            cacheButton.Click += delegate { OpenPath(AppPaths.CacheDir); };
            panel.Controls.Add(cacheButton);
            Button logsButton = NewButton("日志目录", false);
            logsButton.Location = new Point(x + (smallButtonW + buttonGap) * 2, y);
            logsButton.Size = new Size(smallButtonW, 36);
            logsButton.Click += delegate { OpenPath(AppPaths.LogDir); };
            panel.Controls.Add(logsButton);

            y += 36 + 22;
            Panel divider = new Panel();
            divider.Location = new Point(x, y);
            divider.Size = new Size(innerW, 1);
            divider.BackColor = Color.FromArgb(217, 224, 231);
            panel.Controls.Add(divider);

            y += 20;
            Label serviceTitle = new Label();
            serviceTitle.Text = "系统服务";
            serviceTitle.AutoSize = false;
            serviceTitle.AutoEllipsis = true;
            serviceTitle.Font = UiFont(17, FontStyle.Bold);
            serviceTitle.Location = new Point(x, y);
            serviceTitle.Size = new Size(innerW - 94 - statusInset, 26);
            panel.Controls.Add(serviceTitle);
            serviceStatusLabel = new Label();
            serviceStatusLabel.Text = "只挂载已勾选“系统服务启动后自动挂载”的连接。";
            serviceStatusLabel.AutoSize = false;
            serviceStatusLabel.Size = new Size(innerW, 42);
            serviceStatusLabel.Location = new Point(x, y + 32);
            serviceStatusLabel.ForeColor = Color.FromArgb(99, 112, 131);
            serviceStatusLabel.TextAlign = ContentAlignment.TopLeft;
            panel.Controls.Add(serviceStatusLabel);
            serviceBadgeLabel = StatusLabel();
            serviceBadgeLabel.AutoSize = false;
            serviceBadgeLabel.Size = new Size(84, 30);
            serviceBadgeLabel.Location = new Point(x + innerW - statusInset - serviceBadgeLabel.Width, y - 2);
            serviceBadgeLabel.Text = "未安装";
            serviceBadgeLabel.ForeColor = Color.FromArgb(161, 92, 7);
            panel.Controls.Add(serviceBadgeLabel);

            y += 84;
            installServiceButton = NewButton("安装系统服务", true);
            installServiceButton.Location = new Point(x, y);
            installServiceButton.Size = new Size(wideButtonW, 38);
            installServiceButton.Click += delegate { ServiceAction("install"); };
            panel.Controls.Add(installServiceButton);
            startServiceButton = NewButton("启动", false);
            startServiceButton.Location = new Point(x + wideButtonW + buttonGap, y);
            startServiceButton.Size = new Size(wideButtonW, 38);
            startServiceButton.Click += delegate { ServiceAction("start"); };
            panel.Controls.Add(startServiceButton);
            stopServiceButton = NewButton("停止", false);
            stopServiceButton.Location = new Point(x, y + 38 + buttonGap);
            stopServiceButton.Size = new Size(wideButtonW, 38);
            stopServiceButton.Click += delegate { ServiceAction("stop"); };
            panel.Controls.Add(stopServiceButton);
            uninstallServiceButton = NewButton("卸载", false);
            uninstallServiceButton.Location = new Point(x + wideButtonW + buttonGap, y + 38 + buttonGap);
            uninstallServiceButton.Size = new Size(wideButtonW, 38);
            uninstallServiceButton.ForeColor = Color.FromArgb(180, 35, 24);
            uninstallServiceButton.Click += delegate { ServiceAction("uninstall"); };
            panel.Controls.Add(uninstallServiceButton);

            y += 38 + buttonGap + 44;
            Label logTitle = new Label();
            logTitle.Text = "日志";
            logTitle.Font = UiFont(17, FontStyle.Bold);
            logTitle.AutoSize = false;
            logTitle.AutoEllipsis = true;
            logTitle.Location = new Point(x, y);
            logTitle.Size = new Size(innerW - 84 - statusInset, 28);
            panel.Controls.Add(logTitle);
            refreshLogButton = NewButton("刷新", false);
            refreshLogButton.Size = new Size(72, 36);
            refreshLogButton.Location = new Point(x + innerW - statusInset - refreshLogButton.Width, y - 4);
            refreshLogButton.Click += delegate { RenderMountState(); };
            panel.Controls.Add(refreshLogButton);
            logOutput = new TextBox();
            logOutput.Multiline = true;
            logOutput.ScrollBars = ScrollBars.Vertical;
            logOutput.ReadOnly = true;
            logOutput.BackColor = Color.FromArgb(17, 25, 35);
            logOutput.ForeColor = Color.FromArgb(215, 224, 234);
            logOutput.Font = new Font("Consolas", 15F, FontStyle.Regular, GraphicsUnit.Pixel);
            logOutput.Location = new Point(x, y + 42);
            logOutput.Size = new Size(innerW, 160);
            logOutput.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            panel.Controls.Add(logOutput);

            Action layoutOperations = delegate
            {
                int pad = panel.Padding.Left;
                int gap = DpiUtil.Scale(12);
                int rowH = DpiUtil.Scale(40);
                int titleH = DpiUtil.Scale(34);
                int badgeH = DpiUtil.Scale(32);
                int rightInset = DpiUtil.Scale(12);
                int inner = Math.Max(DpiUtil.Scale(160), panel.ClientSize.Width - panel.Padding.Left - panel.Padding.Right);
                int cy = panel.Padding.Top;
                int badgeW = Math.Min(DpiUtil.Scale(96), Math.Max(DpiUtil.Scale(76), inner / 3));
                int badgeRight = pad + inner - rightInset;

                diskTitle.Location = new Point(pad, cy);
                diskTitle.Size = new Size(Math.Max(1, inner - badgeW - rightInset - gap), titleH);
                mountBadgeLabel.Size = new Size(badgeW, badgeH);
                mountBadgeLabel.Location = new Point(badgeRight - mountBadgeLabel.Width, cy - DpiUtil.Scale(2));
                mountStateLabel.Location = new Point(pad, cy + DpiUtil.Scale(38));
                mountStateLabel.Size = new Size(inner, DpiUtil.Scale(30));
                cy += DpiUtil.Scale(74);

                int diskButtonW = Math.Max(1, (inner - gap * 2) / 3);
                mountButton.Location = new Point(pad, cy);
                mountButton.Size = new Size(diskButtonW, rowH);
                unmountButton.Location = new Point(pad + diskButtonW + gap, cy);
                unmountButton.Size = new Size(diskButtonW, rowH);
                openDriveButton.Location = new Point(pad + (diskButtonW + gap) * 2, cy);
                openDriveButton.Size = new Size(diskButtonW, rowH);
                cy += rowH + gap;

                configButton.Location = new Point(pad, cy);
                configButton.Size = new Size(diskButtonW, rowH);
                cacheButton.Location = new Point(pad + diskButtonW + gap, cy);
                cacheButton.Size = new Size(diskButtonW, rowH);
                logsButton.Location = new Point(pad + (diskButtonW + gap) * 2, cy);
                logsButton.Size = new Size(diskButtonW, rowH);
                cy += rowH + DpiUtil.Scale(18);

                divider.Location = new Point(pad, cy);
                divider.Size = new Size(inner, 1);
                cy += DpiUtil.Scale(18);

                serviceTitle.Location = new Point(pad, cy);
                serviceTitle.Size = new Size(Math.Max(1, inner - badgeW - rightInset - gap), titleH);
                serviceBadgeLabel.Size = new Size(badgeW, badgeH);
                serviceBadgeLabel.Location = new Point(badgeRight - serviceBadgeLabel.Width, cy - DpiUtil.Scale(2));
                serviceStatusLabel.Location = new Point(pad, cy + DpiUtil.Scale(38));
                serviceStatusLabel.Size = new Size(inner, DpiUtil.Scale(52));
                cy += DpiUtil.Scale(94);

                int serviceButtonW = Math.Max(1, (inner - gap) / 2);
                installServiceButton.Location = new Point(pad, cy);
                installServiceButton.Size = new Size(serviceButtonW, rowH);
                startServiceButton.Location = new Point(pad + serviceButtonW + gap, cy);
                startServiceButton.Size = new Size(serviceButtonW, rowH);
                cy += rowH + gap;
                stopServiceButton.Location = new Point(pad, cy);
                stopServiceButton.Size = new Size(serviceButtonW, rowH);
                uninstallServiceButton.Location = new Point(pad + serviceButtonW + gap, cy);
                uninstallServiceButton.Size = new Size(serviceButtonW, rowH);
                cy += rowH + DpiUtil.Scale(18);

                logTitle.Location = new Point(pad, cy);
                logTitle.Size = new Size(Math.Max(1, inner - DpiUtil.Scale(86) - rightInset), titleH);
                refreshLogButton.Size = new Size(DpiUtil.Scale(78), rowH);
                refreshLogButton.Location = new Point(badgeRight - refreshLogButton.Width, cy - DpiUtil.Scale(4));
                logOutput.Location = new Point(pad, cy + DpiUtil.Scale(44));
                logOutput.Size = new Size(inner, Math.Max(DpiUtil.Scale(180), panel.ClientSize.Height - cy - DpiUtil.Scale(44) - panel.Padding.Bottom));
                logOutput.BringToFront();
            };
            panel.Resize += delegate { layoutOperations(); };
            relayoutOperations = layoutOperations;
            layoutOperations();
        }

        TableLayoutPanel ButtonGrid(int columns, int rows)
        {
            TableLayoutPanel grid = new TableLayoutPanel();
            grid.ColumnCount = columns;
            grid.RowCount = rows;
            grid.Padding = new Padding(0);
            for (int col = 0; col < columns; col++)
            {
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / columns));
            }
            for (int row = 0; row < rows; row++)
            {
                grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / rows));
            }
            return grid;
        }

        TableLayoutPanel ServiceButtonGrid()
        {
            TableLayoutPanel grid = new TableLayoutPanel();
            grid.ColumnCount = 2;
            grid.RowCount = 3;
            grid.Dock = DockStyle.Top;
            grid.Height = 80 + LayoutGap;
            grid.Padding = new Padding(0);
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, LayoutGap));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            return grid;
        }

        void AddGridButton(TableLayoutPanel grid, Button button, int col, int row, bool rightGap, bool bottomGap)
        {
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(0, 0, rightGap ? ButtonGap() : 0, bottomGap ? ButtonGap() : 0);
            grid.Controls.Add(button, col, row);
        }

        void CreateTray()
        {
            tray = new NotifyIcon();
            tray.Icon = Icon;
            tray.Text = "DavPilot";
            tray.Visible = true;
            tray.DoubleClick += delegate { ShowMainWindow(); };
            RefreshTray();
        }

        void RefreshTray()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("打开 DavPilot", null, delegate { ShowMainWindow(); });
            if (config != null)
            {
                foreach (Profile profile in config.profiles)
                {
                    bool mounted = rclone.Mounts.ContainsKey(profile.id);
                    menu.Items.Add((mounted ? "卸载 " : "挂载 ") + profile.name, null, delegate(object sender, EventArgs e)
                    {
                        if (mounted) rclone.UnmountProfile(profile.id);
                        else TryMount(profile);
                        RenderAll();
                    });
                }
            }
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { QuitApp(); });
            tray.ContextMenuStrip = menu;
        }

        void LoadState()
        {
            config = ConfigStore.Load();
            if (config.settings.closeToTray || config.settings.autoStartBackground)
            {
                config.settings.closeToTray = false;
                config.settings.autoStartBackground = false;
                ConfigStore.Save(config);
                UpdateLoginItem();
            }
            rclone.SyncConfig(config.profiles);
            if (selectedId == null && config.profiles.Count > 0) selectedId = config.profiles[0].id;
            if (!IsServiceRunning())
            {
                foreach (Profile profile in config.profiles)
                {
                    if (!profile.autoMount) continue;
                    try { TryMount(profile); }
                    catch { }
                }
            }
            RenderAll();
        }

        bool IsServiceRunning()
        {
            try { return WindowsServiceManager.GetStatus().Running; }
            catch { return false; }
        }

        bool IsServiceMounted(Profile profile)
        {
            return ServiceMountState(profile) == 1;
        }

        bool IsProfileMounted(Profile profile)
        {
            return profile != null && (rclone.Mounts.ContainsKey(profile.id) || IsServiceMounted(profile));
        }

        int ServiceMountState(Profile profile)
        {
            if (profile == null || !profile.autoMount || !IsServiceRunning()) return 0;
            string path = Path.Combine(AppPaths.LogDir, "service.log");
            if (!File.Exists(path)) return 0;
            string log;
            try { log = File.ReadAllText(path, Encoding.UTF8); }
            catch { return 0; }
            int start = log.LastIndexOf("DavPilot service mode started.", StringComparison.Ordinal);
            if (start >= 0) log = log.Substring(start);
            int mounted = log.LastIndexOf("Mounted " + profile.name + " at " + profile.mountPoint, StringComparison.Ordinal);
            int failed = log.LastIndexOf("Mount failed for " + profile.name + ":", StringComparison.Ordinal);
            if (mounted >= 0 && mounted > failed) return 1;
            if (failed >= 0 && failed > mounted) return -1;
            return 0;
        }

        void RenderAll()
        {
            RenderProfiles();
            RenderForm();
            RenderToolStatus();
            RenderMountState();
            RenderServiceStatus();
            RefreshTray();
        }

        void RelayoutSoon()
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke(new Action(delegate
            {
                if (IsDisposed) return;
                if (relayoutAfterScale != null) relayoutAfterScale();
                if (profileList != null) profileList.Invalidate();
            }));
        }

        void RenderProfiles()
        {
            rendering = true;
            try
            {
                profileList.Items.Clear();
                foreach (Profile profile in config.profiles)
                {
                    string status = rclone.Mounts.ContainsKey(profile.id) ? "已挂载" : "空闲";
                    ProfileItem item = new ProfileItem
                    {
                        Id = profile.id,
                        Name = profile.name,
                        Meta = profile.mountPoint + " - " + status,
                        Url = profile.url,
                        Text = profile.name + "   " + profile.mountPoint + " - " + status
                    };
                    profileList.Items.Add(item);
                    if (profile.id == selectedId) profileList.SelectedItem = item;
                }
            }
            finally
            {
                rendering = false;
            }
        }

        void DrawProfileItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            ProfileItem item = profileList.Items[e.Index] as ProfileItem;
            bool active = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int outerX = DpiUtil.Scale(1);
            int outerY = DpiUtil.Scale(6);
            int padX = DpiUtil.Scale(14);
            Rectangle rect = new Rectangle(e.Bounds.X + outerX, e.Bounds.Y + outerY, e.Bounds.Width - DpiUtil.Scale(4), e.Bounds.Height - DpiUtil.Scale(12));
            Color bg = active ? Color.FromArgb(28, 69, 84) : Color.FromArgb(31, 43, 54);
            Color border = active ? Color.FromArgb(40, 160, 199) : Color.FromArgb(50, 62, 73);
            using (GraphicsPath path = StyledPanel.RoundedRect(rect, 8))
            using (SolidBrush brush = new SolidBrush(bg))
            using (Pen pen = new Pen(border, active ? 2 : 1))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }

            Rectangle nameRect = new Rectangle(rect.X + padX, rect.Y + DpiUtil.Scale(12), rect.Width - padX * 2, DpiUtil.Scale(32));
            Rectangle metaRect = new Rectangle(rect.X + padX, rect.Y + DpiUtil.Scale(50), rect.Width - padX * 2, DpiUtil.Scale(28));
            Rectangle urlRect = new Rectangle(rect.X + padX, rect.Y + DpiUtil.Scale(82), rect.Width - padX * 2, DpiUtil.Scale(28));
            string name = item == null ? "" : item.Name;
            string meta = item == null ? "" : item.Meta;
            string url = item == null ? "" : item.Url;
            TextRenderer.DrawText(e.Graphics, name, UiFont(14, FontStyle.Bold), nameRect, Color.White, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(e.Graphics, meta, UiFont(12), metaRect, Color.FromArgb(200, 212, 223), TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(e.Graphics, url, UiFont(12), urlRect, Color.FromArgb(200, 212, 223), TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
        }

        void RenderForm()
        {
            Profile profile = SelectedProfile();
            if (profile == null)
            {
                titleLabel.Text = "新建 WebDAV";
                subtitleLabel.Text = "把 WebDAV 地址挂载成 Windows 盘符。";
                nameInput.Text = "我的 WebDAV";
                urlInput.Text = "";
                mountPointInput.SelectedItem = "Z:";
                SetComboValue(vendorInput, "other");
                SetComboValue(authTypeInput, "basic");
                usernameInput.Text = "";
                passwordInput.Text = "";
                SetComboValue(cacheModeInput, "writes");
                SetCacheSize("10G");
                networkModeInput.Checked = true;
                skipTlsVerifyInput.Checked = false;
                readOnlyInput.Checked = false;
                autoMountInput.Checked = false;
            }
            else
            {
                titleLabel.Text = profile.name;
                subtitleLabel.Text = profile.mountPoint + " - " + profile.url;
                nameInput.Text = profile.name;
                urlInput.Text = profile.url;
                mountPointInput.SelectedItem = profile.mountPoint;
                SetComboValue(vendorInput, profile.vendor);
                SetComboValue(authTypeInput, profile.authType);
                usernameInput.Text = profile.username;
                passwordInput.Text = string.IsNullOrEmpty(profile.passwordObscured) ? "" : SavedPasswordMask;
                SetComboValue(cacheModeInput, profile.cacheMode);
                SetCacheSize(profile.cacheMaxSize);
                networkModeInput.Checked = profile.networkMode;
                skipTlsVerifyInput.Checked = profile.skipTlsVerify;
                readOnlyInput.Checked = profile.readOnly;
                autoMountInput.Checked = profile.autoMount;
            }
        }

        void RenderToolStatus()
        {
            ToolStatus rc = rclone.CheckRclone();
            ToolStatus wf = rclone.CheckWinFsp();
            rcloneStatus.Text = rc.Available ? (rc.Version + " 已就绪") : "缺少 rclone";
            rcloneStatus.ForeColor = rc.Available ? Color.FromArgb(27, 122, 67) : Color.FromArgb(161, 92, 7);
            winfspStatus.Text = wf.Available ? "WinFsp 已就绪" : "缺少 WinFsp";
            winfspStatus.ForeColor = wf.Available ? Color.FromArgb(27, 122, 67) : Color.FromArgb(161, 92, 7);
        }

        void RenderMountState()
        {
            Profile profile = SelectedProfile();
            bool mounted = profile != null && rclone.Mounts.ContainsKey(profile.id);
            int serviceMountState = ServiceMountState(profile);
            bool serviceMounted = serviceMountState == 1;
            bool serviceBusy = profile != null && profile.autoMount && IsServiceRunning() && !serviceMounted;
            bool serviceFailed = serviceMountState == -1;
            mountStateLabel.Text = serviceMounted ? (profile.mountPoint + " 系统服务已挂载") : (serviceFailed ? "系统服务挂载失败，请查看日志" : (serviceBusy ? "系统服务运行中，等待挂载" : (mounted ? (profile.mountPoint + " 已挂载，PID " + rclone.Mounts[profile.id].Process.Id) : "未挂载")));
            mountBadgeLabel.Text = serviceMounted || mounted ? "已挂载" : "空闲";
            if (serviceFailed) mountBadgeLabel.Text = "失败";
            mountBadgeLabel.ForeColor = serviceMounted || mounted ? Color.FromArgb(27, 122, 67) : (serviceFailed ? Color.FromArgb(161, 92, 7) : Color.FromArgb(99, 112, 131));
            mountButton.Text = serviceMounted ? "系统服务已挂载" : (serviceFailed ? "服务挂载失败" : (serviceBusy ? "系统服务挂载中" : "挂载"));
            mountButton.Font = serviceMounted || serviceBusy || serviceFailed ? UiFont(10, FontStyle.Bold) : UiFont(13, FontStyle.Bold);
            mountButton.Padding = serviceMounted || serviceBusy || serviceFailed ? new Padding(2, 0, 2, 0) : new Padding(8, 0, 8, 0);
            mountButton.Enabled = profile != null && !mounted && !serviceMounted && !serviceBusy && !serviceFailed;
            unmountButton.Enabled = profile != null && mounted;
            openDriveButton.Enabled = profile != null && (mounted || serviceMounted);
            testButton.Enabled = profile != null;
            deleteButton.Enabled = profile != null && !IsProfileMounted(profile);
            if (profile == null) logOutput.Text = "";
            else
            {
                string log = rclone.ReadLog(profile.id);
                logOutput.Text = log.Length == 0 ? "暂无日志。" : log;
            }
        }

        void RenderServiceStatus()
        {
            ServiceStatus status = WindowsServiceManager.GetStatus();
            if (!status.Available)
            {
                serviceStatusLabel.Text = status.Message.Length > 0 ? status.Message : "请使用打包后的便携版启用系统服务。";
            }
            else if (status.Running)
            {
                serviceStatusLabel.Text = "系统服务正在运行，会在开机后自动挂载勾选的连接。";
            }
            else if (status.Pending)
            {
                serviceStatusLabel.Text = "系统服务正在切换状态，请稍等几秒后刷新。";
            }
            else if (status.Installed)
            {
                serviceStatusLabel.Text = "系统服务已安装但未运行，可手动启动或等待下次开机。";
            }
            else
            {
                serviceStatusLabel.Text = "安装时会请求管理员权限，只挂载已勾选“系统服务启动后自动挂载”的连接。";
            }
            serviceBadgeLabel.Text = status.Label;
            serviceBadgeLabel.ForeColor = status.Running ? Color.FromArgb(27, 122, 67) : (status.Installed ? Color.FromArgb(161, 92, 7) : Color.FromArgb(99, 112, 131));
            installServiceButton.Enabled = status.Available && !status.Installed;
            startServiceButton.Enabled = status.Available && status.Installed && !status.Running && !status.Pending;
            stopServiceButton.Enabled = status.Available && status.Installed && status.Running && !status.Pending;
            uninstallServiceButton.Enabled = status.Available && status.Installed && !status.Pending;
        }

        Profile SelectedProfile()
        {
            foreach (Profile profile in config.profiles)
            {
                if (profile.id == selectedId) return profile;
            }
            return null;
        }

        Profile CollectProfile(Profile existing)
        {
            Profile profile = existing == null ? new Profile() : Clone(existing);
            profile.name = nameInput.Text.Trim();
            profile.url = urlInput.Text.Trim();
            profile.mountPoint = Convert.ToString(mountPointInput.SelectedItem);
            profile.vendor = ComboValue(vendorInput);
            profile.authType = ComboValue(authTypeInput);
            profile.username = usernameInput.Text.Trim();
            profile.cacheMode = ComboValue(cacheModeInput);
            profile.cacheMaxSize = CacheSizeText();
            profile.networkMode = networkModeInput.Checked;
            profile.skipTlsVerify = skipTlsVerifyInput.Checked;
            profile.readOnly = readOnlyInput.Checked;
            profile.autoMount = autoMountInput.Checked;
            if (passwordInput.Text.Length > 0 && passwordInput.Text != SavedPasswordMask) profile.passwordObscured = rclone.ObscurePassword(passwordInput.Text);
            return ConfigStore.NormalizeProfile(profile, existing);
        }

        void SetCacheSize(string value)
        {
            Match match = Regex.Match(value ?? "", @"^\s*(\d{1,4})\s*([MGT])B?\s*$", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                cacheSizeValueInput.Value = 10;
                SetComboValue(cacheSizeUnitInput, "G");
                return;
            }

            int number = Math.Max(1, Math.Min(1024, int.Parse(match.Groups[1].Value)));
            cacheSizeValueInput.Value = number;
            SetComboValue(cacheSizeUnitInput, match.Groups[2].Value.ToUpperInvariant());
        }

        string CacheSizeText()
        {
            int number = (int)cacheSizeValueInput.Value;
            string unit = ComboValue(cacheSizeUnitInput);
            if (unit != "M" && unit != "G" && unit != "T") throw new Exception("缓存单位必须是 MB、GB 或 TB");
            return number.ToString() + unit;
        }

        void SaveProfile()
        {
            try
            {
                Profile existing = SelectedProfile();
                Profile normalized = CollectProfile(existing);
                if (existing == null) config.profiles.Add(normalized);
                else
                {
                    int index = config.profiles.FindIndex(delegate(Profile p) { return p.id == existing.id; });
                    config.profiles[index] = normalized;
                }
                selectedId = normalized.id;
                ConfigStore.Save(config);
                rclone.SyncConfig(config.profiles);
                MessageBox.Show("连接已保存", "DavPilot");
                RenderAll();
            }
            catch (Exception ex) { ShowError(ex); }
        }

        void DeleteSelectedProfile()
        {
            Profile profile = SelectedProfile();
            if (profile == null) return;
            if (IsProfileMounted(profile))
            {
                MessageBox.Show("当前连接正在挂载中，不能删除。请先卸载磁盘或停止系统服务。", "DavPilot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (MessageBox.Show("确定删除“" + profile.name + "”吗？", "DavPilot", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            rclone.UnmountProfile(profile.id);
            config.profiles.Remove(profile);
            selectedId = config.profiles.Count > 0 ? config.profiles[0].id : null;
            ConfigStore.Save(config);
            rclone.SyncConfig(config.profiles);
            RenderAll();
        }

        void TestSelectedProfile()
        {
            Profile profile = SelectedProfile();
            if (profile == null) return;
            RunBusy("正在测试连接...", delegate { rclone.TestProfile(profile, config.profiles); }, "连接测试通过", delegate(Exception ex) { return HandleCertificateTrust(profile, ex, delegate { rclone.TestProfile(profile, config.profiles); }); });
        }

        void MountSelectedProfile()
        {
            Profile profile = SelectedProfile();
            if (profile == null) return;
            RunBusy("正在挂载...", delegate { TryMount(profile); }, "已挂载", delegate(Exception ex) { return HandleCertificateTrust(profile, ex, delegate { TryMount(profile); }); });
        }

        void UnmountSelectedProfile()
        {
            Profile profile = SelectedProfile();
            if (profile == null) return;
            rclone.UnmountProfile(profile.id);
            RenderAll();
        }

        void TryMount(Profile profile)
        {
            rclone.MountProfile(profile, config.profiles);
        }

        bool HandleCertificateTrust(Profile profile, Exception ex, Action retry)
        {
            if (profile.skipTlsVerify || !Regex.IsMatch(ex.Message, "certificate signed by unknown authority|x509|tls: failed to verify certificate", RegexOptions.IgnoreCase)) return false;
            DialogResult result = MessageBox.Show("这个 WebDAV 服务器的 HTTPS 证书不受信任。\n\n连接：" + profile.name + "\n地址：" + profile.url + "\n\n如果这是你自己的 OpenList/Alist 服务或内网证书，可以选择信任并继续。DavPilot 只会对这个连接跳过证书校验。", "证书不受信任", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
            if (result != DialogResult.OK) return true;
            profile.skipTlsVerify = true;
            ConfigStore.Save(config);
            rclone.SyncConfig(config.profiles);
            retry();
            return true;
        }

        void OpenSelectedDrive()
        {
            Profile profile = SelectedProfile();
            if (profile != null) Process.Start(profile.mountPoint + "\\");
        }

        void OpenPath(string path)
        {
            Directory.CreateDirectory(Directory.Exists(path) ? path : Path.GetDirectoryName(path));
            Process.Start(path);
        }

        void ServiceAction(string action)
        {
            try
            {
                if (action == "uninstall" && MessageBox.Show("确定要停止并卸载 DavPilot 系统服务吗？", "DavPilot", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                MessageBox.Show("接下来会弹出 UAC，请确认管理员权限。", "DavPilot");
                if (action == "install" || action == "start") UnmountAutoProfilesForService();
                if (action == "install") WindowsServiceManager.Install();
                if (action == "start") WindowsServiceManager.Start();
                if (action == "stop") WindowsServiceManager.Stop();
                if (action == "uninstall") WindowsServiceManager.Uninstall();
                RenderAll();
            }
            catch (Exception ex) { ShowError(ex); }
        }

        void UnmountAutoProfilesForService()
        {
            List<string> ids = new List<string>();
            foreach (Profile profile in config.profiles)
            {
                if (profile.autoMount && rclone.Mounts.ContainsKey(profile.id)) ids.Add(profile.id);
            }
            foreach (string id in ids) rclone.UnmountProfile(id);
        }

        void RunBusy(string busyText, Action action, string success)
        {
            RunBusy(busyText, action, success, null);
        }

        void RunBusy(string busyText, Action action, string success, Func<Exception, bool> onError)
        {
            Cursor old = Cursor;
            Cursor = Cursors.WaitCursor;
            try
            {
                action();
                MessageBox.Show(success, "DavPilot");
            }
            catch (Exception ex)
            {
                if (onError != null && onError(ex))
                {
                    MessageBox.Show(success, "DavPilot");
                }
                else ShowError(ex);
            }
            finally
            {
                Cursor = old;
                RenderAll();
            }
        }

        void UpdateLoginItem()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", true))
                {
                    if (config.settings.autoStartBackground)
                    {
                        key.SetValue("DavPilot", "\"" + AppPaths.AppExePath() + "\" --background");
                    }
                    else key.DeleteValue("DavPilot", false);
                }
            }
            catch { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (isQuitting) { base.OnFormClosing(e); return; }
            isQuitting = true;
            try { rclone.UnmountAll(); } catch { }
            if (tray != null) tray.Visible = false;
            base.OnFormClosing(e);
        }

        void ShowMainWindow()
        {
            ShowInTaskbar = true;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            RelayoutSoon();
        }

        void QuitApp()
        {
            isQuitting = true;
            rclone.UnmountAll();
            tray.Visible = false;
            Application.Exit();
        }

        void ShowError(Exception ex)
        {
            MessageBox.Show(ex.Message, "DavPilot", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        int ButtonGap()
        {
            return LayoutGap;
        }

        void SpaceButton(Button button)
        {
            SpaceButton(button, true);
        }

        void SpaceButton(Button button, bool rightGap)
        {
            int gap = ButtonGap();
            button.Margin = new Padding(0, 0, rightGap ? gap : 0, gap);
        }

        static Button NewButton(string text, bool primary)
        {
            StyledButton button = new StyledButton();
            button.Text = text;
            button.Width = 110;
            button.Height = 40;
            button.NormalBack = primary ? Color.FromArgb(37, 111, 143) : Color.White;
            button.HoverBack = primary ? Color.FromArgb(24, 90, 117) : Color.FromArgb(238, 242, 245);
            button.BorderColor = primary ? Color.FromArgb(37, 111, 143) : Color.FromArgb(217, 224, 231);
            button.ForeColor = primary ? Color.White : Color.FromArgb(23, 32, 42);
            button.Font = UiFont(13, FontStyle.Bold);
            button.Padding = new Padding(8, 0, 8, 0);
            button.Margin = new Padding(0);
            return button;
        }

        static Label StatusLabel()
        {
            Label label = new PillLabel();
            label.AutoSize = false;
            label.Font = UiFont(12, FontStyle.Bold);
            label.Padding = new Padding(12, 6, 12, 6);
            label.Size = new Size(84, 30);
            label.TextAlign = ContentAlignment.MiddleCenter;
            return label;
        }

        static Panel CardPanel()
        {
            StyledPanel panel = new StyledPanel();
            panel.Dock = DockStyle.Fill;
            panel.Margin = new Padding(0);
            panel.Padding = new Padding(20);
            panel.BackColor = Color.White;
            panel.BorderColor = Color.FromArgb(217, 224, 231);
            panel.Radius = 8;
            panel.AutoScroll = true;
            return panel;
        }

        TextBox AddText(TableLayoutPanel table, string label, int col, int row)
        {
            Panel wrapper = FieldWrapper(label);
            TextBox input = new TextBox();
            input.Dock = DockStyle.Bottom;
            input.AutoSize = false;
            input.Height = 40;
            wrapper.Controls.Add(input);
            table.Controls.Add(wrapper, col, row);
            return input;
        }

        ComboBox AddCombo(TableLayoutPanel table, string label, int col, int row, string[] items)
        {
            Panel wrapper = FieldWrapper(label);
            ComboBox combo = new ComboBox();
            combo.Dock = DockStyle.Bottom;
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.DrawMode = DrawMode.OwnerDrawFixed;
            combo.ItemHeight = 37;
            combo.Height = 40;
            combo.DrawItem += DrawComboItem;
            foreach (string item in items) combo.Items.Add(new ComboItem(item));
            if (combo.Items.Count > 0) combo.SelectedIndex = 0;
            wrapper.Controls.Add(combo);
            table.Controls.Add(wrapper, col, row);
            return combo;
        }

        static void DrawComboItem(object sender, DrawItemEventArgs e)
        {
            ComboBox combo = sender as ComboBox;
            if (combo == null || e.Index < 0) return;
            e.DrawBackground();
            string text = combo.GetItemText(combo.Items[e.Index]);
            Color color = (e.State & DrawItemState.Selected) == DrawItemState.Selected ? SystemColors.HighlightText : combo.ForeColor;
            Rectangle textBounds = new Rectangle(e.Bounds.Left + 8, e.Bounds.Top, e.Bounds.Width - 16, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, text, combo.Font, textBounds, color, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            e.DrawFocusRectangle();
        }

        Panel FieldWrapper(string text)
        {
            Panel wrapper = new Panel();
            wrapper.Height = 64;
            wrapper.Dock = DockStyle.Top;
            wrapper.Margin = new Padding(8);
            Label label = new Label();
            label.Text = text;
            label.Dock = DockStyle.Top;
            label.Height = 24;
            label.ForeColor = Color.FromArgb(99, 112, 131);
            label.Font = UiFont(13, FontStyle.Bold);
            wrapper.Controls.Add(label);
            return wrapper;
        }

        static CheckBox AddCheck(Control parent, string text)
        {
            CheckBox check = new CheckBox();
            check.Text = text;
            check.AutoSize = true;
            check.Margin = new Padding(8, 6, 8, 6);
            parent.Controls.Add(check);
            return check;
        }

        static CheckBox AddGridCheck(TableLayoutPanel parent, string text, int col, int row)
        {
            CheckBox check = new CheckBox();
            check.Text = text;
            check.AutoSize = false;
            check.Dock = DockStyle.Fill;
            check.Margin = new Padding(0, 0, 16, 8);
            check.Padding = new Padding(0);
            check.TextAlign = ContentAlignment.MiddleLeft;
            check.Font = UiFont(13);
            parent.Controls.Add(check, col, row);
            return check;
        }

        static string[] DriveLetters()
        {
            List<string> values = new List<string>();
            for (char c = 'D'; c <= 'Z'; c++) values.Add(c + ":");
            return values.ToArray();
        }

        string ComboValue(ComboBox combo)
        {
            ComboItem item = combo.SelectedItem as ComboItem;
            return item == null ? Convert.ToString(combo.SelectedItem) : item.Value;
        }

        void SetComboValue(ComboBox combo, string value)
        {
            foreach (object obj in combo.Items)
            {
                ComboItem item = obj as ComboItem;
                if ((item != null && item.Value == value) || Convert.ToString(obj) == value)
                {
                    combo.SelectedItem = obj;
                    return;
                }
            }
        }

        Profile Clone(Profile source)
        {
            return ConfigStore.NormalizeProfile(source, source);
        }
    }

    class ProfileItem
    {
        public string Id;
        public string Name;
        public string Meta;
        public string Url;
        public string Text;
        public override string ToString() { return Text; }
    }

    class ComboItem
    {
        public string Value;
        public string Label;
        public ComboItem(string encoded)
        {
            string[] parts = encoded.Split(new char[] { '|' }, 2);
            Value = parts[0];
            Label = parts.Length > 1 ? parts[1] : parts[0];
        }
        public override string ToString() { return Label; }
    }

    class StyledPanel : Panel
    {
        public Color BorderColor = Color.FromArgb(217, 224, 231);
        public int Radius = 8;

        public StyledPanel()
        {
            DoubleBuffered = true;
            BackColor = Color.White;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = RoundedRect(rect, Radius))
            using (Pen pen = new Pen(BorderColor))
            {
                e.Graphics.DrawPath(pen, path);
            }
        }

        public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    class ServiceStripPanel : Panel
    {
        public Color LineColor = Color.FromArgb(217, 224, 231);

        public ServiceStripPanel()
        {
            DoubleBuffered = true;
            BackColor = Color.White;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen pen = new Pen(LineColor))
            {
                e.Graphics.DrawLine(pen, 0, 0, Width, 0);
                e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
            }
        }
    }

    class PillLabel : Label
    {
        public Color BorderColor = Color.FromArgb(217, 224, 231);

        public PillLabel()
        {
            DoubleBuffered = true;
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            Rectangle textRect = new Rectangle(10, 0, Math.Max(1, Width - 20), Height);
            Color back = Color.White;
            Color border = BorderColor;
            if (ForeColor.ToArgb() == Color.FromArgb(27, 122, 67).ToArgb())
            {
                back = Color.FromArgb(237, 248, 241);
                border = Color.FromArgb(198, 231, 211);
            }
            else if (ForeColor.ToArgb() == Color.FromArgb(161, 92, 7).ToArgb())
            {
                back = Color.FromArgb(255, 247, 237);
                border = Color.FromArgb(239, 214, 184);
            }

            using (GraphicsPath path = StyledPanel.RoundedRect(rect, Math.Max(1, Height / 2)))
            using (SolidBrush brush = new SolidBrush(back))
            using (Pen pen = new Pen(border))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }
            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                textRect,
                ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix
            );
        }
    }

    class StyledButton : Button
    {
        public int Radius = 6;
        public Color NormalBack = Color.White;
        public Color HoverBack = Color.White;
        public Color BorderColor = Color.FromArgb(217, 224, 231);
        bool hovering = false;

        public StyledButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            DoubleBuffered = true;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hovering = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hovering = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            pevent.Graphics.Clear(Parent == null ? BackColor : Parent.BackColor);
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            Rectangle textRect = new Rectangle(Padding.Left, 0, Math.Max(1, Width - Padding.Left - Padding.Right), Height);
            Color fill = Enabled ? (hovering ? HoverBack : NormalBack) : Color.FromArgb(244, 247, 250);
            Color border = Enabled ? BorderColor : Color.FromArgb(217, 224, 231);
            Color text = Enabled ? ForeColor : Color.FromArgb(140, 148, 158);
            using (GraphicsPath path = StyledPanel.RoundedRect(rect, Radius))
            using (SolidBrush brush = new SolidBrush(fill))
            using (Pen pen = new Pen(border))
            using (SolidBrush textBrush = new SolidBrush(text))
            {
                pevent.Graphics.FillPath(brush, path);
                pevent.Graphics.DrawPath(pen, path);
                TextRenderer.DrawText(
                    pevent.Graphics,
                    Text,
                    Font,
                    textRect,
                    text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix
                );
            }
        }
    }

    static class TextBoxExtensions
    {
        public static void PlaceholderTextCompat(this TextBox textBox, string text)
        {
            textBox.Tag = text;
        }
    }
}
