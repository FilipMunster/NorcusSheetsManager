﻿using NorcusSheetsManager;
using NorcusSheetsManager.NameCorrector;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace AutoPdfToImage
{
    internal class Program
    {
        public static readonly string VERSION = _GetVersion();
        private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
        static void Main(string[] args)
        {
            try
            {
                Console.WriteLine("Norcus Client Manager " + VERSION);
                Console.WriteLine("-------------------------");
                Manager manager = new Manager();
                manager.FullScan();
                manager.StartWatching(true);
                if (manager.Config.AutoScan) manager.AutoFullScan(60000, 5);

                string commandMessage = "Commands:\n" +
                    "\tS - scan all PDF files (checks whether all PDFs have any image)\n" +
                    "\tD - deep scan (checks image files count vs PDF page count)\n" +
                    "\tF - force convert (converts all PDF files)\n" +
                    "\tN - correct invalid file names\n" +
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
                        case "N":
                            CorrectNames(manager);
                            break;
                        default:
                            break;
                    }
                    Console.WriteLine(commandMessage);
                };
            }
            catch (Exception e)
            {
                Logger.Error(e, _logger);
                Console.ReadLine();
            }
        }
        private static string _GetVersion()
        {
            string version = Assembly.GetEntryAssembly()?.GetName()?.Version?.ToString() ?? "";
            while (version.EndsWith('0') || version.EndsWith("."))
            {
                version = version.Substring(0, version.Length - 1);
            }
            return version;
        }
        private static void CorrectNames(Manager manager)
        {
            Console.WriteLine("--------------------");
            Console.WriteLine("File name corrector:");
            Console.WriteLine("--------------------");
            manager.NameCorrector.ReloadData();
            var transactions = manager.NameCorrector.GetRenamingTransactionsForAllSubfolders(1);

            if (transactions.Count() == 0)
            {
                Console.WriteLine("No incorrectly named files were found.");
                Console.WriteLine("--------------------------------------");
                return;
            }

            Console.WriteLine("Invalid file names and suggestions:");
            foreach (var trans in transactions)
            {
                IRenamingSuggestion? suggestion = trans.Suggestions.FirstOrDefault();
                Console.WriteLine($"{trans.InvalidFullPath} -> " +
                    (suggestion is null ? "<NO SUGGESTION>" : $"{Path.GetFileNameWithoutExtension(suggestion?.FullPath)}") +
                    ((suggestion?.FileExists ?? false) ? " (FILE EXISTS!)" : ""));
            }
            if (transactions.Count() == 0)
                return;

            Console.WriteLine("Correct all file names? (Y/N)");
            if (Console.ReadKey(true).Key.ToString().Equals("Y"))
            {
                manager.StopWatching();
                foreach (var trans in transactions)
                {
                    var response = trans.Commit(0);
                    if (!response.Success)
                        Console.WriteLine(response.Message);
                }
                manager.StartWatching();
                Console.WriteLine("File names correction finished.");
            }
            else
            {
                Console.WriteLine("File names correction aborted.");
            }
            Console.WriteLine("-----------------------------------------");
        }
    }
}
