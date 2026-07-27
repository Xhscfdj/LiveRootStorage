using FastFluentFilesFolders.Models;
using System;

namespace FastFluentFilesFolders.Services
{
    public static class FileOperationReporter
    {
        public static event Action<FileOperationItem>? OperationAdded;

        public static void ReportOperation(FileOperationItem item)
        {
            OperationAdded?.Invoke(item);
        }
    }
}
