using Grapevine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NorcusSheetsManager.API.Resources
{
    [RestResource]
    internal class MasterResource
    {
        [RestRoute("Options")]
        public async Task Options(IHttpContext context)
        {
            context.Response.AddHeader("Access-Control-Max-Age", "86400");
            await context.Response.SendResponseAsync(HttpStatusCode.Ok);
        }
    }
}
