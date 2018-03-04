namespace GTOC

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

        member __.Task = mb.Task
        
        interface IMasterState with
            member __.AwaitCont with get () = cont and set v = cont <- v
            member __.CancellationTokenSource with get () = cts
            member __.AwaitNext () = mb.AwaitUnsafeOnCompleted(&cont.Await,&cont)

        
    and AsyncState<'r>(run:unit-> IDuo<'r>) as this =
        
        let mutable master = Unchecked.defaultof<IMasterState>
        let resultFn = Unchecked.defaultof<'r -> unit>

        let mutable task = Unchecked.defaultof<Task<'r>>

        member private __.Run = run ()
        member __.Continue = not master.CancellationTokenSource.IsCancellationRequested

        member __.RunChild<'t>(child:IDuo<'t>,cont:'t -> IDuo<'r>) =
            
            if child.Await <> Unchecked.defaultof<ICriticalNotifyCompletion> then // catches when exceptions or cancelations should stop progression
                child.StateMachine <- this // ERROR StateMachine Mismatch
                master.AwaitCont <- child
                master.AwaitNext()

        member __.AwaitDuo (v:IDuo<'r>) =
            if v.Await <> Unchecked.defaultof<ICriticalNotifyCompletion> then // catches when exceptions or cancelations should stop progression
                v.StateMachine <- this //provides access to statemachine
                master.AwaitCont <- v
                master.AwaitNext()

        member __.Result(v:'r) =
            resultFn v // result function unique for each AsyncState Machine
       

        // This is sort of hack
        member __.Task 
            with get() = 
                if Object.ReferenceEquals(null,master) then
                    let typedMaster = run () |> MasterState<'r>
                    master <- typedMaster  // Starts Master state
                    task <- typedMaster.Task                    
                task

        member __.CancellationToken with get () = master.CancellationTokenSource.Token
        member __.Cancel() = master.CancellationTokenSource.Cancel()

        // interface IAsyncStateMachine with 
        //     member __.MoveNext() = cont this
        //     member __.SetStateMachine _ = ()

    and IStateMachine<'r> =
        abstract member StateMachine : AsyncState<'r> with get, set
        
    and IAwait =
        abstract member Await : ICriticalNotifyCompletion

    and IAwaitCont =
        inherit IAwait
        inherit IAsyncStateMachine

    and IDuo<'r> =
        inherit IStateMachine<'r>
        inherit IAwaitCont
        
    and IReturno<'r> =
        inherit IBind<'r>
        inherit IAsyncStateMachine

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

    type ChldBind<'r>() =
        let mutable sm = Unchecked.defaultof<AsyncState<'r>> 
        interface IDuo< 'r> with
            member __.StateMachine with get () = sm and set v = sm <- v // 
            member __.Await = awt :> ICriticalNotifyCompletion
            member __.MoveNext() = 
                let result = awt.GetResult() |> cont
                result.Value |> sm.Result 
            member __.SetStateMachine _ = ()
        


    // Task Builder
    ////////////////////////////////////////////////                           

    type ParentInsensitiveTaskBuilder() =
        // These methods are consistent between the two builders.
        // Unfortunately, inline members do not work with inheritance.
        member inline __.Delay(f : unit -> _) = f
        member inline __.Run(f : unit -> IDuo< ^r>) : IStateMachine< ^r> = AsyncState< ^r>(f)                         :> IStateMachine< ^r>
        // member inline __.Run< ^r
        //                         when (Task< ^r>) : (member Result : ^r )
        //                         and  (TaskAwaiter< ^r>) : (member GetResult : unit -> ^r) >
        //                     (f : unit -> Task< ^r>)    : IStateMachine< ^r> = f() |> ResultMachine<'r>                    :> IStateMachine< ^r>
        member inline __.Run(f : unit -> SWrap< ^r>)          : IStateMachine< ^r> = (f()).Value |> Task.FromResult |> ResultMachine<'r> :> IStateMachine< ^r>

        member inline __.Zero() = Task.FromResult () |> ResultMachine<unit> :> IStateMachine<unit>
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
        member inline __.Bind(child:AsyncState< ^c>,continuation : ^c -> ^r) : IDuo< ^r> =
            









        // member inline x.Bind(subTask:StateMachine<'r> -> Task< ^a> , continuation : ^a -> unit) : unit =
        //     Binder<'r>.GenericAwait(subTask sm, continuation,sm)
                    
        

        // member inline __.Bind(configurableTaskLike:^abl, continuation : unit -> unit) : unit =
        //     Binder<'r>.GenericAwaitPlain    (continuation,configurableTaskLike,sm)

        // member inline __.Bind< ^abl, ^inp
        //                             when ^abl : (member GetAwaiter : unit -> TaskAwaiter< ^inp>) >
        //     (configurableTaskLike:^abl, continuation : ^inp -> 'r ) : unit =
        //     Binder<'r>.GenericAwaitResult   (continuation,configurableTaskLike,sm)

        // member inline __.Bind< ^abl, ^inp 
        //                             when ^abl : (member GetAwaiter : unit -> TaskAwaiter< ^inp>) >
        //     (configurableTaskLike:^abl, continuation : ^inp -> Task<'r> ) : unit =
        //     Binder<'r>.GenericAwaitTaskReturn(continuation,configurableTaskLike,sm)


        // member inline this.Bind(subTask:StateMachine<'r> -> ^b , continuation : ^a -> unit) : unit =
        //     Binder<'r>.GenericAwait(subTask sm, continuation,sm)
    // type ChildState<'r,'a> =
    //     struct 
    //         val State : StateMachine<'r>
    //         val Final : 'a -> unit
    //     end
    //     new(ism : StateMachine<'r>,final:'a -> unit) = { State = ism ; Final = final}        

    // type ChildTaskBuilder(pstate:ChildState<'r,'a>) =
    //     // These methods are consistent between the two builders.
    //     // Unfortunately, inline members do not work with inheritance.
    //     let sm = pstate.State
    //     let final = pstate.Final
    //     member inline __.Delay(f : unit -> _) = f
    //     member inline __.Run(f : unit -> unit) = f()
    //     member inline __.Run (f : unit -> Task<'a>) = 
    //         let t = f()
    //         sm.Await(t.GetAwaiter(),final)

    //     member inline __.Run(f : unit -> 'a) = final(f())

    //     member inline __.Zero() = zero
    //     member inline __.Return(x:'r) = x
    //     member inline __.ReturnFrom(task : 'r Task) = task

    //      member inline __.Bind(task, continuation) : unit =
    //         Bind.Invoke task continuation sm


    
// let inline ptask () = TaskBuilder.ParentInsensitiveTaskBuilder<_>()

// let t = Task.Factory.StartNew(fun () -> ())
module testing = 

    let task = TaskBuilder.ParentInsensitiveTaskBuilder()
    let work1 = task {
            let t = Task.FromResult 2
            let! a =  t //Task.Factory.StartNew(fun () -> ())
            let b = a + 1
            let tb = Task.FromResult b 
            return! tb
        }
    
    
    let work2 = task {
            let str = "vatsd"
            let! a = Task.FromResult str //Task.Factory.StartNew(fun () -> ())
            do! Task.Factory.StartNew(fun () -> ())
            //let b = a + 1
            return "string"
        }

