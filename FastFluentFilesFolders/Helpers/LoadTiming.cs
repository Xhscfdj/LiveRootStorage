using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace FastFluentFilesFolders.Helpers
{
    /// <summary>
    /// 加载耗时日志：一次加载的各阶段耗时先在内存里缓冲，整段结束时一次性写入文件，
    /// 避免逐阶段文件 I/O 拖慢被测代码本身。
    /// 日志路径：%LOCALAPPDATA%\FastFluentFilesFolders\load_timing.log
    /// </summary>
    public static class LoadTiming
    {
        private static readonly object Lock = new();
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FastFluentFilesFolders", "load_timing.log");

        private static int _seq;

        public static int Begin(string folderPath)
        {
            int id = Interlocked.Increment(ref _seq);
            WriteLine(id, $"[{id}] BEGIN {folderPath}");
            return id;
        }

        public static void Mark(int id, string stage, long ms)
        {
            WriteLine(id, $"[{id}]     {stage}: {ms} ms");
            // id=0 表示“未被带 id 的 Begin/End 包住的独立标记”（如 LrsTableView 的 attach/渲染计时），
            // 没有对应的 End 会刷新缓冲，这里立即写入文件，避免日志丢失。
            if (id == 0)
                Flush(id);
        }

        private static void Flush(int id)
        {
            if (Buffers.TryRemove(id, out var sb))
            {
                try
                {
                    lock (Lock)
                    {
                        var dir = Path.GetDirectoryName(LogPath);
                        if (!string.IsNullOrEmpty(dir))
                            Directory.CreateDirectory(dir);
                        File.AppendAllText(LogPath, sb.ToString());
                    }
                }
                catch { }
            }
        }

        public static void End(int id, long totalMs)
        {
            WriteLine(id, $"[{id}] TOTAL: {totalMs} ms");
            WriteLine(id, "----------------------------------------");
            // 一次性写入文件（只在 End 时做一次文件 I/O）
            if (Buffers.TryRemove(id, out var sb))
            {
                try
                {
                    lock (Lock)
                    {
                        var dir = Path.GetDirectoryName(LogPath);
                        if (!string.IsNullOrEmpty(dir))
                            Directory.CreateDirectory(dir);
                        File.AppendAllText(LogPath, sb.ToString());
                    }
                }
                catch { }
            }
        }

        public static string LogFilePath => LogPath;

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, StringBuilder> Buffers = new();

        private static void WriteLine(int id, string line)
        {
            Debug.WriteLine($"[LoadTiming] {line}");
            var sb = Buffers.GetOrAdd(id, _ => new StringBuilder(512));
            sb.AppendLine($"{DateTime.Now:HH:mm:ss.fff} {line}");
        }
    }
}
