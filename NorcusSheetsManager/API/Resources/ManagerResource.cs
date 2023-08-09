using Grapevine;
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
    [RestResource(BasePath = "api/v1/manager")]
    internal class ManagerResource
    {
        private ITokenAuthenticator _Authenticator { get; set; }
        private Manager _Manager { get; set; }
        public ManagerResource(ITokenAuthenticator authenticator, Manager manager)
        {
            _Authenticator = authenticator;
            _Manager = manager;
        }

        [RestRoute("Post", "/scan")]
        public async Task Scan(IHttpContext context)
        {
            if (!_Authenticator.ValidateFromContext(context, new Claim("NsmAdmin", "true")))
            {
                await context.Response.SendResponseAsync(HttpStatusCode.Forbidden);
                return;
            }

            context.Response.StatusCode = HttpStatusCode.Ok;
            await context.Response.SendResponseAsync();
            
            _Manager.FullScan();
        }
        [RestRoute("Post", "/deep-scan")]
        public async Task DeepScan(IHttpContext context)
        {
            if (!_Authenticator.ValidateFromContext(context, new Claim("NsmAdmin", "true")))
            {
                await context.Response.SendResponseAsync(HttpStatusCode.Forbidden);
                return;
            }

            context.Response.StatusCode = HttpStatusCode.Ok;
            await context.Response.SendResponseAsync();

            _Manager.DeepScan();
        }
        [RestRoute("Post", "/convert-all")]
        public async Task ConvertAll(IHttpContext context)
        {
            if (!_Authenticator.ValidateFromContext(context, new Claim("NsmAdmin", "true")))
            {
                await context.Response.SendResponseAsync(HttpStatusCode.Forbidden);
                return;
            }

            context.Response.StatusCode = HttpStatusCode.Ok;
            await context.Response.SendResponseAsync();

            _Manager.ForceConvertAll();
        }
    }
}
