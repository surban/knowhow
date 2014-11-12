module Handler

open System.IO
open System.Web
open FSharp.Markdown
open Newtonsoft.Json

open PathTools
open Offline

let ActivateCache (response: System.Web.HttpResponse) =
    response.Cache.SetLastModifiedFromFileDependencies()
    response.Cache.SetCacheability(HttpCacheability.Public)
    response.Cache.SetValidUntilExpires(true)

let ReadFile (path: string) =
    use file = new StreamReader(path)
    file.ReadToEnd()

let FillTemplate (fields: Map<string, string>) =
    let tmpl = ReadFile (TemplatePath())
    Map.fold (fun (text: string) variable value -> text.Replace("%(" + variable + ")", value)) 
        tmpl fields    

type Metadata = {RequestPath: string;  PreloadPath: string;  SourceMTime: int64;}

let HandleMDRequest (requestPath: string) (response: System.Web.HttpResponse) =    
    let src = VirtualContentPathToPhysical requestPath
    let preamble_src = PreamblePath requestPath
    let preamble =
        if File.Exists(preamble_src) then ReadFile preamble_src else ""
    let md = ReadFile src |> MDPreprocessor.ParseMDText |> MDPreprocessor.OutputMDText 
                          |> Markdown.Parse
    let title = 
        match MDProcessor.ExtractTitle md with
        | Some t -> t
        | None -> requestPath
    let mdTagged, references = MDProcessor.ExtractAndTagReferences md
    let body = Markdown.WriteHtml mdTagged |> MDProcessor.PatchCitations references 
    
    response.AddFileDependency(src)
    response.AddFileDependency(preamble_src)
    response.AddFileDependency(TemplatePath())
    ActivateCache response
    
    ["title", title;
     "root", VirtualPathUtility.ToAbsolute("~");
     "manifest", match ManifestVirtualPath requestPath with
                 | Some p -> sprintf "manifest=\"%s\"" p
                 | None -> ""
     "preamble", preamble;
     "metadata", {RequestPath=requestPath;
                  PreloadPath=VirtualPathUtility.ToAbsolute("~/preload/" + requestPath.[2..]); 
                  SourceMTime=File.GetLastWriteTimeUtc(src).Ticks;} 
                    |> JsonConvert.SerializeObject;
     "body", body ] 
        |> Map.ofList |> FillTemplate |> response.Write
    
let HandleFileRequest (requestPath: string) (response: System.Web.HttpResponse) =    
    let src = VirtualContentPathToPhysical requestPath
    response.ContentType <- System.Web.MimeMapping.GetMimeMapping(src)
    response.AddFileDependency(src)
    ActivateCache response
    response.TransmitFile src

let HandleOfflineManifestRequest (requestPath: string) (response: System.Web.HttpResponse) =    
    response.ContentType <- "text/cache-manifest"
    response.Write(OfflineManifest requestPath)

let HandleRequest (request: System.Web.HttpRequest) (response: System.Web.HttpResponse) =  
    let requestPath = VirtualPathUtility.ToAppRelative request.Path  
    let prefix, rest = SplitVirtualContentPath requestPath
    let src = VirtualContentPathToPhysical requestPath

    if rest = [|"offline.manifest"|] then 
        HandleOfflineManifestRequest requestPath response
    elif Directory.Exists(src) then
        response.Redirect(sprintf "%s/Contents" (request.Path))
    elif File.Exists(src) then
        match Path.GetExtension(src) with
        | ".md" -> HandleMDRequest requestPath response
        | _ -> HandleFileRequest requestPath response
    else
        response.StatusCode <- 404
        //response.Write(sprintf "Resource not found: %s" src)
        response.Write(sprintf "Resource not found: %s" requestPath)




