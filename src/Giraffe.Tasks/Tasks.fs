// Tasks.fs - TPL task computation expressions for F#
//
// Written in 2016 by Robert Peele (humbobst@gmail.com)
// Original: https://github.com/rspeele/TaskBuilder.fs/blob/master/TaskBuilder.fs
//
// To the extent possible under law, the author(s) have dedicated all copyright and related and neighboring rights
// to this software to the public domain worldwide. This software is distributed without any warranty.
//
// You should have received a copy of the CC0 Public Domain Dedication along with this software.
// If not, see <http://creativecommons.org/publicdomain/zero/1.0/>.
//
// This is a slightly modified version to better fit an ASP.NET Core Giraffe web application.

namespace Giraffe

open System
open System.Threading.Tasks
open System.Runtime.CompilerServices
open System.ComponentModel
open Test

// This module is not really obsolete, but it's not intended to be referenced directly from user code.
// However, it can't be private because it is used within inline functions that *are* user-visible.
// Marking it as obsolete is a workaround to hide it from auto-completion tools.
[<Obsolete>]
module TaskBuilder =

    // type AwaitPass =
    //     struct
    //         val awt  : ICriticalNotifyCompletion
    //         val next : unit -> AwaitPass
    //     end
    //     new (awt,next) = { awt = awt ; next = next }
    //     static member Zero = Unchecked.defaultof<AwaitPass>
    //     static member inline IsZero (v:AwaitPass) = Object.ReferenceEquals(null,v.awt)

    /// Represents the state of a computation:
    /// either awaiting something with a continuation,
    /// or completed with a return value.
    // type Step<'a> =
    //     | Await of ICriticalNotifyCompletion * (unit -> AwaitPass)
    //     | Return of 'a
    //     /// We model tail calls explicitly, but still can't run them without O(n) memory usage.
    //     | ReturnFrom of 'a Task
    // Implements the machinery of running a `Step<'m, 'm>` as a task returning a continuation task.

    type StepStateMachine<'a>() as this =
        let methodBuilder = AsyncTaskMethodBuilder<'a>()
        /// The continuation we left off awaiting on our last MoveNext().
        let mutable continuation = Unchecked.defaultof<unit -> AwaitPass>
        /// Returns next pending awaitable or null if exiting (including tail call).
        let mutable self = this

        let mutable awaiter = Unchecked.defaultof<ICriticalNotifyCompletion>
        
        /// Start execution as a `Task<'a>`.
        member __.Run(awt,next) =
            awaiter <- awt
            continuation <- next
            methodBuilder.Start(&self)
            methodBuilder.Task
        member inline __.Result(v:'a) = methodBuilder.SetResult(v)        

        member __.MethodBuilder = methodBuilder

        interface IAsyncStateMachine with
            /// Proceed to one of three states: result, failure, or awaiting.
            /// If awaiting, MoveNext() will be called again when the awaitable completes.
            member x.MoveNext() =
                try
                    let ap = continuation() // runs cont, will update awaiter & continuation
                    awaiter <- ap.awt
                    continuation <- ap.next

                    //(x :> IAsyncStateMachine).MoveNext()

                    methodBuilder.AwaitUnsafeOnCompleted(&awaiter, &self)
                with
                    | exn ->
                        methodBuilder.SetException(exn)

            member __.SetStateMachine(_) = () // Doesn't really apply since we're a reference type.

    [<Struct>]
    type TypedStateStep< ^awt, ^inp, 'a when ^awt : (member GetResult : unit -> ^inp) and ^awt : not struct >
            (awaiter: ^awt ,continuation: ^inp -> unit ,methodBuilder:AsyncTaskMethodBuilder<'a>) =
                
                    interface IAsyncStateMachine with
                        member __.SetStateMachine sm = methodBuilder.SetStateMachine sm
                        member __.MoveNext() =
                            continuation (^awt : (member GetResult : unit -> ^inp)(awaiter))

   
        
    // type TypedStateStep2< ^awt, ^inp, 'a when ^awt : (member GetResult : unit -> ^inp) > =
    //     struct
    //         val awaiter: ^awt
    //         val continuation: ^inp -> unit
    //         val methodBuilder: AsyncTaskMethodBuilder<'a>
    //     end
    //     new (awaiter: ^awt ,continuation: ^inp -> unit ,methodBuilder:AsyncTaskMethodBuilder<'a>) = { awaiter = awaiter ; continuation = continuation ; methodBuilder = methodBuilder }
            // with interface IAsyncStateMachine with
            //     /// Proceed to one of three states: result, failure, or awaiting.
            //     /// If awaiting, MoveNext() will be called again when the awaitable completes.
            //     member __.MoveNext() =
            //         try
            //             continuation (^awt : (member GetResult : unit -> ^inp)(awaiter))
                        
            //         with
            //             | exn ->
            //                 methodBuilder.SetException(exn)

            //     member __.SetStateMachine sm = methodBuilder.SetStateMachine sm

    [<Struct>]
    type TypedStateStep<'T,'a >(awaiter:TaskAwaiter<'T> ,continuation:'T -> unit,methodBuilder:AsyncTaskMethodBuilder<'a>) =
        interface IAsyncStateMachine with
            /// Proceed to one of three states: result, failure, or awaiting.
            /// If awaiting, MoveNext() will be called again when the awaitable completes.
            member __.MoveNext() =
                try
                    continuation ( awaiter.GetResult() )   // runs cont, will update awaiter & continuation
                    
                with
                    | exn ->
                        methodBuilder.SetException(exn)

            member __.SetStateMachine sm = methodBuilder.SetStateMachine sm // Doesn't really apply since we're a reference type.

    // [<Struct>]
    // type InitStateStep< ^awt, ^inp
    //                         when ^awt :> ICriticalNotifyCompletion
    //                         and ^awt : (member GetResult : unit -> ^inp) >
    //     (awaiter: ^awt ,continuation: ^inp -> unit,target: byref<ICriticalNotifyCompletion> : methodBuilder ) =
    //             interface IAsyncStateMachine with
    //                 member __.MoveNext() =

    //                 member __.SetStateMachine sm = methodBuilder.SetStateMachine sm 
    
    type StateMachine<'a>() =
        let methodBuilder = AsyncTaskMethodBuilder<'a>()
        let mutable awaiter = Unchecked.defaultof<ICriticalNotifyCompletion>
        let mutable stateStep = Unchecked.defaultof<IAsyncStateMachine>

        member inline __.Run< ^awt, ^inp
                                    when ^awt :> ICriticalNotifyCompletion
                                    and ^awt : (member GetResult : unit -> ^inp) >
            (awt : ^awt, next : ^inp -> unit) =

            stateStep <- 
                { new IAsyncStateMachine with 
                        member __.MoveNext() =
                            awaiter <- awt
                            stateStep <- TypedStateStep<_,_,'a>(awt,next,methodBuilder)
                            methodBuilder.AwaitUnsafeOnCompleted(&awaiter, &stateStep)
                        member __.SetStateMachine sm = methodBuilder.SetStateMachine sm
                }
            methodBuilder.Start(&stateStep)
            methodBuilder.SetResult

        member inline __.Await< ^awt, ^inp
                                    when ^awt :> ICriticalNotifyCompletion
                                    and ^awt : (member GetResult : unit -> ^inp) >
            (awt : ^awt, next : ^inp -> unit) : unit =
                awaiter <- awt                
                stateStep <- TypedStateStep<_,_,'a>(awt,next,methodBuilder)
                methodBuilder.AwaitUnsafeOnCompleted(&awaiter, &stateStep)                
                    //continuation (^awt : (member GetResult : unit -> ^inp)(awt))

            


        member __.Result(v:'a) =
            methodBuilder.SetResult(v)


    let unwrapException (agg : AggregateException) =
        let inners = agg.InnerExceptions
        if inners.Count = 1 then inners.[0]
        else agg :> Exception

    /// Used to represent no-ops like the implicit empty "else" branch of an "if" expression.
    let zero = Task.FromResult ()

        


    /// Used to return a value.
    // let inline ret (x : 'a) = Return x

    type Binder<'out> =
        // We put the output generic parameter up here at the class level, so it doesn't get subject to
        // inline rules. If we put it all in the inline function, then the compiler gets confused at the
        // below and demands that the whole function either is limited to working with (x : obj), or must
        // be inline itself.
        //
        // let yieldThenReturn (x : 'a) =
        //     task {
        //         do! Task.Yield()
        //         return x
        //     }

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

        static member inline GenericAwaitConfigureFalse< ^tsk, ^abl, ^awt, ^inp
                                                        when ^tsk : (member ConfigureAwait : bool -> ^abl)
                                                        and ^abl : (member GetAwaiter : unit -> ^awt)
                                                        and ^awt :> ICriticalNotifyCompletion
                                                        and ^awt : (member get_IsCompleted : unit -> bool)
                                                        and ^awt : (member GetResult : unit -> ^inp) >
            (tsk : ^tsk, continuation : ^inp -> AwaitPass) : AwaitPass =
                let abl = (^tsk : (member ConfigureAwait : bool -> ^abl)(tsk, false))
                Binder<'out>.GenericAwait(abl, continuation)

    // new testing
    //////////////////////////

        static member inline GenericAwaitResult< ^abl, ^awt, ^inp
                                            when ^abl : (member GetAwaiter : unit -> ^awt)
                                            and ^awt :> ICriticalNotifyCompletion
                                            and ^awt : (member get_IsCompleted : unit -> bool)
                                            and ^awt : (member GetResult : unit -> ^inp) >
            (abl : ^abl, continuation : ^inp -> 'out,sm : StepStateMachine< 'out>) : unit =
                let awt = (^abl : (member GetAwaiter : unit -> ^awt)(abl)) // get an awaiter from the awaitable
                if (^awt : (member get_IsCompleted : unit -> bool)(awt)) then // shortcut to continue immediately
                    sm.Result(continuation (^awt : (member GetResult : unit -> ^inp)(awt)))
                else
                    sm.Await(awt,fun () ->  sm.Result(continuation (^awt : (member GetResult : unit -> ^inp)(awt))))

    /// Special case of the above for `Task<'a>`. Have to write this out by hand to avoid confusing the compiler
    /// trying to decide between satisfying the constraints with `Task` or `Task<'a>`.
    // let inline bindTask (task : 'a Task,continuation : 'a -> AwaitPass) =
    //     let awt = task.GetAwaiter()
    //     if awt.IsCompleted then // Proceed to the next step based on the result we already have.
    //         continuation(awt.GetResult())
    //     else // Await and continue later when a result is available.

    //         AwaitPass(awt, (fun () -> continuation(awt.GetResult())))

    /// Special case of the above for `Task<'a>`, for the context-insensitive builder.
    /// Have to write this out by hand to avoid confusing the compiler thinking our built-in bind method
    /// defined on the builder has fancy generic constraints on inp and out parameters.
    // let inline bindTaskConfigureFalse (task : 'a Task) (continuation : 'a -> AwaitPass) =
    //     let awt = task.ConfigureAwait(false).GetAwaiter()
    //     if awt.IsCompleted then // Proceed to the next step based on the result we already have.
    //         continuation(awt.GetResult())
    //     else // Await and continue later when a result is available.
    //         AwaitPass(awt, (fun () -> continuation(awt.GetResult())))

    /// Chains together a step with its following step.
    /// Note that this requires that the first step has no result.
    /// This prevents constructs like `task { return 1; return 2; }`.

    /// Builds a step that executes the body while the condition predicate is true.
    let whileLoop (cond : unit -> bool) (body : unit -> AwaitPass) =
        if cond() then
            // Create a self-referencing closure to test whether to repeat the loop on future iterations.
            let rec repeat () =                
                if cond() then
                    let body = body()
                    AwaitPass (body.awt, fun () -> combine (body.next()) repeat)
                else AwaitPass.Zero
            // Run the body the first time and chain it to the repeat logic.
            combine (body()) repeat
        else AwaitPass.Zero

    /// Wraps a step in a try/with. This catches exceptions both in the evaluation of the function
    /// to retrieve the step, and in the continuation of the step (if any).
    let rec tryWith(step : unit -> unit) (catch : exn -> unit) =
        try
            step()
        with
        | exn -> catch exn

    /// Wraps a step in a try/finally. This catches exceptions both in the evaluation of the function
    /// to retrieve the step, and in the continuation of the step (if any).
    let rec tryFinally (step : unit -> unit) fin =
        let step =
            try step()
            // Important point: we use a try/with, not a try/finally, to implement tryFinally.
            // The reason for this is that if we're just building a continuation, we definitely *shouldn't*
            // execute the `fin()` part yet -- the actual execution of the asynchronous code hasn't completed!
            with
            | _ ->
                fin()
                reraise()
        AwaitPass (step.awt, fun () -> tryFinally step.next fin)

    /// Implements a using statement that disposes `disp` after `body` has completed.
    let inline using (disp : #IDisposable) (body : _ -> AwaitPass) =
        // A using statement is just a try/finally with the finally block disposing if non-null.
        tryFinally
            (fun () -> body disp)
            (fun () -> if not (isNull (box disp)) then disp.Dispose())

    /// Implements a loop that runs `body` for each element in `sequence`.
    // let forLoop (sequence : 'a seq) (body : 'a -> AwaitPass) =
    //     // A for loop is just a using statement on the sequence's enumerator...
    //     using (sequence.GetEnumerator())
    //         // ... and its body is a while loop that advances the enumerator and runs the body on each element.
    //         (fun e -> whileLoop e.MoveNext (fun () -> body e.Current))

    /// Runs a step as a task -- with a short-circuit for immediately completed steps.
    // let run (firstStep : unit -> Step<'a>) =
    //     try
    //         match firstStep() with
    //         | Return x -> Task.FromResult(x)
    //         | ReturnFrom t -> t
    //         | Await (await,next) -> StepStateMachine<'a>().Run(await,next).Unwrap() // sadly can't do tail recursion
    //     // Any exceptions should go on the task, rather than being thrown from this call.
    //     // This matches C# behavior where you won't see an exception until awaiting the task,
    //     // even if it failed before reaching the first "await".
    //     with
    //     | exn ->
    //         let src = new TaskCompletionSource<_>()
    //         src.SetException(exn)
    //         src.Task

    /// Builds a `System.Threading.Tasks.Task<'a>` similarly to a C# async/await method, but with
    /// all awaited tasks automatically configured *not* to resume on the captured context.
    /// This is often preferable when writing library code that is not context-aware, but undesirable when writing
    /// e.g. code that must interact with user interface controls on the same thread as its caller.
    type ParentInsensitiveTaskBuilder<'r>() =
        // These methods are consistent between the two builders.
        // Unfortunately, inline members do not work with inheritance.
        let mutable stateMachine = Unchecked.defaultof<StepStateMachine<'r>> //lazy
        member inline __.Delay(f : unit -> _) = f
        member inline __.Run(f : unit -> unit) = 
            stateMachine <- StepStateMachine<'r>()
            f()
            stateMachine.Run()
        member inline __.Run(f : unit -> Task<'r>) = f()
        member inline __.Run(f : unit -> 'r) = Task.FromResult (f())

        member inline __.Zero() = zero
        member inline __.Return(x:'r) = x
        member inline __.ReturnFrom(task : 'r Task) = task
        member inline __.Combine(step : AwaitPass, continuation) = combine step continuation
        // member inline __.While(condition : unit -> bool, body : unit -> AwaitPass) = whileLoop condition body
        // member inline __.For(sequence : _ seq, body : _ -> AwaitPass) = forLoop sequence body
        member inline __.TryWith(body : unit -> AwaitPass, catch : exn -> AwaitPass) = tryWith body catch
        member inline __.TryFinally(body : unit -> AwaitPass, fin : unit -> unit) = tryFinally body fin
        member inline __.Using(disp : #IDisposable, body : #IDisposable -> AwaitPass) = using disp body
        // End of consistent methods -- the following methods are different between
        // `TaskBuilder` and `ContextInsensitiveTaskBuilder`!

        member inline this.Bind(configurableTaskLike, continuation : 'a -> AwaitPass) : AwaitPass =
            Binder<'r>.GenericAwaitConfigureFalse(configurableTaskLike, continuation,stateMachine)

        member inline this.Bind(configurableTaskLike, continuation : 'a -> 'r ) : unit =
            Binder<'r>.GenericAwaitResult(configurableTaskLike, continuation,stateMachine)


        // We have to have a dedicated overload for Task<'a> so the compiler doesn't get confused.
        // Everything else can use bindGenericAwaitable via an extension member (defined later).
        // member inline __.Bind(task : 'a Task, continuation : 'a -> AwaitPass) : AwaitPass =
        //     bindTaskConfigureFalse task continuation


        // Async overload bind
        // member inline __.Bind(work : 'a Async, continuation : 'a -> AwaitPass) : AwaitPass =
        //     let task = Async.StartAsTask work
        //     bindTaskConfigureFalse task continuation


    type ContextInsensitiveTaskBuilder(ssm:StepStateMachine<_>) =
        // These methods are consistent between the two builders.
        // Unfortunately, inline members do not work with inheritance.
        member inline __.Delay(f : unit -> Step<_>) = f
        member inline __.Run(f : unit -> Step<'m>) = run f
        member inline __.Zero() = zero
        member inline __.Return(x) = ret x
        member inline __.ReturnFrom(task : _ Task) = ReturnFrom task
        member inline __.Combine(step : unit Step, continuation) = combine step continuation
        member inline __.While(condition : unit -> bool, body : unit -> unit Step) = whileLoop condition body
        member inline __.For(sequence : _ seq, body : _ -> unit Step) = forLoop sequence body
        member inline __.TryWith(body : unit -> _ Step, catch : exn -> _ Step) = tryWith body catch
        member inline __.TryFinally(body : unit -> _ Step, fin : unit -> unit) = tryFinally body fin
        member inline __.Using(disp : #IDisposable, body : #IDisposable -> _ Step) = using disp body
        // End of consistent methods -- the following methods are different between
        // `TaskBuilder` and `ContextInsensitiveTaskBuilder`!

        // We have to have a dedicated overload for Task<'a> so the compiler doesn't get confused.
        // Everything else can use bindGenericAwaitable via an extension member (defined later).
        member inline __.Bind(task : 'a Task, continuation : 'a -> 'b Step) : 'b Step =
            bindTaskConfigureFalse task continuation

        // Async overload bind
        member inline __.Bind(work : 'a Async, continuation : 'a -> 'b Step) : 'b Step =
            let task = Async.StartAsTask work
            bindTaskConfigureFalse task continuation

// Don't warn about our use of the "obsolete" module we just defined (see notes at start of file).
#nowarn "44"

[<AutoOpen>]
module Tasks =
    /// Builds a `System.Threading.Tasks.Task<'a>` similarly to a C# async/await method, but with
    /// all awaited tasks automatically configured *not* to resume on the captured context.
    /// This is often preferable when writing library code that is not context-aware, but undesirable when writing
    /// e.g. code that must interact with user interface controls on the same thread as its caller.
    
    let inline parent = TaskBuilder.ContextInsensitiveTaskBuilder(TaskBuilder.StepStateMachine<_>())
    let inline task ctx = TaskBuilder.ContextInsensitiveTaskBuilder ctx

    // These are fallbacks when the Bind and ReturnFrom on the builder object itself don't apply.
    // This is how we support binding arbitrary task-like types.
    type TaskBuilder.ContextInsensitiveTaskBuilder with
        member inline this.ReturnFrom(taskLike) =
            TaskBuilder.Binder<_>.GenericAwait(taskLike, TaskBuilder.ret)

        member inline this.ReturnFrom(taskLike) =
            TaskBuilder.Binder<_>.GenericAwaitUnit(taskLike, TaskBuilder.ret)

        member inline this.Bind(taskLike, continuation : unit -> 'a TaskBuilder.Step) : 'a TaskBuilder.Step =
            TaskBuilder.Binder<'a>.GenericAwaitUnit(taskLike, continuation)

        member inline this.Bind(taskLike, continuation : 'v -> 'a TaskBuilder.Step) : 'a TaskBuilder.Step =
            TaskBuilder.Binder<'a>.GenericAwait(taskLike, continuation)

    [<AutoOpen>]
    module HigherPriorityBinds =
        // When it's possible for these to work, the compiler should prefer them since they shadow the ones above.
        type TaskBuilder.ContextInsensitiveTaskBuilder with
            member inline this.ReturnFrom(configurableTaskLike) =
                TaskBuilder.Binder<_>.GenericAwaitConfigureFalse(configurableTaskLike, TaskBuilder.ret)
            
            member inline this.Bind(configurableTaskLike, continuation : _ -> 'a TaskBuilder.Step) : 'a TaskBuilder.Step =
                TaskBuilder.Binder<'a>.GenericAwaitConfigureFalse(configurableTaskLike, continuation)
