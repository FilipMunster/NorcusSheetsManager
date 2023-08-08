﻿using Grapevine;
using Microsoft.Extensions.Logging;
using NLog.Filters;
using NorcusSheetsManager.NameCorrector;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace NorcusSheetsManager.API.Resources
{
    [RestResource(BasePath = "api/v1")]
    internal class NameCorrectorResource
    {
        private ITokenAuthenticator _Authenticator { get; set; }
        private Corrector _Corrector { get; set; }
        private Models.NameCorrectorModel _Model { get; set; }
        public NameCorrectorResource(ITokenAuthenticator authenticator, Corrector corrector)
        {
            _Authenticator = authenticator;
            _Corrector = corrector;
            _Model = new Models.NameCorrectorModel(corrector.DbLoader);
        }

        [RestRoute("Get", "/invalid-names")]
        [RestRoute("Get", "/invalid-names/{suggestionsCount:num}")]
        [RestRoute("Get", "/{folder}/invalid-names")]
        [RestRoute("Get", "/{folder}/invalid-names/{suggestionsCount:num}")]
        public async Task GetInvalidNames(IHttpContext context)
        {
            if (!_Authenticator.ValidateFromContext(context))
            {
                await context.Response.SendResponseAsync(HttpStatusCode.Forbidden);
                return;
            }

            if (!_Corrector.ReloadData())
            {
                context.Response.StatusCode = HttpStatusCode.InternalServerError;
                await context.Response.SendResponseAsync($"No songs were loaded from the database.");
                return;
            }

            context.Request.PathParameters.TryGetValue("folder", out string? folder);

            // Kontrola práv uživatele
            bool isAdmin = _Authenticator.ValidateFromContext(context, new Claim("NsmAdmin", "true"));
            Guid userId = new Guid(_Authenticator.GetClaimValue(context, "UserId"));
            if (!_Model.CanUserRead(isAdmin, userId, folder))
            {
                await context.Response.SendResponseAsync(HttpStatusCode.Forbidden);
                return;
            }

            int suggestionsCount = 1;
            if (context.Request.PathParameters.TryGetValue("suggestionsCount", out string? suggestionsCountString)
                && Int32.TryParse(suggestionsCountString, out int suggestionsCountInt))
            {
                suggestionsCount = suggestionsCountInt;
            }

            IEnumerable<IRenamingTransaction>? transactions;
            if (String.IsNullOrEmpty(folder))
                transactions = _Corrector.GetRenamingTransactionsForAllSubfolders(suggestionsCount);
            else
                transactions = _Corrector.GetRenamingTransactions(folder, suggestionsCount);

            if (transactions is null)
            {
                context.Response.StatusCode = HttpStatusCode.BadRequest;
                await context.Response.SendResponseAsync($"Bad request: Folder \"{folder ?? _Corrector.BaseSheetsFolder}\" does not exist.");
                return;
            }

            context.Response.StatusCode = HttpStatusCode.Ok;
            await context.Response.SendResponseAsync(JsonSerializer.Serialize(transactions));
        }
        [RestRoute("Post", "fix-name")]
        public async Task FixName(IHttpContext context)
        {
            if (!_Authenticator.ValidateFromContext(context))
            {
                await context.Response.SendResponseAsync(HttpStatusCode.Forbidden);
                return;
            }
            
            // Kontrola práv uživatele
            bool isAdmin = _Authenticator.ValidateFromContext(context, new Claim("NsmAdmin", "true"));
            Guid userId = new Guid(_Authenticator.GetClaimValue(context, "UserId"));
            if (!_Model.CanUserCommit(isAdmin, userId))
            {
                await context.Response.SendResponseAsync(HttpStatusCode.Forbidden);
                return;
            }

            Dictionary<string, string> data = (context.Locals["FormData"] as Dictionary<string, string>) 
                ?? new Dictionary<string, string>();
            
            Guid guid = Guid.Empty;
            bool guidOk = data.TryGetValue("guid", out string? guidString);
            guidOk = guidOk && Guid.TryParse(guidString, out guid);
            
            bool fileNameOk = data.TryGetValue("file-name", out string? fileName);

            int suggestionIndex = 0;
            bool suggestionIndexOk = data.TryGetValue("suggestion-index", out string? suggestionIndexString);
            suggestionIndexOk = suggestionIndexOk && Int32.TryParse(suggestionIndexString, out suggestionIndex);

            StringBuilder errorMsg = new StringBuilder();
            if (!guidOk)
                errorMsg.AppendLine("Parameter \"guid\" missing or invalid.");
            if (!fileNameOk && !suggestionIndexOk)
                errorMsg.AppendLine("Both \"file-name\" and \"suggestion-index\" parameters are invalid. One of them must be correct.");

            if (errorMsg.Length > 0)
            {
                string msg = "Bad request: " + errorMsg.ToString();
                context.Response.StatusCode = HttpStatusCode.BadRequest;
                await context.Response.SendResponseAsync(msg);
                return;
            }

            var response = suggestionIndexOk ? _Corrector.CommitTransactionByGuid(guid, suggestionIndex)
                : _Corrector.CommitTransactionByGuid(guid, fileName);

            if (!response.Success)
            {
                context.Response.StatusCode = HttpStatusCode.InternalServerError;
                await context.Response.SendResponseAsync(response.Message);
                return;
            }

            context.Response.StatusCode = HttpStatusCode.Ok;
            await context.Response.SendResponseAsync();
        }
        [RestRoute("Get", "/file-exists/{transaction}/{fileName}")]
        public async Task CheckFileExists(IHttpContext context)
        {
            if (!_Authenticator.ValidateFromContext(context))
            {
                await context.Response.SendResponseAsync(HttpStatusCode.Forbidden);
                return;
            }

            StringBuilder errorMsg = new StringBuilder();

            context.Request.PathParameters.TryGetValue("transaction", out string? guidString);
            if (!Guid.TryParse(guidString, out Guid guid))
                errorMsg.AppendLine($"Parameter \"{guidString}\" is not valid Guid.");

            var trans = _Corrector.GetTransactionByGuid(guid);
            if (trans is null)
                errorMsg.AppendLine($"Transaction \"{guid}\" does not exist.");

            if(errorMsg.Length > 0)
            {
                context.Response.StatusCode = HttpStatusCode.BadRequest;
                await context.Response.SendResponseAsync($"Bad request: " + errorMsg.ToString());
                return;
            }

            context.Request.PathParameters.TryGetValue("fileName", out string? fileName);
            IRenamingSuggestion suggestion = new Suggestion(trans.InvalidFullPath, fileName, 0);
            context.Response.StatusCode = HttpStatusCode.Ok;
            await context.Response.SendResponseAsync(JsonSerializer.Serialize(suggestion));
        }
    }
}
