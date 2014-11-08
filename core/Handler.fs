module Handler

open System.IO
open System.Web
open FSharp.Markdown
open Newtonsoft.Json
open System.Text.RegularExpressions

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



type MathElement =
    | MathEnvironment of string * (MathElement list)
    | MathText of string

type TextElement =
    | Text of string
    | Citation of string
    | EQRef of string
    | InlineMath of (MathElement list)
    | MathBlock of MathElement
    | MathParagraph of string 
    
let SplitIntoLines (txt: string) =
    Array.toList (txt.Split([|'\n'|]))

let AssembleLines (lines: string list) =
    String.concat "\n" lines

let (|FirstChars|_|) (beginning: string) (txt: string) =
    if txt.StartsWith(beginning) then Some (txt.[beginning.Length..]) else None

let (|FirstLine|_|) (txt: string) =
    match txt.Length with
    | 0 -> None
    | _ ->
        match txt.IndexOf('\n') with
        | -1 -> Some (txt, "")
        | pos -> Some (txt.[0..pos+1], txt.[pos+1..])

let FirstParagraph (txt: string) =
    let rec Extract (lines: string list) = 
        match lines with
        | line::rest ->
            if line.Trim().Length = 0 then
                [line], rest
            else
                let paragraph, past = Extract rest
                line::paragraph, past
        | [] -> [], []
    let lines = SplitIntoLines txt
    let fp, rest = Extract lines
    AssembleLines fp, AssembleLines rest
      
let (|MathParagraphText|_|) (txt: string) =
    match txt with
    | FirstLine(line, _) ->
        match line with 
        | FirstChars "$$$" rest ->
            let mp, rest = FirstParagraph rest
            Some (MathParagraph mp, rest)
        | _ -> None
    | _ -> None

let (|ParseRegex|_|) regex str =
   let m = Regex(regex).Match(str)
   if m.Success
   then Some (List.tail [ for x in m.Groups -> x.Value ])
   else None

let (|MathBeginBlockText|_|) (txt: string) =
    match txt with
    | ParseRegex @"^\\begin{(\w+)}(.*)" [env; rest] -> Some (env, rest)
    | FirstChars @"\\[" rest -> Some ("[", rest)
    | _ -> None
    
let (|MathEndBlockText|_|) (txt: string) =
    match txt with
    | ParseRegex @"^\\end{(\w+)}(.*)" [env; rest] -> Some (env, rest)
    | FirstChars @"\\]" rest -> Some ("[", rest)
    | FirstChars @"$" rest -> Some ("$", rest)
    | _ -> None

let NextCandidate (txt: string) =
     match txt.IndexOfAny([|'\\'; '$'|]) with
     | -1 -> txt.Length
     | 0 -> 1
     | pos -> pos

let rec ParseMath env txt =
    match txt with
    | MathEndBlockText(endingEnv, followingTxt) when endingEnv = env ->
        [], followingTxt
    | MathBeginBlockText(childEnv, childContent) -> 
        let child, restTxt = ParseMath childEnv childContent
        let rest, followingTxt = ParseMath env restTxt
        MathEnvironment(childEnv, child)::rest, followingTxt
    | _ when txt.Length = 0 ->
        [], ""
    | _ ->
        let e = NextCandidate txt
        let rest, followingTxt = ParseMath env (txt.[e..])
        MathText(txt.[0..e])::rest, followingTxt
          

let rec ParseMDText (txt: string) =
    match txt with
    | MathParagraphText(mp, rest) ->
        mp::ParseMDText rest
    | MathBeginBlockText(env, contentTxt) -> 
        let content, rest = ParseMath env contentTxt
        MathBlock(MathEnvironment(env, content))::ParseMDText rest
    | FirstChars "$" contentTxt ->
        let content, rest = ParseMath "$" contentTxt
        InlineMath(content)::ParseMDText rest
    | ParseRegex @"\\cite{(\w+)}(.*)" [target; rest] ->
        Citation(target)::ParseMDText rest
    | ParseRegex @"\\eqref{(\w+)}(.*)" [target; rest] ->
        EQRef(target)::ParseMDText rest
    | _ when txt.Length = 0 ->
        []
    | _ ->
        let e = NextCandidate txt
        Text(txt.[0..e])::ParseMDText txt.[e..]
        


    

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
    //let rec 
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
    


