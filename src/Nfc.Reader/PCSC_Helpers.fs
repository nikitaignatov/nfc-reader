namespace Nfc.Reader

module PCSC_Helpers = 
    open PCSC
    open PCSC.Iso7816
    open System
    
    let handleError (message : string) (sc : SCardError) = 
        if (sc <> SCardError.Success) then raise (Exception(message + (SCardHelper.StringifyError(sc))))
        else ()
    
    let CreateApdu(rfidReader : ISCardReader) = 
        new CommandApdu(IsoCase.Case2Short, rfidReader.ActiveProtocol, CLA = 0xFFuy, Instruction = InstructionCode.GetData, P1 = 0uy, P2 = 0uy, Le = 0)
    
    let transmit rfidReader = 
        let apdu = CreateApdu(rfidReader)
        let receivePci = new SCardPCI()
        let sendPci = SCardPCI.GetPci(rfidReader.ActiveProtocol)
        let receiveBuffer = Array.init 256 (fun c -> new Byte())
        let command = apdu.ToArray()
        rfidReader.Transmit(sendPci, command, receivePci, ref receiveBuffer) |> handleError "Error: "
        new ResponseApdu(receiveBuffer, IsoCase.Case2Short, rfidReader.ActiveProtocol)
    
    let toString (response : ResponseApdu) = 
        if (response.HasData) then 
            response.GetData()
            |> Array.rev
            |> Array.skipWhile (fun x -> x = 0x00uy)
            |> Array.rev
            |> BitConverter.ToString
        else raise (Exception("No uid received: " + String.Format("SW1: {0:X2}, SW2: {1:X2}\nUid: {2}", response.SW1, response.SW2)))
    
    let extractId (reader : string, factory : unit -> ISCardReader) = 
        use rfidReader = factory()
        rfidReader.Connect(reader, SCardShareMode.Shared, SCardProtocol.Any) |> handleError (sprintf "Could not connect to reader %s:\n" reader)
        rfidReader.BeginTransaction() |> handleError "Could not begin transaction."
        let responseApdu = transmit rfidReader
        rfidReader.EndTransaction(SCardReaderDisposition.Leave) |> ignore
        responseApdu |> toString

module CardReader = 
    open PCSC
    open PCSC_Helpers
    open System
    open System.Diagnostics
    
    let (|Reader|_|) (context : ISCardContext) = 
        match context.GetReaders() with
        | [| reader |] when reader <> null -> Some reader
        | [| reader; _ |] when reader <> null -> Some reader
        | _ -> None
    
    // merge the insert and remove into one stream, reduce and return the observable
    let streamMapper insert remove = 
        Observable.merge insert remove
        |> Observable.scan (fun acc item -> item acc) None
        |> Observable.choose id
    
    let connect (monitor : ISCardMonitor) (context : ISCardContext) (readerId : string) = 
        let readerCreator() = new SCardReader(context) :> ISCardReader // TODO: chec 
        let insertAction _ _ = extractId (readerId, readerCreator) |> Some
        let insert = monitor.CardInserted |> Observable.map insertAction
        let remove = monitor.CardRemoved |> Observable.map (fun _ x -> x)
        streamMapper insert remove
    
    let start (monitor : ISCardMonitor) (context : ISCardContext) (readerId : string) = 
        async { 
            monitor.Start(readerId)
            printfn "CONNECTED TO CARD READER"
            while true do
                Console.Read() |> ignore
                if (monitor.Monitoring) then monitor.Cancel()
                else monitor.Start(readerId)
        }
    
    let execute() = 
        let context = new SCardContext()
        let monitor = new SCardMonitor(ContextFactory.Instance, SCardScope.System)
        context.Establish(SCardScope.System)
        match context with
        | Reader r -> 
            let observer = connect monitor context r
            (observer), (start monitor context r)
        | _ -> raise (Exception("FAILED TO INITIALIZE READER"))
    
    let mock ci co = 
        let evI = new Event<_>()
        let evR = new Event<_>()
        let insert = evI.Publish |> Observable.map (fun a _ -> Some a)
        let remove = evR.Publish |> Observable.map (fun _ b -> b)
        let observer = streamMapper insert remove
        (observer), 
        (async { 
             printfn "CONNECTED TO MOCK READER"
             while true do
                 Console.WriteLine("Type card name to insert:")
                 ci() |> evI.Trigger
                 Console.WriteLine("Press any key to remove")
                 co() |> evR.Trigger
             ()
         })
    
    let mockWithConsole() = mock Console.ReadLine Console.ReadLine
