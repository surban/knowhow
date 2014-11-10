module Watcher

open System.IO
open System.Collections.Generic

type ConnectionId = string
type FilePath = string
type DirectoryPath = string

type NotifyFunc = delegate of ConnectionId * FilePath * int64 -> unit
let mutable Notify: NotifyFunc = null

module ClientAssociations =
    let ClientsByFile = Dictionary<FilePath, HashSet<ConnectionId>>()
    let FilesByClient = Dictionary<ConnectionId, HashSet<FilePath>>()

    let AddWatch (id: ConnectionId) (path: FilePath) =
        System.Diagnostics.Debug.WriteLine("Registering client " + id + " for file " + path)

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
                    System.Diagnostics.Debug.WriteLine("Deregistering client " + id + " for file " + file)
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

    let Notification (event: FileSystemEventArgs) =
        let src = event.FullPath
        System.Diagnostics.Debug.WriteLine("File change notification: " + src)
        for id in (ClientAssociations.GetClientsByFile src) do
            System.Diagnostics.Debug.WriteLine("Notifying client " + id)
            Notify.Invoke(id, src, File.GetLastWriteTimeUtc(src).Ticks)

    let AddDirectory (dir: DirectoryPath) =
        if not (WatchedDirectories.Contains dir) then
            System.Diagnostics.Debug.WriteLine("Watching directory " + dir)
            let w = new FileSystemWatcher(dir)
            w.Changed.Add(Notification)
            w.Created.Add(Notification)
            Watchers.Add(w)
            WatchedDirectories.Add(dir) |> ignore
            w.EnableRaisingEvents <- true


let RegisterClient connectionId requestPath =
    let src = Handler.GetSourcePath requestPath
    let src_dir = Path.GetDirectoryName src
     
    ClientAssociations.AddWatch connectionId src
    Watch.AddDirectory src_dir

    // send initial notification for cached pages
    Notify.Invoke(connectionId, src, File.GetLastWriteTimeUtc(src).Ticks)


let DeregisterClient connectionId =
    ClientAssociations.RemoveClient connectionId




