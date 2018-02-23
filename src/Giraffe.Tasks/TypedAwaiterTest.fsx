
open System
open System.Threading.Tasks
open System.Runtime.CompilerServices
open System.Threading



[<Obsolete>]
module TaskBuilder =

    type TypePass<'T> = struct end                   

    [<Struct>]
    type TypedGenericStateAwaiter<'T,'r >(awaiter:TaskAwaiter<'T> ,continuation:'T -> TypePass<'r>,methodBuilder:AsyncTaskMethodBuilder<'r>) =
        interface IAsyncStateMachine with
            member __.MoveNext() =
                    continuation ( awaiter.GetResult() ) |> ignore  // runs cont, will update awaiter & continuation
            member __.SetStateMachine (_) = () 
            ) =sm = methodBuilder.SetStateMachine sm 
    
    [<Struct>]
    type TypedPlainStateAwaiter<'r >(continuation:unit -> TypePass<'r>,methodBuilder:AsyncTaskMethodBuilder<'r>) =
        interface IAsyncStateMachine with
            member __.MoveNext() =
                    continuation () |> ignore  // runs cont, will update awaiter & continuation
            member __.SetStateMachine sm = methodBuilder.SetStateMachine sm 
    
    type StateMachine<'r>() =
        let methodBuilder = AsyncTaskMethodBuilder<'r>()
        let mutable awaiter = Unchecked.defaultof<ICriticalNotifyCompletion>
        let mutable stateStep = Unchecked.defaultof<IAsyncStateMachine>
        let cts = new CancellationTokenSource()

        member __.Run< 'r>(fn:unit -> TypePass< 'r>) =
            stateStep <- // set 
                { new IAsyncStateMachine with 
                        member __.MoveNext() = ()
                        member __.SetStateMachine sm = methodBuilder.SetStateMachine sm
                }
            methodBuilder.Start(&stateStep)
            fn() |> ignore //fn will call .Await and set new awiater & 
            methodBuilder.Task

        member inline __.Await<'T>(awt : TaskAwaiter<'T>, next : 'T -> TypePass<'r>) : unit =
                awaiter <- awt                
                stateStep <- TypedGenericStateAwaiter<'T,'r>(awt,next,methodBuilder)
                methodBuilder.AwaitUnsafeOnCompleted(&awaiter, &stateStep)                
                    //continuation (^awt : (member GetResult : unit -> ^inp)(awt))

        member inline __.Await(awt : TaskAwaiter, next : unit -> TypePass<'r>) : unit =
                awaiter <- awt                
                stateStep <- TypedPlainStateAwaiter(next,methodBuilder)
                methodBuilder.AwaitUnsafeOnCompleted(&awaiter, &stateStep)               

        member inline __.Await(t:Task< 'r>) : unit =
            awaiter <- t.GetAwaiter()
            stateStep <- { new IAsyncStateMachine with 
                    member __.MoveNext() = methodBuilder.SetResult t.Result
                    member __.SetStateMachine sm = methodBuilder.SetStateMachine sm
                }
            methodBuilder.AwaitUnsafeOnCompleted(&awaiter, &stateStep)                

        member inline __.Result(v:'r) =
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
      
        // Generic Standard Binding between awaits
        //////////////////////////
        static member inline GenericAwait< ^abl, ^inp 
                                            when ^abl : (member Result : ^inp )
                                            //and ^inp :> IComparable
                                            //and ^abl :> IComparable
                                            and  (TaskAwaiter< ^inp>) : (member GetResult : unit -> ^inp) 
                                            and ^abl : (member GetAwaiter : unit -> TaskAwaiter< ^inp> ) 
                                            >
            (continuation : ^inp -> TypePass<'out>, abl : ^abl, sm : StateMachine<'out>) : TypePass<'out> =
                let awt = (^abl : (member GetAwaiter : unit -> TaskAwaiter< ^inp>)(abl)) 
                if awt.IsCompleted then
                    continuation (awt.GetResult())
                else
                    sm.Await(awt,continuation)
                    TypePass<'out>()                

        // Plain Standard Binding
        ////////////////////////////
        static member inline GenericAwaitPlain< ^abl
                                            when 
                                            //^abl : (member Result : unit )
                                            //and ^abl :> IComparable
                                            //(TaskAwaiter) : (member GetResult : unit -> unit)
                                            ^abl : (member GetAwaiter : unit -> TaskAwaiter) >
            (continuation : unit -> TypePass<'out>, abl : ^abl, sm : StateMachine<'out>) : TypePass<'out> =
                let awt = (^abl : (member GetAwaiter : unit -> TaskAwaiter)(abl))
                if awt.IsCompleted then
                    continuation ()
                else
                    sm.Await(awt,continuation)
                    TypePass<'out>()                

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
            (continuation : ^inp -> 'out, abl : ^abl,sm : StateMachine< 'out>) : TypePass<'out> =
                let awt = (^abl : (member GetAwaiter : unit -> TaskAwaiter< ^inp>)(abl)) 
                if awt.IsCompleted then
                    sm.Result(continuation (awt.GetResult()))
                else
                    sm.Await(awt, fun v -> v |> continuation |> sm.Result ; TypePass<'out>())
                TypePass<'out>()                
        
        
        // Unique overload to receive Task ReturnFrom
        //////////////////////////
        static member inline GenericAwaitTaskReturn< ^abl, ^inp when ^abl : (member GetAwaiter : unit -> TaskAwaiter< ^inp>) >
            (continuation : ^inp -> Task<'out>, abl : ^abl, sm : StateMachine<'out>) : TypePass<'out> =
                
                let awt = (^abl : (member GetAwaiter : unit -> TaskAwaiter< ^inp>)(abl))
                if awt.IsCompleted then
                    let t = continuation(awt.GetResult())
                    GenericTaskResultSet(t,sm)
                    
                else
                    sm.Await(awt, fun v -> 
                        let t = continuation v                        
                        GenericTaskResultSet(t,sm)
                        TypePass<'out>()          
                        )
                TypePass<'out>()                             

    type Priority3 = obj
    type Priority2 = IComparable

    type Bind = Priority1 with
        static member inline ($) (_:Priority3, task: ^t) = fun (k: _ -> _) sm -> Binder<'r>.GenericAwait(k,task,sm)
        //static member inline ($) (_:Priority2, task: ^t) = fun (k: _ -> _) sm -> Binder<'r>.GenericAwaitTaskReturn(k,task,sm)
        static member        ($) (  Priority1, task: 't) = fun (k: _ -> _) sm -> Binder<'r>.GenericAwaitResult(k,task,sm)
        //static member        ($) (  Priority1, task: 't) = fun (k: _ -> _) sm -> Binder<'r>.GenericAwaitPlain(k,task,sm)
        static member inline Invoke task continuation sm = (Bind.Priority1 $ task) continuation sm    
    



    type ParentInsensitiveTaskBuilder<'r>() =
        // These methods are consistent between the two builders.
        // Unfortunately, inline members do not work with inheritance.
        let mutable sm = Unchecked.defaultof<StateMachine<'r>> //lazy
        member inline __.Delay(f : unit -> _) = f
        member inline __.Run(f : unit -> TypePass<'r>) = 
            sm <- StateMachine<'r>()
            sm.Run(f)
        // member inline __.Run(f : unit -> Task<'r>) = f()

        // member inline __.Run(f : unit -> 'r) = Task.FromResult (f())

        member inline __.Zero() = zero
        member inline __.Return(x:'r) = sm.Result(x) ; TypePass<'r>() // x
        member inline __.ReturnFrom(task : 'r Task) = sm.Await(task) ; TypePass<'r>()

        // member inline __.Bind(task, continuation) : TypePass<'r> =
        //     Bind.Invoke task continuation sm

        member inline __.Bind(configurableTaskLike:^abl, continuation : ^inp -> TypePass<'r>) : TypePass<'r> =
            Binder<'r>.GenericAwait         (continuation,configurableTaskLike,sm)

        // member inline __.Bind(configurableTaskLike:^abl, continuation : unit -> TypePass<'r>) : TypePass<'r> =
        //     Binder<'r>.GenericAwaitPlain    (continuation,configurableTaskLike,sm)

        // member inline __.Bind< ^abl, ^inp
        //                             when ^abl : (member GetAwaiter : unit -> TaskAwaiter< ^inp>) >
        //     (configurableTaskLike:^abl, continuation : ^inp -> 'r ) : TypePass<'r> =
        //     Binder<'r>.GenericAwaitResult   (continuation,configurableTaskLike,sm)

        // member inline __.Bind< ^abl, ^inp 
        //                             when ^abl : (member GetAwaiter : unit -> TaskAwaiter< ^inp>) >
        //     (configurableTaskLike:^abl, continuation : ^inp -> Task<'r> ) : TypePass<'r> =
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


let task = TaskBuilder.ParentInsensitiveTaskBuilder
let inline ptask () = TaskBuilder.ParentInsensitiveTaskBuilder<_>()

let t = Task.Factory.StartNew(fun () -> ())


let work2 = ptask () {
        let! a =  Task.FromResult "vatsd" //Task.Factory.StartNew(fun () -> ())
        //let b = a + 1
        return "string"
    }

let work1 = ptask () {
        let t = Task.FromResult 2
        let! a =  t //Task.Factory.StartNew(fun () -> ())
        //let b = a + 1
        return 3  
    }
let inline (?<-) (a:Task< ^a>,b: ^a -> ^b) = a.ContinueWith< ^b>(fun (t:Task< ^a>) -> b t.Result)   

let tb =
    (Task.FromResult 3) !> (fun v -> printf "%A" v)


let {
    let! x = ( TaskA )
    ...
}
Bind(TaskA, x -> ... )