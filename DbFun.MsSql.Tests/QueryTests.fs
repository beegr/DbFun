﻿namespace DbFun.MsSql.Tests

open System
open Xunit
open DbFun.TestTools.Models
open DbFun.TestTools.Mocks
open DbFun.Core
open DbFun.Core.Builders
open DbFun.MsSql.Builders

module QueryTests = 

            
    [<Fact>]
    let ``Procedures``() = 

        let createConnection() = 
            setupCommandOutParams
                [   "userId", box 1
                    "name", box "jacentino"
                    "email", box "jacentino@gmail.com"
                    "created", box (DateTime(2023, 1, 1))
                    "ret_val", box 5
                ]

        let connector = new Connector(createConnection(), null)

        let qb = QueryBuilder (QueryConfig.Default(createConnection))
               
        let query = qb.Proc(Params.Auto<int> "id") (OutParams.ReturnAnd<User>("ret_val", "user")) Results.Unit "getUser"

        let _, (retVal, user) = query 1 connector |> Async.RunSynchronously

        let expected = 
            {
                userId = 1
                name = "jacentino"
                email = "jacentino@gmail.com"
                created = DateTime(2023, 1, 1)
            }

        Assert.Equal(5, retVal)
        Assert.Equal(expected, user)


    [<Fact>]
    let ``Record seq - using TVP`` () =
    
        let createConnection () = 
            createConnectionMock              
                []
                [
                    [ col<string> "name"; col<string> "typeName"; col<int16> "max_length"; col<int16> "precision"; col<byte> "scale"; col<byte> "is_nullable" ],
                    [
                        [ "userId"; "int"; 4s; 10uy; 0uy; 0uy ]
                        [ "name"; "nvarchar"; 20s; 0uy; 0uy; 0uy ]
                        [ "email"; "nvarchar"; 100s; 0uy; 0uy; 0uy ]
                        [ "created"; "datetime"; 8s; 0uy; 0uy; 0uy ]
                    ]                            
                ]

        let connector = new Connector(createConnection(), null)
        let qb = QueryBuilder(QueryConfig.Default(createConnection))
        let query = qb.Timeout(30).Sql(Params.TableValuedSeq<User>("users")) Results.Unit 
                        "insert into User (userId, name, email, created) 
                         select userId, name, email, created from @users"

        let user = 
            {
                userId = 3
                name = "jacentino"
                email = "jacentino@gmail.com" 
                created = DateTime(2023, 1, 1)
            }

        query [user] connector |> Async.RunSynchronously


    [<Fact>]
    let ``Custom converters procedure outparams``() = 

        let createConnection() = 
            setupCommandOutParams
                [   "userId", box 1
                    "name", box "jacentino"
                    "email", box "jacentino@gmail.com"
                    "created", box (DateTime(2023, 1, 1))
                    "ret_val", box 5
                ]

        let connector = new Connector(createConnection(), null)

        let config = QueryConfig.Default(createConnection).AddRowConverter(UserId)
        let qb = QueryBuilder(config)
               
        let query = qb.Proc(Params.Auto<int> "id")         
                           (OutParams.Tuple(OutParams.Return("ret_val"), OutParams.Tuple<UserId, string, string, DateTime>("userId", "name", "email", "created")))
                           Results.Unit 
                           "getUser"

        let _, (retVal, user) = query 1 connector |> Async.RunSynchronously

        let expected = (UserId 1, "jacentino", "jacentino@gmail.com", DateTime(2023, 1, 1))

        Assert.Equal(box 5, retVal)
        Assert.Equal(expected, user)


    [<Fact>]
    let ``Custom converters in TVP`` () =
    
        let createConnection () = 
            createConnectionMock              
                []
                [
                    [ col<string> "name"; col<string> "typeName"; col<int16> "max_length"; col<int16> "precision"; col<byte> "scale"; col<byte> "is_nullable" ],
                    [
                        [ "userId"; "int"; 4s; 10uy; 0uy; 0uy ]
                        [ "name"; "nvarchar"; 20s; 0uy; 0uy; 0uy ]
                        [ "email"; "nvarchar"; 100s; 0uy; 0uy; 0uy ]
                        [ "created"; "datetime"; 8s; 0uy; 0uy; 0uy ]
                    ]                            
                ]

        let connector = new Connector(createConnection(), null)
        let config = QueryConfig.Default(createConnection).AddParamConverter(fun (UserId id) -> id)                        
        let qb = QueryBuilder(config)
        let query = qb.Timeout(30).Sql(Params.TableValuedSeq(TVParams.Tuple<int, string, string, DateTime>("userId", "name", "email", "created"), "users", "User")) Results.Unit 
                        "insert into User (userId, name, email, created) 
                         select userId, name, email, created from @users"

        let user = (3, "jacentino", "jacentino@gmail.com", DateTime(2023, 1, 1))

        query [user] connector |> Async.RunSynchronously
