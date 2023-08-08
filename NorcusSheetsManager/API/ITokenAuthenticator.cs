using Grapevine;
using System.Collections.Generic;
using System.Security.Claims;

namespace NorcusSheetsManager.API
{
    public interface ITokenAuthenticator
    {
        bool IsTokenValid(string token);
        string GetClaimValue(IHttpContext context, string claimType);
        string GetClaimValue(string token, string claimType);
        bool ValidateFromContext(IHttpContext context);
        bool ValidateFromContext(IHttpContext context, Claim requiredClaim);
        bool ValidateFromContext(IHttpContext context, IEnumerable<Claim> requiredClaims);
    }
}