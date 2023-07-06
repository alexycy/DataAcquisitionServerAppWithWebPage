using System.Collections.Concurrent;
using System.Data;

namespace DataAcquisitionServerAppWithWebPage.Service
{
    public class DbOperation
    {
        // 操作类型，可以是 "Query" 或 "NonQuery"
        public string OperationType { get; set; }

        // SQL 语句
        public string SqlStatement { get; set; }

        // 对于查询操作，你可能还需要一个回调函数来处理查询结果
        public Action<DataTable> QueryResultHandler { get; set; }
    }

    public class DatabaseManager
    {
        public BlockingCollection<DbOperation> DbOperations { get; } = new BlockingCollection<DbOperation>();

        // 其他数据库管理相关的代码...
    }

}
