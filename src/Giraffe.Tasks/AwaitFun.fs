namespace GTOC

open System
open System.Threading.Tasks
open System.Runtime.CompilerServices
open System.Threading


[<Obsolete>]
module TaskBuilder =
    
    type IStateMachine<'r> =
        abstract member Task : Task<'r>
        abstract member CancellationToken : CancellationToken
        abstract member Cancel : unit -> unit
        abstract member getMethodBuilder : unit -> AsyncTaskMethodBuilder<'r> 
        abstract member setMethodBuilder: AsyncTaskMethodBuilder<'r> -> unit 

    and TResult<'r> = StateMachine<'r> -> unit

    and ResultMachine<'r>(t:Task<'r>)=
        interface IStateMachine<'r> with
            member __.Task = t
            member __.CancellationToken = CancellationToken.None
            member __.Cancel () = ()
            member __.getMethodBuilder () = Unchecked.defaultof<AsyncTaskMethodBuilder<'r>>
            member __.setMethodBuilder _ = ()

    and StateMachine<'r>(run:unit->TResult<'r>) as this =
        let mutable methodBuilder = Unchecked.defaultof<AsyncTaskMethodBuilder<'r>> //AsyncTaskMethodBuilder<'r>()
        let mutable awaiter = Unchecked.defaultof<ICriticalNotifyCompletion>
        let mutable cont = run // initial call will do nothing
        let cts = new CancellationTokenSource()
        
        // member __.Run< 'r>(fn:unit -> unit) =
        //     methodBuilder.Start(&this)
        //     fn() //fn will call .Await and set new awiater & 
        //     methodBuilder.Task

        member inline __.Await<'T>(awt : TaskAwaiter<'T>, next : 'T -> TResult<'r>) : unit =
            awaiter <- awt                
            cont <- fun () -> awt.GetResult() |> next
            methodBuilder.AwaitUnsafeOnCompleted(&awaiter, &this)
            
        member inline __.Await(awt : TaskAwaiter, next : unit -> TResult<'r>) : unit =
            awaiter <- awt                
            cont <- next
            methodBuilder.AwaitUnsafeOnCompleted(&awaiter, &this)               

        member inline __.Await(t:Task< 'r>) : unit =
            awaiter <- t.GetAwaiter()
            cont <- fun () -> fun _ -> methodBuilder.SetResult t.Result // HACK << Clean up
            methodBuilder.AwaitUnsafeOnCompleted(&awaiter, &this)               

        member inline __.Result(v:'r) =
            methodBuilder.SetResult(v)
            cts.Dispose()
       
        member inline __.AwaitAndResultGen<'T>(awt : TaskAwaiter<'T>, next : 'T -> 'r) : unit =
            awaiter <- awt                
            cont <- fun () -> fun _ -> awt.GetResult() |> next |> methodBuilder.SetResult // HACK << Clean up
            methodBuilder.AwaitUnsafeOnCompleted(&awaiter, &this)

        member inline __.AwaitAndResult(awt : TaskAwaiter, next :  unit -> 'r) : unit =
            awaiter <- awt                
            cont <- fun () -> fun _ ->  next () |> methodBuilder.SetResult // HACK << Clean up
            methodBuilder.AwaitUnsafeOnCompleted(&awaiter, &this)                                       

        interface IStateMachine<'r> with
            member __.Task 
                with get() = 
                    if Object.ReferenceEquals(null,methodBuilder) then
                        methodBuilder <- AsyncTaskMethodBuilder<'r>()
                        methodBuilder.Start(&this)
                    methodBuilder.Task
            member __.CancellationToken with get () = cts.Token
            member __.Cancel() = cts.Cancel()

            member __.getMethodBuilder () = methodBuilder
            member __.setMethodBuilder mb = methodBuilder <- mb

        interface IAsyncStateMachine with 
            member __.MoveNext() = cont () this
            member __.SetStateMachine _ = ()

    let zero = Task.FromResult ()

    let inline GenericTaskResultSet<'out> (t:Task<'out>) (sm:StateMachine<'out>) =
        if t.IsCompleted then
            sm.Result( t.Result )
        else
            sm.Await(t)

    // type Binder<'out> =
      
    //     // Generic Standard Binding between awaits
    //     //////////////////////////
    //     static member inline GenericAwait< ^abl, ^inp 
    //                                         when ^abl : (member Result : ^inp )
    //                                         //and ^inp :> IComparable
    //                                         //and ^abl :> IComparable
    //                                         and  (TaskAwaiter< ^inp>) : (member GetResult : unit -> ^inp) 
    //                                         and ^abl : (member GetAwaiter : unit -> TaskAwaiter< ^inp> ) 
    //                                         >
    //         (continuation : ^inp -> unit, abl : ^abl, sm : StateMachine<'out>) : unit =
    //             let awt = (^abl : (member GetAwaiter : unit -> TaskAwaiter< ^inp>)(abl)) 
    //             if awt.IsCompleted then
    //                 continuation (awt.GetResult())
    //             else
    //                 sm.Await(awt,continuation)                

    //     // Plain Standard Binding
    //     ////////////////////////////
    //     static member inline GenericAwaitPlain< ^abl
    //                                         when 
    //                                         //^abl : (member Result : unit )
    //                                         //and ^abl :> IComparable
    //                                         //(TaskAwaiter) : (member GetResult : unit -> unit)
    //                                         ^abl : (member GetAwaiter : unit -> TaskAwaiter) >
    //         (continuation : unit -> unit, abl : ^abl, sm : StateMachine<'out>) : unit =
    //             let awt = (^abl : (member GetAwaiter : unit -> TaskAwaiter)(abl))
    //             if awt.IsCompleted then
    //                 continuation ()
    //             else
    //                 sm.Await(awt,continuation)

    //     // Standard Binding between awaits with configure await false
    //     //////////////////////////
    //     // static member inline GenericAwaitConfigureFalse< ^tsk, ^abl, ^awt, ^inp
    //     //                                                 when ^tsk : (member ConfigureAwait : bool -> ^abl)
    //     //                                                 and  ^abl : (member GetAwaiter : unit -> TaskAwaiter< ^inp>) 
    //     //                                                 and  ^abl : (member Result : ^inp )
    //     //                                                 and  (TaskAwaiter< ^inp>) : (member GetResult : unit -> ^inp) >
    //     //     (tsk : ^tsk, continuation : ^inp -> unit,sm : StateMachine<'out>) : unit =
    //     //         let abl = (^tsk : (member ConfigureAwait : bool -> ^abl)(tsk, false))
    //     //         Binder<'out>.GenericAwait(abl, continuation,sm)

    //     // static member inline GenericAwaitConfigureFalse< ^tsk, ^abl, ^awt, ^inp
    //     //                                                 when ^tsk : (member ConfigureAwait : bool -> ^abl)
    //     //                                                 and ^abl : (member GetAwaiter : unit -> TaskAwaiter) >
    //     //     (tsk : ^tsk, continuation : unit -> unit,sm : StateMachine<'out>) : unit =
    //     //         let abl = (^tsk : (member ConfigureAwait : bool -> ^abl)(tsk, false))
    //     //         Binder<'out>.GenericAwait(abl, continuation,sm)            


    //     // Unique overload to receive Return Result
    //     //////////////////////////
    //     static member inline GenericAwaitResult< ^abl, ^inp
    //                                         when ^abl : (member GetAwaiter : unit -> TaskAwaiter< ^inp>) >
    //         (continuation : ^inp -> 'out, abl : ^abl,sm : StateMachine< 'out>) : unit =
    //             let awt = (^abl : (member GetAwaiter : unit -> TaskAwaiter< ^inp>)(abl)) 
    //             if awt.IsCompleted then
    //                 sm.Result(continuation (awt.GetResult()))
    //             else
    //                 sm.Await(awt, continuation >> sm.Result)             
        
        
    //     // Unique overload to receive Task ReturnFrom
    //     //////////////////////////
    //     static member inline GenericAwaitTaskReturn< ^abl, ^inp when ^abl : (member GetAwaiter : unit -> TaskAwaiter< ^inp>) >
    //         (continuation : ^inp -> Task<'out>, abl : ^abl, sm : StateMachine<'out>) : unit =
                
    //             let awt = (^abl : (member GetAwaiter : unit -> TaskAwaiter< ^inp>)(abl))
    //             if awt.IsCompleted then
    //                 let t = continuation(awt.GetResult())
    //                 GenericTaskResultSet(t,sm)
                    
    //             else
    //                 sm.Await(awt, fun v -> 
    //                     let t = continuation v                        
    //                     GenericTaskResultSet(t,sm)                                  
    //                     )

    type ParentInsensitiveTaskBuilder() =
        // These methods are consistent between the two builders.
        // Unfortunately, inline members do not work with inheritance.
        member inline __.Delay(f : unit -> _) = f
        member inline __.Run< ^r 
                            when (TResult< ^r>) : ( member Invoke : IStateMachine< ^r> -> unit ) >
                            (f : unit -> TResult< ^r>) : IStateMachine< ^r> = StateMachine< ^r>(f)                         :> IStateMachine< ^r>
        member inline __.Run< ^r
                                when (Task< ^r>) : (member Result : ^r )
                                and  (TaskAwaiter< ^r>) : (member GetResult : unit -> ^r) >
                            (f : unit -> Task< ^r>)    : IStateMachine< ^r> = f() |> ResultMachine<'r>                    :> IStateMachine< ^r>
        member inline __.Run(f : unit -> ^r)          : IStateMachine< ^r> = f() |> Task.FromResult |> ResultMachine<'r> :> IStateMachine< ^r>

        //member inline __.Zero() = Task.FromResult () |> ResultMachine<unit> :> IStateMachine<unit>
        // member inline __.Return(v: ^r) = v // x
        // member inline __.ReturnFrom< ^r
        //                         when (Task< ^r>) : (member Result : ^r )
        //                         and  (TaskAwaiter< ^r>) : (member GetResult : unit -> ^r) >
        //                     (task : ^r Task) = task 



        member inline __.Return(v: ^r) : TResult< ^r> = fun sm -> sm.Result v 
        member inline __.ReturnFrom< ^r
                                when (Task< ^r>) : (member Result : ^r )
                                and  (TaskAwaiter< ^r>) : (member GetResult : unit -> ^r) >
                            (task : ^r Task) = 
                            let awt = task.GetAwaiter()
                            if awt.IsCompleted then
                                fun (sm:StateMachine< ^r>) -> awt.GetResult() |> sm.Result
                            else
                                fun (sm:StateMachine< ^r>) -> sm.Await(task)


        // member inline __.Bind(task, continuation) : unit =
        //     Bind.Invoke task continuation sm

        // member inline x.Bind(configurableTaskLike:^abl, continuation : ^inp -> unit) : unit =
        //     Binder<'r>.GenericAwait         (continuation,configurableTaskLike,x.StateMachine)

        member inline __.Bind<  ^inp, ^r 
                                    when (Task< ^inp>) : (member Result : ^inp )
                                    and  (TaskAwaiter< ^inp>) : (member GetResult : unit -> ^inp) >
                (abl: Task< ^inp>, continuation : ^inp -> TResult< ^r>) : TResult< ^r> =
                let awt = abl.GetAwaiter() 
                if awt.IsCompleted then
                    awt.GetResult() |> continuation
                else
                    fun sm -> sm.Await(awt,continuation)

        // member inline __.Bind< ^inp, ^r 
        //                             when (Task< ^inp>) : (member Result : ^inp )
        //                             and  (TaskAwaiter< ^inp>) : (member GetResult : unit -> ^inp) >
        //         (abl: Task< ^inp>, continuation : ^inp -> Task< ^r>) : TResult< ^r> =
        //         let awt = abl.GetAwaiter()
        //         if awt.IsCompleted then
        //             GenericTaskResultSet (awt.GetResult() |> continuation)
        //         else
        //             fun sm -> sm.Await(awt,fun v -> GenericTaskResultSet(continuation v))

        // member inline __.Bind< ^inp, ^r 
        //                             when (Task< ^inp>) : (member Result : ^inp )
        //                             and  (TaskAwaiter< ^inp>) : (member GetResult : unit -> ^inp) >
        //         (abl: Task< ^inp>, continuation : ^inp -> ^r) : TResult< ^r> =
        //         let awt = abl.GetAwaiter()                                
        //         if awt.IsCompleted then
        //             fun sm -> sm.Result(awt.GetResult() |> continuation )
        //         else
        //             fun sm -> sm.AwaitAndResultGen< ^inp>(awt, continuation )  


        /// Plain Task Overloads
        //////////////////////////////////////

        member inline __.Bind(abl: Task, continuation : unit -> TResult< ^r>) : TResult< ^r> =
                let awt = abl.GetAwaiter() 
                if awt.IsCompleted then
                    continuation ()
                else
                    fun sm -> sm.Await(awt,continuation)

        // member inline __.Bind(abl: Task, continuation : unit -> Task< ^r>) : TResult< ^r> =
        //         let awt = abl.GetAwaiter() 
        //         if awt.IsCompleted then
        //             GenericTaskResultSet(continuation())
        //         else
        //             fun sm -> sm.Await(awt, fun () -> GenericTaskResultSet( continuation () ) )

        // member inline __.Bind(abl: Task, continuation : unit -> ^r) : TResult< ^r> =
        //         let awt = abl.GetAwaiter() 
        //         if awt.IsCompleted then
        //             fun sm -> sm.Result(continuation ())
        //         else
        //             fun sm -> sm.AwaitAndResult(awt,continuation)

        // Child Executions
        member inline __.Bind(child:#IStateMachine< ^r>,continuation : ^c -> ^r) : TResult< ^r> =
            fun (sm:StateMachine< ^r>) -> 
                let ism = sm :> IStateMachine< ^r> in child.setMethodBuilder (ism.getMethodBuilder ())









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

