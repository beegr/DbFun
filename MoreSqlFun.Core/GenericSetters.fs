﻿namespace MoreSqlFun.Core.Builders

open System
open System.Reflection
open FSharp.Reflection
open System.Linq.Expressions
open MoreSqlFun.Core
open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Quotations.Patterns

module GenericSetters =

    type ISetter<'DbObject, 'Arg> = 
        abstract member SetValue: 'Arg * 'DbObject -> unit
        abstract member SetNull: 'DbObject -> unit
        abstract member SetArtificial: 'DbObject -> unit

    type ISetterProvider<'Prototype, 'DbObject> = 
        abstract member Setter: Type * string * 'Prototype -> obj
        abstract member Setter: string * 'Prototype -> ISetter<'DbObject, 'Arg>
        abstract member Builder: Type -> IBuilder<'Prototype, 'DbObject> option
    and
        IBuilder<'Prototype, 'DbObject> = 
        abstract member CanBuild: Type -> bool
        abstract member Build: ISetterProvider<'Prototype, 'DbObject> * string -> 'Prototype -> ISetter<'DbObject, 'Arg>

    type IBuilderEx<'Prototype, 'DbObject> = 
        abstract member Build: ISetterProvider<'Prototype, 'DbObject> * string -> 'Prototype -> ISetter<'DbObject, 'Arg>

    type IOverride<'Prototype, 'DbObject> = 
        abstract member IsRelevant: string -> bool
        abstract member IsFinal: bool
        abstract member Shift: unit -> IOverride<'Prototype, 'DbObject>
        abstract member Build: ISetterProvider<'Prototype, 'DbObject> * 'Prototype -> ISetter<'DbObject, 'Arg>

    type Override<'Prototype, 'DbObject, 'Arg>(propNames: string list, setter: ISetterProvider<'Prototype, 'DbObject> * 'Prototype -> ISetter<'DbObject, 'Arg>) =         

        static let rec getPropChain(expr: Expr) = 
            match expr with
            | PropertyGet (Some inner, property, _) -> getPropChain(inner) @ [ property.Name ]
            | _ -> []

        new (path: Expr<'Arg>, setter: ISetterProvider<'Prototype, 'DbObject> * 'Prototype -> ISetter<'DbObject, 'Arg>) = 
            Override(getPropChain(path), setter)

        interface IOverride<'Prototype, 'DbObject> with
            member __.IsRelevant (propertyName: string) = propNames |> List.tryHead |> Option.map ((=) propertyName) |> Option.defaultValue false
            member __.IsFinal = propNames |> List.isEmpty
            member __.Shift() = Override(propNames |> List.tail, setter)
            member __.Build<'Arg2>(provider: ISetterProvider<'Prototype, 'DbObject>, prototype: 'Prototype) = 
                setter(provider, prototype) :?> ISetter<'DbObject, 'Arg2>


    type BaseSetterProvider<'Prototype, 'DbObject>(builders: IBuilder<'Prototype, 'DbObject> seq) = 

        member this.GetSetter(argType: Type, name: string, prototype: 'Prototype): obj = 
            let method = this.GetType().GetMethods() |> Seq.find (fun m -> m.Name = "GetSetter" && m.IsGenericMethod && m.GetGenericArguments().Length = 1)
            let gmethod = method.MakeGenericMethod(argType)
            gmethod.Invoke(this, [| name; prototype |])

        member this.GetSetter<'Arg>(name: string, prototype: 'Prototype): ISetter<'DbObject, 'Arg> = 
            match builders |> Seq.tryFind (fun b -> b.CanBuild typeof<'Arg>) with
            | Some builder -> builder.Build(this, name) prototype
            | None -> failwithf "Could not found param builder for type: %A" typeof<'Arg>

        interface ISetterProvider<'Prototype, 'DbObject> with
            member this.Setter<'Arg>(name: string, prototype: 'Prototype): ISetter<'DbObject, 'Arg> = 
                this.GetSetter<'Arg>(name, prototype)
            member this.Setter(argType: Type, name: string, prototype: 'Prototype): obj = 
                this.GetSetter(argType, name, prototype)
            member __.Builder(argType: Type) = 
                builders |> Seq.tryFind (fun b -> b.CanBuild argType)

    type DerivedSetterProvider<'Prototype, 'DbObject>(baseProvider: ISetterProvider<'Prototype, 'DbObject>, overrides: IOverride<'Prototype, 'DbObject> seq) =

        member this.GetSetter(argType: Type, name: string, prototype: 'Prototype): obj = 
            let method = this.GetType().GetMethods() |> Seq.find (fun m -> m.Name = "GetSetter" && m.IsGenericMethod && m.GetGenericArguments().Length = 1)
            let gmethod = method.MakeGenericMethod(argType)
            gmethod.Invoke(this, [| name; prototype |])

        member __.GetSetter<'Arg>(name: string, prototype: 'Prototype): ISetter<'DbObject, 'Arg> = 
            let relevant = overrides |> Seq.filter (fun x -> x.IsRelevant name) |> Seq.map (fun x -> x.Shift()) |> Seq.toList
            match relevant |> List.tryFind (fun x -> x.IsFinal) with
            | Some ov -> ov.Build(DerivedSetterProvider(baseProvider, relevant), prototype)
            | None -> baseProvider.Setter(name, prototype)

        member __.GetBuilder(argType: Type) = 
            baseProvider.Builder(argType)

        interface ISetterProvider<'Prototype, 'DbObject> with
            member this.Setter<'Arg>(name: string, prototype: 'Prototype): ISetter<'DbObject, 'Arg> = 
                this.GetSetter<'Arg>(name, prototype)
            member this.Setter(argType: Type, name: string, prototype: 'Prototype): obj = 
                this.GetSetter(argType, name, prototype)
            member this.Builder(argType: Type) = 
                this.GetBuilder(argType)

    type UnitBuilder<'Prototype, 'DbObject>() =

        interface IBuilder<'Prototype, 'DbObject> with

            member __.CanBuild (argType: Type) = argType = typeof<unit>

            member __.Build<'Arg> (_: ISetterProvider<'Prototype, 'DbObject>, _: string) (_: 'Prototype) = 
                { new ISetter<'DbObject, 'Arg> with
                    member __.SetValue (_: 'Arg, _: 'DbObject) = 
                        ()
                    member __.SetNull(_: 'DbObject) = 
                        ()
                    member __.SetArtificial(_: 'DbObject) = 
                        ()
                }

                
    type SequenceBuilder<'Prototype, 'DbObject>() =

        interface IBuilder<'Prototype, 'DbObject> with

            member __.CanBuild (resType: Type) = Types.isCollectionType resType 

            member __.Build<'Arg> (_: ISetterProvider<'Prototype, 'DbObject>, _: string) (_: 'Prototype) = 
                { new ISetter<'DbObject, 'Arg> with
                    member __.SetValue (_: 'Arg, _: 'DbObject) = 
                        ()
                    member __.SetNull(_: 'DbObject) = 
                        ()
                    member __.SetArtificial(_: 'DbObject) = 
                        ()
                }


    type Converter<'Prototype, 'DbObject, 'Source, 'Target>(convert: 'Source -> 'Target) =

        interface IBuilder<'Prototype, 'DbObject> with

            member __.CanBuild (argType: Type) = typeof<'Source>.IsAssignableFrom argType

            member __.Build<'Arg> (provider: ISetterProvider<'Prototype, 'DbObject>, name: string) (prototype: 'Prototype) = 
                let setter = provider.Setter<'Target>(name, prototype) 
                let convert' = box convert :?> 'Arg -> 'Target
                { new ISetter<'DbObject, 'Arg> with
                    member __.SetValue (value: 'Arg, command: 'DbObject) = 
                        setter.SetValue(convert'(value), command)
                    member __.SetNull(command: 'DbObject) = 
                        setter.SetNull(command)
                    member __.SetArtificial(command: 'DbObject) = 
                        setter.SetArtificial(command)
                }

                
    type EnumConverter<'Prototype, 'DbObject, 'Underlying>() = 

        interface IBuilder<'Prototype, 'DbObject> with

            member __.CanBuild(argType: Type): bool = argType.IsEnum && argType.GetEnumUnderlyingType() = typeof<'Underlying>

            member __.Build(provider: ISetterProvider<'Prototype, 'DbObject>, name: string) (prototype: 'Prototype): ISetter<'DbObject, 'Arg> = 
                let setter = provider.Setter<'Underlying>(name, prototype)   
                let enumParam = Expression.Parameter(typeof<'Arg>)
                let convert = Expression.Lambda<Func<'Arg, 'Underlying>>(Expression.Convert(enumParam, typeof<'Underlying>), enumParam).Compile()                    
                { new ISetter<'DbObject, 'Arg> with
                    member __.SetValue (value: 'Arg, command: 'DbObject) = 
                        setter.SetValue(convert.Invoke(value), command)
                    member __.SetNull(command: 'DbObject) = 
                        setter.SetNull(command)
                    member __.SetArtificial(command: 'DbObject) = 
                        setter.SetArtificial(command)
                }
        

    type AttrEnumConverter<'Prototype, 'DbObject>() = 

        interface IBuilder<'Prototype, 'DbObject> with

            member __.CanBuild(argType: Type): bool = 
                argType.IsEnum 
                    && 
                argType.GetFields() 
                |> Seq.filter (fun f -> f.IsStatic) 
                |> Seq.forall (fun f -> not (Seq.isEmpty (f.GetCustomAttributes<Models.EnumValueAttribute>())))

            member __.Build(provider: ISetterProvider<'Prototype, 'DbObject>, name: string) (prototype: 'Prototype): ISetter<'DbObject, 'Arg> = 
                let setter = provider.Setter<string>(name, prototype)   
                let op1 = Expression.Parameter(typeof<'Arg>)
                let op2 = Expression.Parameter(typeof<'Arg>)
                let eq = Expression.Lambda<Func<'Arg, 'Arg, bool>>(Expression.Equal(op1, op2), op1, op2).Compile()                    
                let underlyingValues = 
                    [ for f in typeof<'Arg>.GetFields() do
                        if f.IsStatic then
                            f.GetValue(null) :?> 'Arg, f.GetCustomAttribute<Models.EnumValueAttribute>().Value
                    ] 
                let convert (x: 'Arg): string = underlyingValues |> List.find (fun (k, _) -> eq.Invoke(x, k)) |> snd
                { new ISetter<'DbObject, 'Arg> with
                    member __.SetValue (value: 'Arg, command: 'DbObject) = 
                        setter.SetValue(convert(value), command)
                    member __.SetNull(command: 'DbObject) = 
                        setter.SetNull(command)
                    member __.SetArtificial(command: 'DbObject) = 
                        setter.SetArtificial(command)
                }

        
    type OptionBuilder<'Prototype, 'DbObject>() =

        member __.GetSetter(provider: ISetterProvider<'Prototype, 'DbObject>, name: string) (prototype: 'Prototype): ISetter<'DbObject, 'Underlying option> = 
            let underlying = provider.Setter<'Underlying>(name, prototype)
            { new ISetter<'DbObject, 'Underlying option> with
                member __.SetValue (value: 'Underlying option, command: 'DbObject) = 
                    match value with
                    | Some value -> underlying.SetValue(value, command)
                    | None -> underlying.SetNull(command)
                member __.SetNull(command: 'DbObject) = 
                    underlying.SetNull(command)
                member __.SetArtificial(command: 'DbObject) = 
                    underlying.SetArtificial(command)
            }

        interface IBuilder<'Prototype, 'DbObject> with

            member __.CanBuild(argType: Type): bool = Types.isOptionType argType

            member this.Build<'Arg>(provider: ISetterProvider<'Prototype, 'DbObject>, name: string) (prototype: 'Prototype): ISetter<'DbObject, 'Arg> = 
                let method = this.GetType().GetMethod("GetSetter")
                let gmethod = method.MakeGenericMethod(typeof<'Arg>.GetGenericArguments().[0])
                let assigner = gmethod.Invoke(this, [| provider; name; prototype |]) 
                assigner :?> ISetter<'DbObject, 'Arg>


    module FieldListAssigner = 
            
        let private callSetValue(assigner: Expression, value: Expression, command: Expression) = 
            Expression.Call(assigner, assigner.Type.GetInterface("ISetter`2").GetMethod("SetValue"), value, command) :> Expression

        let private callSetNull(assigner: Expression, command: Expression) = 
            Expression.Call(assigner, assigner.Type.GetInterface("ISetter`2").GetMethod("SetNull"), command) :> Expression

        let private callSetArtificial(assigner: Expression, command: Expression) = 
            Expression.Call(assigner, assigner.Type.GetInterface("ISetter`2").GetMethod("SetArtificial"), command) :> Expression

        let build(provider: ISetterProvider<'Prototype, 'DbObject>, fields: (PropertyInfo * string) seq, prototype: 'Prototype): ISetter<'DbObject, 'Arg> = 
                    
                let valueParam = Expression.Parameter(typeof<'Arg>)
                let commandParam = Expression.Parameter(typeof<'DbObject>)

                let assignments = 
                    [ for field, name in fields do
                        let fieldValue = Expression.Property(valueParam, field)
                        let assigner = Expression.Constant(provider.Setter(field.PropertyType, name, prototype))
                        callSetValue(assigner, fieldValue, commandParam), callSetNull(assigner, commandParam), callSetArtificial(assigner, commandParam)
                    ]

                let valueAssignments = assignments |> Seq.map (fun (setVal, _, _) -> setVal) |> Expression.Block
                let setValueFunction = Expression.Lambda<Action<'Arg, 'DbObject>>(valueAssignments, valueParam, commandParam).Compile()

                let nullAssignments = assignments |> Seq.map (fun (_, setNull, _) -> setNull) |> Expression.Block
                let setNullFunction = Expression.Lambda<Action<'DbObject>>(nullAssignments, commandParam).Compile()

                let artifAssignments = assignments |> Seq.map (fun (_, _, setArtif) -> setArtif) |> Expression.Block
                let setArtifFunction = Expression.Lambda<Action<'DbObject>>(artifAssignments, commandParam).Compile()

                { new ISetter<'DbObject, 'Arg> with
                    member __.SetValue (value: 'Arg, command: 'DbObject) = 
                        setValueFunction.Invoke(value, command)
                    member __.SetNull(command: 'DbObject) = 
                        setNullFunction.Invoke(command)
                    member __.SetArtificial(command: 'DbObject) = 
                        setArtifFunction.Invoke(command)
                }


    type RecordBuilder<'Prototype, 'DbObject>() = 
            
        interface IBuilder<'Prototype, 'DbObject> with

            member __.CanBuild(argType: Type): bool = FSharpType.IsRecord(argType)

            member __.Build(provider: ISetterProvider<'Prototype, 'DbObject>, name: string) (prototype: 'Prototype): ISetter<'DbObject, 'Arg> = 
                let fields = FSharpType.GetRecordFields typeof<'Arg> |> Array.map (fun f -> f, f.Name)
                FieldListAssigner.build(provider, fields, prototype)

        interface IBuilderEx<'Prototype, 'DbObject> with

            member __.Build(provider: ISetterProvider<'Prototype, 'DbObject>, name: string) (prototype: 'Prototype): ISetter<'DbObject, 'Arg> = 
                let fields = FSharpType.GetRecordFields typeof<'Arg> |> Array.map (fun f -> f, sprintf "%s%s" name f.Name)
                FieldListAssigner.build(provider, fields, prototype)


    type TupleBuilder<'Prototype, 'DbObject>() = 

        interface IBuilder<'Prototype, 'DbObject> with

            member __.CanBuild(argType: Type): bool = FSharpType.IsTuple argType

            member __.Build(provider: ISetterProvider<'Prototype, 'DbObject>, name: string) (prototype: 'Prototype): ISetter<'DbObject, 'Arg> =                     
                let fields = typeof<'Arg>.GetProperties() |> Array.mapi (fun i f -> f, sprintf "%s%d" name (i + 1))
                FieldListAssigner.build(provider, fields, prototype)  


    let getDefaultBuilders(): IBuilder<'Prototype, 'DbObject> list = 
        [
            UnitBuilder<'Prototype, 'DbObject>()
            SequenceBuilder<'Prototype, 'DbObject>()
            RecordBuilder<'Prototype, 'DbObject>()
            TupleBuilder<'Prototype, 'DbObject>()
            OptionBuilder<'Prototype, 'DbObject>()
            Converter<'Prototype, 'DbObject, _, _>(fun (dtOnly: DateOnly) -> dtOnly.ToDateTime(TimeOnly.MinValue))
            Converter<'Prototype, 'DbObject, _, _>(fun (tmOnly: TimeOnly) -> tmOnly.ToTimeSpan())
            AttrEnumConverter<'Prototype, 'DbObject>()
            EnumConverter<'Prototype, 'DbObject, char>()
            EnumConverter<'Prototype, 'DbObject, int>()
        ]


    type GenericSetterBuilder<'Prototype, 'DbObject>() = 

        static member Unit (provider: ISetterProvider<'Prototype, 'DbObject>, prototype: 'Prototype) = 
            provider.Setter<unit>("", prototype)

        static member Simple<'Arg> (name: string) (provider: ISetterProvider<'Prototype, 'DbObject>, prototype: 'Prototype) = 
            provider.Setter<'Arg>(name, prototype)

        static member Optional<'Arg> (createUnderlyingSetter: ISetterProvider<'Prototype, 'DbObject> * 'Prototype -> ISetter<'DbObject, 'Arg>) = 
            fun (provider: ISetterProvider<'Prototype, 'DbObject>, prototype: 'Prototype) ->
                let underlyingSetter = createUnderlyingSetter(provider, prototype)
                { new ISetter<'DbObject, 'Arg option> with
                    member __.SetValue (value: 'Arg option, command: 'DbObject) = 
                        match value with
                        | Some value -> underlyingSetter.SetValue(value, command)
                        | None -> underlyingSetter.SetNull(command)
                    member __.SetNull(command: 'DbObject) = 
                        underlyingSetter.SetNull(command)
                    member __.SetArtificial(command: 'DbObject) = 
                        underlyingSetter.SetArtificial(command)
                }

        static member Tuple<'Arg1, 'Arg2>(
                createItem1Setter: ISetterProvider<'Prototype, 'DbObject> * 'Prototype -> ISetter<'DbObject, 'Arg1>, 
                createItem2Setter: ISetterProvider<'Prototype, 'DbObject> * 'Prototype -> ISetter<'DbObject, 'Arg2>) = 
            fun (provider: ISetterProvider<'Prototype, 'DbObject>, prototype: 'Prototype) ->
                let item1Setter = createItem1Setter(provider, prototype)
                let item2Setter = createItem2Setter(provider, prototype)
                { new ISetter<'DbObject, 'Arg1 * 'Arg2> with
                    member __.SetValue((val1: 'Arg1, val2: 'Arg2), command: 'DbObject) =
                        item1Setter.SetValue(val1, command)
                        item2Setter.SetValue(val2, command)
                    member __.SetNull(command: 'DbObject) = 
                        item1Setter.SetNull(command)
                        item2Setter.SetNull(command)
                    member __.SetArtificial(command: 'DbObject) = 
                        item1Setter.SetArtificial(command)
                        item2Setter.SetArtificial(command)
                }
        
        static member Tuple<'Arg1, 'Arg2>(name1: string, name2: string) = 
            fun (provider: ISetterProvider<'Prototype, 'DbObject>, prototype: 'Prototype) ->
                let item1Setter = provider.Setter<'Arg1>(name1, prototype) 
                let item2Setter = provider.Setter<'Arg2>(name2, prototype)
                { new ISetter<'DbObject, 'Arg1 * 'Arg2> with
                    member __.SetValue((val1: 'Arg1, val2: 'Arg2), command: 'DbObject) =
                        item1Setter.SetValue(val1, command)
                        item2Setter.SetValue(val2, command)
                    member __.SetNull(command: 'DbObject) = 
                        item1Setter.SetNull(command)
                        item2Setter.SetNull(command)
                    member __.SetArtificial(command: 'DbObject) = 
                        item1Setter.SetArtificial(command)
                        item2Setter.SetArtificial(command)
                }

        static member Tuple<'Arg1, 'Arg2, 'Arg3>(
                createItem1Setter: ISetterProvider<'Prototype, 'DbObject> * 'Prototype -> ISetter<'DbObject, 'Arg1>, 
                createItem2Setter: ISetterProvider<'Prototype, 'DbObject> * 'Prototype -> ISetter<'DbObject, 'Arg2>, 
                createItem3Setter: ISetterProvider<'Prototype, 'DbObject> * 'Prototype -> ISetter<'DbObject, 'Arg3>) = 
            fun (provider: ISetterProvider<'Prototype, 'DbObject>, prototype: 'Prototype) ->
                let item1Setter = createItem1Setter(provider, prototype)
                let item2Setter = createItem2Setter(provider, prototype)
                let item3Setter = createItem3Setter(provider, prototype)
                { new ISetter<'DbObject, 'Arg1 * 'Arg2 * 'Arg3> with
                    member __.SetValue((val1: 'Arg1, val2: 'Arg2, val3: 'Arg3), command: 'DbObject) =
                        item1Setter.SetValue(val1, command)
                        item2Setter.SetValue(val2, command)
                        item3Setter.SetValue(val3, command)
                    member __.SetNull(command: 'DbObject) = 
                        item1Setter.SetNull(command)
                        item2Setter.SetNull(command)
                        item3Setter.SetNull(command)
                    member __.SetArtificial(command: 'DbObject) = 
                        item1Setter.SetArtificial(command)
                        item2Setter.SetArtificial(command)
                        item3Setter.SetArtificial(command)
                }
        
        static member Tuple<'Arg1, 'Arg2, 'Arg3>(name1: string, name2: string, name3: string) = 
            fun (provider: ISetterProvider<'Prototype, 'DbObject>, prototype: 'Prototype) ->
                let item1Setter = provider.Setter<'Arg1>(name1, prototype) 
                let item2Setter = provider.Setter<'Arg2>(name2, prototype)
                let item3Setter = provider.Setter<'Arg3>(name3, prototype)
                { new ISetter<'DbObject, 'Arg1 * 'Arg2 * 'Arg3> with
                    member __.SetValue((val1: 'Arg1, val2: 'Arg2, val3: 'Arg3), command: 'DbObject) =
                        item1Setter.SetValue(val1, command)
                        item2Setter.SetValue(val2, command)
                        item3Setter.SetValue(val3, command)
                    member __.SetNull(command: 'DbObject) = 
                        item1Setter.SetNull(command)
                        item2Setter.SetNull(command)
                        item3Setter.SetNull(command)
                    member __.SetArtificial(command: 'DbObject) = 
                        item1Setter.SetArtificial(command)
                        item2Setter.SetArtificial(command)
                        item3Setter.SetArtificial(command)
                }

        static member Optional<'Arg> (name: string) = 
            fun (provider: ISetterProvider<'Prototype, 'DbObject>, prototype: 'Prototype) -> 
                provider.Setter<'Arg option>(name, prototype)

        static member Record<'Arg>(prefix: string, [<ParamArray>] overrides: IOverride<'Prototype, 'DbObject> array) = 
            fun (provider: ISetterProvider<'Prototype, 'DbObject>, prototype: 'Prototype) ->
                let provider = DerivedSetterProvider<'Prototype, 'DbObject>(provider, overrides)
                match provider.GetBuilder(typeof<'Arg>) with
                | Some builder ->
                    match builder with
                    | :? IBuilderEx<'Prototype, 'DbObject> as builderEx -> builderEx.Build<'Arg>(provider, prefix) prototype
                    | _ -> builder.Build<'Arg>(provider, prefix) prototype
                | None -> failwithf "Could not found param builder for type: %A" typeof<'Arg>

        static member Record<'Arg>([<ParamArray>] overrides: IOverride<'Prototype, 'DbObject> array) = 
            GenericSetterBuilder<'Prototype, 'DbObject>.Record<'Arg>("", overrides)

        static member Record<'Arg>(?prefix: string) = 
            GenericSetterBuilder<'Prototype, 'DbObject>.Record<'Arg>(defaultArg prefix "", [||])

