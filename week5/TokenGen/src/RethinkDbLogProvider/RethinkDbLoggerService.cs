using RethinkDb.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RethinkDbLogProvider
{
    public class RethinkDbLoggerService: IRethinkDbLoggerService
    {
        private static RethinkDB R = RethinkDB.R;
        private static IRethinkDbConnectionFactory _connectionFactory;
        private string _dbName;

        public string LogTable { get; set; } = "Logs";
        public string ExceptionTable { get; set; } = "Exceptions";

        public RethinkDbLoggerService(IRethinkDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
            _dbName = _connectionFactory.GetOptions().Database;
        }

        public void Log(string categoryName, string logLevel, int eventId, string eventName, string message, Exception exception)
        {
            var conn = _connectionFactory.CreateConnection();

            try
            {
                InsertLog(conn, categoryName, logLevel, eventId, eventName, message, exception);
            }
            catch (Exception)
            {
                if(!conn.Open)
                {
                    conn.Reconnect();
                }
                else
                {
                    conn.Close();
                    conn.Reconnect();
                }

                InsertLog(conn, categoryName, logLevel, eventId, eventName, message, exception);
            }
        }

        private void InsertLog(RethinkDb.Driver.Net.Connection conn, string categoryName, string logLevel, int eventId, string eventName, string message, Exception exception)
        {
            string exceptionId = null;

            if (exception != null)
            {
                // insert exception
                var result = R.Db(_dbName).Table(ExceptionTable)
                    .Insert(exception)
                    .RunResult(conn);

                exceptionId = result.GeneratedKeys.First().ToString();
            }

            var logEntry = new LogEntry
            {
                Application = _connectionFactory.GetOptions().Application,
                Category = categoryName,
                Event = eventName,
                EventId = eventId,
                ExceptionId = exceptionId,
                Host = Environment.MachineName,
                Level = logLevel,
                Message = message,
                Timestamp = DateTime.UtcNow
            };

            R.Db(_dbName).Table(LogTable)
                    .Insert(logEntry)
                    .RunResult(conn);
        }

        public void InitializeDatabase()
        {
            // database
            CreateDb(_dbName);

            // tables
            CreateTable(_dbName, LogTable);
            CreateTable(_dbName, ExceptionTable);

            // indexes
            CreateIndex(_dbName, LogTable, nameof(LogEntry.EventId));
            CreateIndex(_dbName, LogTable, nameof(LogEntry.Application));
            CreateIndex(_dbName, LogTable, nameof(LogEntry.Timestamp));
            CreateIndex(_dbName, LogTable, nameof(LogEntry.Host));
        }

        protected void CreateDb(string dbName)
        {
            var conn = _connectionFactory.CreateConnection();
            var exists = R.DbList().Contains(db => db == dbName).Run(conn);

            if (!exists)
            {
                R.DbCreate(dbName).Run(conn);
                R.Db(dbName).Wait_().Run(conn);
            }
        }

        protected void CreateTable(string dbName, string tableName)
        {
            var conn = _connectionFactory.CreateConnection();
            var exists = R.Db(dbName).TableList().Contains(t => t == tableName).Run(conn);
            if (!exists)
            {
                R.Db(dbName).TableCreate(tableName).Run(conn);
                R.Db(dbName).Table(tableName).Wait_().Run(conn);
            }
        }

        protected void CreateIndex(string dbName, string tableName, string indexName)
        {
            var conn = _connectionFactory.CreateConnection();
            var exists = R.Db(dbName).Table(tableName).IndexList().Contains(t => t == indexName).Run(conn);
            if (!exists)
            {
                R.Db(dbName).Table(tableName).IndexCreate(indexName).Run(conn);
                R.Db(dbName).Table(tableName).IndexWait(indexName).Run(conn);
            }
        }

        public void CloseConnection()
        {
            _connectionFactory.CloseConnection();
        }
    }
}
