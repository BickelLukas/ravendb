﻿using System;
using System.Collections.Generic;
using System.Linq;
using Raven.Client.Documents;
using Raven.Client.Documents.Indexes;
using Raven.Client.Documents.Linq;
using Raven.Client.Documents.Session;
using Raven.Tests.Core.Utils.Entities;
using Tests.Infrastructure;
using Tests.Infrastructure.Entities;
using Xunit;
using Xunit.Abstractions;

namespace FastTests.Sharding.Queries
{
    public class BasicShardedQueryTests : RavenTestBase
    {
        public BasicShardedQueryTests(ITestOutputHelper output) : base(output)
        {
        }

        [RavenFact(RavenTestCategory.Querying | RavenTestCategory.Sharding)]
        public void Queries_With_LoadDocument_Should_Work()
        {
            using (var store = Sharding.GetDocumentStore())
            {
                using (var session = store.OpenSession())
                {
                    session.Store(new Company
                    {
                        Name = "Acme Inc."
                    }, "Companies/1");

                    session.Store(new Company
                    {
                        Name = "Evil Corp"
                    }, "Companies/2");

                    session.Store(new Order
                    {
                        Company = "Companies/1",
                        Lines = new List<OrderLine>
                        {
                            new OrderLine{ PricePerUnit = (decimal)1.0, Quantity = 3 },
                            new OrderLine{ PricePerUnit = (decimal)1.5, Quantity = 3 }
                        }
                    }, "orders/1$Companies/1");
                    session.Store(new Order
                    {
                        Company = "Companies/1",
                        Lines = new List<OrderLine>
                        {
                            new OrderLine{ PricePerUnit = (decimal)1.0, Quantity = 5 },
                        }
                    }, "orders/2$Companies/1");
                    session.Store(new Order
                    {
                        Company = "Companies/2",
                        Lines = new List<OrderLine>
                        {
                            new OrderLine{ PricePerUnit = (decimal)3.0, Quantity = 6, Discount = (decimal)3.5},
                            new OrderLine{ PricePerUnit = (decimal)8.0, Quantity = 3, Discount = (decimal)3.5},
                            new OrderLine{ PricePerUnit = (decimal)1.8, Quantity = 2 }
                        }
                    }, "orders/3$Companies/2");
                    session.SaveChanges();
                }

                Indexes.WaitForIndexing(store);

                using (var session = store.OpenSession())
                {
                    var complexLinqQuery =
                        (from o in session.Query<Order>()
                         let TotalSpentOnOrder =
                             (Func<Order, decimal>)(order =>
                                 order.Lines.Sum(l => l.PricePerUnit * l.Quantity - l.Discount))
                         select new
                         {
                             OrderId = o.Id,
                             TotalMoneySpent = TotalSpentOnOrder(o),
                             CompanyName = session.Load<Company>(o.Company).Name
                         }).ToList();

                    Assert.NotEmpty(complexLinqQuery);
                    Assert.Equal(3, complexLinqQuery.Count);
                    Assert.DoesNotContain(complexLinqQuery, item => item == null);

                    foreach (var item in complexLinqQuery)
                    {
                        Assert.True((string)item.CompanyName == "Acme Inc." || (string)item.CompanyName == "Evil Corp");
                    }
                }
            }
        }

        [RavenFact(RavenTestCategory.Querying | RavenTestCategory.Sharding)]
        public void Query_With_Customize()
        {
            using (var store = Sharding.GetDocumentStore())
            {
                new DogsIndex().Execute(store);

                using (var session = store.OpenSession())
                {
                    session.Store(new Dog { Name = "Snoopy", Breed = "Beagle", Color = "White", Age = 6, IsVaccinated = true }, "dogs/1");
                    session.Store(new Dog { Name = "Brian", Breed = "Labrador", Color = "White", Age = 12, IsVaccinated = false }, "dogs/2");
                    session.Store(new Dog { Name = "Django", Breed = "Jack Russel", Color = "Black", Age = 3, IsVaccinated = true }, "dogs/3");
                    session.Store(new Dog { Name = "Beethoven", Breed = "St. Bernard", Color = "Brown", Age = 1, IsVaccinated = false }, "dogs/4");
                    session.Store(new Dog { Name = "Scooby Doo", Breed = "Great Dane", Color = "Brown", Age = 0, IsVaccinated = false }, "dogs/5");
                    session.Store(new Dog { Name = "Old Yeller", Breed = "Black Mouth Cur", Color = "White", Age = 2, IsVaccinated = true }, "dogs/6");
                    session.Store(new Dog { Name = "Benji", Breed = "Mixed", Color = "White", Age = 0, IsVaccinated = false }, "dogs/7");
                    session.Store(new Dog { Name = "Lassie", Breed = "Collie", Color = "Brown", Age = 6, IsVaccinated = true }, "dogs/8");

                    session.SaveChanges();
                }

                using (var session = store.OpenSession())
                {
                    var queryResult = session.Query<DogsIndex.Result, DogsIndex>()
                        .Customize(x => x.WaitForNonStaleResults())
                        .OrderBy(x => x.Name, OrderingType.AlphaNumeric)
                        .Where(x => x.Age > 2)
                        .ToList();

                    Assert.Equal(queryResult[0].Name, "Brian");
                    Assert.Equal(queryResult[1].Name, "Django");
                    Assert.Equal(queryResult[2].Name, "Lassie");
                    Assert.Equal(queryResult[3].Name, "Snoopy");
                }
            }
        }

        [RavenTheory(RavenTestCategory.Querying | RavenTestCategory.Sharding)]
        [RavenData(DatabaseMode = RavenDatabaseMode.Sharded, SearchEngineMode = RavenSearchEngineMode.All)]
        public void Simple_Projection_With_Order_By(Options options)
        {
            using (var store = GetDocumentStore(options))
            {
                store.ExecuteIndex(new UserMapIndex());

                using (var session = store.OpenSession())
                {
                    session.Store(new User { Name = "Grisha", Age = 1 }, "users/1");
                    session.Store(new User { Name = "Igal", Age = 2 }, "users/2");
                    session.Store(new User { Name = "Egor", Age = 3 }, "users/3");
                    session.SaveChanges();

                    Indexes.WaitForIndexing(store);

                    var queryResult = session.Query<UserMapIndex.Result, UserMapIndex>()
                        .OrderBy(x => x.Name)
                        .As<User>()
                        .Select(x => new
                        {
                            x.Age
                        })
                        .ToList();

                    Assert.Equal(3, queryResult.Count);
                    Assert.Equal(3, queryResult[0].Age);
                    Assert.Equal(1, queryResult[1].Age);
                    Assert.Equal(2, queryResult[2].Age);
                }
            }
        }
        
        [RavenFact(RavenTestCategory.Querying | RavenTestCategory.Sharding)]
        public void Simple_Projection_With_Order_By2()
        {
            using (var store = Sharding.GetDocumentStore())
            {
                store.ExecuteIndex(new UserMapIndex());

                using (var session = store.OpenSession())
                {
                    session.Store(new User { Name = "Grisha", Age = 1 }, "users/1");
                    session.Store(new User { Name = "Igal", Age = 2 }, "users/2");
                    session.Store(new User { Name = "Egor", Age = 3 }, "users/3");
                    session.SaveChanges();

                    Indexes.WaitForIndexing(store);

                    var queryResult = (from user in session.Query<User, UserMapIndex>()
                                       let age = user.Age
                                       orderby user.Name
                                       select new AgeResult
                                       {
                                           Age = age
                                       })
                        .ToList();

                    Assert.Equal(3, queryResult.Count);
                    Assert.Equal(3, queryResult[0].Age);
                    Assert.Equal(1, queryResult[1].Age);
                    Assert.Equal(2, queryResult[2].Age);
                }
            }
        }

        [RavenFact(RavenTestCategory.Querying | RavenTestCategory.Sharding)]
        public void Simple_Projection_With_Order_By_AlphaNumeric()
        {
            using (var store = Sharding.GetDocumentStore())
            {
                store.ExecuteIndex(new UserMapIndex());

                using (var session = store.OpenSession())
                {
                    session.Store(new User { Name = "a1", Age = 1 }, "users/1");
                    session.Store(new User { Name = "a2", Age = 2 }, "users/2");
                    session.Store(new User { Name = "a10", Age = 3 }, "users/3");
                    session.SaveChanges();

                    Indexes.WaitForIndexing(store);

                    var queryResult = session.Query<UserMapIndex.Result, UserMapIndex>()
                        .OrderBy(x => x.Name, OrderingType.AlphaNumeric)
                        .As<User>()
                        .Select(x => new
                        {
                            x.Age
                        })
                        .ToList();

                    Assert.Equal(3, queryResult.Count);
                    Assert.Equal(1, queryResult[0].Age);
                    Assert.Equal(2, queryResult[1].Age);
                    Assert.Equal(3, queryResult[2].Age);
                }
            }
        }

        [RavenFact(RavenTestCategory.Querying | RavenTestCategory.Sharding)]
        public void Simple_Projection_With_Order_By_And_Raw_Query()
        {
            using (var store = Sharding.GetDocumentStore())
            {
                store.ExecuteIndex(new UserMapIndex());

                using (var session = store.OpenSession())
                {
                    session.Store(new User { Name = "Grisha", Age = 1 }, "users/1");
                    session.Store(new User { Name = "Igal", Age = 2 }, "users/2");
                    session.Store(new User { Name = "Egor", Age = 3 }, "users/3");
                    session.SaveChanges();

                    Indexes.WaitForIndexing(store);

                    var queryResult = (session.Advanced.RawQuery<AgeResult>(@$"from index {new UserMapIndex().IndexName} as user
order by user.Name
select {{
    Age: user.Age
}}
")).ToList();

                    Assert.Equal(3, queryResult.Count);
                    Assert.Equal(3, queryResult[0].Age);
                    Assert.Equal(1, queryResult[1].Age);
                    Assert.Equal(2, queryResult[2].Age);
                }
            }
        }

        [RavenTheory(RavenTestCategory.Querying | RavenTestCategory.Sharding)]
        [InlineData("long")]
        [InlineData("double")]
        public void Auto_Map_With_Order_By_Multiple_Results(string sortType)
        {
            using (var store = Sharding.GetDocumentStore())
            {
                using (var session = store.OpenSession())
                {
                    session.Store(new Order
                    {
                        Freight = 20.8m,
                        Lines = new List<OrderLine>
                        {
                            new()
                            {
                                Discount = 0.2m
                            },
                            new()
                            {
                                Discount = 0.4m
                            }
                        }
                    });
                    session.Store(new Order
                    {
                        Freight = 10.7m,
                        Lines = new List<OrderLine>
                        {
                            new()
                            {
                                Discount = 0.3m
                            },
                            new()
                            {
                                Discount = 0.5m
                            }
                        }
                    });
                    session.SaveChanges();

                    var queryResult = session.Advanced.RawQuery<OrderLine>(
                            $@"
declare function project(o) {{
    return o.Lines;
}}

from Orders as o
order by Freight as {sortType}
select project(o)")
                        .ToList();

                    Assert.Equal(4, queryResult.Count);
                    Assert.Equal(0.3m, queryResult[0].Discount);
                    Assert.Equal(0.5m, queryResult[1].Discount);
                    Assert.Equal(0.2m, queryResult[2].Discount);
                    Assert.Equal(0.4m, queryResult[3].Discount);
                }
            }
        }

        [RavenTheory(RavenTestCategory.Querying | RavenTestCategory.Sharding)]
        [RavenData("long", DatabaseMode = RavenDatabaseMode.Sharded, SearchEngineMode = RavenSearchEngineMode.All)]
        [RavenData("double", DatabaseMode = RavenDatabaseMode.Sharded, SearchEngineMode = RavenSearchEngineMode.All)]
        public void Map_Index_With_Order_By_Multiple_Results(Options options, string sortType)
        {
            using (var store = GetDocumentStore(options))
            {
                store.ExecuteIndex(new OrderMapIndex());

                using (var session = store.OpenSession())
                {
                    session.Store(new Order
                    {
                        Freight = 20.5m,
                        Lines = new List<OrderLine>
                        {
                            new()
                            {
                                Discount = 0.2m
                            },
                            new()
                            {
                                Discount = 0.4m
                            }
                        }
                    });
                    session.Store(new Order
                    {
                        Freight = 10.3m,
                        Lines = new List<OrderLine>
                        {
                            new()
                            {
                                Discount = 0.3m
                            },
                            new()
                            {
                                Discount = 0.5m
                            }
                        }
                    });
                    session.SaveChanges();

                    Indexes.WaitForIndexing(store);

                    var queryResult = session.Advanced.RawQuery<OrderLine>(
                            $@"
declare function project(o) {{
    return o.Lines;
}}

from index 'OrderMapIndex' as o
order by o.Freight as {sortType}
                    select project(o)")
                        .ToList();

                    Assert.Equal(4, queryResult.Count);
                    Assert.Equal(0.3m, queryResult[0].Discount);
                    Assert.Equal(0.5m, queryResult[1].Discount);
                    Assert.Equal(0.2m, queryResult[2].Discount);
                    Assert.Equal(0.4m, queryResult[3].Discount);
                }
            }
        }

        [RavenFact(RavenTestCategory.Querying | RavenTestCategory.Sharding)]
        public void BasicInclude()
        {
            using (var store = Sharding.GetDocumentStore())
            {
                using (var session = store.OpenSession())
                {
                    session.Store(new Dog {Owner = "users/1"}, "dogs/1");
                    session.Store(new Dog {Owner = "users/2"}, "dogs/2");
                    session.Store(new Dog {Owner = "users/3"}, "dogs/3");

                    session.Store(new User {Count = 7}, "users/1");
                    session.Store(new User {Count = 19}, "users/2");
                    session.Store(new User {Count = 13}, "users/3");

                    session.SaveChanges();
                }

                using (var session = store.OpenSession())
                {
                    var queryResult = session.Query<Dog>()
                        .Include<Dog, User>(x => x.Owner)
                        .ToList();

                    Assert.Equal(3, queryResult.Count);

                    var numberOfRequests = session.Advanced.NumberOfRequests;
                    Assert.Equal(1, numberOfRequests);

                    for (var i = 0; i < 3; i++)
                    {
                        Assert.NotNull(session.Load<User>($"users/{i + 1}"));
                    }

                    Assert.Equal(numberOfRequests, session.Advanced.NumberOfRequests);
                }
            }
        }

        [RavenFact(RavenTestCategory.Querying | RavenTestCategory.Sharding)]
        public void BasicIncludeWithSpecificIds()
        {
            using (var store = Sharding.GetDocumentStore())
            {
                using (var session = store.OpenSession())
                {
                    session.Store(new Dog { Owner = "users/1" }, "dogs/1");
                    session.Store(new Dog { Owner = "users/2" }, "dogs/2");
                    session.Store(new Dog { Owner = "users/3" }, "dogs/3");

                    session.Store(new User { Count = 7 }, "users/1");
                    session.Store(new User { Count = 19 }, "users/2");
                    session.Store(new User { Count = 13 }, "users/3");

                    session.SaveChanges();
                }

                using (var session = store.OpenSession())
                {
                    var queryResult = session.Query<Dog>()
                        .Where(x => x.Id.In(new[] { "dogs/1", "dogs/2", "dogs/3" }))
                        .Include<Dog, User>(x => x.Owner)
                        .ToList();

                    Assert.Equal(3, queryResult.Count);

                    var numberOfRequests = session.Advanced.NumberOfRequests;
                    Assert.Equal(1, numberOfRequests);

                    for (var i = 0; i < 3; i++)
                    {
                        Assert.NotNull(session.Load<User>($"users/{i + 1}"));
                    }

                    Assert.Equal(numberOfRequests, session.Advanced.NumberOfRequests);
                }
            }
        }

        [RavenFact(RavenTestCategory.Querying | RavenTestCategory.Sharding)]
        public void BasicQueryWithSpecificIds()
        {
            using (var store = Sharding.GetDocumentStore())
            {
                using (var session = store.OpenSession())
                {
                    session.Store(new Dog { Owner = "users/1" }, "dogs/1");
                    session.Store(new Dog { Owner = "users/2" }, "dogs/2");
                    session.Store(new Dog { Owner = "users/3" }, "dogs/3");

                    session.SaveChanges();
                }

                using (var session = store.OpenSession())
                {
                    var queryResult = session.Query<Dog>()
                        .Where(x => x.Id.In(new[] { "dogs/1", "dogs/2", "dogs/3" }))
                        .ToList();

                    Assert.Equal(3, queryResult.Count);

                    for (var i = 0; i < 3; i++)
                    {
                        Assert.NotNull(session.Load<Dog>($"dogs/{i + 1}"));
                    }
                }
            }
        }

        [RavenFact(RavenTestCategory.Querying | RavenTestCategory.Sharding)]
        public void QueryWithSkipTake()
        {
            using (var store = Sharding.GetDocumentStore())
            {
                store.ExecuteIndex(new UserMapIndex());

                using (var session = store.OpenSession())
                {
                    session.Store(new User { Name = "f", Age = 6 }, "users/1");
                    session.Store(new User { Name = "e", Age = 5 }, "users/2");
                    session.Store(new User { Name = "d", Age = 4 }, "users/3");
                    session.Store(new User { Name = "c", Age = 3 }, "users/4");
                    session.Store(new User { Name = "b", Age = 2 }, "users/5");
                    session.Store(new User { Name = "a", Age = 1 }, "users/6");
                    session.SaveChanges();

                    Indexes.WaitForIndexing(store);

                    var queryResult = session.Query<UserMapIndex.Result, UserMapIndex>()
                        .OrderBy(x => x.Name)
                        .As<User>()
                        .Skip(2)
                        .Take(3)
                        .ToList();

                    Assert.Equal(3, queryResult.Count);
                    Assert.Equal("c", queryResult[0].Name);
                    Assert.Equal("d", queryResult[1].Name);
                    Assert.Equal("e", queryResult[2].Name);
                }
            }
        }

        [RavenFact(RavenTestCategory.Querying | RavenTestCategory.Sharding)]
        public void Simple_collection_query_with_projection()
        {
            using (var store = Sharding.GetDocumentStore())
            {
                using (var session = store.OpenSession())
                {
                    session.Store(new User { Name = "Grisha", Age = 1 }, "users/1");
                    session.SaveChanges();

                    var queryResult = session.Query<User>()
                        .Where(x => x.Id == "users/1")
                        .Select(x => new
                        {
                            x.Age
                        })
                        .ToList();

                    Assert.Equal(1, queryResult.Count);
                    Assert.Equal(1, queryResult[0].Age);

                    var queryResult2 = session.Query<User>()
                        .Where(x => x.Id == "users/1")
                        .Select(x => new
                        {
                            x.Age,
                            x.Name
                        })
                        .ToList();

                    Assert.Equal(1, queryResult2.Count);
                    Assert.Equal(1, queryResult2[0].Age);
                    Assert.Equal("Grisha", queryResult2[0].Name);

                    var queryResult3 = session.Advanced.RawQuery<User>("from Users as u where id() = 'users/1' select { Age: u.Age, Name: u.Name }")
                        .ToList();

                    Assert.Equal(1, queryResult3.Count);
                    Assert.Equal(1, queryResult3[0].Age);
                    Assert.Equal("Grisha", queryResult3[0].Name);
                }
            }
        }

        [RavenFact(RavenTestCategory.Querying | RavenTestCategory.Sharding)]
        public void Simple_collection_query_with_projection_and_order_by()
        {
            using (var store = Sharding.GetDocumentStore())
            {
                using (var session = store.OpenSession())
                {
                    session.Store(new User { Name = "Grisha", Age = 1 }, "users/1");
                    session.Store(new User { Name = "Adam", Age = 2 }, "users/2");
                    session.Store(new User { Name = "Carlos", Age = 3 }, "users/3");
                    session.SaveChanges();

                    var queryResult = session.Query<User>()
                        .Where(x => x.Id == "users/1" || x.Id == "users/2" || x.Id == "users/3")
                        .OrderBy(x => x.Name)
                        .Customize(x => x.WaitForNonStaleResults())
                        .Select(x => new
                        {
                            x.Age
                        })
                        .ToList();

                    Assert.Equal(3, queryResult.Count);
                    Assert.Equal(2, queryResult[0].Age);
                    Assert.Equal(3, queryResult[1].Age);
                    Assert.Equal(1, queryResult[2].Age);

                    var queryResult2 = session.Query<User>()
                        .Where(x => x.Id == "users/1" || x.Id == "users/2" || x.Id == "users/3")
                        .OrderBy(x => x.Name)
                        .Customize(x => x.WaitForNonStaleResults())
                        .Select(x => new
                        {
                            x.Age,
                            x.Name
                        })
                        .ToList();

                    Assert.Equal(3, queryResult2.Count);
                    Assert.Equal(2, queryResult2[0].Age);
                    Assert.Equal("Adam", queryResult2[0].Name);
                    Assert.Equal(3, queryResult2[1].Age);
                    Assert.Equal("Carlos", queryResult2[1].Name);
                    Assert.Equal(1, queryResult2[2].Age);
                    Assert.Equal("Grisha", queryResult2[2].Name);

                    var queryResult3 = session.Advanced.RawQuery<User>("from Users as u where id() = 'users/1' or id() = 'users/2' or id() = 'users/3' select { Age: u.Age, Name: u.Name }")
                        .ToList();

                    // all users were saved in single batched, so it is hard to determine which one will be saved first
                    // because we are saving them in parallel
                    // and we are ordering by @last-modified by default
                    queryResult3 = queryResult3
                        .OrderBy(x => x.Age)
                        .ToList();

                    Assert.Equal(3, queryResult3.Count);
                    Assert.Equal(2, queryResult3[1].Age);
                    Assert.Equal("Adam", queryResult3[1].Name);
                    Assert.Equal(3, queryResult3[2].Age);
                    Assert.Equal("Carlos", queryResult3[2].Name);
                    Assert.Equal(1, queryResult3[0].Age);
                    Assert.Equal("Grisha", queryResult3[0].Name);
                }
            }
        }

        private class AgeResult
        {
            public int Age { get; set; }
        }

        private class Dog
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string Owner { get; set; }
            public string Breed { get; set; }
            public string Color { get; set; }
            public int Age { get; set; }
            public bool IsVaccinated { get; set; }
        }

        private class DogsIndex : AbstractIndexCreationTask<Dog>
        {
            public class Result
            {
                public string Name { get; set; }
                public int Age { get; set; }
                public bool IsVaccinated { get; set; }
            }

            public DogsIndex()
            {
                Map = dogs => from dog in dogs
                              select new
                              {
                                  dog.Name,
                                  dog.Age,
                                  dog.IsVaccinated
                              };
            }
        }

        private class UserMapIndex : AbstractIndexCreationTask<User>
        {
            public class Result
            {
                public string Name;
            }

            public UserMapIndex()
            {
                Map = users =>
                    from user in users
                    select new Result
                    {
                        Name = user.Name
                    };
            }
        }

        private class OrderMapIndex : AbstractIndexCreationTask<Order>
        {
            public class Result
            {
                public decimal Freight;
            }

            public OrderMapIndex()
            {
                Map = orders =>
                    from order in orders
                    select new Result
                    {
                        Freight = order.Freight
                    };
            }
        }
    }
}
