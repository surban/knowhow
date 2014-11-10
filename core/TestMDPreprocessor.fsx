#r "bin\Debug\core.dll"

open MDPreprocessor

let txt = Handler.ReadFile @"C:\Local\surban\dev\knowhow\test\SVM.md"

let pmd = ParseMDText txt
