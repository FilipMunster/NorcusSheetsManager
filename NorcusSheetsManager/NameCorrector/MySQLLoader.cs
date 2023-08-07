using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MySqlConnector;

namespace NorcusSheetsManager.NameCorrector
{
    internal class MySQLLoader : IDbLoader
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
        public string Server { get; set; }
        public string Database { get; set; }
        public string UserId { get; set; }
        public string Password { get; set; }
        public string ConnectionString => $"Server={Server}; Database={Database}; User Id={UserId}; Password={Password};";
        private List<string> _Songs { get; set; } = new();
        public MySQLLoader(string server, string database, string userId, string password)
        {
            Server = server;
            Database = database;
            UserId = userId;
            Password = password;
        }

        public IEnumerable<string> GetSongNames()
        {
            if (_Songs.Count == 0) 
                ReloadDataAsync().Wait();

            return _Songs;
        }

        public async Task ReloadDataAsync()
        {
            List<string> songs = new List<string>();
            if (string.IsNullOrEmpty(Server) || string.IsNullOrEmpty(Database))
            {
                _Songs = new List<string>();
                return;
            }

            try
            {
                using var connection = new MySqlConnection(ConnectionString);
                await connection.OpenAsync();
                using var command = new MySqlCommand("SELECT filename FROM songs", connection);
                using var reader = await command.ExecuteReaderAsync();
    
                while (await reader.ReadAsync())
                {
                    songs.Add(reader.GetString(0));
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, _logger);
                songs = new();
            }
            finally
            {
                _Songs = songs;
            }
        }
    }
}
