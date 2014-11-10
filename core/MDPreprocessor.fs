module MDPreprocessor

open System.Text.RegularExpressions

type MathElement =
    | MathText of string
    | MathEnvironment of string * (MathElement list)

type TextElement =
    | Text of string
    | Citation of string
    | EQRef of string
    | InlineMath of (MathElement list)
    | MathBlock of MathElement
    | MathParagraph of string 

type MDText = TextElement list
    
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
        | pos -> Some (txt.[0..pos], txt.[pos+1..])

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
    | FirstChars "\n$$$" following ->
        let mp, rest =
            match following with
            | FirstLine (fl, rest) when fl.Trim().Length = 0 -> FirstParagraph rest
            | FirstLine (fl, rest) -> fl, rest
            | _ -> following, ""
        Some (MathParagraph mp, rest)
    | _ -> None

let (|ParseRegex|_|) regex str =
   let m = Regex(regex, RegexOptions.Singleline).Match(str)
   if m.Success
   then Some (List.tail [ for x in m.Groups -> x.Value ])
   else None

let (|MathBeginBlockText|_|) (txt: string) =
    match txt with
    | ParseRegex @"^\\begin{([\w*]+)}(.*)" [env; rest] -> Some (env, rest)
    | FirstChars @"\[" rest -> Some ("[", rest)
    | _ -> None
    
let (|MathEndBlockText|_|) (txt: string) =
    match txt with
    | ParseRegex @"^\\end{([\w*]+)}(.*)" [env; rest] -> Some (env, rest)
    | FirstChars @"\]" rest -> Some ("[", rest)
    | FirstChars @"$" rest -> Some ("$", rest)
    | _ -> None

let NextCandidate (txt: string) =
     match txt.IndexOfAny([|'\\'; '$'; '\n'|]) with
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
        MathText(txt.[0..e-1])::rest, followingTxt
          

let ParseMDText (txt: string) =
    let rec parse (mdt: MDText) (txt: string) =
        match txt with
        | MathParagraphText(mp, rest) ->
            parse (mp::mdt) rest
        | MathBeginBlockText(env, contentTxt) -> 
            let content, rest = ParseMath env contentTxt
            parse (MathBlock(MathEnvironment(env, content))::mdt) rest
        | FirstChars "$" contentTxt ->
            let content, rest = ParseMath "$" contentTxt
            parse (InlineMath(content)::mdt) rest
        | ParseRegex @"^\\cite{([\w_:-]+)}(.*)" [target; rest] ->
            parse (Citation(target)::mdt) rest
        | ParseRegex @"^\\eqref{([\w_:-]+)}(.*)" [target; rest] ->
            parse (EQRef(target)::mdt) rest
        | _ when txt.Length = 0 ->
            List.rev mdt
        | _ ->
            let e = NextCandidate txt
            parse (Text(txt.[0..e-1])::mdt) txt.[e..]
    parse [] txt
            
let rec OutputMathElement (me: MathElement) =
    match me with
    | MathText txt -> txt.Replace("\n", " ")
    | MathEnvironment(env, content) ->
        let outenv = if env = "[" then "align*" else env
        sprintf "\\begin{%s} %s \\end{%s}" outenv (OutputMathElements content) outenv

and OutputMathElements (mes: MathElement list) =
    List.map OutputMathElement mes |> String.concat ""
    
let OutputTextElement (te: TextElement) =
    match te with
    | Text txt -> txt
    | Citation target -> sprintf @"\cite{%s}" target
    | EQRef target -> sprintf @"$\eqref{%s}$" target
    | InlineMath me -> sprintf @"$%s$" (OutputMathElements me)
    | MathBlock me -> sprintf "\n\n$$$\n%s\n\n" (OutputMathElement me)
    | MathParagraph txt -> sprintf "\n\n$$$\n%s\n\n" txt
        
let OutputMDText (md: MDText) =
    List.map OutputTextElement md |> String.concat ""