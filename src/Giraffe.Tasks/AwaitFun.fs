namespace GTOC

open System
open System.Threading.Tasks
open System.Runtime.CompilerServices
open System.Threading
open TaskBuilder


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

    and IMasterState =
        abstract Await : (ICriticalNotifyCompletion ref * IAsyncStateMachine ref) -> unit
        abstract CancellationTokenSource : CancellationTokenSource
    
    and MasterState<'f>() =
        let mb = AsyncTaskMethodBuilder<'f>()
        
        interface IMasterState with
            member __.Await = mb.AwaitUnsafeOnCompleted

        member val CancellationTokenSource = new CancellationTokenSource()
        
    and StateMachine<'r>(run:unit->IDuo<'r>) as this =
        
        // member __.Run< 'r>(fn:unit -> unit) =
        //     methodBuilder.Start(&this)
        //     fn() //fn will call .Await and set new awiater & 
        //     methodBuilder.Task
        let master = Unchecked.defaultof<MasterState<>>

        let mutable duo = run ()

        member __.MethodBuilder = methodBuilder 
        
        member __.Duo with get () = duo 
                        and set (v:IDuo<'r>) = 
                            duo.StateMachine <- this
                            duo <- v

        member __.Continue = not cts.IsCancellationRequested

        member __.RunChild<'t>(child:IDuo<'t>,cont:'t -> IDuo<'r>) =
            
            if child.Await <> Unchecked.defaultof<ICriticalNotifyCompletion> then // catches when exceptions or cancelations should stop progression
                duo <- v
                duo.StateMachine <- this
                methodBuilder.AwaitUnsafeOnCompleted(&duo.Await,&duo)

        member __.AwaitDuo (v:IDuo<'r>) =
            if v.Await <> Unchecked.defaultof<ICriticalNotifyCompletion> then // catches when exceptions or cancelations should stop progression
                duo <- v
                duo.StateMachine <- this
                methodBuilder.AwaitUnsafeOnCompleted(&duo.Await,&duo)

        // member inline __.Await<'T>(awt : TaskAwaiter<'T>, next : 'T -> TResult<'r>) : unit =
        //     awaiter <- awt                
        //     cont <- fun () -> awt.GetResult() |> next
        //     methodBuilder.AwaitUnsafeOnCompleted(&awaiter, &this)
            
        // member inline __.Await(awt : TaskAwaiter, next : unit -> TResult<'r>) : unit =
        //     awaiter <- awt                
        //     cont <- next
        //     methodBuilder.AwaitUnsafeOnCompleted(&awaiter, &this)               

        // member inline __.Await(t:Task< 'r>) : unit =
        //     awaiter <- t.GetAwaiter()
        //     cont <- fun () -> fun _ -> methodBuilder.SetResult t.Result // HACK << Clean up
        //     methodBuilder.AwaitUnsafeOnCompleted(&awaiter, &this)               

        member __.Result(v:'r) =
            methodBuilder.SetResult(v)
            cts.Dispose()
       
        // member inline __.AwaitAndResultGen<'T>(awt : TaskAwaiter<'T>, next : 'T -> 'r) : unit =
        //     awaiter <- awt                
        //     cont <- fun () -> fun _ -> awt.GetResult() |> next |> methodBuilder.SetResult // HACK << Clean up
        //     methodBuilder.AwaitUnsafeOnCompleted(&awaiter, &this)

        // member inline __.AwaitAndResult(awt : TaskAwaiter, next :  unit -> 'r) : unit =
        //     awaiter <- awt                
        //     cont <- fun () -> fun _ ->  next () |> methodBuilder.SetResult // HACK << Clean up
        //     methodBuilder.AwaitUnsafeOnCompleted(&awaiter, &this)                                       

        interface IStateMachine<'r> with
            member __.Task 
                with get() = 
                    if Object.ReferenceEquals(null,methodBuilder) then
                        methodBuilder <- AsyncTaskMethodBuilder<'r>()
                        methodBuilder.Start(&duo)
                    methodBuilder.Task
            member __.CancellationToken with get () = cts.Token
            member __.Cancel() = cts.Cancel()

            member __.getMethodBuilder () = methodBuilder
            member __.setMethodBuilder mb = methodBuilder <- mb

        // interface IAsyncStateMachine with 
        //     member __.MoveNext() = cont this
        //     member __.SetStateMachine _ = ()


    and IBind<'r> =
        abstract member StateMachine : StateMachine<'r> with get, set
        abstract member Await : ICriticalNotifyCompletion

    and IDuo<'r> =
        inherit IBind<'r>
        inherit IAsyncStateMachine

    and IReturno<'r> =
        inherit IBind<'r>
        inherit IAsyncStateMachine

    and SWrap<'r> =
        struct
            val Value : 'r
        end
        new (v) = {Value = v}            


    let zero = Task.FromResult ()

    // let inline GenericTaskResultSet<'out> (t:Task<'out>) (sm:StateMachine<'out>) =
    //     if t.IsCompleted then
    //         sm.Result( t.Result )
    //     else
    //         sm.Await(t)

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
    type ContGen<'t,'r>(awt:TaskAwaiter<'t>,cont:'t -> IDuo< 'r>) =
        let mutable sm = Unchecked.defaultof<StateMachine<'r>> 
        interface IDuo< 'r> with
            member __.StateMachine with get () = sm and set v = sm <- v // 
            member __.Await = awt :> ICriticalNotifyCompletion
            member __.MoveNext() = awt.GetResult() |> cont |> sm.AwaitDuo /// get next awaiter using this result, duo setter sets statemachine
            member __.SetStateMachine _ = ()

    type ContPln<'r>(awt:TaskAwaiter,cont:unit -> IDuo< 'r>) =
        let mutable sm = Unchecked.defaultof<StateMachine<'r>> 
        interface IDuo< 'r> with
            member __.StateMachine with get () = sm and set v = sm <- v // 
            member __.Await = awt :> ICriticalNotifyCompletion
            member __.MoveNext() =  cont () |> sm.AwaitDuo        /// get next awaiter using this result, duo setter sets statemachine
            member __.SetStateMachine _ = ()

    type RsltGen<'t,'r>(awt:TaskAwaiter<'t>,cont:'t -> SWrap< 'r>) = 
        let mutable sm = Unchecked.defaultof<StateMachine<'r>> 
        interface IDuo< 'r> with
            member __.StateMachine with get () = sm and set v = sm <- v // 
            member __.Await = awt :> ICriticalNotifyCompletion
            member __.MoveNext() = 
                let result = awt.GetResult() |> cont
                result.Value |> sm.Result 
            member __.SetStateMachine _ = ()

    
    type RsltPln<'r>(awt:TaskAwaiter,cont:unit -> SWrap< 'r>) = 
        let mutable sm = Unchecked.defaultof<StateMachine<'r>> 
        interface IDuo< 'r> with
            member __.StateMachine with get () = sm and set v = sm <- v // 
            member __.Await = awt :> ICriticalNotifyCompletion
            member __.MoveNext() = 
                let result = cont ()
                result.Value |> sm.Result 
            member __.SetStateMachine _ = ()        

    type ContFrom<'r>(awt:TaskAwaiter<'r>) = 
        let mutable sm = Unchecked.defaultof<StateMachine<'r>> 
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

    type ParentInsensitiveTaskBuilder() =
        // These methods are consistent between the two builders.
        // Unfortunately, inline members do not work with inheritance.
        member inline __.Delay(f : unit -> _) = f
        member inline __.Run(f : unit -> IDuo< ^r>) : IStateMachine< ^r> = StateMachine< ^r>(f)                         :> IStateMachine< ^r>
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

        // member inline __.Return(v: ^r) : IDuo< ^r> =                     
        //             let mutable sm = Unchecked.defaultof<StateMachine< ^r>> // this should be in the object expression is it exta alloc? ?!?
        //             { 
        //                 new IDuo< ^r> with
        //                     member __.StateMachine with get () = sm and set v = sm <- v // 
        //                     member __.Await = Unchecked.defaultof<ICriticalNotifyCompletion>
                            
        //                     member x.MoveNext() = 
        //                         let sm = x.StateMachine // has been set late on return of Duo, before await
        //                         sm.Duo <- awt.GetResult() |> continuation       // get next awaiter using this result, duo setter sets statemachine
        //                         sm.AwaitDuo()
        //                     member __.SetStateMachine _ = ()
        //             }

        // member inline __.ReturnFrom< ^r
        //                         when (Task< ^r>) : (member Result : ^r )
        //                         and  (TaskAwaiter< ^r>) : (member GetResult : unit -> ^r) >
        //                     (task : ^r Task) = 
        //                     let awt = task.GetAwaiter()
        //                     if awt.IsCompleted then
        //                         fun (sm:StateMachine< ^r>) -> awt.GetResult() |> sm.Result
        //                     else
        //                         fun (sm:StateMachine< ^r>) -> sm.Await(task)


        // member inline __.Bind(task, continuation) : unit =
        //     Bind.Invoke task continuation sm

        // member inline x.Bind(configurableTaskLike:^abl, continuation : ^inp -> unit) : unit =
        //     Binder<'r>.GenericAwait         (continuation,configurableTaskLike,x.StateMachine)

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
                    // let mutable sm = Unchecked.defaultof<StateMachine< ^r>> // this should be in the object expression is it exta alloc? ?!?
                    // { 
                    //     new IDuo< ^r> with
                    //         member __.StateMachine with get () = sm and set v = sm <- v // 
                    //         member __.Await = awt :> ICriticalNotifyCompletion
                            
                    //         member x.MoveNext() = 
                    //             let sm = x.StateMachine // has been set late on return of Duo, before await
                    //             sm.Duo <- awt.GetResult() |> continuation       // get next awaiter using this result, duo setter sets statemachine
                    //             sm.AwaitDuo()
                    //         member __.SetStateMachine _ = ()
                    // }
                    
                    //fun sm -> sm.Await(awt,continuation)

        // member inline __.Bind< ^inp, ^r 
        //                             when (Task< ^inp>) : (member Result : ^inp )
        //                             and  (TaskAwaiter< ^inp>) : (member GetResult : unit -> ^inp) >
        //         (abl: Task< ^inp>, continuation : ^inp -> Task< ^r>) : TResult< ^r> =
        //         let awt = abl.GetAwaiter()
        //         if awt.IsCompleted then
        //             GenericTaskResultSet (awt.GetResult() |> continuation)
        //         else
        //             fun sm -> sm.Await(awt,fun v -> GenericTaskResultSet(continuation v))

        member inline __.Bind< ^inp, ^r 
                                    when (Task< ^inp>) : (member Result : ^inp )
                                    and  (TaskAwaiter< ^inp>) : (member GetResult : unit -> ^inp) >
                (abl: Task< ^inp>, continuation : ^inp -> SWrap< ^r>) : IDuo< ^r> =
                let awt = abl.GetAwaiter()
                RsltGen< ^inp, ^r>(awt,continuation) :> IDuo<'r>

                // let mutable sm = Unchecked.defaultof<StateMachine< ^r>> // this should be in the object expression is it exta alloc? ?!?
                // { 
                //     new IDuo< ^r> with
                //         member __.StateMachine with get () = sm and set v = sm <- v // 
                //         member __.Await = awt :> ICriticalNotifyCompletion
                        
                //         member x.MoveNext() = 
                //             let sm = x.StateMachine
                //             let restult = awt.GetResult() |> continuation
                //             restult.Value |> sm.Result       /// get next awaiter using this result, duo setter sets statemachine
                //         member __.SetStateMachine _ = ()
                // }
                // if awt.IsCompleted then
                    
                //     awt.GetResult() |> continuation
                // else
                //     fun sm -> sm.AwaitAndResultGen< ^inp>(awt, continuation )  


        /// Plain Task Overloads
        //////////////////////////////////////

        member inline __.Bind(abl: Task, continuation : unit -> IDuo< ^r>) : IDuo< ^r> =
                let awt = abl.GetAwaiter() 
                if awt.IsCompleted then
                    continuation ()
                else
                    ContPln< ^r>(awt,continuation) :> IDuo<'r>
                    // let mutable sm = Unchecked.defaultof<StateMachine< ^r>> // this should be in the object expression is it exta alloc? ?!?
                    // { 
                    //     new IDuo< ^r> with
                    //         member __.StateMachine with get () = sm and set v = sm <- v // 
                    //         member __.Await = awt :> ICriticalNotifyCompletion
                            
                    //         member x.MoveNext() = 
                    //             let sm = x.StateMachine
                    //             sm.Duo <- continuation ()      /// get next awaiter using this result, duo setter sets statemachine
                    //             sm.AwaitDuo()
                    //         member __.SetStateMachine _ = ()
                    // }
                    //fun sm -> sm.Await(awt,continuation)

        // member inline __.Bind(abl: Task, continuation : unit -> Task< ^r>) : TResult< ^r> =
        //         let awt = abl.GetAwaiter() 
        //         if awt.IsCompleted then
        //             GenericTaskResultSet(continuation())
        //         else
        //             fun sm -> sm.Await(awt, fun () -> GenericTaskResultSet( continuation () ) )

        member inline __.Bind(abl: Task, continuation : unit -> SWrap< ^r>) : IDuo< ^r> =
                let awt = abl.GetAwaiter()
                RsltPln< ^r>(awt,continuation) :> IDuo<'r>
                // let mutable sm = Unchecked.defaultof<StateMachine< ^r>> // this should be in the object expression is it exta alloc? ?!?
                // { 
                //     new IDuo< ^r> with
                //         member __.StateMachine with get () = sm and set v = sm <- v // 
                //         member __.Await = awt :> ICriticalNotifyCompletion
                        
                //         member x.MoveNext() = 
                //             let result = continuation () 
                //             result.Value |> x.StateMachine.Result     /// get next awaiter using this result, duo setter sets statemachine
                //         member __.SetStateMachine _ = ()
                // }

        // Child Executions
        member inline __.Bind(child:StateMachine< ^r>,continuation : ^c -> ^r) : IDuo< ^r> =
            









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

