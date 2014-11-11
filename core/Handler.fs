module Handler

open System.IO
open System.Web
open FSharp.Markdown
open Newtonsoft.Json

// Settings
let settings = Configuration.WebConfigurationManager.AppSettings
let userPath user = settings.["UserPath"].Replace("%USER%", user)
let sharedPath = settings.["SharedPath"]

let ReadFile (path: string) =
    use file = new StreamReader(path)
    file.ReadToEnd()

let SplitPath (requestPath: string) =
    let components = requestPath.Split [|'/'|]
    let rec split (components: string array) = 
        match components.[1] with
            | "preload" -> split components.[1..]
            | "shared" -> sharedPath, components.[2..]
            | "user" -> userPath components.[2], components.[3..]
            | _ -> failwith "unknown prefix" 
    split components

let GetPathUserComponent (requestPath: string) =
    let components = requestPath.Split [|'/'|]
    match components.[1] with
        | "shared" -> "/shared"
        | "user" -> "/user/" + components.[2]
        | _ -> failwith "unknown prefix"

let GetOfflineManifestPath (requestPath: string) =
    let components = requestPath.Split [|'/'|]
    match components.[1] with
        | "shared" -> Some "/shared/offline.manifest"
        | "user" -> Some (sprintf "/user/%s/offline.manifest" components.[2])
        | "preload" -> None
        | _ -> None

let GetSourcePath (requestPath: string) =
    let prefix, rest = SplitPath requestPath
    let path = Array.concat [[|prefix|]; rest] |> Path.Combine 
    if File.Exists(path + ".md") then
        path + ".md"
    else
        path

let GetPreamblePath requestPath =
    let prefix, _ = SplitPath requestPath
    Path.Combine(prefix, "Config", "preamble.tex")

let GetTemplatePath() = HttpContext.Current.Server.MapPath("~/template.html")

let FillTemplate (fields: Map<string, string>) =
    let tmpl = ReadFile (GetTemplatePath())
    Map.fold (fun (text: string) variable value -> text.Replace("%(" + variable + ")", value)) 
        tmpl fields    
 
type Metadata = {RequestPath: string;  SourceMTime: int64;}

let SetCache (response: System.Web.HttpResponse) =
    response.Cache.SetLastModifiedFromFileDependencies()
    response.Cache.SetCacheability(HttpCacheability.Public)
    response.Cache.SetValidUntilExpires(true)

let HandleMDRequest (request: System.Web.HttpRequest) (response: System.Web.HttpResponse) =    
    //let isPreload = request.QueryString.["preload"] <> null
    let src = GetSourcePath request.Path
    let preamble_src = GetPreamblePath request.Path
    let preamble =
        if File.Exists(preamble_src) then ReadFile preamble_src else ""
    let md = ReadFile src |> MDPreprocessor.ParseMDText |> MDPreprocessor.OutputMDText 
                          |> Markdown.Parse
    let title = 
        match MDProcessor.ExtractTitle md with
        | Some t -> t
        | None -> request.Path
    let mdTagged, references = MDProcessor.ExtractAndTagReferences md
    let body = Markdown.WriteHtml mdTagged |> MDProcessor.PatchCitations references 
    
    response.AddFileDependency(src)
    response.AddFileDependency(preamble_src)
    response.AddFileDependency(GetTemplatePath())
    SetCache response
    
    ["title", title;
     "manifest", match GetOfflineManifestPath request.Path with
                 | Some p -> sprintf "manifest=\"%s\"" p
                 | None -> ""
     "preamble", preamble;
     "metadata", {RequestPath=request.Path; 
                  SourceMTime=File.GetLastWriteTimeUtc(src).Ticks;} 
                    |> JsonConvert.SerializeObject;
     "body", body ] 
        |> Map.ofList |> FillTemplate |> response.Write
    
let HandleFileRequest (request: System.Web.HttpRequest) (response: System.Web.HttpResponse) =    
    let src = GetSourcePath request.Path
    response.ContentType <- System.Web.MimeMapping.GetMimeMapping(src)
    response.AddFileDependency(src)
    SetCache response
    response.TransmitFile src

let ToURLPath (path: string) =
    path.Replace(@"\", "/") 

let rec AllFiles dir =
    let files = Directory.GetFileSystemEntries(dir)
    seq {
        for entry in files do
            if File.Exists(entry) then
                yield Path.GetFileName(entry)
            elif Directory.Exists(entry) then
                for subfile in AllFiles entry do
                    yield Path.Combine(Path.GetFileName(entry), subfile)
    }

let AllFilesInVirtualPath (request: System.Web.HttpRequest) virtualPath =
    let physicalPath = request.MapPath(virtualPath)
    seq {
        if File.Exists(physicalPath) then 
            yield virtualPath
        elif Directory.Exists(physicalPath) then
            for physicalFile in AllFiles physicalPath do
                yield virtualPath + "/" + ToURLPath physicalFile
    }


let offlineSupportFiles = ["/script.js"; "/style.css"; "/icon16.png"; "/icon32.png"; "/fonts/OpenSans-Regular.ttf";
                           "/Scripts/jquery-1.10.2.js"; "/Scripts/jquery.signalR-2.0.2.js";
                           "/MathJax/MathJax.js?config=TeX-AMS-MML_HTMLorMML"]

let MathJaxFiles = [@"MathJax\MathJax.js"; 
                    @"MathJax\config"; @"MathJax\extensions"; 
                    @"MathJax\fonts\HTML-CSS\Latin-Modern";
                    @"MathJax\fonts\HTML-CSS\TeX\eot";
                    @"MathJax\fonts\HTML-CSS\TeX\otf";
                    @"MathJax\fonts\HTML-CSS\TeX\woff";
                    @"MathJax\jax\element"; 
                    @"MathJax\jax\input";
                    @"MathJax\jax\output\HTML-CSS\config.js"; 
                    @"MathJax\jax\output\HTML-CSS\imageFonts.js";
                    @"MathJax\jax\output\HTML-CSS\jax.js"; 
                    @"MathJax\jax\output\HTML-CSS\autoload";
                    @"MathJax\jax\output\HTML-CSS\fonts\Latin-Modern";
                    @"MathJax\jax\output\HTML-CSS\fonts\TeX"]


let SubpathFiles (request: System.Web.HttpRequest) file = 
    let fullpath = Path.Combine(request.PhysicalApplicationPath, file)
    seq {
        if File.Exists(fullpath) then
            yield file
        else
            for f in AllFiles fullpath do
                yield Path.Combine(file, f)
    }

let HandleOfflineManifestRequest (request: System.Web.HttpRequest) (response: System.Web.HttpResponse) =    
    let prefix, _ = SplitPath request.Path
    let userComponent = GetPathUserComponent request.Path

    response.ContentType <- "text/cache-manifest"

    response.Write("CACHE MANIFEST\n")
    response.Write("CACHE:\n")
    AllFiles prefix 
        |> Seq.map (fun f -> if Path.GetExtension(f) = ".md" then 
                                 Path.ChangeExtension(f, null) 
                             else f)
        |> Seq.iter (fun f -> let ssrc = Path.Combine(prefix, f)
                              let src = if File.Exists(ssrc + ".md") then ssrc + ".md" else ssrc
                              let mtime = File.GetLastWriteTimeUtc(src)
                              response.Write(sprintf "# %s\n" (mtime.ToString())); 
                              response.Write(ToURLPath f + "\n") )
    response.Write("\n")
    ["/Scripts"; "/Web"] |> Seq.collect (AllFilesInVirtualPath request) |> String.concat "\n" |> response.Write
    offlineSupportFiles |> String.concat "\n" |> response.Write
    response.Write("\n")
    for d in MathJaxFiles do
        for f in SubpathFiles request d do
            response.Write("/" + ToURLPath f + "\n")
            response.Write("/" + ToURLPath f + "?rev=2.4-beta-2\n")
    response.Write("NETWORK:\n")
    response.Write("preload/\n")
    response.Write("*\n")
    response.Write("FALLBACK:\n")
    //response.Write(sprintf "%s %s/Contents\n" userComponent userComponent)
    response.Write(sprintf "%s/ %s/Contents\n" userComponent userComponent)


let HandleRequest (request: System.Web.HttpRequest) (response: System.Web.HttpResponse) =    
    let prefix, rest = SplitPath request.Path
    let src = GetSourcePath request.Path

    if rest = [|"offline.manifest"|] then 
        HandleOfflineManifestRequest request response
    elif Directory.Exists(src) then
        response.Redirect(sprintf "%s/Contents" (request.Path))
    elif File.Exists(src) then
        match Path.GetExtension(src) with
        | ".md" -> HandleMDRequest request response
        | _ -> HandleFileRequest request response
    else
        response.StatusCode <- 404
        //response.Write(sprintf "Resource not found: %s" src)
        response.Write(sprintf "Resource not found: %s" request.Path)




