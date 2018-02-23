namespace GTOC

open System
open System.Threading.Tasks
open System.Runtime.CompilerServices
open System.Threading


[<Obsolete>]
module TaskBuilder =
                 

    [<Struct>]
    type TypedGenericStateAwaiter<'T,'r >(awaiter:TaskAwaiter<'T> ,continuation:'T -> unit,methodBuilder:AsyncTaskMethodBuilder<'r>) =
        interface IAsyncStateMachine with
            member __.MoveNext() =
                    continuation ( awaiter.GetResult() )   // runs cont, will update awaiter & continuation
            member __.SetStateMachine sm = methodBuilder.SetStateMachine sm 
    
    [<Struct>]
    type TypedPlainStateAwaiter<'r >(continuation:unit -> unit,methodBuilder:AsyncTaskMethodBuilder<'r>) =
        interface IAsyncStateMachine with
            member __.MoveNext() =
                    continuation () |> ignore  // runs cont, will update awaiter & continuation
            member __.SetStateMachine sm = methodBuilder.SetStateMachine sm 
    
    type StateMachine<'r>() =
        let methodBuilder = AsyncTaskMethodBuilder<'r>()
        let mutable awaiter = Unchecked.defaultof<ICriticalNotifyCompletion>
        let mutable stateStep = Unchecked.defaultof<IAsyncStateMachine>
        let cts = new CancellationTokenSource()

        member __.Run< 'r>(fn:unit -> unit) =
            stateStep <- // set 
                { new IAsyncStateMachine with 
                        member __.MoveNext() = ()
                        member __.SetStateMachine sm = methodBuilder.SetStateMachine sm
                }
            methodBuilder.Start(&stateStep)
            fn() |> ignore //fn will call .Await and set new awiater & 
            methodBuilder.Task

        member __.Await<'T>(awt : TaskAwaiter<'T>, next : 'T -> unit) : unit =
                awaiter <- awt                
                stateStep <- TypedGenericStateAwaiter<'T,'r>(awt,next,methodBuilder)
                methodBuilder.AwaitUnsafeOnCompleted(&awaiter, &stateStep)                
                    //continuation (^awt : (member GetResult : unit -> ^inp)(awt))

        member __.Await(awt : TaskAwaiter, next : unit -> unit) : unit =
                awaiter <- awt                
                stateStep <- TypedPlainStateAwaiter(next,methodBuilder)
                methodBuilder.AwaitUnsafeOnCompleted(&awaiter, &stateStep)               

        member __.Await(t:Task< 'r>) : unit =
            awaiter <- t.GetAwaiter()
            stateStep <- { new IAsyncStateMachine with 
                    member __.MoveNext() = methodBuilder.SetResult t.Result
                    member __.SetStateMachine sm = methodBuilder.SetStateMachine sm
                }
            methodBuilder.AwaitUnsafeOnCompleted(&awaiter, &stateStep)                

        member __.Result(v:'r) =
            methodBuilder.SetResult(v)
            cts.Dispose()

        member __.CancelationToken with get () = cts.Token
        member __.Cancel() = cts.Cancel()

    let zero = Task.FromResult ()

    let inline GenericTaskResultSet<'out>(t:Task<'out>,sm:StateMachine<'out>) =
        if t.IsCompleted then
            sm.Result( t.Result )
        else
            sm.Await(t) 

    type Binder<'out> =
      
        // Generic Standard Binding between awaits
        //////////////////////////
        static member inline GenericAwait< ^abl, ^inp 
                                            when ^abl : (member Result : ^inp )
                                            //and ^inp :> IComparable
                                            //and ^abl :> IComparable
                                            and  (TaskAwaiter< ^inp>) : (member GetResult : unit -> ^inp) 
                                            and ^abl : (member GetAwaiter : unit -> TaskAwaiter< ^inp> ) 
                                            >
            (continuation : ^inp -> unit, abl : ^abl, sm : StateMachine<'out>) : unit =
                let awt = (^abl : (member GetAwaiter : unit -> TaskAwaiter< ^inp>)(abl)) 
                if awt.IsCompleted then
                    continuation (awt.GetResult())
                else
                    sm.Await(awt,continuation)                

        // Plain Standard Binding
        ////////////////////////////
        static member inline GenericAwaitPlain< ^abl
                                            when 
                                            //^abl : (member Result : unit )
                                            //and ^abl :> IComparable
                                            //(TaskAwaiter) : (member GetResult : unit -> unit)
                                            ^abl : (member GetAwaiter : unit -> TaskAwaiter) >
            (continuation : unit -> unit, abl : ^abl, sm : StateMachine<'out>) : unit =
                let awt = (^abl : (member GetAwaiter : unit -> TaskAwaiter)(abl))
                if awt.IsCompleted then
                    continuation ()
                else
                    sm.Await(awt,continuation)

        // Standard Binding between awaits with configure await false
        //////////////////////////
        // static member inline GenericAwaitConfigureFalse< ^tsk, ^abl, ^awt, ^inp
        //                                                 when ^tsk : (member ConfigureAwait : bool -> ^abl)
        //                                                 and  ^abl : (member GetAwaiter : unit -> TaskAwaiter< ^inp>) 
        //                                                 and  ^abl : (member Result : ^inp )
        //                                                 and  (TaskAwaiter< ^inp>) : (member GetResult : unit -> ^inp) >
        //     (tsk : ^tsk, continuation : ^inp -> unit,sm : StateMachine<'out>) : unit =
        //         let abl = (^tsk : (member ConfigureAwait : bool -> ^abl)(tsk, false))
        //         Binder<'out>.GenericAwait(abl, continuation,sm)

        // static member inline GenericAwaitConfigureFalse< ^tsk, ^abl, ^awt, ^inp
        //                                                 when ^tsk : (member ConfigureAwait : bool -> ^abl)
        //                                                 and ^abl : (member GetAwaiter : unit -> TaskAwaiter) >
        //     (tsk : ^tsk, continuation : unit -> unit,sm : StateMachine<'out>) : unit =
        //         let abl = (^tsk : (member ConfigureAwait : bool -> ^abl)(tsk, false))
        //         Binder<'out>.GenericAwait(abl, continuation,sm)            


        // Unique overload to receive Return Result
        //////////////////////////
        static member inline GenericAwaitResult< ^abl, ^inp
                                            when ^abl : (member GetAwaiter : unit -> TaskAwaiter< ^inp>) >
            (continuation : ^inp -> 'out, abl : ^abl,sm : StateMachine< 'out>) : unit =
                let awt = (^abl : (member GetAwaiter : unit -> TaskAwaiter< ^inp>)(abl)) 
                if awt.IsCompleted then
                    sm.Result(continuation (awt.GetResult()))
                else
                    sm.Await(awt, fun v -> v |> continuation |> sm.Result)             
        
        
        // Unique overload to receive Task ReturnFrom
        //////////////////////////
        static member inline GenericAwaitTaskReturn< ^abl, ^inp when ^abl : (member GetAwaiter : unit -> TaskAwaiter< ^inp>) >
            (continuation : ^inp -> Task<'out>, abl : ^abl, sm : StateMachine<'out>) : unit =
                
                let awt = (^abl : (member GetAwaiter : unit -> TaskAwaiter< ^inp>)(abl))
                if awt.IsCompleted then
                    let t = continuation(awt.GetResult())
                    GenericTaskResultSet(t,sm)
                    
                else
                    sm.Await(awt, fun v -> 
                        let t = continuation v                        
                        GenericTaskResultSet(t,sm)                                  
                        )

    type ParentInsensitiveTaskBuilder<'r>() =
        // These methods are consistent between the two builders.
        // Unfortunately, inline members do not work with inheritance.
        let mutable sm = Unchecked.defaultof<StateMachine<'r>> //lazy

        member __.StateMachine = sm
        member inline __.Delay(f : unit -> _) = f
        member __.Run(f : unit -> unit) = 
            sm <- StateMachine<'r>()
            sm.Run(f)
        member __.Run(f : unit -> Task<'r>) = f()

        member __.Run(f : unit -> 'r) = Task.FromResult (f())

        member inline __.Zero() = zero
        member inline __.Return(v:'r) = v // x
        member inline __.ReturnFrom(task : 'r Task) = task 

        // member inline __.Bind(task, continuation) : unit =
        //     Bind.Invoke task continuation sm

        // member inline x.Bind(configurableTaskLike:^abl, continuation : ^inp -> unit) : unit =
        //     Binder<'r>.GenericAwait         (continuation,configurableTaskLike,x.StateMachine)

        member inline x.Bind<  ^inp 
                                    when (Task< ^inp>) : (member Result : ^inp )
                                    and  (TaskAwaiter< ^inp>) : (member GetResult : unit -> ^inp) >
                (abl: Task< ^inp>, continuation : ^inp -> unit) : unit =
                let awt = abl.GetAwaiter() 
                if awt.IsCompleted then
                    awt.GetResult() |> continuation
                else
                    x.StateMachine.Await(awt,continuation)

        member inline x.Bind< ^inp 
                                    when (Task< ^inp>) : (member Result : ^inp )
                                    and  (TaskAwaiter< ^inp>) : (member GetResult : unit -> ^inp) >
                (abl: Task< ^inp>, continuation : ^inp -> Task<'r>) : unit =
                let awt = abl.GetAwaiter()
                if awt.IsCompleted then
                    GenericTaskResultSet(awt.GetResult() |> continuation,x.StateMachine)
                else
                    x.StateMachine.Await(awt, fun v -> GenericTaskResultSet(continuation v,x.StateMachine))

        member inline x.Bind< ^inp 
                                    when (Task< ^inp>) : (member Result : ^inp )
                                    and  (TaskAwaiter< ^inp>) : (member GetResult : unit -> ^inp) >
                (abl: Task< ^inp>, continuation : ^inp -> 'r) : unit =
                let awt = abl.GetAwaiter()                                
                if awt.IsCompleted then
                    x.StateMachine.Result(continuation (awt.GetResult()))
                else
                    x.StateMachine.Await(awt, fun v -> v |> continuation |> x.StateMachine.Result)  


        /// Plain Task Overloads
        //////////////////////////////////////

        member inline x.Bind(abl: Task, continuation : unit -> unit) : unit =
                let awt = abl.GetAwaiter() 
                if awt.IsCompleted then
                    continuation ()
                else
                    x.StateMachine.Await(awt,continuation)

        member inline x.Bind(abl: Task, continuation : unit -> Task<'r>) : unit =
                let awt = abl.GetAwaiter() 
                if awt.IsCompleted then
                    GenericTaskResultSet(continuation(),x.StateMachine)
                else
                    x.StateMachine.Await(awt, fun () -> GenericTaskResultSet( continuation () ,x.StateMachine) )

        member inline x.Bind(abl: Task, continuation : unit -> 'r) : unit =
                let awt = abl.GetAwaiter() 
                if awt.IsCompleted then
                    x.StateMachine.Result(continuation ())
                else
                    x.StateMachine.Await(awt,fun () -> continuation () |> x.StateMachine.Result)

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

    let inline task< ^a> () = TaskBuilder.ParentInsensitiveTaskBuilder< ^a>()
    let work1 = task () {
            let t = Task.FromResult 2
            let! a =  t //Task.Factory.StartNew(fun () -> ())
            //let b = a + 1
            return 3  
        }
    
    let work2 = task () {
            let str = "vatsd"
            let! a = Task.FromResult str //Task.Factory.StartNew(fun () -> ())
            do! Task.Factory.StartNew(fun () -> ())
            //let b = a + 1
            return "string"
        }

