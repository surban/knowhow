using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Microsoft.AspNet.SignalR;

namespace knowhow
{
    public class SignalHub : Hub
    {
        public SignalHub()
        {
            Watcher.Notify = this.Notify;
        }

        public void WatchFile(string requestPath)
        {
            Watcher.RegisterClient(Context.ConnectionId, requestPath);
        }

        public override System.Threading.Tasks.Task OnDisconnected()
        {
            Watcher.DeregisterClient(Context.ConnectionId);            
            return base.OnDisconnected();
        }

        protected void Notify(string connectionId, string path, long mtime)
        {
            Clients.Client(connectionId).fileChanged(path, mtime);
        }
    }
}