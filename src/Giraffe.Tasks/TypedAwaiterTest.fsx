
open System
open System.Threading.Tasks
open System.Runtime.CompilerServices


[<Obsolete>]
module TaskBuilder =
                           

    [<Struct>]
    type TypedGenericStateAwaiter<'T,'a >(awaiter:TaskAwaiter<'T> ,continuation:'T -> unit,methodBuilder:AsyncTaskMethodBuilder<'a>) =
        interface IAsyncStateMachine with
            member __.MoveNext() =
                    continuation ( awaiter.GetResult() )   // runs cont, will update awaiter & continuation
            member __.SetStateMachine sm = methodBuilder.SetStateMachine sm 
    
    [<Struct>]
    type TypedPlainStateAwaiter<'a >(continuation:unit -> unit,methodBuilder:AsyncTaskMethodBuilder<'a>) =
        interface IAsyncStateMachine with
            member __.MoveNext() =
                    continuation ()   // runs cont, will update awaiter & continuation
            member __.SetStateMachine sm = methodBuilder.SetStateMachine sm 
    
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

        member inline __.Await<'T>(awt : TaskAwaiter<'T>, next : 'T -> unit) : unit =
                awaiter <- awt                
                stateStep <- TypedGenericStateAwaiter<'T,'a>(awt,next,methodBuilder)
                methodBuilder.AwaitUnsafeOnCompleted(&awaiter, &stateStep)                
                    //continuation (^awt : (member GetResult : unit -> ^inp)(awt))

        member inline __.Await(awt : TaskAwaiter, next : unit -> unit) : unit =
                awaiter <- awt                
                stateStep <- TypedPlainStateAwaiter(next,methodBuilder)
                methodBuilder.AwaitUnsafeOnCompleted(&awaiter, &stateStep)               

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
        static member inline GenericAwaitTaskReturn< ^abl, ^awt, ^inp when ^abl : (member GetAwaiter : unit -> TaskAwaiter< ^inp>) >
            (abl : ^abl, continuation : ^inp -> Task<'out>,sm : StateMachine<'out>) : unit =
                
                let awt = (^abl : (member GetAwaiter : unit -> TaskAwaiter< ^inp>)(abl))
                if awt.IsCompleted then
                    let t = continuation(awt.GetResult())
                    GenericTaskResultSet(t,sm)                                                                                   
                else
                    sm.Await(awt, fun v -> 
                        let t = continuation v                        
                        GenericTaskResultSet(t,sm)          
                        )         

        // Generic Standard Binding between awaits
        //////////////////////////
        static member inline GenericAwait< ^abl, ^inp
                                            when ^abl : (member GetAwaiter : unit -> TaskAwaiter< ^inp>) >
            (abl : ^abl, continuation : ^inp -> unit, sm : StateMachine<'out>) : unit =
                let awt = (^abl : (member GetAwaiter : unit -> TaskAwaiter< ^inp>)(abl)) 
                if awt.IsCompleted then
                    continuation (awt.GetResult())
                else
                    sm.Await(awt,continuation)
                    //AwaitPass(awt, fun () -> continuation (^awt : (member GetResult : unit -> ^inp)(awt)))

        static member inline GenericAwait< ^abl, ^inp
                                            when ^abl : (member GetAwaiter : unit -> TaskAwaiter) >
            (abl : ^abl, continuation : unit -> unit, sm : StateMachine<'out>) : unit =
                let awt = (^abl : (member GetAwaiter : unit -> TaskAwaiter)(abl)) 
                if awt.IsCompleted then
                    continuation ()
                else
                    sm.Await(awt,continuation)

        // Standard Binding between awaits with configure await false
        //////////////////////////
        static member inline GenericAwaitConfigureFalse< ^tsk, ^abl, ^awt, ^inp
                                                        when ^tsk : (member ConfigureAwait : bool -> ^abl)
                                                        and ^abl : (member GetAwaiter : unit -> TaskAwaiter< ^inp>) >
            (tsk : ^tsk, continuation : ^inp -> unit,sm : StateMachine<'out>) : unit =
                let abl = (^tsk : (member ConfigureAwait : bool -> ^abl)(tsk, false))
                Binder<'out>.GenericAwait(abl, continuation,sm)

        
        static member inline GenericAwaitConfigureFalse< ^tsk, ^abl, ^awt, ^inp
                                                        when ^tsk : (member ConfigureAwait : bool -> ^abl)
                                                        and ^abl : (member GetAwaiter : unit -> TaskAwaiter) >
            (tsk : ^tsk, continuation : unit -> unit,sm : StateMachine<'out>) : unit =
                let abl = (^tsk : (member ConfigureAwait : bool -> ^abl)(tsk, false))
                Binder<'out>.GenericAwait(abl, continuation,sm)            

    // Unique overload to receive Return Result
    //////////////////////////

        static member inline GenericAwaitResult< ^abl, ^inp
                                            when ^abl : (member GetAwaiter : unit -> TaskAwaiter< ^inp>) >
            (abl : ^abl, continuation : ^inp -> 'out,sm : StateMachine< 'out>) : unit =
                let awt = (^abl : (member GetAwaiter : unit -> TaskAwaiter< ^inp>)(abl)) 
                if awt.IsCompleted then
                    sm.Result(continuation (awt.GetResult()))
                else
                    sm.Await(awt, continuation >> sm.Result)

       

    type ParentInsensitiveTaskBuilder<'r>() =
        // These methods are consistent between the two builders.
        // Unfortunately, inline members do not work with inheritance.
        let mutable sm = Unchecked.defaultof<StateMachine<'r>> //lazy
        member inline __.Delay(f : unit -> _) = f
        member inline __.Run(f : unit -> unit) = 
            sm <- StateMachine<'r>()
            sm.Run(f)
        member inline __.Run(f : unit -> Task<'r>) = f()
        member inline __.Run(f : unit -> 'r) = Task.FromResult (f())

        member inline __.Zero() = zero
        member inline __.Return(x:'r) = x
        member inline __.ReturnFrom(task : 'r Task) = task

        // member inline this.Bind(configurableTaskLike, continuation : 'a -> unit) : unit =
        //     Binder<'r>.GenericAwaitConfigureFalse(configurableTaskLike, continuation,stateMachine)

        // member inline this.Bind(configurableTaskLike, continuation : 'a -> 'r ) : unit =
        //     Binder<'r>.GenericAwaitResult(configurableTaskLike, continuation,stateMachine)

        // member inline this.Bind(configurableTaskLike, continuation : 'a -> Task<'r> ) : unit =
        //     Binder<'r>.GenericAwaitTaskReturn(configurableTaskLike, continuation,stateMachine)

        // Continue plain
        member inline __.Bind< ^abl, ^inp when ^abl : (member GetAwaiter : unit -> TaskAwaiter) >
            (abl : ^abl, continuation : unit -> unit) : unit =
                let awt = (^abl : (member GetAwaiter : unit -> TaskAwaiter)(abl)) 
                if awt.IsCompleted then
                    continuation ()
                else
                    sm.Await(awt,continuation)

        // Task ReturnFrom Plain
        member inline __.Bind< ^abl, ^awt, ^inp when ^abl : (member GetAwaiter : unit -> TaskAwaiter< ^inp>) >
            (abl : ^abl, continuation : ^inp -> Task<'r>) : unit =
                
                let awt = (^abl : (member GetAwaiter : unit -> TaskAwaiter< ^inp>)(abl))
                if awt.IsCompleted then
                    let t = continuation(awt.GetResult())
                    GenericTaskResultSet(t,sm)                                                                                   
                else
                    sm.Await(awt, fun v -> 
                        let t = continuation v                        
                        GenericTaskResultSet(t,sm)          
                        )

        // Continue Generic
        member inline __.Bind< ^abl, ^inp when ^abl : (member GetAwaiter : unit -> TaskAwaiter< ^inp>) >
            (abl : ^abl, continuation : ^inp -> unit) : unit =
                let awt = (^abl : (member GetAwaiter : unit -> TaskAwaiter< ^inp>)(abl)) 
                if awt.IsCompleted then
                    continuation (awt.GetResult())
                else
                    sm.Await(awt,continuation)

        // Return Result 
        member inline __.Bind< ^abl, ^inp when ^abl : (member GetAwaiter : unit -> TaskAwaiter< ^inp>) >
            (abl : ^abl, continuation : ^inp -> 'r) : unit =
                let awt = (^abl : (member GetAwaiter : unit -> TaskAwaiter< ^inp>)(abl)) 
                if awt.IsCompleted then
                    sm.Result(continuation (awt.GetResult()))
                else
                    sm.Await(awt, continuation >> sm.Result)


let task = TaskBuilder.ParentInsensitiveTaskBuilder()

let work2 = task {
    let! a = Task.FromResult 1
    return a
}