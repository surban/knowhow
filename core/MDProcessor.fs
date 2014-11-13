module MDProcessor

open FSharp.Markdown

open Tools

let ReferenceSectionTitles = ["References"; "Bibliography"; "Literature"]

let CompareStrings a b =
    System.String.Equals(a, b, System.StringComparison.CurrentCultureIgnoreCase)

let ExtractAndTagReferences (md: MarkdownDocument) =
    let rec extractTarget = function
        | DirectLink(_, (target, _))::rest -> Some target
        | _::rest -> extractTarget rest
        | [] -> None

    let extractAndTag (out, references, inReferenceSection, no) elem = 
        match elem with
        | Heading(_, [Literal text]) -> 
            if (Seq.exists (fun (h:string) -> CompareStrings (text.Trim()) h) ReferenceSectionTitles) then
                elem::out, references, true, no
            else
                elem::out, references, false, no
        | Paragraph(mds) when inReferenceSection -> 
            match extractTarget mds with
            | Some target -> 
                let tagged = Paragraph((Literal (sprintf "[%d] " no))::mds)
                tagged::out, Map.add target no references, inReferenceSection, no + 1
            | None -> 
                elem::out, references, inReferenceSection, no
        | _ -> elem::out, references, false, no

    let (tagged, references, _, _) = 
        Seq.fold extractAndTag ([], Map.empty, false, 1) md.Paragraphs

    MarkdownDocument(List.rev tagged, md.DefinedLinks), references

let PatchCitations references txt = 
    Map.fold (fun (txt: string) (target: string) no -> 
                let shortName = 
                    match target with
                    | ParseRegex @"/?([\w_-]+)\.\w+" [filename] -> filename.ToLower()
                    | _ -> target
                txt.Replace(sprintf @"\cite{%s}" shortName, 
                            sprintf "<a href=\"%s\">[%d]</a>" target no))
        txt references

let ExtractTitle (md: MarkdownDocument) =
    let rec extract = function
        | Heading(_, [Literal text])::rest -> Some text
        | _::rest -> extract rest
        | [] -> None
    extract md.Paragraphs

