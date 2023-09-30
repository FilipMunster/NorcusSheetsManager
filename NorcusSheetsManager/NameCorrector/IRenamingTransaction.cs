using System.Text.Json.Serialization;

namespace NorcusSheetsManager.NameCorrector
{
    
    internal interface IRenamingTransaction : IRenamingTransactionBase
    {
        [JsonPropertyName("TransactionGuid")]
        new Guid Guid { get; }

        [JsonPropertyName("Folder")]
        string? InvalidRelativePath { get; }
        
        /// <summary>
        /// Název chybného souboru.
        /// </summary>
        new string InvalidFileName { get; }
        new IEnumerable<IRenamingSuggestion> Suggestions { get; }
    }
}