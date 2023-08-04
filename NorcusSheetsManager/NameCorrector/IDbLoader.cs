using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NorcusSheetsManager.NameCorrector
{
    internal interface IDbLoader
    {
        public string Server { get; set; }
        public string Database { get; set; }
        public string UserId { get; set; }
        public string Password { get; set; }
        public string ConnectionString { get; }
        public IEnumerable<string> GetSongNames();
        public void ReloadData();
    }
}
