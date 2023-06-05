using MySql.Data.MySqlClient;
using System;
using System.Data;

namespace DataAcquisitionServerAppWithWebPage.Service
{


    public class DatabaseHelper
    {
        private string _connectionString;

        public DatabaseHelper(string connectionString)
        {
            _connectionString = connectionString;
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
    }

}
