
open System
open System.Threading.Tasks
open System.Runtime.CompilerServices
open System.Threading



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
        let cts = new CancellationTokenSource()

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

        member inline __.Result(v:'a) =
            methodBuilder.SetResult(v)
            cts.Dispose()

        member __.CancelationToken with get () = cts.Token
        member __.Cancel() = cts.Cancel()

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
                                            when ^abl : (member GetAwaiter : unit -> TaskAwaiter< ^inp>) 
                                            and  ^abl : (member Result : ^inp )
                                            and  (TaskAwaiter< ^inp>) : (member GetResult : unit -> ^inp) >
            (abl : ^abl, continuation : ^inp -> unit, sm : StateMachine<'out>) : unit =
                let awt = (^abl : (member GetAwaiter : unit -> TaskAwaiter< ^inp>)(abl)) 
                if awt.IsCompleted then
                    continuation (awt.GetResult())
                else
                    sm.Await(awt,continuation)

        // Plain Standard Binding
        ////////////////////////////
        static member inline GenericAwaitPlain< ^abl, ^inp
                                            when ^abl : (member GetAwaiter : unit -> TaskAwaiter) 
                                            and  (TaskAwaiter) : (member GetResult : unit -> unit) >
            (abl : ^abl, continuation : unit -> unit, sm : StateMachine<'out>) : unit =
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
            (abl : ^abl, continuation : ^inp -> 'out,sm : StateMachine< 'out>) : unit =
                let awt = (^abl : (member GetAwaiter : unit -> TaskAwaiter< ^inp>)(abl)) 
                if awt.IsCompleted then
                    sm.Result(continuation (awt.GetResult()))
                else
                    sm.Await(awt, continuation >> sm.Result)  

    type Priority3 = obj
    type Priority2 = IComparable

    type Bind = Priority1 with
        static member inline ($) (_:Priority3, task: 't) = fun (k: _ -> _) sm -> Binder<'r>.GenericAwait(task, k,sm)
        static member inline ($) (_:Priority2, task: 't) = fun (k: _ -> _) sm -> Binder<'r>.GenericAwaitTaskReturn(task, k,sm)
        static member inline ($) (  Priority1, task: 't) = fun (k: _ -> _) sm -> Binder<'r>.GenericAwaitResult(task, k,sm)
        static member        ($) (  Priority1, task: 't) = fun (k: _ -> _) sm -> Binder<'r>.GenericAwaitPlain(task, k,sm)
        static member inline Invoke task continuation sm = (Bind.Priority1 $ task) continuation sm    

    type ParentInsensitiveTaskBuilder<'r>() =
        // These methods are consistent between the two builders.
        // Unfortunately, inline members do not work with inheritance.
        let mutable sm = Unchecked.defaultof<StateMachine<'r>> //lazy
        member inline __.Delay(f : unit -> _) = f
        member inline __.Run(f : unit -> unit) = 
            sm <- StateMachine<'r>()
            sm.Run(f)
        member inline __.Run (f : unit -> Task<'r>) = f()

        member inline __.Run(f : unit -> 'r) = Task.FromResult (f())

        member inline __.Zero() = zero
        member inline __.Return(x:'r) = x
        member inline __.ReturnFrom(task : 'r Task) = task

         member inline __.Bind(task, continuation) : unit =
            Bind.Invoke task continuation sm

        // member inline this.Bind(configurableTaskLike, continuation : 'a -> unit) : unit =
        //     Binder<'r>.GenericAwait(configurableTaskLike, continuation,sm)

        // member inline this.Bind(configurableTaskLike, continuation : unit -> unit) : unit =
        //     Binder<'r>.GenericAwaitPlain(configurableTaskLike, continuation,sm)

        // member inline this.Bind(configurableTaskLike, continuation : 'a -> 'r ) : unit =
        //     Binder<'r>.GenericAwaitResult(configurableTaskLike, continuation,sm)

        // member inline this.Bind(configurableTaskLike, continuation : 'a -> Task<'r> ) : unit =
        //     Binder<'r>.GenericAwaitTaskReturn(configurableTaskLike, continuation,sm)

        // member inline this.Bind(subTask:StateMachine<'r> -> ^b , continuation : ^a -> unit) : unit =
        //     Binder<'r>.GenericAwait(subTask sm, continuation,sm)
    type ChildState<'r,'a> =
        struct 
            val State : StateMachine<'r>
            val Final : 'a -> unit
        end
        new(ism : StateMachine<'r>,final:'a -> unit) = { State = ism ; Final = final}        

    type ChildTaskBuilder(pstate:ChildState<'r,'a>) =
        // These methods are consistent between the two builders.
        // Unfortunately, inline members do not work with inheritance.
        let sm = pstate.State
        let final = pstate.Final
        member inline __.Delay(f : unit -> _) = f
        member inline __.Run(f : unit -> unit) = f()
        member inline __.Run (f : unit -> Task<'a>) = 
            let t = f()
            sm.Await(t.GetAwaiter(),final)

        member inline __.Run(f : unit -> 'a) = final(f())

        member inline __.Zero() = zero
        member inline __.Return(x:'r) = x
        member inline __.ReturnFrom(task : 'r Task) = task

         member inline __.Bind(task, continuation) : unit =
            Bind.Invoke task continuation sm


let task = TaskBuilder.ParentInsensitiveTaskBuilder
let ptask  = TaskBuilder.ParentInsensitiveTaskBuilder<'r>()

let work2 = ptask {
        let! a = Task.FromResult 3
        return 3  
    }   
