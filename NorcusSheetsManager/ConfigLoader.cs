using ImageMagick;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace NorcusSheetsManager
{
    public static class ConfigLoader
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

        private static string GetDefaultConfigFilePath() =>
            Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) + "\\" +
            Path.GetFileNameWithoutExtension(System.Reflection.Assembly.GetExecutingAssembly().Location) + "Cfg.xml";
        public static IConfig Load() => Load(GetDefaultConfigFilePath());
        public static IConfig Load(string configFilePath)
        {
            _logger.Info("Loading config file: " + configFilePath);
            if (!System.IO.File.Exists(configFilePath))
            {
                _logger.Warn("File was not found ({0}), creating default Config", configFilePath);
                Config newConfig = _GetDefaultConfig();
                _Save(newConfig);
                return Load(configFilePath);
            }

            System.Xml.Serialization.XmlSerializer serializer = new System.Xml.Serialization.XmlSerializer(typeof(Config));
            System.IO.FileStream file = System.IO.File.OpenRead(configFilePath);
            IConfig deserialized = null;
            try { deserialized = (IConfig)serializer.Deserialize(file); }
            catch (Exception e) { _logger.Warn(e, "Deserialization failed"); }
            file.Close();

            if (deserialized != null) _SaveRegistry(deserialized.RunOnStartup);

            return deserialized ?? _GetDefaultConfig();
        }

        private static void _Save(Config config) => Save(GetDefaultConfigFilePath(), config);
        private static void Save(string configFilePath, Config config)
        {
            _logger.Debug("Saving config file to {0}", configFilePath);

            System.Xml.Serialization.XmlSerializer serializer =
                new System.Xml.Serialization.XmlSerializer(typeof(Config));

            System.IO.FileStream file = System.IO.File.Create(configFilePath);

            serializer.Serialize(file, config);
            file.Close();
        }

        private static void _SaveRegistry(bool runOnStartup)
        {
            RegistryKey key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
            if (runOnStartup)
                key?.SetValue("AutoPdfToImage", 
                    "\"" + System.Reflection.Assembly.GetExecutingAssembly().Location.Replace(".dll", ".exe") + "\"");
            else
                key?.DeleteValue("AutoPdfToImage", false);
        }

        private static Config _GetDefaultConfig() =>
            new Config()
            {
                SheetsPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
                RunOnStartup = true,
                OutFileFormat = MagickFormat.Png,
                MultiPageDelimiter = "-",
                MultiPageCounterLength = 3,
                MultiPageInitNumber = 1,
                DPI = 200,
                TransparentBackground = false,
                CropImage = true,
                MovePdfToSubfolder = true,
                PdfSubfolder = "PDF",
                FixGDriveNaming = true,
                WatchedExtensions = new[] { ".pdf", ".jpg", ".png", ".txt" }
            };

        public class Config : IConfig
        {
            public string? SheetsPath { get; set; }
            public bool RunOnStartup { get; set; }
            public MagickFormat OutFileFormat { get; set; }
            public string MultiPageDelimiter { get; set; } = "";
            public int MultiPageCounterLength { get; set; }
            public int MultiPageInitNumber { get; set; }
            public int DPI { get; set; }
            public bool TransparentBackground { get; set; }
            public bool CropImage { get; set; }
            public bool MovePdfToSubfolder { get; set; }
            public string PdfSubfolder { get; set; }
            public bool FixGDriveNaming { get; set; }
            public string[] WatchedExtensions { get; set; }
        }
    }
}
