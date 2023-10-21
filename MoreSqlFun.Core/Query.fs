﻿namespace MoreSqlFun.Core.Builders

open System.Data
open MoreSqlFun.Core
open MoreSqlFun.Core.Diagnostics
open System.Runtime.CompilerServices
open System.Runtime.InteropServices
open System

type QueryConfig = 
    {
        CreateConnection    : unit -> IDbConnection
        ParamBuilders       : ParamsImpl.IBuilder list
        OutParamBuilders    : OutParamsImpl.IBuilder list
        RowBuilders         : RowsImpl.IBuilder list
        Timeout             : int option
        LogCompileTimeErrors: bool
    }
    with static member Default(createConnection): QueryConfig = 
            {
                CreateConnection    = createConnection
                ParamBuilders       = ParamsImpl.getDefaultBuilders()
                OutParamBuilders    = OutParamsImpl.getDefaultBuilders()
                RowBuilders         = RowsImpl.getDefaultBuilders()
                Timeout             = None
                LogCompileTimeErrors= false
            }


type QueryBuilder(config: QueryConfig) =

    let compileTimeErrorLog = if config.LogCompileTimeErrors then Some (ref<CompileTimeErrorLog> []) else None

    let executePrototypeQuery(commandType: CommandType, commandText: string, setParams: IDbCommand -> unit, resultReaderBuilder: IDataReader -> IResultReader<'Result>) =
        use connection = config.CreateConnection()
        connection.Open()
        use command = connection.CreateCommand()
        command.CommandType <- commandType
        command.CommandText <- commandText
        setParams(command)
        use prototype = command.ExecuteReader(CommandBehavior.SchemaOnly)
        resultReaderBuilder(prototype)

    let executeQuery (provider: IConnector, commandText: string, resultReader: IResultReader<'Result>, setParams: IDbCommand -> unit) = 
        async {
            use command = provider.Connection.CreateCommand()
            command.CommandType <- CommandType.Text
            command.CommandText <- commandText
            command.Transaction <- provider.Transaction
            match config.Timeout with
            | Some timeout -> command.CommandTimeout <- timeout
            | None -> ()
            setParams(command)
            use! dataReader = Executor.executeReaderAsync(command, CommandBehavior.Default)
            return! resultReader.Read(dataReader)
        }

    let executeProcedure (provider: IConnector, commandText: string, outParamGetter: IOutParamGetter<'OutParams>, resultReader: IResultReader<'Result>, setParams: IDbCommand -> unit) = 
        async {
            use command = provider.Connection.CreateCommand()
            command.CommandType <- CommandType.StoredProcedure
            command.CommandText <- commandText
            command.Transaction <- provider.Transaction
            setParams(command)
            outParamGetter.Create(command)
            use! dataReader = Executor.executeReaderAsync(command, CommandBehavior.Default) 
            let! result = resultReader.Read(dataReader)
            return result, outParamGetter.Get(command)
        }

    let handleException (sourcePath: string, sourceLine: int, ex: exn) = 
        match compileTimeErrorLog with
        | Some errorLog -> 
            errorLog.Value <- (sourceLine, sourcePath, ex) :: errorLog.Value
            fun _ -> 
                raise <| AggregateException("One or more exceptions occured when compiling queries.", 
                            errorLog.Value 
                            |> List.map (fun (line, source, ex) -> CompileTimeException($"Cannot compile query in {sourcePath}, line: {sourceLine}", ex) :> exn))
        | None ->
            raise <| CompileTimeException($"Cannot compile query in {sourcePath}, line: {sourceLine}", ex)

    new(createConnection: unit -> IDbConnection) = 
        QueryBuilder(QueryConfig.Default(createConnection))

    member __.Config = config

    member __.Timeout(timeout: int) = 
        QueryBuilder({ config with Timeout = Some timeout })

    member __.LogCompileTimeErrors() = 
        QueryBuilder({ config with LogCompileTimeErrors = true })

    member __.CompileTimeErrorLog = 
        match compileTimeErrorLog with
        | Some log -> log.Value
        | None -> []

    member __.Sql (createParamSetter: BuildParamSetter<'Params>, 
                   [<CallerFilePath; Optional; DefaultParameterValue("")>] sourcePath: string,
                   [<CallerLineNumber; Optional; DefaultParameterValue(0)>] sourceLine: int)
                   : BuildResultReader<'Result> -> string -> 'Params -> IConnector -> Async<'Result> =         
        fun (createResultReader: BuildResultReader<'Result>) (commandText: string) ->
            try
                let provider = GenericSetters.BaseSetterProvider<unit, IDbCommand>(config.ParamBuilders)
                let paramSetter = createParamSetter(provider, ())

                let rowGetterProvider = GenericGetters.BaseGetterProvider<IDataRecord, IDataRecord>(config.RowBuilders)
                let createResultReader' prototype = createResultReader(rowGetterProvider, prototype)
                let resultReader = executePrototypeQuery(CommandType.Text, commandText, paramSetter.SetArtificial, createResultReader')

                fun (parameters: 'Params) (provider: IConnector) ->
                    executeQuery(provider, commandText, resultReader, fun cmd -> paramSetter.SetValue(parameters, cmd))
            with ex ->
                handleException(sourcePath, sourceLine, ex)

    member __.Sql (createParamSetter1: BuildParamSetter<'Params1>, 
                   createParamSetter2: BuildParamSetter<'Params2>,
                   [<CallerFilePath; Optional; DefaultParameterValue("")>] sourcePath: string,
                   [<CallerLineNumber; Optional; DefaultParameterValue(0)>] sourceLine: int)
                   : BuildResultReader<'Result> -> string -> 'Params1 -> 'Params2 -> IConnector -> Async<'Result> = 
        fun (createResultReader: BuildResultReader<'Result>) (commandText: string) ->
            try                        
                let provider = GenericSetters.BaseSetterProvider<unit, IDbCommand>(config.ParamBuilders)
                let paramSetter1 = createParamSetter1(provider, ())
                let paramSetter2 = createParamSetter2(provider, ())

                let rowGetterProvider = GenericGetters.BaseGetterProvider<IDataRecord, IDataRecord>(config.RowBuilders)
                let createResultReader' prototype = createResultReader(rowGetterProvider, prototype)
                let resultReader = executePrototypeQuery(CommandType.Text, commandText, (fun cmd -> paramSetter1.SetArtificial cmd; paramSetter2.SetArtificial cmd), createResultReader')

                let setParams (parameters1: 'Params1, parameters2: 'Params2) (command: IDbCommand) = 
                    paramSetter1.SetValue(parameters1, command) 
                    paramSetter2.SetValue(parameters2, command)

                fun (parameters1: 'Params1) (parameters2: 'Params2) (provider: IConnector) ->
                    executeQuery(provider, commandText, resultReader, setParams(parameters1, parameters2))
            with ex ->
                handleException(sourcePath, sourceLine, ex)

    member __.Proc (paramSetter: BuildParamSetter<'Params>,
                    [<CallerFilePath; Optional; DefaultParameterValue("")>] sourcePath: string,
                    [<CallerLineNumber; Optional; DefaultParameterValue(0)>] sourceLine: int)
                    : BuildOutParamGetter<'OutParams> -> BuildResultReader<'Result> -> string -> 'Params -> IConnector -> Async<'Result * 'OutParams> = 
        fun (outParamGetter: IOutParamGetterProvider * unit -> IOutParamGetter<'OutParams>) (resultReaderBuilder: BuildResultReader<'Result>) (commandText: string) ->
            try                        
                let provider = GenericSetters.BaseSetterProvider<unit, IDbCommand>(config.ParamBuilders)
                let paramSetter = paramSetter(provider, ())
                        
                let outParamProvider = GenericGetters.BaseGetterProvider<unit, IDbCommand>(config.OutParamBuilders)
                let outParamGetter = outParamGetter(outParamProvider, ())

                let setArtificialParams command = 
                    paramSetter.SetArtificial(command)
                    outParamGetter.Create(command)

                let rowGetterProvider = GenericGetters.BaseGetterProvider<IDataRecord, IDataRecord>(config.RowBuilders)
                let resultReaderBuilder' prototype = resultReaderBuilder(rowGetterProvider, prototype)

                let resultReader = executePrototypeQuery(CommandType.StoredProcedure, commandText, setArtificialParams, resultReaderBuilder')
                fun (parameters: 'Params) (provider: IConnector) ->
                    executeProcedure(provider, commandText, outParamGetter, resultReader, fun cmd -> paramSetter.SetValue(parameters, cmd))
            with ex ->
                handleException(sourcePath, sourceLine, ex)
    