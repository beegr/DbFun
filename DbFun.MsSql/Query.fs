﻿namespace DbFun.MsSql.Builders

open DbFun.Core.Builders
open DbFun.MsSql.Builders
open System.Data

/// <summary>
/// Microsoft SQL Server-specific configuration, including tvp-parameter builders.
/// </summary>
type QueryConfig = 
    {
        Common      : DbFun.Core.Builders.QueryConfig
        TvpBuilders : TableValuedParamsImpl.IBuilder list
    }
    with 
        /// <summary>
        /// Creates default configuration.
        /// </summary>
        /// <param name="createConnection">
        /// The function creating database connection (with proper connection string, but not open).
        /// </param>
        static member Default(createConnection: unit -> IDbConnection) = 
            let common = DbFun.Core.Builders.QueryConfig.Default(createConnection)
            {
                Common      = { common with ParamBuilders = ParamsImpl.getDefaultBuilders(createConnection >> unbox) }
                TvpBuilders = TableValuedParamsImpl.getDefaultBuilders()
            }

        /// <summary>
        /// Adds a converter mapping application values of a given type to ptoper database parameter values.
        /// </summary>
        /// <param name="convert">
        /// Function converting application values to database parameter values.
        /// </param>
        member this.AddRowConverter(converter: 'Source -> 'Target) = 
            { this with Common = this.Common.AddRowConverter(converter) }

        /// <summary>
        /// Adds builder for table-valued parameters.
        /// </summary>
        /// <param name="builder">
        /// The builder.
        /// </param>
        member this.AddTvpBuilder(builder: TableValuedParamsImpl.IBuilder) = 
            let tvpBuilders = builder :: this.TvpBuilders
            let tvpProvider = ParamsImpl.BaseSetterProvider(tvpBuilders)
            let tvpCollBuilder = ParamsImpl.TVPCollectionBuilder(this.Common.CreateConnection, tvpProvider) :> ParamsImpl.IBuilder
            let paramBuilders = this.Common.ParamBuilders |> List.map (function :? ParamsImpl.TVPCollectionBuilder -> tvpCollBuilder | b -> b)
            { this with
                Common      = { this.Common with ParamBuilders = paramBuilders }
                TvpBuilders = tvpBuilders
            }

        /// <summary>
        /// Adds a converter mapping database values to application values.
        /// </summary>
        /// <param name="convert">
        /// Function converting database column values to application values.
        /// </param>
        member this.AddParamConverter(converter: 'Source -> 'Target) = 
            let tvpBuilder = ParamsImpl.Converter<'Source, 'Target>(converter) 
            let tvpSeqBuilder = ParamsImpl.SeqItemConverter<'Source, 'Target>(converter) 
            { this with Common = this.Common.AddParamConverter(converter) }
                .AddTvpBuilder(tvpBuilder)
                .AddTvpBuilder(tvpSeqBuilder)


/// <summary>
/// Provides methods creating various query functions.
/// </summary>
type QueryBuilder(config: QueryConfig) =
    inherit DbFun.Core.Builders.QueryBuilder(config.Common)

    /// <summary>
    /// The configuration of the query builder.
    /// </summary>
    member __.Config = config

    /// <summary>
    /// Creates query builder object with default configuration
    /// </summary>
    /// <param name="createConnection">
    /// Function creating connection, assigned with a proper connection string, but not open.
    /// </param>
    new(createConnection: unit -> IDbConnection) = 
        QueryBuilder(QueryConfig.Default(createConnection))

    /// <summary>
    /// Creates new builder with the specified command timeout.
    /// </summary>
    /// <param name="timeout">
    /// The timeout value in seconds.
    /// </param>
    member this.Timeout(timeout: int) = 
        QueryBuilder({ this.Config with Common = { this.Config.Common with Timeout = Some timeout } })

    /// <summary>
    /// Creates new builder with compile-time error logging and deferred exceptions.
    /// </summary>
    member this.LogCompileTimeErrors() = 
        QueryBuilder({ this.Config with Common = { this.Config.Common with LogCompileTimeErrors = true } })
