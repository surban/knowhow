﻿module Handler

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
    if not ((Seq.last rest).Contains(".")) then
        path + ".md"
    else
        path

let GetPreamblePath requestPath =
    let prefix, _ = SplitPath requestPath
    Path.Combine(prefix, "preamble.tex")

let FillTemplate (fields: Map<string, string>) =
    let tmpl = ReadFile (HttpContext.Current.Server.MapPath("~/template.html"))
    Map.fold (fun (text: string) variable value -> text.Replace("%(" + variable + ")", value)) 
        tmpl fields    

let PreprocessMarkdown (md: string) =
    let lines = md.Split([|'\n'|])
    // add empty line before "$$$" math paragraph to satisfy markdown parser
    let rec AddMathParagraph (prev: string) (lines: string list) = seq { 
        match lines with
        | line::rest -> 
            if line.Trim().StartsWith("$$$") && prev.Trim().Length <> 0 then
                yield ""
            yield line
            yield! AddMathParagraph line rest
        | [] -> ()
        }
    lines |> Array.toList |> AddMathParagraph "" |> String.concat "\n"

let ExtractTitle (md: MarkdownDocument) =
    let rec extract = function
        | Heading(_, [Literal text])::rest -> Some text
        | _::rest -> extract rest
        | [] -> None
    extract md.Paragraphs

type Metadata = {RequestPath: string;  SourceMTime: int64;}

let HandleRequest (request: System.Web.HttpRequest) (response: System.Web.HttpResponse) =
    let src = GetSourcePath request.FilePath
    let preamble_src = GetPreamblePath request.FilePath
    let preamble =
        if File.Exists(preamble_src) then ReadFile preamble_src else ""
    let md = ReadFile src |> PreprocessMarkdown |> Markdown.Parse
    let title = 
        match ExtractTitle md with
        | Some t -> t
        | None -> request.FilePath

    let body = Markdown.WriteHtml md
    
    ["title", title;
     "preamble", preamble;
     "metadata", {RequestPath=request.FilePath; 
                  SourceMTime=File.GetLastWriteTimeUtc(src).Ticks;} 
                    |> JsonConvert.SerializeObject;
     "body", body ] 
        |> Map.ofList |> FillTemplate |> response.Write
    


