module Handler

open System.IO
open System.Web
open System.Text.RegularExpressions
open FSharp.Markdown
open Newtonsoft.Json

open Tools
open Offline

let DisableOffline = false

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
    if File.Exists(preamble_src) then response.AddFileDependency(preamble_src)
    response.AddFileDependency(TemplatePath())
    ActivateCache response
    
    ["title", title;
     "root", VirtualPathUtility.ToAbsolute("~/");
     "manifest", if DisableOffline then ""
                 else match ManifestVirtualPath requestPath with
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

type AccessLevel =
    | PublicAccess
    | InternalAccess
    | PrivateAccess

let GetAccessLevel owner =
    match owner with
    | Shared -> PublicAccess
    | User u ->
        let cfgFile = Path.Combine(UserPath u, "Config", "Access.txt")
        try
            let text = (ReadFile cfgFile).ToLower()
            if text.StartsWith("public") then
                PublicAccess
            elif text.StartsWith("internal") then
                InternalAccess
            else
                PrivateAccess
        with
            | _ -> InternalAccess

let (|InternalRequest|ExternalRequest|) (request: System.Web.HttpRequest) =
    match request.UserHostName with
    | ParseRegex InternalHostRegex _ -> InternalRequest
    | _ -> ExternalRequest

let SAMName (user: System.Security.Principal.WindowsIdentity) =
    match user.Name.Split([|'\\'|]) with
    | [|domain; account|] -> account
    | _ as account -> account.[0]

let AccessAllowed (request: System.Web.HttpRequest) owner =
    match (GetAccessLevel owner, request) with
    | PublicAccess, _ -> true
    | InternalAccess, InternalRequest -> true
    | InternalAccess, ExternalRequest
    | PrivateAccess, _ -> 
        match owner with
        | User u -> (SAMName request.LogonUserIdentity).ToLower() = u.ToLower()
        | _ -> failwith "not possible"

let HandleDebugInfo (request: System.Web.HttpRequest) (response: System.Web.HttpResponse) =  
    response.ContentType <- "text/plain"
    response.Write(sprintf "UserHostName:              %s\n" request.UserHostName)
    response.Write(sprintf "LogonUserIdentity.Name:    %s\n" request.LogonUserIdentity.Name)
    
let HandleRequest (request: System.Web.HttpRequest) (response: System.Web.HttpResponse) =  
    let requestPath = VirtualPathUtility.ToAppRelative request.Path  
    let owner, _ = ParseVirtualContentPath requestPath
    let prefix, rest = SplitVirtualContentPath requestPath
    let src = VirtualContentPathToPhysical requestPath
    
    if AccessAllowed request owner then
        if rest = [|"offline.manifest"|] then 
            HandleOfflineManifestRequest requestPath response
        elif rest = [|"DebugInfo"|] then
            HandleDebugInfo request response
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
    else
        if request.LogonUserIdentity.IsAuthenticated then
            response.StatusCode <- 403   // user is known but has no permission to access
        else
            response.StatusCode <- 401   // user is not yet authenticated
        response.Write(sprintf "No permission for user %s from host %s to access %s" 
                               request.LogonUserIdentity.Name request.UserHostName requestPath)




