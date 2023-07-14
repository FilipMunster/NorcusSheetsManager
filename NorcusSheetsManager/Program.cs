using NorcusSheetsManager;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutoPdfToImage
{
    internal class Program
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
        static void Main(string[] args)
        {
            Manager manager = new Manager();
            manager.FullScan();
            manager.StartWatching(true);

            string commandMessage = "Commands:\n" +
                "\tS - scan all PDF files (checks whether all PDFs have any image)\n" +
                "\tD - deep scan (checks image files count vs PDF page count)\n" +
                "\tF - force convert (converts all PDF files)\n" +
                "\tX - stop program";
            Console.WriteLine(commandMessage);

            bool @continue = true;
            while (@continue)
            {
                switch (Console.ReadKey(true).Key.ToString())
                {
                    case "X":
                        @continue = false;
                        break;
                    case "S":
                        manager.FullScan();
                        break;
                    case "D":
                        manager.DeepScan();
                        break;
                    case "F":
                        Console.WriteLine("Are you sure? (y/n)");
                        if (Console.ReadKey(true).Key.ToString() == "Y")
                            manager.ForceConvertAll();
                        break;
                    default:
                        break;
                }
                Console.WriteLine(commandMessage);
            };
        }
    }
}
