
open System
open System.Threading.Tasks
open System.Runtime.CompilerServices

[<Obsolete>]
module TaskBuilder =

    [<Struct>]
    type TypedStateStep< ^awt, ^inp, 'a when ^awt : (member GetResult : unit -> ^inp) >
        (awaiter: ^awt ,continuation: ^inp -> unit ,methodBuilder:AsyncTaskMethodBuilder<'a>) =
            interface IAsyncStateMachine with
                member __.MoveNext () = ()
                    //continuation (^awt : (member GetResult : unit -> ^inp)(awaiter))
                member __.SetStateMachine sm = methodBuilder.SetStateMachine sm
            //static member Bind< ^awt,^inp,'a>(tss : TypedStateStep< ^awt,^inp','a'>) =
                
    type Test< ^awt, ^inp, 'a when ^awt : (member GetResult : unit -> ^inp)> =
        struct
            val awaiter : ^awt
            val continuation : ^inp -> unit
        end        
        
    let testVal = Test()                         

    testVal.

    [<Struct;Obsolete>]
    type TypedStateAwaiter<'T,'a >(awaiter:TaskAwaiter<'T> ,continuation:'T -> unit,methodBuilder:AsyncTaskMethodBuilder<'a>) =
        interface IAsyncStateMachine with
            member __.MoveNext() =
                    continuation ( awaiter.GetResult() )   // runs cont, will update awaiter & continuation
            member __.SetStateMachine sm = methodBuilder.SetStateMachine sm // Doesn't really apply since we're a reference type.
    
    type StateMachine<'a>() =
        let methodBuilder = AsyncTaskMethodBuilder<'a>()
        let mutable awaiter = Unchecked.defaultof<ICriticalNotifyCompletion>
        let mutable stateStep = Unchecked.defaultof<IAsyncStateMachine>

        member inline __.Run(fn:unit -> unit) =
            stateStep <- // set 
                { new IAsyncStateMachine with 
                        member __.MoveNext() = ()
                        member __.SetStateMachine sm = methodBuilder.SetStateMachine sm
                }
            methodBuilder.Start(&stateStep)
            fn() //fn will call .Await and set new awiater & 
            methodBuilder.Task

        member inline __.Await< ^awt, ^inp
                                    when ^awt :> ICriticalNotifyCompletion
                                    and ^awt : (member GetResult : unit -> ^inp) >
            (awt : ^awt, next : ^inp -> unit) : unit =
                awaiter <- awt                
                stateStep <- TypedStateStep< ^awt, ^inp,'a>(awt,next,methodBuilder)
                methodBuilder.AwaitUnsafeOnCompleted(&awaiter, &stateStep)                
                    //continuation (^awt : (member GetResult : unit -> ^inp)(awt))

        member inline __.Await(t:Task<'a>) : unit =
            awaiter <- t.GetAwaiter()
            stateStep <- { new IAsyncStateMachine with 
                    member __.MoveNext() = methodBuilder.SetResult t.Result
                    member __.SetStateMachine sm = methodBuilder.SetStateMachine sm
                }
            methodBuilder.AwaitUnsafeOnCompleted(&awaiter, &stateStep)                

        member __.Result(v:'a) =
            methodBuilder.SetResult(v)

    let zero = Task.FromResult ()

    let inline GenericTaskResultSet(t:Task<'out>,sm:StateMachine<'out>) =
        if t.IsCompleted then
            sm.Result( t.Result )
        else
            sm.Await(t) 

    type Binder<'out> =
      
        // Unique overload to receive Task ReturnFrom
        //////////////////////////
        static member inline GenericAwaitTaskReturn< ^abl, ^awt, ^inp
                                            when ^abl : (member GetAwaiter : unit -> ^awt)
                                            and ^awt :> ICriticalNotifyCompletion
                                            and ^awt : (member get_IsCompleted : unit -> bool)
                                            and ^awt : (member GetResult : unit -> ^inp) >
            (abl : ^abl, continuation : ^inp -> Task<'out>,sm : StateMachine<'out>) : unit =
                
                let awt = (^abl : (member GetAwaiter : unit -> ^awt)(abl))
                if (^awt : (member get_IsCompleted : unit -> bool)(awt)) then
                    let t = (^awt : (member GetResult : unit -> ^inp)(awt)) |> continuation
                    GenericTaskResultSet(t,sm)                                                                                   
                else
                    ()
                    sm.Await(awt, fun v -> 
                        let t = continuation v                        
                        GenericTaskResultSet(t,sm)          
                        )         

        // Standard Binding between awaits
        //////////////////////////
        static member inline GenericAwait< ^abl, ^awt, ^inp
                                            when ^abl : (member GetAwaiter : unit -> ^awt)
                                            and ^awt :> ICriticalNotifyCompletion
                                            and ^awt : (member get_IsCompleted : unit -> bool)
                                            and ^awt : (member GetResult : unit -> ^inp) >
            (abl : ^abl, continuation : ^inp -> unit, sm : StateMachine<'out>) : unit =
                let awt = (^abl : (member GetAwaiter : unit -> ^awt)(abl)) // get an awaiter from the awaitable
                if (^awt : (member get_IsCompleted : unit -> bool)(awt)) then // shortcut to continue immediately
                    continuation (^awt : (member GetResult : unit -> ^inp)(awt))
                else
                    sm.Await(awt,continuation)
                    //AwaitPass(awt, fun () -> continuation (^awt : (member GetResult : unit -> ^inp)(awt)))

        // Standard Binding between awaits with configure await false
        //////////////////////////
        static member inline GenericAwaitConfigureFalse< ^tsk, ^abl, ^awt, ^inp
                                                        when ^tsk : (member ConfigureAwait : bool -> ^abl)
                                                        and ^abl : (member GetAwaiter : unit -> ^awt)
                                                        and ^awt :> ICriticalNotifyCompletion
                                                        and ^awt : (member get_IsCompleted : unit -> bool)
                                                        and ^awt : (member GetResult : unit -> ^inp) >
            (tsk : ^tsk, continuation : ^inp -> unit,sm : StateMachine<'out>) : unit =
                let abl = (^tsk : (member ConfigureAwait : bool -> ^abl)(tsk, false))
                Binder<'out>.GenericAwait(abl, continuation,sm)

    // Unique overload to receive Return Result
    //////////////////////////

        static member inline GenericAwaitResult< ^abl, ^awt, ^inp
                                            when ^abl : (member GetAwaiter : unit -> ^awt)
                                            and ^awt :> ICriticalNotifyCompletion
                                            and ^awt : (member get_IsCompleted : unit -> bool)
                                            and ^awt : (member GetResult : unit -> ^inp) >
            (abl : ^abl, continuation : ^inp -> 'out,sm : StateMachine< 'out>) : unit =
                let awt = (^abl : (member GetAwaiter : unit -> ^awt)(abl)) // get an awaiter from the awaitable
                if (^awt : (member get_IsCompleted : unit -> bool)(awt)) then // shortcut to continue immediately
                    sm.Result(continuation (^awt : (member GetResult : unit -> ^inp)(awt)))
                else
                    sm.Await(awt, continuation >> sm.Result)

       

    type ParentInsensitiveTaskBuilder() =
        // These methods are consistent between the two builders.
        // Unfortunately, inline members do not work with inheritance.
        let mutable stateMachine = Unchecked.defaultof<StateMachine<'r>> //lazy
        member inline __.Delay(f : unit -> _) = f
        member inline __.Run(f : unit -> unit) = 
            stateMachine <- StateMachine<'r>()
            stateMachine.Run(f)
        member inline __.Run(f : unit -> Task<'r>) = f()
        member inline __.Run(f : unit -> 'r) = Task.FromResult (f())

        member inline __.Zero() = zero
        member inline __.Return(x:'r) = x
        member inline __.ReturnFrom(task : 'r Task) = task

        member inline this.Bind(configurableTaskLike, continuation : 'a -> unit) : unit =
            Binder<'r>.GenericAwaitConfigureFalse(configurableTaskLike, continuation,stateMachine)

        member inline this.Bind(configurableTaskLike, continuation : 'a -> 'r ) : unit =
            Binder<'r>.GenericAwaitResult(configurableTaskLike, continuation,stateMachine)

        member inline this.Bind(configurableTaskLike, continuation : 'a -> Task<'r> ) : unit =
            Binder<'r>.GenericAwaitTaskReturn(configurableTaskLike, continuation,stateMachine)

let task = TaskBuilder.ParentInsensitiveTaskBuilder()

let work2 = task {
    let! a = Task.FromResult 1
    let! b = Task.FromResult (a + 1)
    return b
}