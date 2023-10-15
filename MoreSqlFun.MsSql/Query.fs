﻿namespace MoreSqlFun.MsSql.Builders

open System.Data.Common
open MoreSqlFun.Core.Builders
open MoreSqlFun.MsSql.Builders

[<AutoOpen>]
module Extensions = 

    type QueryConfig with
        static member MsSqlDefault(createConnection: unit -> DbConnection): QueryConfig = 
            { QueryConfig.Default(createConnection) with
                ParamBuilders = ParamsImpl.getDefaultBuilders(createConnection >> unbox)
            }

type QueryBuilder(config: QueryConfig) =
    inherit MoreSqlFun.Core.Builders.QueryBuilder(config)

    new(createConnection: unit -> DbConnection) = 
        QueryBuilder(QueryConfig.MsSqlDefault(createConnection))

    member this.Timeout(timeout: int) = 
        QueryBuilder({ this.Config with Timeout = Some timeout })

    member this.LogCompileTimeErrors() = 
        QueryBuilder({ this.Config with LogCompileTimeErrors = true })