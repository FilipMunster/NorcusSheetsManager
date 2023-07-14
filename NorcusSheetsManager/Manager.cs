using ImageMagick.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;
using System.Xml.Linq;
using System.Text.RegularExpressions;
using ImageMagick.Formats;
using System.Diagnostics;
using System.Reflection;
using System.Collections.ObjectModel;

namespace NorcusSheetsManager
{
    public class Manager
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
        private Converter _Converter { get; set; }
        private FileSystemWatcher _FileSystemWatcher { get; set; }
        public IConfig Config { get; private set; }
        public Manager()
        {
            Config = ConfigLoader.Load();

            _Converter = new Converter()
            {
                OutFileFormat = Config.OutFileFormat,
                MultiPageDelimiter = Config.MultiPageDelimiter,
                MultiPageCounterLength = Config.MultiPageCounterLength,
                MultiPageInitNumber = Config.MultiPageInitNumber,
                DPI = Config.DPI,
                TransparentBackground = Config.TransparentBackground,
                CropImage = Config.CropImage
            };
            _FileSystemWatcher = _CreateFileSystemWatcher();
        }
        private FileSystemWatcher _CreateFileSystemWatcher()
        {
            FileSystemWatcher watcher = new FileSystemWatcher
            {
                Path = Config.FilesPath,
                IncludeSubdirectories = Config.IncludeSubdirectories,
                NotifyFilter = NotifyFilters.CreationTime |
                                NotifyFilters.DirectoryName |
                                NotifyFilters.FileName |
                                NotifyFilters.LastWrite
            };
            foreach (string ext in Config.WatchedExtensions)
            {
                watcher.Filters.Add("*" + ext);
            }
            watcher.Changed += Watcher_Changed;
            watcher.Created += Watcher_Created;
            watcher.Renamed += Watcher_Renamed;
            watcher.Deleted += Watcher_Deleted;

            return watcher;
        }

        public void StartWatching(bool verbose = false)
        {
            _FileSystemWatcher.EnableRaisingEvents = true;
            if (verbose) Logger.Debug($"File system watcher started.", _logger);
        }
        public void StopWatching(bool verbose = false)
        {
            _FileSystemWatcher.EnableRaisingEvents = false;
            if (verbose) Logger.Debug($"File system watcher stoppped.", _logger);
        }
        public void FullScan()
        {
            StopWatching();
            Logger.Debug($"Scanning all PDF files in {Config.FilesPath}.", _logger);
            if (Config.FixGDriveNaming) _FixAllGoogleFiles();
            FileInfo[] pdfFiles = Directory.GetFiles(Config.FilesPath, "*.pdf", SearchOption.AllDirectories)
                .Select(f => new FileInfo(f))
                .ToArray();

            Logger.Debug($"Found {pdfFiles.Length} PDF files in {Config.FilesPath}.", _logger);

            int convertCounter = 0;
            foreach (FileInfo pdfFile in pdfFiles)
            {
                bool converted = _DeleteOlderAndConvert(pdfFile);
                if (converted) convertCounter++;
            }

            Logger.Debug($"{convertCounter} file(s) converted to {Config.OutFileFormat}.", _logger);
            StartWatching();
        }
        /// <summary>
        /// Prohledá všechny PDF soubory a zjistí, zda ke každému existuje správný počet obrázků dle počtu stránek PDF. Nevyhovující znovu konvertuje.
        /// </summary>
        public void DeepScan()
        {
            StopWatching();
            Logger.Debug($"Deep scanning all PDF files in {Config.FilesPath}.", _logger);
            if (Config.FixGDriveNaming) _FixAllGoogleFiles();
            FileInfo[] pdfFiles = Directory.GetFiles(Config.FilesPath, "*.pdf", SearchOption.AllDirectories)
                .Select(f => new FileInfo(f))
                .ToArray();

            Logger.Debug($"Found {pdfFiles.Length} PDF files in {Config.FilesPath}.", _logger);

            int convertCounter = 0;
            foreach (FileInfo pdfFile in pdfFiles)
            {
                if (!_Converter.TryGetPdfPageCount(pdfFile, out int pageCount))
                {
                    Logger.Warn($"Unable to get page count of file {pdfFile.FullName}.", _logger);
                    continue;
                };

                int fileCount = _GetImagesForPdf(pdfFile).Length;
                if (pageCount == fileCount)
                    continue;

                Logger.Debug($"File {pdfFile.FullName} has {pageCount} page(s), but {fileCount} file(s).", _logger);
                bool converted = _DeleteOlderAndConvert(pdfFile, true);
                if (converted) convertCounter++;
            }

            Logger.Debug($"{convertCounter} file(s) converted to {Config.OutFileFormat}.", _logger);
            StartWatching();
        }
        /// <summary>
        /// Převede všechny PDF soubory do obrázku.
        /// </summary>
        public void ForceConvertAll()
        {
            StopWatching();
            Logger.Debug($"Force converting all PDF files in {Config.FilesPath}.", _logger);
            if (Config.FixGDriveNaming) _FixAllGoogleFiles();
            FileInfo[] pdfFiles = Directory.GetFiles(Config.FilesPath, "*.pdf", SearchOption.AllDirectories)
                .Select(f => new FileInfo(f))
                .ToArray();

            Logger.Debug($"Found {pdfFiles.Length} PDF files in {Config.FilesPath}.", _logger);

            int convertCounter = 0;
            foreach (FileInfo pdfFile in pdfFiles)
            {
                bool converted = _DeleteOlderAndConvert(pdfFile, true);
                if (converted) convertCounter++;
            }

            Logger.Debug($"{convertCounter} file(s) converted to {Config.OutFileFormat}.", _logger);
            StartWatching();
        }
        private void _FixAllGoogleFiles()
        {
            StopWatching();
            GDriveFix.FixAllFiles(Config.FilesPath, SearchOption.AllDirectories, false, Config.WatchedExtensions);
            StartWatching();
        }
        private string _FixGoogleFile(string fullFileName)
        {
            StopWatching();
            string newFileName = GDriveFix.FixFile(fullFileName, false);
            StartWatching();
            return newFileName;
        }

        private void Watcher_Renamed(object sender, RenamedEventArgs e)
        {
            Logger.Debug($"Detected: {e.OldFullPath} was renamed to {e.FullPath}.", _logger);

            // Pokud se jedná o fixnutý název google souboru, potřebuji znovu převést pdf:
            if (Path.GetExtension(e.FullPath) == ".pdf" && Regex.IsMatch(e.OldFullPath, GDriveFix.GDriveFile.VerPattern))
            {
                _DeleteOlderAndConvert(new FileInfo(e.FullPath), true);
                return;
            }

            var images = _GetImagesForPdf(new FileInfo(e.OldFullPath));
            _RenameImages(images, e.OldName, e.Name);
        }

        private void Watcher_Created(object sender, FileSystemEventArgs e)
        {
            string fullPath = e.FullPath;
            Logger.Debug($"Detected: {fullPath} was created.", _logger);
            if (Config.FixGDriveNaming && Regex.IsMatch(fullPath, GDriveFix.GDriveFile.VerPattern))
                fullPath = _FixGoogleFile(fullPath);

            FileInfo file = new FileInfo(fullPath);
            if (file.Extension == ".pdf")
                _DeleteOlderAndConvert(file);
        }

        private void Watcher_Changed(object sender, FileSystemEventArgs e)
        {
            FileInfo pdfFile = new FileInfo(e.FullPath);
            if (pdfFile.Extension != ".pdf") return;

            Logger.Debug($"Detected: {pdfFile} has changed.", _logger);
            _DeleteOlderAndConvert(pdfFile);
        }
        private void Watcher_Deleted(object sender, FileSystemEventArgs e)
        {
            string fullPath = e.FullPath;
            Logger.Debug($"Detected: {fullPath} was deleted.", _logger);
            if (Config.FixGDriveNaming && Regex.IsMatch(fullPath, GDriveFix.GDriveFile.VerPattern))
            {
                StopWatching();
                fullPath = _FixGoogleFile(fullPath);
                StartWatching();
            }

            FileInfo file = new FileInfo(fullPath);
            if (file.Extension == ".pdf")
                _DeleteOlderAndConvert(file, true);
        }

        private FileInfo[] _GetImagesForPdf(FileInfo pdfFile)
        {
            string dir = pdfFile.Directory.FullName;
            string name = Path.GetFileNameWithoutExtension(pdfFile.Name);
            string ext = "." + Config.OutFileFormat.ToString().ToLower();
            string pattern = $".*{Regex.Escape(name)}({Config.MultiPageDelimiter}\\d*)?(\\s\\(\\d+\\))?\\{ext}";
            
            FileInfo[] foundFiles = Directory.GetFiles(dir, $"{name}*{ext}", SearchOption.TopDirectoryOnly)
                .Where(path => Regex.IsMatch(path, pattern))
                .Select(f => new FileInfo(f))
                .ToArray();
            return foundFiles;
        }
        /// <summary>
        /// Pokud k PDF neexistují obrázky, nebo jsou starší, tak je smaže a PDF zkonvertuje.
        /// </summary>
        /// <param name="pdfFile"></param>
        /// <param name="forceDeleteAndConvert">Smaže všechny soubory náležící k PDF a PDF vždy zkonvertuje</param>
        /// <returns>Vrací true, pokud proběhla konverze</returns>
        private bool _DeleteOlderAndConvert(FileInfo pdfFile, bool forceDeleteAndConvert = false)
        {
            try
            {
                var images = _GetImagesForPdf(pdfFile);
                bool imgsAreOlder = images.Any(i => (i.CreationTimeUtc < pdfFile.CreationTimeUtc) || (i.CreationTimeUtc < pdfFile.LastWriteTimeUtc));
                if (imgsAreOlder || forceDeleteAndConvert)
                {
                    foreach (var image in images)
                    {
                        File.Delete(image.FullName);
                        Logger.Debug($"Image {image.FullName} was deleted" + (imgsAreOlder? " (found newer PDF)." : "."), _logger);
                    }
                }
                if (images.Length == 0 || imgsAreOlder || forceDeleteAndConvert)
                {
                    _Converter.Convert(pdfFile);
                    return true;
                }
                else return false;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, _logger);
                return false;
            }
        }
        private void _RenameImages(FileInfo[] images, string oldName, string newName)
        {
            try
            {
                string oldNameNoExt = Path.GetFileNameWithoutExtension(oldName);
                string newNameNoExt = Path.GetFileNameWithoutExtension(newName);
                foreach (var image in images)
                {
                    string dir = Path.GetDirectoryName(image.FullName);
                    string name = Path.GetFileName(image.FullName);
                    string newPath = Path.Combine(dir, name.Replace(oldNameNoExt, newNameNoExt));
                    
                    if (image.FullName == newPath) return;
                    if (File.Exists(newPath)) File.Delete(newPath);
                    File.Move(image.FullName, newPath);
                    
                    Logger.Debug($"File {Path.GetFileName(image.FullName)} was renamed to {newPath}", _logger);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, _logger);
            }
        }
    }
}
