using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ImageMagick;
using ImageMagick.Formats;
using NLog;

namespace NorcusSheetsManager
{
    public class Converter
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
        private readonly MagickReadSettings _magickReadSettings;
        /// <summary>
        /// default = Png
        /// </summary>
        public MagickFormat OutFileFormat { get; set; } = MagickFormat.Png;
        /// <summary>
        /// default = "-"
        /// </summary>
        public string MultiPageDelimiter { get; set; } = "-";
        /// <summary>
        /// Počet číslic (default 3 => 003)
        /// </summary>
        public int MultiPageCounterLength { get; set; } = 3;
        /// <summary>
        /// Určuje počáteční index (default = 1)
        /// </summary>
        public int MultiPageInitNumber { get; set; } = 1;
        /// <summary>
        /// default = 200
        /// </summary>
        public int? DPI
        {
            get => System.Convert.ToInt32(_magickReadSettings.Density?.X);
            set
            {
                _magickReadSettings.Density = value.HasValue ? new Density((int)value) : null;
            }
        }
        /// <summary>
        /// default = false
        /// </summary>
        public bool TransparentBackground { get; set; } = false;
        /// <summary>
        /// Oříznout obrázek dle obsahu. Default = true;
        /// </summary>
        public bool CropImage { get; set; } = true;
        static Converter()
        {
            string assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            MagickNET.SetGhostscriptDirectory(assemblyDir);
        }
        public Converter()
        {
            _magickReadSettings = new MagickReadSettings()
            {
                Density = new Density(200),
                Format = MagickFormat.Pdf
            };
        }
        public bool Convert(FileInfo pdfFile)
        {
            if (!pdfFile.Exists) return false;
            if (pdfFile.Extension.ToLower() != ".pdf")
                throw new FormatException("Input file must be PDF");

            Logger.Debug($"Converting {pdfFile.FullName} into {OutFileFormat} image.", _logger);

            string outFileNoExt = Path.Combine(pdfFile.Directory.FullName, Path.GetFileNameWithoutExtension(pdfFile.FullName));
            string outExtension = "." + OutFileFormat.ToString().ToLower();
            int imagesCount = 0;
            
            using (var images = new MagickImageCollection())
            {
                FileStream fileStream;
                try
                {
                    fileStream = pdfFile.Open(FileMode.Open);
                }
                catch (IOException e)
                {
                    Logger.Warn(e, _logger);
                    Logger.Warn("I will sleep for 100ms and try again...", _logger);
                    Thread.Sleep(100);
                    fileStream = pdfFile.Open(FileMode.Open);
                }
                images.Read(fileStream, _magickReadSettings);
                imagesCount = images.Count;
                if (images.Count == 1)
                {
                    _ModifyImage(images[0]);
                    images[0].Write(outFileNoExt + outExtension, OutFileFormat);
                }
                else if (images.Count > 1)
                {
                    var page = MultiPageInitNumber;
                    foreach (var image in images)
                    {
                        image.Format = OutFileFormat;
                        _ModifyImage(image);
                        image.Write(outFileNoExt + MultiPageDelimiter + _GetCounter(page) + outExtension);
                        page++;
                    }
                }
                else return false;
                fileStream.Close();
            }
            Logger.Debug($"{pdfFile.FullName} was converted into {imagesCount} {OutFileFormat} image"
                + (imagesCount > 1 ? "s." : "."), _logger);
            return true;
        }
        public bool TryGetPdfPageCount(FileInfo pdfFile, out int pageCount)
        {
            // Metoda PdfInfo.Create(pdfFile).PageCount z nějakého důvodu hází chybu. Použiji tedy Ghostscript napřímo:
            string fullPath = pdfFile.FullName.Replace("\\", "/");
            ProcessStartInfo startInfo = new ProcessStartInfo()
            {
                FileName = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "gswin64c.exe"),
                Arguments = $"-q -dQUIET -dSAFER -dBATCH -dNOPAUSE -dNOPROMPT --permit-file-read=\"{fullPath}\" -sPDFPassword=\"\" -c \"({fullPath}) (r) file runpdfbegin pdfpagecount = quit\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            var proc = Process.Start(startInfo);
            bool success = Int32.TryParse(proc.StandardOutput.ReadToEnd(), out pageCount);

            return success && pageCount > 0;
        }
        private string _GetCounter(int num)
        {
            if (num.ToString().Length > MultiPageCounterLength)
                return num.ToString();

            string counter = "";
            for (int i = 0; i < MultiPageCounterLength; i++)
            {
                counter += "0";
            }
            counter += num;
            counter = counter.Substring(counter.Length - MultiPageCounterLength, MultiPageCounterLength);
            return counter;
        }
        private void _ModifyImage(IMagickImage image)
        {
            if (!TransparentBackground) image.Alpha(AlphaOption.Deactivate);
            if (CropImage)
            {
                image.Trim();
                int newWidth = System.Convert.ToInt32(image.Width * 1.02);
                int newHeight = System.Convert.ToInt32(image.Height * 1.02);
                image.Extent(newWidth, newHeight, Gravity.Center);
            }
        }
    }
}
