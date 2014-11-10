
open MDPreprocessor

[<EntryPoint>]
let main argv = 
    let txt = Handler.ReadFile @"\\brml.tum.de\dfs\nthome\surban\KnowHow\GPLVM.md"
    let pmd = PraseMDText txt
    printfn "parsed: %A" pmd
    let gtxt = OutputMDText pmd
    printfn "generated: %s" gtxt
    0
