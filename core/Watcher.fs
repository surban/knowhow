module Watcher

open System.Timers
open System.IO
open System.Collections.Generic

open Tools

type ConnectionId = string
type FilePath = string
type DirectoryPath = string

type NotifyFunc = delegate of ConnectionId * FilePath * int64 -> unit
let mutable Notify: NotifyFunc = null
let WatchLock = ref 0

module internal Watch = 
    type WatchInfo = {Path: FilePath; LastChanged: System.DateTime; Notify: FilePath -> unit }
    let DirectoryWatchers = Dictionary<DirectoryPath, FileSystemWatcher>()
    let WatchedFiles = Dictionary<FilePath, WatchInfo>() 
    let WatchedFilesByDirectory = Dictionary<DirectoryPath, List<FilePath>>()

    let FileChanged (path: FilePath) =
        Tools.Log.Info(sprintf "Watcher: File %s changed" path)
        WatchedFiles.[path] <- {WatchedFiles.[path] with LastChanged=File.GetLastWriteTimeUtc(path)}
        WatchedFiles.[path].Notify(path)

    let ChangeNotification (event: FileSystemEventArgs) =
        FileChanged event.FullPath

    let ErrorNotification (w: FileSystemWatcher) (event: ErrorEventArgs) =
        Tools.Log.Warning(sprintf "Watcher: FileSystemWatcher reported error: %s" (event.GetException().Message))
        w.EnableRaisingEvents <- false
        w.EnableRaisingEvents <- true

    let CreateWatcher (dir: DirectoryPath) =
        Tools.Log.Info(sprintf "Watcher: Creating FileSystemWatcher for directory %s" dir)
        let w = new FileSystemWatcher(dir)
        w.Changed.Add(ChangeNotification)
        w.Created.Add(ChangeNotification)
        w.Error.Add(fun evnt -> ErrorNotification w evnt)     
        w.EnableRaisingEvents <- true       
        DirectoryWatchers.[dir] <- w

    let ReviveWatcher (dir: DirectoryPath) =
        Tools.Log.Warning(sprintf "Watcher: Reviving FileSystemWatcher for directory %s" dir)
        DirectoryWatchers.[dir].Dispose()
        CreateWatcher dir     
        
    let RemoveWatcher (dir: DirectoryPath) =
        Tools.Log.Info(sprintf "Watcher: Removing FileSystemWatcher for directory %s" dir)
        DirectoryWatchers.[dir].Dispose()
        DirectoryWatchers.Remove(dir) |> ignore

    let PeriodicCheck () =
        for wi in WatchedFiles.Values do
            if wi.LastChanged <> File.GetLastWriteTimeUtc(wi.Path) then
                FileChanged wi.Path
                ReviveWatcher (Path.GetDirectoryName wi.Path)

    let AddFile (path: FilePath) notify =
        Tools.Log.Info(sprintf "Watcher: Watching file %s" path)
        WatchedFiles.Add(path, {Path=path; LastChanged=File.GetLastWriteTimeUtc(path); Notify=notify})

        let dir = Path.GetDirectoryName path
        if not (WatchedFilesByDirectory.ContainsKey(dir)) then 
            WatchedFilesByDirectory.[dir] <- List<FilePath>()
            CreateWatcher dir
        WatchedFilesByDirectory.[dir].Add(path)

    let RemoveFile (path: FilePath) =
        Tools.Log.Info(sprintf "Watcher: Unwatching file %s" path)
        WatchedFiles.Remove(path) |> ignore

        let dir = Path.GetDirectoryName path
        WatchedFilesByDirectory.[dir].Remove(path) |> ignore
        if WatchedFilesByDirectory.[dir].Count = 0 then
            RemoveWatcher dir
            WatchedFilesByDirectory.Remove(dir) |> ignore


module internal ClientAssociations =
    let ClientsByFile = Dictionary<FilePath, HashSet<ConnectionId>>()
    let FilesByClient = Dictionary<ConnectionId, HashSet<FilePath>>()

    let GetClientsByFile (path: FilePath) =
        if ClientsByFile.ContainsKey(path) then
            Seq.cast ClientsByFile.[path]
        else
            Seq.empty

    let FileChanged (path: FilePath) =
        for id in (GetClientsByFile path) do
            Tools.Log.Info(sprintf "Watcher: Notifying client %s of changed file %s" id path)
            Notify.Invoke(id, path, File.GetLastWriteTimeUtc(path).Ticks)

    let AddWatch (id: ConnectionId) (path: FilePath) =
        Tools.Log.Info(sprintf "Watcher: Registering client %s for file %s" id path)

        if not (ClientsByFile.ContainsKey(path)) then
            ClientsByFile.[path] <- HashSet<ConnectionId>()
            Watch.AddFile path FileChanged
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
                    if ClientsByFile.[file].Count = 0 then
                        ClientsByFile.Remove(file) |> ignore
                        Watch.RemoveFile file
            FilesByClient.Remove(id) |> ignore



let RegisterClient connectionId requestPath =
    lock WatchLock (fun () -> 
        let src = VirtualContentPathToPhysical requestPath    
        ClientAssociations.AddWatch connectionId src
        // send initial notification for cached pages
        Notify.Invoke(connectionId, src, File.GetLastWriteTimeUtc(src).Ticks)
    )

let DeregisterClient connectionId =
    lock WatchLock (fun () ->   
        ClientAssociations.RemoveClient connectionId
    )

let PeriodicCheckTimer = new Timer()

let StartPeriodicCheck () =
    Tools.Log.Info("Watcher: Starting periodic file modification check")
    PeriodicCheckTimer.Interval <- 30000.
    PeriodicCheckTimer.Elapsed.Add(fun e ->
        lock WatchLock (fun () -> if PeriodicCheckTimer.Enabled then Watch.PeriodicCheck())
    )
    PeriodicCheckTimer.Enabled <- true

let StopPeriodicCheck () =
    Tools.Log.Info("Watcher: Stopping periodic file modification check")
    lock WatchLock (fun () -> PeriodicCheckTimer.Enabled <- false)



