module Test

type StateMachine() =
    member val State : obj = null with get,set

type ValPass<'T> =
    struct
        val Value : 'T
        val State : StateMachine
        val None : bool
    end    
    new (v,s,n) = {Value = v ; State = s ; None = n }

    
type TraceBuilder() =

    member inline __.Bind(m : 'T option, f ) = 
        match m with 
        | Some v -> v |> f
        | None -> None
    member inline __.Return(x) = Some x

    member inline __.ReturnFrom(x) = x

    member inline __.Yield(x) = Some x

    member inline __.YieldFrom(x) = x
    
    member inline x.Zero() = x.Return ()

    member inline __.Delay(f) = f

    member inline __.Run(f) = f()

    member inline x.While(guard, body) =
        if not (guard()) 
        then x.Zero() 
        else x.Bind( body(), fun () -> 
            x.While(guard, body))  

    member inline x.TryWith(body, handler) =
        try x.ReturnFrom(body())
        with e -> handler e

    member inline x.TryFinally(body, compensation) =
        try x.ReturnFrom(body())
        finally compensation() 

    member inline x.Using(disposable:#System.IDisposable, body) =
        let body' = fun () -> body disposable
        x.TryFinally(body', fun () -> 
            match disposable with 
                | null -> () 
                | disp -> disp.Dispose())

    member inline x.For(sequence:seq<_>, body) =
        x.Using(sequence.GetEnumerator(),fun enum -> 
            x.While(enum.MoveNext, 
                x.Delay(fun () -> body enum.Current)))

let trace = TraceBuilder()

let comp = trace {
    let! v1 = Some 2
    let! v2 = Some v1
    if 10 > 5 then 
        return v2
    else 
        return v1
}
