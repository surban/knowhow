module Watcher

open System.IO
open System.Collections.Generic

open Tools

type ConnectionId = string
type FilePath = string
type DirectoryPath = string

type NotifyFunc = delegate of ConnectionId * FilePath * int64 -> unit
let mutable Notify: NotifyFunc = null

module ClientAssociations =
    let ClientsByFile = Dictionary<FilePath, HashSet<ConnectionId>>()
    let FilesByClient = Dictionary<ConnectionId, HashSet<FilePath>>()

    let AddWatch (id: ConnectionId) (path: FilePath) =
        Tools.Log.Info(sprintf "Watcher: Registering client %s for file %s" id path)

        if not (ClientsByFile.ContainsKey(path)) then
            ClientsByFile.[path] <- HashSet<ConnectionId>()
        ClientsByFile.[path].Add(id) |> ignore

        if not (FilesByClient.ContainsKey(id)) then
            FilesByClient.[id] <- HashSet<FilePath>()
        FilesByClient.[id].Add(path) |> ignore

    let RemoveClient (id: ConnectionId) =
        if FilesByClient.ContainsKey(id) then
            for file in FilesByClient.[id] do
                if ClientsByFile.[file].Contains(id) then
                    Tools.Log.Info(sprintf "Watcher: Deregistering client %s for file %s" id file)
                    ClientsByFile.[file].Remove(id) |> ignore
            FilesByClient.Remove(id) |> ignore

    let GetClientsByFile (path: FilePath) =
        if ClientsByFile.ContainsKey(path) then
            Seq.cast ClientsByFile.[path]
        else
            Seq.empty

module Watch = 
    let Watchers = List<FileSystemWatcher>()
    let WatchedDirectories = HashSet<DirectoryPath>()

    let ChangeNotification (event: FileSystemEventArgs) =
        let src = event.FullPath
        for id in (ClientAssociations.GetClientsByFile src) do
            Tools.Log.Info(sprintf "Watcher: Notifying client %s for of changed file %s" id src)
            Notify.Invoke(id, src, File.GetLastWriteTimeUtc(src).Ticks)

    let ErrorNotification (event: ErrorEventArgs) =
        Tools.Log.Warning(sprintf "Watcher: FileSystemWatcher reported error: %s" (event.GetException().Message))

    let AddDirectory (dir: DirectoryPath) =
        if not (WatchedDirectories.Contains dir) then
            Tools.Log.Info(sprintf "Watcher: Watching directory %s" dir)
            let w = new FileSystemWatcher(dir)
            w.Changed.Add(ChangeNotification)
            w.Created.Add(ChangeNotification)
            w.Error.Add(ErrorNotification)
            Watchers.Add(w)
            WatchedDirectories.Add(dir) |> ignore
            w.EnableRaisingEvents <- true

let RegisterClient connectionId requestPath =
    let src = VirtualContentPathToPhysical requestPath
    let src_dir = Path.GetDirectoryName src
     
    ClientAssociations.AddWatch connectionId src
    Watch.AddDirectory src_dir

    // send initial notification for cached pages
    Notify.Invoke(connectionId, src, File.GetLastWriteTimeUtc(src).Ticks)


let DeregisterClient connectionId =
    ClientAssociations.RemoveClient connectionId




