using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NorcusSheetsManager.NameCorrector
{
    internal class TestFileLoader : IDbLoader
    {
        public string Server { get; set; }
        public string Database { get; set; }
        public string UserId { get; set; }
        public string Password { get; set; }

        public string ConnectionString => $"Server={Server}; Database={Database}; User Id={UserId}; Password={Password};";
        private string[] _songs;
        public TestFileLoader()
        {
            _songs = File.ReadAllLines("db.txt");
        }

        public IEnumerable<string> GetSongNames() => _songs;

        public void ReloadData()
        {
            _songs = File.ReadAllLines("db.txt");
        }
    }
}
