﻿using ImageMagick.Configuration;
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
using System.Timers;
using NorcusSheetsManager.NameCorrector;
using NorcusSheetsManager.API;

namespace NorcusSheetsManager
{
    internal class Manager
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
        private Converter _Converter { get; set; }
        private List<FileSystemWatcher> _FileSystemWatchers { get; set; }
        private bool _IsWatcherEnabled { get; set; }
        private bool _ScanningInProgress { get; set; }
        public IConfig Config { get; private set; }
        public Corrector NameCorrector { get; private set; }
        public Manager()
        {
            Config = ConfigLoader.Load();
            if (String.IsNullOrEmpty(Config.SheetsPath))
            {
                Exception e = new ArgumentNullException(nameof(Config.SheetsPath));
                Logger.Error(e, _logger);
                throw e;
            }

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

            _FileSystemWatchers = _CreateFileSystemWatchers();
            
            var sqlLoader = new MySQLLoader(Config.DbConnection.Server, 
                Config.DbConnection.Database, 
                Config.DbConnection.UserId, 
                Config.DbConnection.Password);
            NameCorrector = new Corrector(sqlLoader, Config.SheetsPath, Config.WatchedExtensions);
            
            if (Config.APISettings.RunServer)
            {            
                Server.Initialize(Config.APISettings.Port, Config.APISettings.Key, NameCorrector);
                Server.Start();
                Logger.Debug("API server started.", _logger);
            }
        }
        /// <summary>
        /// Vytvoří FileSystemWatchers pro každou složku s notami. Kontroluje pouze první úroveň každé složky.
        /// </summary>
        private List<FileSystemWatcher> _CreateFileSystemWatchers()
        {
            List<FileSystemWatcher> _fileSystemWatchers = new ();
            var directories = Directory.GetDirectories(Config.SheetsPath);
            foreach (var dir in directories)
            {
                FileSystemWatcher watcher = new FileSystemWatcher
                {
                    Path = dir,
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.CreationTime |
                    NotifyFilters.DirectoryName |
                    NotifyFilters.FileName |
                    NotifyFilters.LastWrite,
                    EnableRaisingEvents = true
                };
                foreach (string ext in Config.WatchedExtensions)
                {
                    watcher.Filters.Add("*" + ext);
                }
                watcher.Changed += Watcher_Changed;
                watcher.Created += Watcher_Created;
                watcher.Renamed += Watcher_Renamed;
                watcher.Deleted += Watcher_Deleted;

                _fileSystemWatchers.Add(watcher);
            }
            return _fileSystemWatchers;
        }

        public void StartWatching(bool verbose = false)
        {
            _IsWatcherEnabled = true;
            if (verbose) Logger.Debug($"File system watcher started.", _logger);
        }
        public void StopWatching(bool verbose = false)
        {
            _IsWatcherEnabled = false;
            if (verbose) Logger.Debug($"File system watcher stoppped.", _logger);
        }
        public void AutoFullScan(double interval, int repeats)
        {
            System.Timers.Timer timer = new System.Timers.Timer(interval);
            int hitCount = 0;
            timer.Elapsed += (sender, e) =>
            {
                Logger.Debug("Autoscan:", _logger);
                if (_ScanningInProgress) 
                { 
                    Logger.Debug("Autoscan skipped (scanning already running).", _logger);
                    return;
                }
                hitCount++;
                FullScan();

                if (hitCount >= repeats)
                {
                    System.Timers.Timer? senderTimer = sender as System.Timers.Timer;
                    senderTimer?.Stop();
                    senderTimer?.Dispose();
                    Logger.Debug("Autoscan finished.", _logger);
                    return;
                }
            };
            timer.Start();
        }
        public void FullScan()
        {
            StopWatching();
            _ScanningInProgress = true;
            Logger.Debug($"Scanning all PDF files in {Config.SheetsPath}.", _logger);
            if (Config.FixGDriveNaming) _FixAllGoogleFiles();
            var pdfFiles = _GetPdfFiles(false);

            Logger.Debug($"Found {pdfFiles.Count()} PDF files in {Config.SheetsPath}.", _logger);

            int convertCounter = 0;
            foreach (FileInfo pdfFile in pdfFiles)
            {
                bool converted = _DeleteOlderAndConvert(pdfFile);
                if (converted) convertCounter++;
            }
            if (convertCounter > 0)
                Logger.Debug($"{convertCounter} files converted to {Config.OutFileFormat}.", _logger);

            _ScanningInProgress = false;
            StartWatching();
        }
        /// <summary>
        /// Prohledá všechny PDF soubory a zjistí, zda ke každému existuje správný počet obrázků dle počtu stránek PDF. Nevyhovující znovu konvertuje.
        /// </summary>
        public void DeepScan()
        {
            StopWatching();
            _ScanningInProgress = true;
            Logger.Debug($"Deep scanning all PDF files in {Config.SheetsPath}.", _logger);
            if (Config.FixGDriveNaming) _FixAllGoogleFiles();
            var pdfFiles = _GetPdfFiles(false);
            var archivePdfFiles = _GetPdfFiles(true);

            if (!Config.MovePdfToSubfolder)
                Logger.Debug($"Found {pdfFiles.Count()} PDF files in {Config.SheetsPath}.", _logger);
            else
                Logger.Debug($"Found {pdfFiles.Count() + archivePdfFiles.Count()} PDF files in {Config.SheetsPath} and PDF subfolders.", _logger);

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

            if (Config.MovePdfToSubfolder)
            {
                foreach (FileInfo pdfFile in archivePdfFiles)
                {
                    if (!_Converter.TryGetPdfPageCount(pdfFile, out int pageCount))
                    {
                        Logger.Warn($"Unable to get page count of file {pdfFile.FullName}.", _logger);
                        continue;
                    };

                    FileInfo pdfFileParentDir = new FileInfo(
                        Path.Combine(Directory.GetParent(pdfFile.Directory.FullName).FullName, pdfFile.Name));
                    int fileCount = _GetImagesForPdf(pdfFileParentDir).Length;
                    if (pageCount == fileCount)
                        continue;

                    Logger.Debug($"File {pdfFile.FullName} has {pageCount} page(s), but {fileCount} file(s).", _logger);
                    File.Move(pdfFile.FullName, pdfFileParentDir.FullName);
                    bool converted = _DeleteOlderAndConvert(pdfFileParentDir, true);
                    if (converted) convertCounter++;
                }
            }

            Logger.Debug($"{convertCounter} file(s) converted to {Config.OutFileFormat}.", _logger);
            _ScanningInProgress = false;
            StartWatching();
        }
        /// <summary>
        /// Převede všechny PDF soubory do obrázku.
        /// </summary>
        public void ForceConvertAll()
        {
            StopWatching();
            _ScanningInProgress = true;
            Logger.Debug($"Force converting all PDF files in {Config.SheetsPath}.", _logger);
            if (Config.FixGDriveNaming) _FixAllGoogleFiles();
            var pdfFiles = _GetPdfFiles(false);
            var archivePdfFiles = _GetPdfFiles(true);

            if (!Config.MovePdfToSubfolder)
                Logger.Debug($"Found {pdfFiles.Count()} PDF files in {Config.SheetsPath}.", _logger);
            else
            {
                Logger.Debug($"Found {pdfFiles.Count() + archivePdfFiles.Count()} PDF files in {Config.SheetsPath} and PDF subfolders.", _logger);
                // Pokud je povoleno přesouvání PDFka do podsložky, přesunu PDFka z podsložek do složky o úroveň výš.
                foreach (var archivePdf in archivePdfFiles)
                {
                    var pdfInParentDir = new FileInfo(Path.Combine(Directory.GetParent(archivePdf.DirectoryName).FullName, archivePdf.Name));
                    if (!pdfInParentDir.Exists)
                        File.Move(archivePdf.FullName, pdfInParentDir.FullName);
                }
                // Aktualizuji seznam PDF v Config.SheetsPath:
                pdfFiles = _GetPdfFiles(false);
            }

            int convertCounter = 0;
            foreach (FileInfo pdfFile in pdfFiles)
            {
                bool converted = _DeleteOlderAndConvert(pdfFile, true);
                if (converted) convertCounter++;
            }

            Logger.Debug($"{convertCounter} file(s) converted to {Config.OutFileFormat}.", _logger);
            _ScanningInProgress = false;
            StartWatching();
        }
        private void _FixAllGoogleFiles()
        {
            bool isWatcherActive = _IsWatcherEnabled;
            if (isWatcherActive) StopWatching();
            GDriveFix.FixAllFiles(Config.SheetsPath, SearchOption.AllDirectories, false, Config.WatchedExtensions);
            if (isWatcherActive) StartWatching();
        }
        private string _FixGoogleFile(string fullFileName)
        {
            bool isWatcherActive = _IsWatcherEnabled;
            if (isWatcherActive) StopWatching();
            string newFileName = GDriveFix.FixFile(fullFileName, false);
            if (isWatcherActive) StartWatching();
            return newFileName;
        }

        private void Watcher_Renamed(object sender, RenamedEventArgs e)
        {
            if (!_IsWatcherEnabled) return;
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
            if (!_IsWatcherEnabled) return;
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
            if (!_IsWatcherEnabled) return;
            FileInfo pdfFile = new FileInfo(e.FullPath);
            if (pdfFile.Extension != ".pdf" || !pdfFile.Exists) return;

            Logger.Debug($"Detected: {pdfFile} has changed.", _logger);
            _DeleteOlderAndConvert(pdfFile);
        }
        private void Watcher_Deleted(object sender, FileSystemEventArgs e)
        {
            if (!_IsWatcherEnabled) return;
            string fullPath = e.FullPath;
            Logger.Debug($"Detected: {fullPath} was deleted.", _logger);
            //if (Config.FixGDriveNaming && Regex.IsMatch(fullPath, GDriveFix.GDriveFile.VerPattern))
            //{
            //    StopWatching();
            //    fullPath = _FixGoogleFile(fullPath);
            //    StartWatching();
            //}

            //FileInfo file = new FileInfo(fullPath);
            //if (file.Extension == ".pdf")
            //    _DeleteOlderAndConvert(file, true);
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
                bool imgsAreOlder = images.Any(i => i.LastWriteTimeUtc < pdfFile.LastWriteTimeUtc);
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
                    var createdImages = _Converter.Convert(pdfFile);
                    _SyncFileTimes(pdfFile, createdImages);
                    if (Config.MovePdfToSubfolder) _MovePdfToSubfolder(pdfFile);
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
        private void _SyncFileTimes(FileInfo source, IEnumerable<FileInfo> targets)
        {
            foreach (var target in targets)
            {
                target.CreationTime = source.CreationTime;
                target.CreationTimeUtc = source.CreationTimeUtc;
                target.LastAccessTime = source.LastAccessTime;
                target.LastAccessTimeUtc = source.LastAccessTimeUtc;
                target.LastWriteTime = source.LastWriteTime;
                target.LastWriteTimeUtc = source.LastWriteTimeUtc;
            }
        }
        private void _MovePdfToSubfolder(FileInfo pdfFile)
        {
            if (!pdfFile.Exists) return;

            string sourceFile = pdfFile.FullName;
            string newPath = Path.Combine(Path.GetDirectoryName(pdfFile.FullName), Config.PdfSubfolder);
            if (!Directory.Exists(newPath))
            {
                Directory.CreateDirectory(newPath);

            }

            string targetFile = Path.Combine(newPath, pdfFile.Name);
            try
            {
                File.Move(sourceFile, targetFile, true);
                Logger.Debug($"File {sourceFile} was moved to {Config.PdfSubfolder} subfolder.", _logger);
            }
            catch (Exception e)
            {
                Logger.Warn($"File {sourceFile} could not be moved to {Config.PdfSubfolder} subfolder.", _logger);
                Logger.Warn(e, _logger);
            }
        }
        private IEnumerable<FileInfo> _GetPdfFiles(bool filesInPdfSubfolder)
        {
            var directories = Directory.GetDirectories(Config.SheetsPath);
            List<FileInfo> pdfFiles = new List<FileInfo>();

            foreach (var dir in directories)
            {
                string dirx = filesInPdfSubfolder ? Path.Combine(dir, Config.PdfSubfolder) : dir;
                if (!Directory.Exists(dirx)) continue;
                pdfFiles.AddRange(Directory.GetFiles(dirx, "*.pdf", SearchOption.TopDirectoryOnly)
                    .Select(f => new FileInfo(f)));
            }
            return pdfFiles;
        }
    }
}
