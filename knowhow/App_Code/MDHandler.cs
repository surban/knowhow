using System;
using System.Web;

namespace knowhow
{
    public class MDHandler : IHttpHandler
    {
        public MDHandler()
        {

        }

        #region IHttpHandler Members

        public bool IsReusable
        {
            get { return true; }
        }

        public void ProcessRequest(HttpContext context)
        {
            HttpRequest Request = context.Request;
            HttpResponse Response = context.Response;
            Handler.HandleRequest(Request, Response);
        }

        #endregion
    }
}
