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
    match components.[1] with
        | "shared" -> sharedPath, components.[2..]
        | "user" -> userPath components.[2], components.[3..]
        | _ -> failwithf "unknown prefix %s" (components.[1])

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

let FillTemplate (fields: Map<string, string>) =
    let tmpl = ReadFile (HttpContext.Current.Server.MapPath("~/template.html"))
    Map.fold (fun (text: string) variable value -> text.Replace("%(" + variable + ")", value)) 
        tmpl fields    
 
type Metadata = {RequestPath: string;  SourceMTime: int64;}

let SetCache (response: System.Web.HttpResponse) =
    response.Cache.SetLastModifiedFromFileDependencies()
    response.Cache.SetCacheability(HttpCacheability.Public)
    response.Cache.SetValidUntilExpires(true)

let HandleMDRequest (request: System.Web.HttpRequest) (response: System.Web.HttpResponse) =    
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
    SetCache response
    
    ["title", title;
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

let HandleRequest (request: System.Web.HttpRequest) (response: System.Web.HttpResponse) =    
    let src = GetSourcePath request.Path
    if Directory.Exists(src) then
        response.Redirect(sprintf "%s/Contents" (request.Path))
    elif File.Exists(src) then
        match Path.GetExtension(src) with
        | ".md" -> HandleMDRequest request response
        | _ -> HandleFileRequest request response
    else
        response.StatusCode <- 404
        //response.Write(sprintf "Resource not found: %s" src)
        response.Write(sprintf "Resource not found: %s" request.Path)




