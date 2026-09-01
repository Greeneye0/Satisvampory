using BepInEx;
using ProjectM.Network;
using Stunlock.Core;
using System;
using System.IO;
using System.Text;
using Unity.Entities;

namespace Satisvampory.Services
{
    /// <summary>
    /// Rolling dest/debug log. Appends across restarts. Not LogOutput.log.
    /// Path: {BepInEx}/Log/KindredDest.log  (~8 MB, 5 backups).
    /// </summary>
    internal static class DestDebugLog
    {
        const long MaxBytes = 8L * 1024 * 1024;
        const int Backups = 5;
        static readonly object Gate = new();
        static string _dir;
        static string _path;
        static StreamWriter _writer;
        static bool _initTried;

        public static void Init()
        {
            lock (Gate)
            {
                EnsureWriter_NoLock();
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
                _path = Path.Combine(_dir, "SatisvamporyDest.log");
                _writer = new StreamWriter(new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false))
                {
                    AutoFlush = false
                };
                _writer.WriteLine($"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ} via=boot plot=-1 item= SatisvamporyDest.log append FileVersion={MyPluginInfo.PLUGIN_VERSION}");
                _writer.Flush();
            }
            catch (Exception e)
            {
                try { Core.Log?.LogWarning($"[Satisvampory] DestDebugLog init failed: {e.Message}"); } catch { }
                _writer = null;
            }
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
                var oldest = Path.Combine(_dir, $"KindredDest.{Backups}.log");
                if (File.Exists(oldest))
                    File.Delete(oldest);
                for (var i = Backups - 1; i >= 1; i--)
                {
                    var src = Path.Combine(_dir, $"KindredDest.{i}.log");
                    var dst = Path.Combine(_dir, $"KindredDest.{i + 1}.log");
                    if (File.Exists(src))
                        File.Move(src, dst);
                }
                File.Move(_path, Path.Combine(_dir, "KindredDest.1.log"));
                _writer = new StreamWriter(new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false))
                {
                    AutoFlush = false
                };
            }
            catch { }
        }

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
            Write($"via={via} plot={plot} item={StashRouting.ItemLabel(item)} amount={amount} src=\"{ChestTag(src)}\" dest=\"{ChestTag(dest)}\" destClass={destClass} leftover={leftover} belt={belt}");
        }

        public static void Skip(string via, int plot, PrefabGUID item, Entity src, string reason)
        {
            if (string.IsNullOrEmpty(reason))
                return;
            Write($"via={via} plot={plot} item={StashRouting.ItemLabel(item)} amount=0 src=\"{ChestTag(src)}\" dest=\"-\" destClass= skip={reason}");
        }

        public static void Write(string line)
        {
            if (string.IsNullOrEmpty(line))
                return;
            lock (Gate)
            {
                try
                {
                    EnsureWriter_NoLock();
                    if (_writer == null)
                        return;
                    RotateIfNeeded_NoLock();
                    if (_writer == null)
                        return;
                    _writer.WriteLine($"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ} {line}");
                    _writer.Flush();
                }
                catch { }
            }
        }
    }
}
