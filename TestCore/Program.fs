
open MDPreprocessor

[<EntryPoint>]
let main argv = 
    for file in Tools.AllFilesInPath @"\\brml.tum.de\dfs\nthome\surban\KnowHow\" do
        printfn "%s" file

    0
//    let txt = Handler.ReadFile @"\\brml.tum.de\dfs\nthome\surban\KnowHow\GPLVM.md"
//    let pmd = PraseMDText txt
//    printfn "parsed: %A" pmd
//    let gtxt = OutputMDText pmd
//    printfn "generated: %s" gtxt
//    0




