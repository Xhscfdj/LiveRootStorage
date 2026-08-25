using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Search;

namespace FastFluentFilesFolders.Services
{
    /// <summary>
    /// 通过 Windows 系统索引（SystemIndex）按文件名搜索，毫秒级返回匹配路径。
    /// 非索引位置 / 查询词非法 / 任何异常 → 返回空列表，由调用方回退到递归枚举。
    /// </summary>
    public static class WindowsSearchHelper
    {
        public static async Task<List<(string Path, bool IsDirectory)>> QueryIndexAsync(
            string scope, string query, CancellationToken token)
        {
            // 查询词含文件名非法字符（也是 AQS 特殊字符）→ 不用索引，由调用方回退到递归枚举
            if (query.IndexOfAny(new[] { '"', '*', '?', ':', '<', '>', '|' }) >= 0)
                return new List<(string Path, bool IsDirectory)>();

            var result = new List<(string Path, bool IsDirectory)>();
            try
            {
                var folder = await StorageFolder.GetFolderFromPathAsync(scope);
                var escaped = query.Replace("\"", "\"\"");

                // 目录（先目录后文件，与递归回退顺序一致）；分页拉取，最多 200
                var folderOptions = new QueryOptions
                {
                    FolderDepth = FolderDepth.Deep,
                    IndexerOption = IndexerOption.OnlyUseIndexer,
                    ApplicationSearchFilter = $"System.ItemName:~\"{escaped}\""
                };
                var dirs = await folder.CreateFolderQueryWithOptions(folderOptions).GetFoldersAsync(0, 200);
                foreach (var d in dirs)
                {
                    if (token.IsCancellationRequested || result.Count >= 200) break;
                    if (Directory.Exists(d.Path)) result.Add((d.Path ?? string.Empty, true)); // 跳过索引过期条目
                }

                // 文件
                if (!token.IsCancellationRequested && result.Count < 200)
                {
                    var fileOptions = new QueryOptions
                    {
                        FolderDepth = FolderDepth.Deep,
                        IndexerOption = IndexerOption.OnlyUseIndexer,
                        ApplicationSearchFilter = $"System.FileName:~\"{escaped}\""
                    };
                    var files = await folder.CreateFileQueryWithOptions(fileOptions)
                        .GetFilesAsync(0, (uint)(200 - result.Count));
                    foreach (var f in files)
                    {
                        if (token.IsCancellationRequested || result.Count >= 200) break;
                        if (File.Exists(f.Path)) result.Add((f.Path ?? string.Empty, false)); // 跳过索引过期条目
                    }
                }
            }
            catch
            {
                // 索引不可用/路径不可访问/已取消 → 返回空（绝不返回部分结果），由调用方回退到递归枚举
                return new List<(string Path, bool IsDirectory)>();
            }
            return result;
        }
    }
}
