using System;
using System.Web;

namespace knowhow
{
    public class MMDHandler : IHttpHandler
    {
        public MMDHandler()
        {

        }

        #region IHttpHandler Members

        public bool IsReusable
        {
            // Return false in case your Managed Handler cannot be reused for another request.
            // Usually this would be false in case you have some state information preserved per request.
            get { return true; }
        }

        public void ProcessRequest(HttpContext context)
        {
            HttpRequest Request = context.Request;
            HttpResponse Response = context.Response;
            core.HandleRequest(Request, Response);
        }

        #endregion
    }
}
