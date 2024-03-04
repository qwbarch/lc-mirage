module Mirage.Utilities.Print
open Lock

let printSeq (v: 'T seq) =
    let result = "[" + String.concat ", " (Seq.map (fun x -> x.ToString()) v) + "]"
    printfn "%s" result

// printf does not synchronize; simultaneous calls will have garbled text.
// Here is a helper print function that does synchronize.
let private _prlock = createLock()
let pr s : unit =
    Async.RunSynchronously <| async {
        use! ctx = withLock _prlock
        printf s
    }
