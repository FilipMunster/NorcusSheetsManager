using ImageMagick;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NorcusSheetsManager
{
    public interface IConfig
    {
        string FilesPath { get; set; }
        bool IncludeSubdirectories { get; set; }
        bool RunOnStartup { get; set; }
        MagickFormat OutFileFormat { get; set; }
        string MultiPageDelimiter { get; set; }
        int MultiPageCounterLength { get; set;}
        int MultiPageInitNumber { get; set; }
        int DPI { get; set; }
        bool TransparentBackground { get; set; }
        bool CropImage { get; set; }
        bool FixGDriveNaming { get; set; }
        string[] WatchedExtensions { get; set; }
    }
}
