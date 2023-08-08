using NorcusSheetsManager.NameCorrector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NorcusSheetsManager.API.Models
{
    internal class NameCorrectorModel
    {
        private IDbLoader _dbLoader;
        public NameCorrectorModel(IDbLoader dbLoader)
        {
            _dbLoader = dbLoader;
        }
        public bool CanUserRead(bool nsmAdmin, Guid guid, string? sheetsFolder)
        {
            if (nsmAdmin)
                return true;

            INorcusUser? user = _dbLoader.GetUsers().FirstOrDefault(u => u.Guid == guid);            
            if (user is null)
                return false;

            // User existuje a není admin:
            if (String.IsNullOrEmpty(sheetsFolder)) // Chce získat info ke všem složkám
                return false;

            if (sheetsFolder != user.Folder) // Chce získat info k cizí složce
                return false;

            return true;
        }
        public bool CanUserCommit(bool nsmAdmin, Guid guid)
        {
            // Pokud je admin, tak může všechno. Jinak testuji, jestli uživatel alespoň existuje.
            // Složku, kde chce dělat úpravu kotrolovat nemusím, protože aby mohl zapisovat,
            // musí znát Guid transakce, který by při čtení nezískal.
            // Odkud může číst, tam může i zapisovat. Pokud by mohl číst i cizí složky, musela by se kontrolovat i složka.
            if (nsmAdmin)
                return true;

            INorcusUser? user = _dbLoader.GetUsers().FirstOrDefault(u => u.Guid == guid);
            if (user is null)
                return false;

            return true;
        }
    }
}
