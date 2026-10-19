namespace SaveSystem
{
    /** <summary>Turns a read save document into a load: restores it, or reports why it couldn't be read.</summary> */
    internal static class DocumentRestore
    {
        /** <summary>Restores a read document into the service, carrying over whether the backup was used.</summary> */
        public static LoadResult Restore(SaveService service, ReadResult<SaveDocument> read)
        {
            switch (read.Status)
            {
                case ReadStatus.Missing:
                    return new LoadResult(LoadStatus.Missing);
                case ReadStatus.Damaged:
                    return new LoadResult(LoadStatus.Damaged, problem: read.Problem);
                default:
                    var result = service.Restore(read.Value);
                    return read.UsedBackup ? result.FromBackup(read.Problem) : result;
            }
        }
    }
}
