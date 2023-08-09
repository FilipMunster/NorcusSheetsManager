using System.Text.Json.Serialization;

namespace NorcusSheetsManager.NameCorrector
{
    internal interface IRenamingTransaction
    {
        [JsonPropertyName("TransactionGuid")]
        Guid Guid { get; }
        [JsonIgnore]
        string InvalidFullPath { get; }
        /// <summary>
        /// Název chybného souboru. Cesta relativní k výchozí složce BaseSheetsFolder.
        /// </summary>
        string InvalidName { get; }
        IEnumerable<IRenamingSuggestion> Suggestions { get; }

        ITransactionResponse Commit(int suggestionIndex);
        ITransactionResponse Commit(IRenamingSuggestion suggestion);
        ITransactionResponse Commit(string newFileName);
    }
}