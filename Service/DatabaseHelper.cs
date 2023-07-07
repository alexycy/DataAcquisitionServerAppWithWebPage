using MySql.Data.MySqlClient;
using System.Data;
using System.Xml;

namespace DataAcquisitionServerAppWithWebPage.Service
{


    public class DatabaseHelper
    {
        private string _connectionString;
        private MySqlTransaction _transaction;

        public DatabaseHelper()
        {
            string connectionString;
            XmlDocument doc = new XmlDocument();
            doc.Load("databseConfig.xml"); // 加载 XML 文件

            XmlNode node = doc.SelectSingleNode("/configuration/connectionStrings/add"); // 选择 XML 节点

            if (node != null)
            {
                XmlAttribute attribute = node.Attributes["connectionString"]; // 获取连接字符串属性

                if (attribute != null)
                {
                    connectionString = attribute.Value; // 获取连接字符串
                    _connectionString = connectionString;

                }
            }



        }


        public bool ChecSQLConnection()
        {
            try
            {
                using (var connection = new MySqlConnection(_connectionString))
                {
                    connection.Open();
                }
                return true;
            }
            catch (Exception)
            {

                return false;
            }
        }

        public DataTable ExecuteQuery(string query)
        {
            using (var connection = new MySqlConnection(_connectionString))
            {
                connection.Open();

                using (var command = new MySqlCommand(query, connection))
                {
                    using (var reader = command.ExecuteReader())
                    {
                        var result = new DataTable();
                        result.Load(reader);
                        return result;
                    }
                }
            }
        }

        public int ExecuteNonQuery(string commandText)
        {
            using (var connection = new MySqlConnection(_connectionString))
            {
                connection.Open();

                using (var command = new MySqlCommand(commandText, connection))
                {
                    return command.ExecuteNonQuery();
                }
            }
        }



        public void BeginTransaction()
        {
            var connection = new MySqlConnection(_connectionString);
            connection.Open();
            _transaction = connection.BeginTransaction();
        }

        public void CommitTransaction()
        {
            _transaction.Commit();
            _transaction.Connection.Close();
        }

        public void RollbackTransaction()
        {
            _transaction.Rollback();
            _transaction.Connection.Close();
        }

    }

}
