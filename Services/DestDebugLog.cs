using BepInEx;
using ProjectM.Network;
using Stunlock.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Entities;

namespace Satisvampory.Services
{
    /// <summary>
    /// Rolling diagnostic log for dupe / missing-item / perf. Not BepInEx LogOutput.
    /// Path: {BepInEx}/Log/Satisvampory.log (2 MB x current+3 backups, ~8 MB cap).
    /// In-memory ring is what the debug mailbox / .s diag returns for remote users.
    /// </summary>
    internal static class DestDebugLog
    {
        const long MaxBytes = 2L * 1024 * 1024;
        const int Backups = 3;
        const int RingSize = 256;
        const int MailboxLines = 80;
        const string FileStem = "Satisvampory";
        static readonly object Gate = new();
        static string _dir;
        static string _path;
        static StreamWriter _writer;
        static bool _initTried;
        static readonly string[] Ring = new string[RingSize];
        static int ringWrite;
        static int ringCount;
        static readonly Dictionary<string, DateTime> throttle = new();
        static readonly string[] lastPerf = new string[24];
        static int perfWrite;
        static int perfCount;
        static int unflushed;
        static DateTime lastFlushUtc = DateTime.MinValue;
        const int FlushEveryLines = 32;
        const int FlushEveryMs = 250;

        public static string LogPath => _path ?? "";
        public static string LogDir => _dir ?? "";

        public static void Init()
        {
            lock (Gate)
            {
                EnsureWriter_NoLock();
            }
        }

        public static void Close()
        {
            lock (Gate)
            {
                try
                {
                    FlushWriter_NoLock(force: true);
                    _writer?.Dispose();
                }
                catch { }
                _writer = null;
            }
        }

        static void EnsureWriter_NoLock()
        {
            if (_writer != null)
                return;
            if (_initTried && _writer == null)
                return;
            _initTried = true;
            try
            {
                _dir = Path.Combine(Paths.BepInExRootPath, "Log");
                Directory.CreateDirectory(_dir);
                _path = Path.Combine(_dir, FileStem + ".log");
                TryDeleteLegacyKindredLogs_NoLock();
                _writer = NewWriter(FileMode.Append);
                _writer.WriteLine($"{Stamp()} kind=boot via=boot plot=-1 item= FileVersion={MyPluginInfo.PLUGIN_VERSION}");
                _writer.Flush();
            }
            catch (Exception e)
            {
                try { Core.Log?.LogWarning($"[Satisvampory] DestDebugLog init failed: {e.Message}"); } catch { }
                _writer = null;
            }
        }

        static StreamWriter NewWriter(FileMode mode)
        {
            return new StreamWriter(new FileStream(_path, mode, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false))
            {
                AutoFlush = false
            };
        }

        static void TryDeleteLegacyKindredLogs_NoLock()
        {
            try
            {
                var leftovers = Directory.GetFiles(_dir, "KindredDest*.log");
                for (var i = 0; i < leftovers.Length; i++)
                    File.Delete(leftovers[i]);
            }
            catch { }
        }

        static void RotateIfNeeded_NoLock()
        {
            if (string.IsNullOrEmpty(_path))
                return;
            try
            {
                var fi = new FileInfo(_path);
                if (!fi.Exists || fi.Length < MaxBytes)
                    return;
                _writer?.Flush();
                _writer?.Dispose();
                _writer = null;
                unflushed = 0;
                lastFlushUtc = DateTime.UtcNow;
                var oldest = Path.Combine(_dir, $"{FileStem}.{Backups}.log");
                if (File.Exists(oldest))
                    File.Delete(oldest);
                for (var i = Backups - 1; i >= 1; i--)
                {
                    var src = Path.Combine(_dir, $"{FileStem}.{i}.log");
                    var dst = Path.Combine(_dir, $"{FileStem}.{i + 1}.log");
                    if (File.Exists(src))
                        File.Move(src, dst);
                }
                File.Move(_path, Path.Combine(_dir, FileStem + ".1.log"));
                _writer = NewWriter(FileMode.Create);
            }
            catch { }
        }

        static string Stamp() => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

        public static string ChestTag(Entity stash)
        {
            if (stash == Entity.Null || !Core.EntityManager.Exists(stash))
                return "-";
            var name = StashRouting.DestName(stash);
            if (string.IsNullOrEmpty(name))
                name = "(unnamed)";
            var net = "-";
            try
            {
                if (stash.Has<NetworkId>())
                    net = stash.Read<NetworkId>().ToString();
            }
            catch { }
            return name.Replace('"', '\'') + "#" + net;
        }

        public static void Move(string via, int plot, PrefabGUID item, int amount, Entity src, Entity dest, string destClass, int leftover, string belt)
        {
            if (amount <= 0)
                return;
            Write($"kind=move via={via} plot={plot} item={StashRouting.ItemLabel(item)} amount={amount} src=\"{ChestTag(src)}\" dest=\"{ChestTag(dest)}\" destClass={destClass} leftover={leftover} belt={belt}");
        }

        public static void Skip(string via, int plot, PrefabGUID item, Entity src, string reason)
        {
            if (string.IsNullOrEmpty(reason))
                return;
            Write($"kind=skip via={via} plot={plot} item={StashRouting.ItemLabel(item)} amount=0 src=\"{ChestTag(src)}\" dest=\"-\" skip={reason}");
        }

        public static void Dupe(string via, int plot, PrefabGUID item, int amount, string detail)
        {
            Write($"kind=dupe via={via} plot={plot} item={StashRouting.ItemLabel(item)} amount={amount} {detail}");
        }

        public static void Miss(string via, int plot, PrefabGUID item, int have, int need, string reason)
        {
            var guid = item.GuidHash;
            var key = via + ":" + plot + ":" + guid + ":" + (reason ?? "");
            var seconds = via == "covering" ? 30 : 8;
            if (!Allow(key, seconds))
                return;
            Write($"kind=miss via={via} plot={plot} item={StashRouting.ItemLabel(item)} have={have} need={need} reason={reason}");
        }

        public static void Perf(string via, int occupied, int pulls, int ms, int queue, int players, int skip = 0, int backoff = 0)
        {
            var line = $"kind=perf via={via} occupied={occupied} pulls={pulls} ms={ms} queue={queue} players={players} skip={skip} backoff={backoff}";
            Write(line);
            lock (Gate)
            {
                lastPerf[perfWrite % lastPerf.Length] = Stamp() + " " + line;
                perfWrite++;
                if (perfCount < lastPerf.Length)
                    perfCount++;
                FlushWriter_NoLock(force: true);
            }
        }

        public static void Guest(int plot, ulong steam, string reason)
        {
            if (!Allow("guest:" + steam + ":" + plot, 30))
                return;
            Write($"kind=guest via=occupy plot={plot} steam={steam} reason={reason}");
        }

        public static void Note(string via, int plot, ulong steam, string detail)
        {
            Write($"kind=note via={via} plot={plot} steam={steam} {detail}");
        }

        static bool Allow(string key, int seconds)
        {
            lock (Gate)
            {
                var now = DateTime.UtcNow;
                if (throttle.Count > 240)
                    throttle.Clear();
                if (throttle.TryGetValue(key, out var at) && (now - at).TotalSeconds < seconds)
                    return false;
                throttle[key] = now;
                return true;
            }
        }

        public static void Write(string line)
        {
            if (string.IsNullOrEmpty(line))
                return;
            var stamped = Stamp() + " " + line;
            lock (Gate)
            {
                Ring[ringWrite % RingSize] = stamped;
                ringWrite++;
                if (ringCount < RingSize)
                    ringCount++;
                try
                {
                    EnsureWriter_NoLock();
                    if (_writer == null)
                        return;
                    RotateIfNeeded_NoLock();
                    if (_writer == null)
                        return;
                    _writer.WriteLine(stamped);
                    unflushed++;
                    FlushWriter_NoLock(force: false);
                }
                catch { }
            }
        }

        static void FlushWriter_NoLock(bool force)
        {
            if (_writer == null)
                return;
            if (!force
                && unflushed < FlushEveryLines
                && (DateTime.UtcNow - lastFlushUtc).TotalMilliseconds < FlushEveryMs)
                return;
            _writer.Flush();
            unflushed = 0;
            lastFlushUtc = DateTime.UtcNow;
        }

        public static string MailboxTail(int plot, string filter, int limit)
        {
            if (limit <= 0 || limit > MailboxLines)
                limit = MailboxLines;
            var lines = CopyRing();
            var sb = new StringBuilder();
            var n = 0;
            var filt = string.IsNullOrWhiteSpace(filter) ? "" : filter.Trim();
            sb.Append("{\"path\":\"").Append(Esc(LogPath)).Append('"')
                .Append(",\"bytes\":").Append(CurrentBytes())
                .Append(",\"backups\":").Append(Backups)
                .Append(",\"maxMB\":").Append(MaxBytes / (1024 * 1024))
                .Append(",\"ring\":").Append(ringCount)
                .Append(",\"lines\":[");
            var first = true;
            for (var i = 0; i < lines.Length && n < limit; i++)
            {
                var line = lines[i];
                if (line == null)
                    continue;
                if (plot >= 0 && line.IndexOf("plot=" + plot, StringComparison.Ordinal) < 0
                    && line.IndexOf("plot=-1", StringComparison.Ordinal) < 0)
                    continue;
                if (filt.Length > 0 && line.IndexOf(filt, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (!first)
                    sb.Append(',');
                first = false;
                sb.Append('"').Append(Esc(line)).Append('"');
                n++;
            }
            sb.Append("],\"matched\":").Append(n).Append('}');
            return sb.ToString();
        }

        public static string MailboxPerf()
        {
            string[] copy;
            int count;
            int write;
            lock (Gate)
            {
                count = perfCount;
                write = perfWrite;
                copy = new string[count];
                for (var i = 0; i < count; i++)
                    copy[i] = lastPerf[(write - count + i) % lastPerf.Length];
            }
            var sb = new StringBuilder();
            sb.Append("{\"queue\":").Append(Core.WorkQueue != null ? Core.WorkQueue.QueueDepth : 0)
                .Append(",\"path\":\"").Append(Esc(LogPath)).Append('"')
                .Append(",\"lines\":[");
            for (var i = 0; i < copy.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(Esc(copy[i] ?? "")).Append('"');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        public static string MailboxDump(string destPath)
        {
            var lines = CopyRing();
            try
            {
                var dir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllLines(destPath, lines);
            }
            catch (Exception e)
            {
                return "{\"ok\":false,\"error\":\"" + Esc(e.Message) + "\"}";
            }
            return "{\"ok\":true,\"path\":\"" + Esc(destPath) + "\",\"lines\":" + lines.Length
                + ",\"log\":\"" + Esc(LogPath) + "\",\"bytes\":" + CurrentBytes() + "}";
        }

        static string[] CopyRing()
        {
            lock (Gate)
            {
                var n = ringCount;
                var write = ringWrite;
                var copy = new string[n];
                for (var i = 0; i < n; i++)
                    copy[i] = Ring[(write - n + i) % RingSize];
                return copy;
            }
        }

        static long CurrentBytes()
        {
            try
            {
                if (!string.IsNullOrEmpty(_path) && File.Exists(_path))
                    return new FileInfo(_path).Length;
            }
            catch { }
            return 0;
        }

        static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");
        }
    }
}
