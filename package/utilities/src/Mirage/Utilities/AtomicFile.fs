module Mirage.Utilities.AtomicFile
open System.IO
open System

// Warning: "the temporary files have a .temp extension". The cleanup code will delete every file with a .temp extension

// Write to a temporary file, then rename that temporary file
// Requires that the directory to the file must exist
// If retry is set to true, it will keep trying the operation with waits of length 1 second, 2, 4, 8, etc
// Otherwise if retry is set to false, it will return false upon failure.
let atomicFileWrite
    (destPath: string)
    (contents: string)
    (retry: bool)
    (logInfo: string -> unit)
    (logError: string -> unit) =
        let rec loop (wait: int) : Async<bool> = 
            async {
                let dir = Path.GetDirectoryName(destPath)
                let tempName = Guid.NewGuid().ToString() + ".temp"
                let tempPath = Path.Combine(dir, tempName)

                try
                    use tempWriter = new StreamWriter(tempPath)
                    do! Async.AwaitTask(tempWriter.WriteLineAsync(contents))
                    tempWriter.Close()
                    File.Copy(tempPath, destPath, true)
                    File.Delete(tempPath)
                    return true
                with
                | ex -> 
                    logError <| "Could not perform a file write. " + ex.Message
                    if retry then
                        logInfo <| "File write failed. Retrying in " + wait.ToString() + " ms"
                        do! Async.Sleep wait
                        return! loop(wait * 2)
                    else
                        return false
            }
        loop(1000)

// Remove all files in the directory with a .temp extension
let removeTempFiles 
    (dir: string)
    (logInfo: string -> unit)
    (logError: string -> unit) =
    async {
        try
            let files = Directory.GetFiles(dir)
            let mutable counter = 0
            for file in files do
                let extension = Path.GetExtension(file)
                if extension = ".temp" then
                    File.Delete(file)
                    counter <- counter + 1
            if counter > 0 then
                logInfo $"Removed {counter} temp files"
        with
        | ex -> logError <| "Something went wrong while removing temp files. " + ex.Message
    }