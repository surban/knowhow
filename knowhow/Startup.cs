using System;
using System.Threading.Tasks;
using Microsoft.Owin;
using Owin;

[assembly: OwinStartup(typeof(knowhow.Startup))]

namespace knowhow
{
    public class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            Tools.Log.Init();
            app.MapSignalR();
        }
    }
}
