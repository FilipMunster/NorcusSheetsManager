using Grapevine;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Logging;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace NorcusSheetsManager.API
{
    internal class JWTAuthenticator : ITokenAuthenticator
    {
        private string _key;
        private readonly NLog.Logger _logger;
        public JWTAuthenticator(string secureKey)
        {
            _key = secureKey;
            _logger = NLog.LogManager.GetCurrentClassLogger();
            IdentityModelEventSource.ShowPII = true;
            if (string.IsNullOrEmpty(secureKey))
                _logger.Warn("Secure key was not set. All requests will be accepted.");
        }
        public bool IsTokenValid(string token) => _ProcessToken(token).Valid;

        public string GetClaimValue(IHttpContext context, string claimType)
        {
            string jwtToken = context.Request.Headers.GetValue<string>("Authorization").Split(' ', StringSplitOptions.RemoveEmptyEntries)[1];
            return GetClaimValue(jwtToken, claimType);
        }
        public string GetClaimValue(string token, string claimType)
        {
            ClaimsPrincipal? claims = _ProcessToken(token).Claims;
            return claims?.Claims.FirstOrDefault(c => c.Type.ToLower() == claimType.ToLower())?.Value ?? "";
        }

        public bool ValidateFromContext(IHttpContext context)
            => ValidateFromContext(context, Enumerable.Empty<Claim>());
        public bool ValidateFromContext(IHttpContext context, Claim requiredClaim)
            => ValidateFromContext(context, new[] { requiredClaim });
        public bool ValidateFromContext(IHttpContext context, IEnumerable<Claim> requiredClaims)
        {
            if (string.IsNullOrEmpty(_key)) return true;

            string jwtToken = context.Request.Headers.GetValue<string>("Authorization").Split(' ', StringSplitOptions.RemoveEmptyEntries)[1];
            var token = _ProcessToken(jwtToken);

            if (!token.Valid) return false;
            foreach (var requiredClaim in requiredClaims)
            {
                Claim? claim = token.Claims?.FindFirst((c) => c.Type == requiredClaim.Type && c.Value == requiredClaim.Value);
                if (claim is null) return false;
            }
            return true;
        }

        private (bool Valid, ClaimsPrincipal? Claims) _ProcessToken(string token)
        {
            if (string.IsNullOrEmpty(_key)) return (true, null);

            if (string.IsNullOrEmpty(token)) return (false, null);
            TokenValidationParameters tokenValidationParameters = _GetTokenValidationParameters();
            JwtSecurityTokenHandler jwtSecurityTokenHandler = new JwtSecurityTokenHandler();
            try
            {
                ClaimsPrincipal claims = jwtSecurityTokenHandler.ValidateToken(token, tokenValidationParameters, out SecurityToken validatedToken);
                return (true, claims);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in JWT token validation");
                return (false, null);
            }
        }

        private TokenValidationParameters _GetTokenValidationParameters()
        {
            return new TokenValidationParameters()
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                IssuerSigningKey = _GetSymmetricSecurityKey(),
                LifetimeValidator = _LifetimeValidator,
                ValidateLifetime = false
            };
        }
        private SecurityKey _GetSymmetricSecurityKey()
        {
            byte[] symmetricKey = Encoding.UTF8.GetBytes(_key);
            return new SymmetricSecurityKey(symmetricKey);
        }
        private bool _LifetimeValidator(DateTime? notBefore, DateTime? expires, SecurityToken securityToken, TokenValidationParameters validationParameters)
        {
            if (!validationParameters.ValidateLifetime) return true;
            DateTime now = DateTime.UtcNow;
            if (now > securityToken.ValidFrom && now < securityToken.ValidTo) return true;
            return false;
        }
    }
}
