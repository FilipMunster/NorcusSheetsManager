﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Transactions;
using F23.StringSimilarity;
using F23.StringSimilarity.Interfaces;

namespace NorcusSheetsManager.NameCorrector
{
    internal class Corrector
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
        private List<string> _Songs { get; set; }
        private List<Transaction> _RenamingTransactions { get; set; }
        private IEnumerable<string> _ExtensionFilter { get; set; }
        public string BaseSheetsFolder { get; }
        private IDbLoader _dbLoader;
        private IStringDistance _stringSimilarityModel;

        public Corrector(IDbLoader dbLoader, string baseSheetsFolder, IEnumerable<string> extensionsFilter)
        {
            _dbLoader = dbLoader;
            BaseSheetsFolder = baseSheetsFolder;
            _Songs = new List<string>(dbLoader.GetSongNames());
            
            if (_Songs.Count == 0)
                Logger.Warn("No song was loaded from the database.", _logger);
            else
            {
                Logger.Debug($"Connected to the database.", _logger);
            }
            
            _stringSimilarityModel = new QGram(2);
            _RenamingTransactions = new List<Transaction>();
            _ExtensionFilter = extensionsFilter;
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns>true if more than 0 songs were loaded from database</returns>
        public bool ReloadData()
        {
            _dbLoader.ReloadDataAsync().Wait();
            _Songs = _dbLoader.GetSongNames().ToList();

            if (_Songs.Count == 0)
            {
                Logger.Warn("No song was loaded from database.", _logger);
                return false;
            }
            return true;
        }
        public IEnumerable<IRenamingTransaction>? GetRenamingTransactionsForAllSubfolders(int suggestionsCount)
        {
            if (!Directory.Exists(BaseSheetsFolder))
                return null;

            List<IRenamingTransaction> result = new();
            var directories = Directory.GetDirectories(BaseSheetsFolder)
                .Select(d => d.Replace(BaseSheetsFolder, "").Replace("\\", ""));
            foreach (var directory in directories)
            {
                result.AddRange(GetRenamingTransactions(directory, suggestionsCount) ?? Enumerable.Empty<IRenamingTransaction>());
            }
            return result;
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="sheetsSubfolder"></param>
        /// <param name="suggestionsCount"></param>
        /// <returns>Vrací null, pokud <paramref name="sheetsSubfolder"/> neexistuje</returns>
        public IEnumerable<IRenamingTransaction>? GetRenamingTransactions(string sheetsSubfolder, int suggestionsCount)
        {
            List<IRenamingTransaction> transactions = new();
            string path = Path.Combine(BaseSheetsFolder, sheetsSubfolder);
            if (!Directory.Exists(path))
                return null;

            var files = Directory.GetFiles(path, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => _ExtensionFilter.Contains(Path.GetExtension(f)));
            foreach (var file in files)
            {
                if (_Songs.Contains(Path.GetFileNameWithoutExtension(file)))
                    continue;

                Transaction? transaction = _RenamingTransactions.FirstOrDefault(t => t.InvalidFullPath == file);

                if (transaction is null)
                {
                    transaction = new Transaction(BaseSheetsFolder, file, _GetSuggestionsForFile(file, Transaction.MaxSuggestionsCount));
                    _RenamingTransactions.Add(transaction);
                }
                transaction.SuggestionsCount = suggestionsCount;
                transactions.Add(transaction);
            }
            return transactions;
        }
        public ITransactionResponse CommitTransactionByGuid(Guid transactionGuid, int suggestionIndex)
        {
            Transaction? transaction = _RenamingTransactions.FirstOrDefault(t => t.Guid == transactionGuid);
            var response = transaction?.Commit(suggestionIndex) 
                ?? new TransactionResponse(false, $"Transaction {transactionGuid} does not exist");
            
            if (transaction is not null) 
                _RenamingTransactions.Remove(transaction);

            return response;
        }
        public ITransactionResponse CommitTransactionByGuid(Guid transactionGuid, string newFileName)
        {
            Transaction? transaction = _RenamingTransactions.FirstOrDefault(t => t.Guid == transactionGuid);
            var response = transaction?.Commit(newFileName)
                ?? new TransactionResponse(false, $"Transaction {transactionGuid} does not exist");

            if (transaction is not null) 
                _RenamingTransactions.Remove(transaction);

            return response;
        }
        public IRenamingTransaction? GetTransactionByGuid(Guid transactionGuid) 
            => _RenamingTransactions.FirstOrDefault(t => t.Guid == transactionGuid);

        private List<Suggestion> _GetSuggestionsForFile(string fullFileName, int suggestionsCount)
        {
            List<Suggestion> suggestions = new();
            string name = Path.GetFileNameWithoutExtension(fullFileName);
            foreach (var song in _Songs)
            {
                suggestions.Add(new Suggestion(fullFileName, song, _stringSimilarityModel.Distance(name, song)));
            }
            if (suggestionsCount <= 0) suggestionsCount = 1;
            if (suggestionsCount > suggestions.Count) suggestionsCount = suggestions.Count;
            if (suggestions.Count <= 1) return suggestions;
            return suggestions.OrderBy(s => s.Distance).Take(suggestionsCount).ToList();            
        }
    }
}
