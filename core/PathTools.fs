module PathTools

open System.Web
open System.IO

let Settings = Configuration.WebConfigurationManager.AppSettings
let SharedPath = Settings.["SharedPath"]

let UserPath user = 
    Settings.["UserPath"].Replace("%USER%", user)

let ToForwardSlashes (path: string) =
    path.Replace(@"\", "/") 
 
let SplitVirtualContentPath (requestPath: string) =
    let components = requestPath.Split [|'/'|]
    let rec split (components: string array) = 
        match components.[1] with
            | "preload" -> split components.[1..]
            | "shared" -> SharedPath, components.[2..]
            | "user" -> UserPath components.[2], components.[3..]
            | _ -> failwith "unknown prefix" 
    split components

let VirtualContentRoot (requestPath: string) =
    let components = requestPath.Split [|'/'|]
    match components.[1] with
        | "shared" -> "/shared"
        | "user" -> "/user/" + components.[2]
        | _ -> failwith "unknown prefix"

let ManifestVirtualPath (requestPath: string) =
    let components = requestPath.Split [|'/'|]
    match components.[1] with
        | "preload" -> None
        | _ -> Some (VirtualPathUtility.ToAbsolute("~" + VirtualContentRoot requestPath + 
                                                   "/offline.manifest"))

let VirtualContentPathToPhysical requestPath =
    let prefix, rest = SplitVirtualContentPath requestPath
    let path = Array.concat [[|prefix|]; rest] |> Path.Combine 
    if File.Exists(path + ".md") then path + ".md" else path

let PreamblePath requestPath =
    let prefix, _ = SplitVirtualContentPath requestPath
    Path.Combine(prefix, "Config", "preamble.tex")

let VirtualToPhysical virtualPath =
    HttpContext.Current.Server.MapPath(virtualPath)

let TemplatePath() = 
    VirtualToPhysical "~/Web/template.html"

let rec AllFilesInPath dir =
    let files = Directory.GetFileSystemEntries(dir)
    seq {
        for entry in files do
            if File.Exists(entry) then
                yield Path.GetFileName(entry)
            elif Directory.Exists(entry) then
                for subfile in AllFilesInPath entry do
                    yield Path.Combine(Path.GetFileName(entry), subfile)
    }

let AllFilesInVirtualPath virtualPath =
    let physicalPath = VirtualToPhysical virtualPath
    seq {
        if File.Exists(physicalPath) then 
            yield VirtualPathUtility.ToAbsolute(virtualPath)
        elif Directory.Exists(physicalPath) then
            for physicalFile in AllFilesInPath physicalPath do
                yield VirtualPathUtility.ToAbsolute(virtualPath + "/" + ToForwardSlashes physicalFile)
    }