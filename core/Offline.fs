module Offline

open System.IO
open System.Text

open PathTools

let MathJaxItems = ["/MathJax/MathJax.js?config=TeX-AMS_HTML"]
let MathJaxRevision = "2.4-beta-2"

let OfflineManifest requestPath =
    let contentDir, _ = SplitVirtualContentPath requestPath
    let virtualContentRoot = VirtualContentRoot requestPath

    let contentFiles = AllFilesInPath contentDir
    let supportVirtualFiles = ["/Scripts"; "/Web"] |> Seq.collect AllFilesInVirtualPath 
    let supportFiles = supportVirtualFiles |> Seq.map VirtualToPhysical
    let mathJaxVirtualFiles = AllFilesInVirtualPath "/MathJax"
    let mathJaxFiles = mathJaxVirtualFiles |> Seq.map VirtualToPhysical

    let sb = StringBuilder()
    let write txt = sb.AppendLine(txt) |> ignore

    write "CACHE MANIFEST"
    write ""

    let lastMTime = 
        Seq.concat [contentFiles; supportFiles; mathJaxFiles] 
        |> Seq.fold (fun s f -> max s (File.GetLastWriteTimeUtc(f).Ticks)) 0L
    write (sprintf "# last modification: %d" lastMTime)
    write ""

    write "CACHE:"
    contentFiles
        |> Seq.iter (fun f -> let fshort = if Path.GetExtension(f) = ".md" then 
                                                Path.ChangeExtension(f, null) 
                                            else f
                              write (ToForwardSlashes fshort))
    write ""

    supportVirtualFiles
        |> Seq.iter (fun f -> write f)
    write ""

    mathJaxVirtualFiles
        |> Seq.iter (fun f -> write f
                              write (f + "?rev=" + MathJaxRevision))
    MathJaxItems
        |> Seq.iter (fun f -> write f)
    write ""

    write "NETWORK:"
    write "preload/"
    write "*"
    write ""

    write "FALLBACK:"
    write (sprintf "%s/ %s/Contents" virtualContentRoot virtualContentRoot)

    sb.ToString()