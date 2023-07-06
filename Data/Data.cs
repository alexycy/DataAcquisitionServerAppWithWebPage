using Azure;
using System.Collections.Concurrent;

namespace DataAcquisitionServerAppWithWebPage.Data
{

    public static  class DataGlobal
    {
        public static string nowRecordTableName = "fct_measure";

        public static object dbLock = new object();


    }
}
