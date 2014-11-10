namespace UnitTestProject1

open System
open Microsoft.VisualStudio.TestTools.UnitTesting

open MDPreprocessor

[<TestClass>]
type UnitTest() = 
    [<TestMethod>]
    member x.TestParse () = 
        let txt = Handler.ReadFile @"C:\Local\surban\dev\knowhow\test\SVM.md"
        let pmd = ParseMDText txt
        printf "%A" pmd
        ()

