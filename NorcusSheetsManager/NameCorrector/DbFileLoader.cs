﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NorcusSheetsManager.NameCorrector
{
    internal class DbFileLoader : IDbLoader
    {
        public string Server { get; set; }
        public string Database { get; set; }
        public string UserId { get; set; }
        public string Password { get; set; }

        public string ConnectionString => $"Server={Server}; Database={Database}; User Id={UserId}; Password={Password};";
        private string[] _songs;
        private string _dbFile;
        public DbFileLoader(string fileName)
        {
            _songs = File.ReadAllLines(fileName);
            _dbFile = fileName;
        }

        public IEnumerable<string> GetSongNames() => _songs;

        public async Task ReloadDataAsync()
        {
            _songs = File.ReadAllLines(_dbFile);
        }

        public IEnumerable<INorcusUser> GetUsers()
        {
            Guid userGuid = Guid.Empty;
            try
            {
                userGuid = new Guid(UserId);
            }
            catch { }
            var user = new NorcusUser() { Guid = userGuid };
            return new List<NorcusUser>() { user };
        }
    }
}
