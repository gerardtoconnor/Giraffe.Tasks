
open System
open System.Threading.Tasks
open System.Runtime.CompilerServices
open System.Threading


[<Obsolete>]
module TaskBuilder =
        
    type IMasterState =
        abstract AwaitCont : IAwaitCont with get, set
        abstract CancellationTokenSource : CancellationTokenSource
        abstract AwaitNext : unit -> unit
    and DoNothing() =
        interface IAsyncStateMachine with
            member __.MoveNext () = ()
            member __.SetStateMachine _ = ()
    
    and MasterState<'f>(run:IAwaitCont) =
        let mb = AsyncTaskMethodBuilder<'f>()
        let mutable cont = run
        let cts = new CancellationTokenSource()

        do
            let mutable doNothing = DoNothing() // HACK, we compute the first run/movenext without using interface
            mb.Start(&doNothing)
            mb.AwaitUnsafeOnCompleted(&cont.Await,&cont)

        member __.SetResult = mb.SetResult        

        member __.Task = mb.Task
        
        interface IMasterState with
            member __.AwaitCont with get () = cont and set v = cont <- v
            member __.CancellationTokenSource with get () = cts
            member __.AwaitNext () = mb.AwaitUnsafeOnCompleted(&cont.Await,&cont)

        
    and AsyncState<'r>(run:unit-> IDuo<'r>) as this =
        
        let mutable master = Unchecked.defaultof<IMasterState>
        let mutable resultFn = Unchecked.defaultof<'r -> unit>

        let mutable task = Unchecked.defaultof<Task<'r>> // HACK to only set once

        member __.Run = run ()
        member __.Continue = not master.CancellationTokenSource.IsCancellationRequested

        member __.MasterState with get () = master and set v = master <- v

        member __.SetResultFn(fn: 'r -> unit) = resultFn <- fn


        // member __.RunChild<'t>(cont:'t -> IDuo<'r>) = 
            // if child.Await <> Unchecked.defaultof<ICriticalNotifyCompletion> then // catches when exceptions or cancelations should stop progression
            //     child.StateMachine <- this // ERROR StateMachine Mismatch
            //     master.AwaitCont <- child
            //     master.AwaitNext()

        /// Sets statemachine then awaits
        member __.AwaitDuo (v:IDuo<'r>) =
            if v.Await <> Unchecked.defaultof<ICriticalNotifyCompletion> then // catches when exceptions or cancelations should stop progression
                v.StateMachine <- this //provides access to statemachine
                master.AwaitCont <- v
                master.AwaitNext()

        member __.Result(v:'r) =
            resultFn v // result function unique for each AsyncState Machine

        member __.SetTask t = task <- t       
       
        // This is sort of hack
        member __.Task 
            with get() =
                if Object.ReferenceEquals(null,task) then
                    if Object.ReferenceEquals(null,master) then
                        let init = run ()
                        init.StateMachine <- this
                        let typedMaster = init |> MasterState<'r>
                        master <- typedMaster  // Starts Master state
                        task <- typedMaster.Task
                        resultFn <- typedMaster.SetResult                   
                task

        member __.CancellationToken 
            with get () = master.CancellationTokenSource.Token
        member __.Cancel() = master.CancellationTokenSource.Cancel()

        // interface IAsyncStateMachine with 
        //     member __.MoveNext() = cont this
        //     member __.SetStateMachine _ = ()

    and IStateMachine<'r> =
        abstract member StateMachine : AsyncState<'r> with get, set

    and IAsyncState<'r> = 
        abstract member Task : Task<'r> with get
        abstract member CancellationToken : CancellationToken with get
        abstract member Cancel : unit -> unit
        
    and IAwait =
        abstract member Await : ICriticalNotifyCompletion

    and IAwaitCont =
        inherit IAwait
        inherit IAsyncStateMachine

    and IDuo<'r> =
        inherit IStateMachine<'r>
        inherit IAwaitCont

    and ResultState<'r>(tv:Task<'r>) =
        interface IAsyncState<'r> with
            member __.Task = tv
            member __.CancellationToken = CancellationToken.None
            member __.Cancel () = ()
        new (sv:SWrap<'r>) = ResultState<'r>(sv.Value |> Task.FromResult)

    // Hack wrapper to help inference differncitate result from Task<result>
    and SWrap<'r> =
        struct
            val Value : 'r
        end
        new (v) = {Value = v}


    let zero = Task.FromResult ()

    // Explicit Continuation Containers
    /////////////////////////////////////
    
    type ContGen<'t,'r>(awt:TaskAwaiter<'t>,cont:'t -> IDuo< 'r>) =
        let mutable sm = Unchecked.defaultof<AsyncState<'r>> 
        interface IDuo< 'r> with
            member __.StateMachine with get () = sm and set v = sm <- v // 
            member __.Await = awt :> ICriticalNotifyCompletion
            member __.MoveNext() = awt.GetResult() |> cont |> sm.AwaitDuo /// get next awaiter using this result, duo setter sets statemachine
            member __.SetStateMachine _ = ()

    type ContPln<'r>(awt:TaskAwaiter,cont:unit -> IDuo< 'r>) =
        let mutable sm = Unchecked.defaultof<AsyncState<'r>> 
        interface IDuo< 'r> with
            member __.StateMachine with get () = sm and set v = sm <- v // 
            member __.Await = awt :> ICriticalNotifyCompletion
            member __.MoveNext() =  cont () |> sm.AwaitDuo        /// get next awaiter using this result, duo setter sets statemachine
            member __.SetStateMachine _ = ()

    type RsltGen<'t,'r>(awt:TaskAwaiter<'t>,cont:'t -> SWrap< 'r>) = 
        let mutable sm = Unchecked.defaultof<AsyncState<'r>> 
        interface IDuo< 'r> with
            member __.StateMachine with get () = sm and set v = sm <- v // 
            member __.Await = awt :> ICriticalNotifyCompletion
            member __.MoveNext() = 
                let result = awt.GetResult() |> cont
                result.Value |> sm.Result 
            member __.SetStateMachine _ = ()

    
    type RsltPln<'r>(awt:TaskAwaiter,cont:unit -> SWrap< 'r>) = 
        let mutable sm = Unchecked.defaultof<AsyncState<'r>> 
        interface IDuo< 'r> with
            member __.StateMachine with get () = sm and set v = sm <- v // 
            member __.Await = awt :> ICriticalNotifyCompletion
            member __.MoveNext() = 
                let result = cont ()
                result.Value |> sm.Result 
            member __.SetStateMachine _ = ()        

    type ContFrom<'r>(awt:TaskAwaiter<'r>) = 
        let mutable sm = Unchecked.defaultof<AsyncState<'r>> 
        interface IDuo< 'r> with
            member __.StateMachine with get () = sm and set v = sm <- v // 
            member __.Await = awt :> ICriticalNotifyCompletion
            member __.MoveNext() = awt.GetResult() |> sm.Result 
            member __.SetStateMachine _ = ()

    type ContStop<'r>() =
        interface IDuo< 'r> with
            member __.StateMachine with get () = Unchecked.defaultof<_> and set _ = () // 
            member __.Await = Unchecked.defaultof<_>
            member __.MoveNext() = ()
            member __.SetStateMachine _ = ()

    type ChldBind<'r,'c>(csm:AsyncState<'c>,cont:'c -> IDuo<'r>) =
        let mutable psm = Unchecked.defaultof<AsyncState<'r>>
        let run = csm.Run
        do run.StateMachine <-  csm
        interface IDuo< 'r> with
            member __.StateMachine 
                with get () = psm 
                and set v =
                    psm <- v
                    csm.MasterState <- psm.MasterState
                    csm.SetResultFn (fun crv ->
                        let result = cont crv
                        psm.AwaitDuo result
                    ) 
            member __.Await = run.Await // typeless representing child 'c 
            member __.MoveNext() = run.MoveNext()
            member __.SetStateMachine _ = ()

    
    type ChldRslt<'r,'c>(csm:AsyncState<'c>,cont:'c ->SWrap<'r>)=
        let mutable sm = Unchecked.defaultof<AsyncState<'r>>
        let run = csm.Run
        do run.StateMachine <-  csm
        interface IDuo< 'r> with
            member __.StateMachine 
                with get () = sm 
                and set v =
                    sm <- v
                    csm.SetResultFn (fun crv ->
                        let result = cont crv
                        v.Result result.Value
                    ) 
                    csm.MasterState <- v.MasterState // 
            member __.Await = run.Await // typeless representing child 'c 
            member __.MoveNext() = run.MoveNext()
            member __.SetStateMachine _ = ()    
    
    // Cancelable 
    //////////////////

    type CancContGen<'t,'r>(fn:CancellationToken -> Task<'t>,cont:'t -> IDuo< 'r>) =
        let mutable sm = Unchecked.defaultof<AsyncState<'r>>
        let mutable awt = Unchecked.defaultof<TaskAwaiter<'t>>
        interface IDuo< 'r> with
            member __.StateMachine 
                with get () = sm 
                and set v =     // statemachine set before provided to awaiter & movenext
                    let t = fn v.CancellationToken
                    awt <- t.GetAwaiter()
                    sm <- v // 
            member __.Await = awt :> ICriticalNotifyCompletion
            member __.MoveNext() = awt.GetResult() |> cont |> sm.AwaitDuo /// get next awaiter using this result, duo setter sets statemachine
            member __.SetStateMachine _ = ()

    type CancContPln<'r>(fn:CancellationToken -> Task,cont:unit -> IDuo< 'r>) =
        let mutable sm = Unchecked.defaultof<AsyncState<'r>>
        let mutable awt = Unchecked.defaultof<TaskAwaiter>
        interface IDuo< 'r> with
            member __.StateMachine 
                with get () = sm 
                and set v = 
                    let t = fn v.CancellationToken
                    awt <- t.GetAwaiter()
                    sm <- v // 
            member __.Await =  awt :> ICriticalNotifyCompletion
            member __.MoveNext() = cont () |> sm.AwaitDuo /// get next awaiter using this result, duo setter sets statemachine
            member __.SetStateMachine _ = ()        

    type CancRsltGen<'t,'r>(fn:CancellationToken -> Task<'t>,cont:'t -> SWrap< 'r>) =
        let mutable sm = Unchecked.defaultof<AsyncState<'r>>
        let mutable awt = Unchecked.defaultof<TaskAwaiter<'t>>
        interface IDuo< 'r> with
            member __.StateMachine 
                with get () = sm 
                and set v = 
                    let t = fn v.CancellationToken
                    awt <- t.GetAwaiter()
                    sm <- v // 
            member __.Await =  awt :> ICriticalNotifyCompletion
            member __.MoveNext() = 
                let wrapped = awt.GetResult() |> cont 
                wrapped.Value |> sm.Result /// get next awaiter using this result, duo setter sets statemachine
            member __.SetStateMachine _ = ()

    type CancRsltPln<'r>(fn:CancellationToken -> Task,cont:unit -> SWrap< 'r>) =
        let mutable sm = Unchecked.defaultof<AsyncState<'r>>
        let mutable awt = Unchecked.defaultof<TaskAwaiter>
        interface IDuo< 'r> with
            member __.StateMachine 
                with get () = sm 
                and set v = 
                    let t = fn v.CancellationToken
                    awt <- t.GetAwaiter()
                    sm <- v //
            member __.Await =  awt :> ICriticalNotifyCompletion
            member __.MoveNext() = 
                let wrapped = cont () 
                wrapped.Value |> sm.Result /// get next awaiter using this result, duo setter sets statemachine
            member __.SetStateMachine _ = ()        


    // Task Builder
    ////////////////////////////////////////////////                           

    // type Binder< 'r> =
    //     static member Await1(smr:byref<StateMachine<'r>>,awt:byref<TaskAwaiter<'a>>,cont:'a -> unit) =
    //         let sm = &smr in sm.getMethodBuilder.AwaitUnsafeOnCompleted(&awt,&sm)

    type ParentInsensitiveTaskBuilder() =
        // These methods are consistent between the two builders.
        // Unfortunately, inline members do not work with inheritance.
        member inline __.Delay(f : unit -> _) = f
        member inline __.Run(f : unit -> IDuo< ^r>) : AsyncState< ^r> = AsyncState< ^r>(f)
        
        // member inline __.Run< ^r
        //                         when (Task< ^r>) : (member Result : ^r )
        //                         and  (TaskAwaiter< ^r>) : (member GetResult : unit -> ^r) >
        //                     (f : unit -> Task< ^r>)    : IStateMachine< ^r> = f() |> ResultMachine<'r>                    :> IStateMachine< ^r>
        
        member inline __.Run(f : unit -> SWrap< ^r>) : AsyncState< ^r> = 
            AsyncState< ^r>((fun () -> RsltPln(Task.CompletedTask.GetAwaiter(),f) :> IDuo< ^r> ))  // << HACK, needs better implimentationn

        member inline __.Zero() : IAsyncState<unit> = ResultState<unit>(Task.FromResult () ) :> IAsyncState<unit>
        member inline __.Return(v: 'r) = SWrap(v) // x
        
        // member inline __.ReturnFrom< ^r
        //                         when (Task< ^r>) : (member Result : ^r )
        //                         and  (TaskAwaiter< ^r>) : (member GetResult : unit -> ^r) >
        //                     (task : ^r Task) = task
                                
        member inline __.ReturnFrom< ^r
                                when (Task< ^r>) : (member Result : ^r )
                                and  (TaskAwaiter< ^r>) : (member GetResult : unit -> ^r) >
                            (task : ^r Task) : IDuo< ^r> =
                            let awt = task.GetAwaiter()
                            ContFrom< ^r>(awt) :> IDuo<'r>

        member inline __.Bind<  ^inp, ^r 
                                    when (Task< ^inp>) : (member Result : ^inp )
                                    and  (TaskAwaiter< ^inp>) : (member GetResult : unit -> ^inp) >
                (abl: Task< ^inp>, continuation : ^inp -> IDuo< ^r>) : IDuo< ^r> =
                let awt = abl.GetAwaiter() 
                if awt.IsCompleted then
                    awt.GetResult() |> continuation
                elif abl.IsCanceled || abl.IsCanceled then
                    ContStop< ^r>() :> IDuo< ^r>
                else
                    ContGen< ^inp, ^r>(awt,continuation) :> IDuo<'r>

        member inline __.Bind< ^inp, ^r 
                                    when (Task< ^inp>) : (member Result : ^inp )
                                    and  (TaskAwaiter< ^inp>) : (member GetResult : unit -> ^inp) >
                (abl: Task< ^inp>, continuation : ^inp -> SWrap< ^r>) : IDuo< ^r> =
                let awt = abl.GetAwaiter()
                RsltGen< ^inp, ^r>(awt,continuation) :> IDuo<'r>

        /// Plain Task Overloads
        //////////////////////////////////////

        member inline __.Bind(abl: Task, continuation : unit -> IDuo< ^r>) : IDuo< ^r> =
                let awt = abl.GetAwaiter() 
                if awt.IsCompleted then
                    continuation ()
                else
                    ContPln< ^r>(awt,continuation) :> IDuo<'r>
                    
        member inline __.Bind(abl: Task, continuation : unit -> SWrap< ^r>) : IDuo< ^r> =
                let awt = abl.GetAwaiter()
                RsltPln< ^r>(awt,continuation) :> IDuo<'r>

        // Child Executions
        member inline __.Bind(child:AsyncState< ^c>,continuation : ^c -> IDuo< ^r>) : IDuo< ^r> =
            ChldBind< ^r, ^c>(child, continuation) :> IDuo<'r>


        member inline __.Bind(child:AsyncState< ^c>,continuation : ^c -> SWrap< ^r>) : IDuo< ^r> =
            ChldRslt< ^r, ^c>(child, continuation) :> IDuo<'r>

        // Cancelations Token passing

        member inline __.Bind<  ^inp, ^r 
                                    when (Task< ^inp>) : (member Result : ^inp )
                                    and  (TaskAwaiter< ^inp>) : (member GetResult : unit -> ^inp) >
            (cancelFn:CancellationToken -> Task< ^inp>, continuation : ^inp -> IDuo< ^r>) : IDuo< ^r> =
            CancContGen< ^inp, ^r>(cancelFn,continuation) :> IDuo<'r>

        member inline __.Bind(cancelFn:CancellationToken -> Task,continuation : unit -> IDuo< ^r>) : IDuo< ^r> =
            CancContPln< ^r>(cancelFn, continuation) :> IDuo<'r>
        
        member inline __.Bind<  ^inp, ^r 
                                    when (Task< ^inp>) : (member Result : ^inp )
                                    and  (TaskAwaiter< ^inp>) : (member GetResult : unit -> ^inp) >
            (cancelFn:CancellationToken -> Task< ^inp>, continuation : ^inp -> SWrap< ^r>) : IDuo< ^r> =
            CancRsltGen< ^inp, ^r>(cancelFn,continuation) :> IDuo<'r>

        member inline __.Bind(cancelFn:CancellationToken -> Task,continuation : unit -> SWrap< ^r>) : IDuo< ^r> =
            CancRsltPln< ^r>(cancelFn, continuation) :> IDuo<'r>     





let task = TaskBuilder.ParentInsensitiveTaskBuilder()
// let work1 = task {
//         let t = Task.FromResult 2
//         let! a =  t //Task.Factory.StartNew(fun () -> ())
//         let b = a + 1
//         let tb = Task.FromResult b 
//         return! tb
//     }


let work2 = task { 
        let str = "vatsd"
        let! a = Task.FromResult str //Task.Factory.StartNew(fun () -> ())
        printfn "a:%s" a
        //do! Task.Factory.StartNew(fun () -> ())
        printfn "plain task executed"
        let! child = task {
            let! ca = Task.FromResult 64356
            return ca
        }
        printfn "child:%i" child
        //let! cancelableTaskString = (fun ct -> Task.FromResult "abc")  
        //let b = a + 1
        //printfn "cancel fn %s" cancelableTaskString
        return "finsihed" //cancelableTaskString
}

work2.Task.ContinueWith<_>(fun (_ : Task) -> printfn "work2: %s" work2.Task.Result)

