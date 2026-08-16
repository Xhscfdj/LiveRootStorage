namespace FastFluentFilesFolders.Models
{
    /// <summary>
    /// 文件操作（复制/移动）的累积进度快照，用于向操作岛实时上报进度。
    /// </summary>
    public sealed class FileOperationProgress
    {
        /// <summary>已完成（已复制/已移动）的文件数。</summary>
        public int CompletedFiles { get; set; }

        /// <summary>已传输的字节数。</summary>
        public long CompletedBytes { get; set; }
    }
}
