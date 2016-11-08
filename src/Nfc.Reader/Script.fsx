#load "PCSC_Helpers.fs"

open Nfc.Reader.CardReader
open System

// mock the card reader, in order to be able to simulate cardid from command line input.
let obs, task = mock Console.ReadLine Console.ReadLine

obs.Subscribe(fun c -> printfn "INSERTED: %A" c)
Async.RunSynchronously task

// TODO:  connect the card reader to the device
// then run execte inorder to be notified about checkins.
let obs2, task2 = start()

obs2.Subscribe(fun c -> printfn "PRINT: %A" c)
Async.RunSynchronously task2