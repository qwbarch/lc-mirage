module Predictor.FileHandler
open Predictor.Domain
open System.IO
open System
open Utilities
open FSharp.Json
open System.Collections.Generic
open Config
open Mirage.Utilities.AtomicFile
open FSharpPlus

let toCompressedObsFileFormat (obs: CompressedObservation) : CompressedObservationFileFormat =
    {   time = obs.time
        spokeEmbedding = obs.spokeEmbedding
        heardEmbedding = obs.heardEmbedding
        lastSpoke = obs.lastSpoke
        lastHeard = obs.lastHeard
    }

let fromCompressedObsFileFormat (obs: CompressedObservationFileFormat) : CompressedObservation =
    {   time = obs.time
        spokeEmbedding = obs.spokeEmbedding
        heardEmbedding = obs.heardEmbedding
        lastSpoke = obs.lastSpoke
        lastHeard = obs.lastHeard
    }

let createDirIfDoesNotExist (par: string) (dir: string) =
    let fullPath = Path.Combine(par, dir)
    try 
        let _ = Directory.CreateDirectory fullPath
        ()
    with
    | ex -> logError $"Could not create directory {ex}"

let readStoredPolicy 
    (dir: string)
    (logWarning: string -> unit) = async {
    do! removeTempFiles dir logInfo logWarning

    let dateToFileInfo = Dictionary()
    let fileToData = Dictionary()
    let filesSet = SortedSet()
    let mutable counter = 0
    let add (fileName: string) (asFileFormat: FileFormat) =
        let fileInfo : Domain.FileInfo =
            {   creationDate = asFileFormat.creationDate
                name = fileName
            }
        for (observation, _) in asFileFormat.data do
            dateToFileInfo.Add(observation.time, fileInfo)
            counter <- counter + 1
        fileToData.Add(fileName, asFileFormat.data)
        let _ = filesSet.Add(fileInfo)
        ()

    try
        let files = Directory.GetFiles(dir)
        for file in files do
            let extension = Path.GetExtension(file)
            if extension = ".json" then
                let fullPath = Path.Combine(dir, file)
                use fileReader = new StreamReader(fullPath)
                let! contents = Async.AwaitTask <| fileReader.ReadToEndAsync()
                fileReader.Close()
                let asFileFormatOption = 
                    try
                        Some <| Json.deserialize<FileFormat>(contents)
                    with
                    | ex -> 
                        logWarning <| "Unable to parse '" + file.ToString() + "'. Deleting..."
                        try 
                            File.Delete fullPath
                        with
                        | deleteEx -> logWarning $"Could not delete '{file.ToString()}'. {deleteEx.Message}"

                        None
                match asFileFormatOption with
                | None -> ()
                | Some asFileFormat -> add file asFileFormat
                ()
    with
    | anyex -> logError <| "Something went wrong while reading stored files: " + anyex.ToString()


    logInfo <| "Read " + counter.ToString() + " data points"
    return {
        dateToFileInfo = dateToFileInfo
        fileToData = fileToData
        files = filesSet
    }
}

let toCompressedObs (observation: Observation) (context: (CompressedObservationFileFormat * FutureAction) array) : CompressedObservation =
    let isNotPrev (x : ObsEmbedding) =
        match x with
        | Prev -> false
        | Value _ -> true

    let unObsEmbedding (x: ObsEmbedding option) : Option<string * TextEmbedding> option =
        match x with
        | None -> None
        | Some obsEmbedding ->
            match obsEmbedding with
            | Prev -> None
            | Value res -> Some res

    let lastSpokeEncoding = 
        unObsEmbedding << map (fun x -> (fst x).spokeEmbedding) 
            <| Array.tryFindBack (fun (obs, _) -> isNotPrev obs.spokeEmbedding) context
    let lastHeardEncoding = 
        unObsEmbedding << map (fun x -> (fst x).heardEmbedding) 
            <| Array.tryFindBack (fun (obs, _) -> isNotPrev obs.heardEmbedding) context
    {   time = observation.time
        spokeEmbedding = toObsEmbedding lastSpokeEncoding observation.spokeEmbedding
        heardEmbedding = toObsEmbedding lastHeardEncoding observation.heardEmbedding
        lastSpoke = observation.lastSpoke
        lastHeard = observation.lastHeard
    }

let createFileHandler 
    (fileState: FileState)
    (dir: string)
    (sizeLimit: int64) =
    MailboxProcessor<FileMessage>.Start(fun inbox ->
        let rec loop () =
            async {
                let! message = inbox.Receive()
                try
                    match message with
                    | Update (obsTime: DateTime, newAction: FutureAction) ->
                        if fileState.dateToFileInfo.ContainsKey(obsTime) then
                            // Update a file
                            let fileInfo = fileState.dateToFileInfo[obsTime]
                            let prevData = fileState.fileToData[fileInfo.name]
                            let newData = Array.copy prevData
                            let obsIndex = Array.findIndex (fun (obs, _) -> obs.time = obsTime) prevData
                            let (existingObs, _) = prevData.[obsIndex]
                            newData.[obsIndex] <- (existingObs, newAction)

                            let newFileData = {
                                creationDate = fileInfo.creationDate
                                data = newData
                            }
                            let newFileString = Json.serialize(newFileData)
                            let path = Path.Combine(dir, fileInfo.name)

                            let! _ = atomicFileWrite path newFileString true logInfo logError

                            let _ = fileState.fileToData.Remove(fileInfo.name)
                            let _ = fileState.fileToData.TryAdd(fileInfo.name, newFileData.data)
                            ()
                        ()
                    | Add (observation, futureAction) ->
                        if fileState.files.Count = 0 || fileState.fileToData[fileState.files.Max.name].Length >= config.FILE_SPLIT_SIZE then
                            // Create a new file
                            let now = DateTime.Now
                            let newFileName = now.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString() + ".json"
                            let newFileData = {
                                creationDate = now
                                data = Array.init 1 (fun _ -> (toCompressedObsFileFormat <| toCompressedObs observation Array.empty, futureAction))
                            }
                            let newFileInfo = {
                                creationDate = now
                                name = newFileName
                            }
                            let newFileString = Json.serialize newFileData
                            let path = Path.Combine(dir, newFileName)

                            let! _ = atomicFileWrite path newFileString true logInfo logError

                            let _ = fileState.files.Add(newFileInfo)
                            let _ = fileState.fileToData.TryAdd(newFileName, newFileData.data)
                            let _ = fileState.dateToFileInfo.TryAdd(observation.time, newFileInfo)
                            ()
                        else
                            // Append to the latest file
                            let fileInfo = fileState.files.Max
                            let prevData = fileState.fileToData[fileInfo.name]
                            let newObs = toCompressedObsFileFormat <| toCompressedObs observation prevData
                            let newFileData = {
                                creationDate = fileInfo.creationDate
                                data = Array.append prevData [|(newObs, futureAction)|]
                            }
                            let newFileString = Json.serialize newFileData
                            let path = Path.Combine(dir, fileInfo.name)
                            let! _ = atomicFileWrite path  newFileString true logInfo logError

                            let _ = fileState.fileToData.Remove(fileInfo.name)
                            let _ = fileState.fileToData.TryAdd(fileInfo.name, newFileData.data)

                            let _ = fileState.dateToFileInfo.TryAdd(observation.time, fileInfo)
                            ()
                with
                | ex -> logError $"Something went wrong in the file handler {ex.ToString()}"
                do! loop()
            }
        loop()
    )