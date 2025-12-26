using System.Data.SQLite;
using System.Data;
using StockAnalyzer.Models;
using System.Collections.Concurrent;

namespace StockAnalyzer.Data
{
    public class DatabaseHelper
    {
        private readonly string _connectionString;
        private static readonly object _dbLock = new object(); 

        public DatabaseHelper(string dbPath = "stocks.db")
        {
            _connectionString = $"Data Source={dbPath};Version=3;";
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            lock (_dbLock) //блокировка для потока безопасности 
            {
                using var connection = new SQLiteConnection(_connectionString);
                connection.Open();
                
                var createTickersTable = @"
                    CREATE TABLE IF NOT EXISTS Tickers (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Symbol TEXT UNIQUE NOT NULL
                    )";

                var createPricesTable = @"
                    CREATE TABLE IF NOT EXISTS Prices (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        TickerId INTEGER NOT NULL,
                        Value DECIMAL(18, 4) NOT NULL,
                        Date DATE NOT NULL,
                        UNIQUE(TickerId, Date),
                        FOREIGN KEY (TickerId) REFERENCES Tickers(Id)
                    )";

                var createConditionsTable = @"
                    CREATE TABLE IF NOT EXISTS TodaysConditions (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        TickerId INTEGER UNIQUE NOT NULL,
                        State TEXT NOT NULL,
                        FOREIGN KEY (TickerId) REFERENCES Tickers(Id)
                    )";

                using var command = new SQLiteCommand(connection);
                
                command.CommandText = createTickersTable;
                command.ExecuteNonQuery();
                
                command.CommandText = createPricesTable;
                command.ExecuteNonQuery();
                
                command.CommandText = createConditionsTable;
                command.ExecuteNonQuery();
            }
        }

        public int GetOrCreateTicker(string symbol) //получение или создание тикера 
        {
            lock (_dbLock)
            {
                using var connection = new SQLiteConnection(_connectionString); //создание и подключение к базе данных 
                connection.Open();
                
                var selectCmd = new SQLiteCommand( //пытаемся найти существующий тикер 
                    "SELECT Id FROM Tickers WHERE Symbol = @Symbol", 
                    connection);
                selectCmd.Parameters.AddWithValue("@Symbol", symbol);
                
                var existingId = selectCmd.ExecuteScalar();
                if (existingId != null)
                {
                    return Convert.ToInt32(existingId);
                }

                var insertCmd = new SQLiteCommand( //если не нашли создаем новый 
                    "INSERT INTO Tickers (Symbol) VALUES (@Symbol); " +
                    "SELECT last_insert_rowid();", 
                    connection);
                insertCmd.Parameters.AddWithValue("@Symbol", symbol);
                
                return Convert.ToInt32(insertCmd.ExecuteScalar());
            }
        }

        public bool PriceExists(int tickerId, DateTime date)
        {
            lock (_dbLock)
            {
                using var connection = new SQLiteConnection(_connectionString);
                connection.Open();

                var cmd = new SQLiteCommand(
                    "SELECT COUNT(*) FROM Prices WHERE TickerId = @TickerId AND Date = @Date", 
                    connection);
                cmd.Parameters.AddWithValue("@TickerId", tickerId);
                cmd.Parameters.AddWithValue("@Date", date.ToString("yyyy-MM-dd"));
                
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
        }

        public void InsertPrice(int tickerId, DateTime date, decimal value)
        {
            lock (_dbLock)
            {
                using var connection = new SQLiteConnection(_connectionString);
                connection.Open();

                var cmd = new SQLiteCommand(
                    "INSERT INTO Prices (TickerId, Date, Value) VALUES (@TickerId, @Date, @Value)", 
                    connection);
                cmd.Parameters.AddWithValue("@TickerId", tickerId);
                cmd.Parameters.AddWithValue("@Date", date.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("@Value", value);
                
                cmd.ExecuteNonQuery();
            }
        }

        public void UpdateTodaysCondition(int tickerId, string state)
        {
            lock (_dbLock)
            {
                using var connection = new SQLiteConnection(_connectionString);
                connection.Open();
                
                var updateCmd = new SQLiteCommand( //команда для обновления существующей записи
                    "UPDATE TodaysConditions SET State = @State WHERE TickerId = @TickerId", 
                    connection);
                updateCmd.Parameters.AddWithValue("@State", state);
                updateCmd.Parameters.AddWithValue("@TickerId", tickerId);
                
                if (updateCmd.ExecuteNonQuery() == 0)
                {
                    var insertCmd = new SQLiteCommand(
                        "INSERT INTO TodaysConditions (TickerId, State) VALUES (@TickerId, @State)", 
                        connection);
                    insertCmd.Parameters.AddWithValue("@TickerId", tickerId);
                    insertCmd.Parameters.AddWithValue("@State", state);
                    insertCmd.ExecuteNonQuery();
                }
            }
        }

        public List<Price> GetLastTwoPrices(int tickerId)
        {
            lock (_dbLock)
            {
                var prices = new List<Price>();
                
                using var connection = new SQLiteConnection(_connectionString);
                connection.Open();

                var cmd = new SQLiteCommand(
                    "SELECT Id, TickerId, Value, Date " +
                    "FROM Prices " +
                    "WHERE TickerId = @TickerId " +
                    "ORDER BY Date DESC " +
                    "LIMIT 2", 
                    connection);
                cmd.Parameters.AddWithValue("@TickerId", tickerId);
                
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    prices.Add(new Price
                    {
                        Id = reader.GetInt32(0),
                        TickerId = reader.GetInt32(1),
                        Value = reader.GetDecimal(2),
                        Date = reader.GetDateTime(3)
                    });
                }
                
                return prices;
            }
        }
    }
}
