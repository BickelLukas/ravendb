﻿using System;
using System.Threading.Tasks;
using FastTests;
using Orders;
using Raven.Client.Documents.Operations.ETL;
using Raven.Client.Documents.Operations.ETL.OLAP;
using Raven.Server.Documents.ETL.Providers.OLAP;
using Raven.Server.Documents.ETL.Providers.OLAP.Test;
using Raven.Server.ServerWide.Context;
using Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace SlowTests.Server.Documents.ETL.Olap
{
    public class RavenDB_17263 : RavenTestBase
    {
        public RavenDB_17263(ITestOutputHelper output) : base(output)
        {
        }

        [RavenTheory(RavenTestCategory.Etl)]
        [RavenData(DatabaseMode = RavenDatabaseMode.All)]
        public async Task ShouldNotReuseCustomPartitionFromPreviousTestRun(Options options)
        {
            using (var store = GetDocumentStore(options))
            {
                var baseline = new DateTime(2020, 1, 1);

                using (var session = store.OpenAsyncSession())
                {
                    for (int i = 0; i < 31; i++)
                    {
                        await session.StoreAsync(new Order
                        {
                            Id = $"orders/{i}",
                            OrderedAt = baseline.AddDays(i),
                            ShipVia = $"shippers/{i}",
                            Company = $"companies/{i}"
                        });
                    }

                    for (int i = 0; i < 28; i++)
                    {
                        await session.StoreAsync(new Order
                        {
                            Id = $"orders/{i + 31}",
                            OrderedAt = baseline.AddMonths(1).AddDays(i),
                            ShipVia = $"shippers/{i + 31}",
                            Company = $"companies/{i + 31}"
                        });
                    }

                    await session.SaveChangesAsync();
                }

                var database = await Etl.GetDatabaseFor(store, "orders/1");
                var configuration = new OlapEtlConfiguration
                {
                    Name = "simulate",
                    Transforms =
                    {
                        new Transformation
                        {
                            Collections = { "Orders" },
                            Name = "MonthlyOrders",
                            Script =
                                @"
                                    var orderDate = new Date(this.OrderedAt);
                                    var year = orderDate.getFullYear();
                                    var month = orderDate.getMonth();
                                    var key = new Date(year, month);

                                    loadToOrders(partitionBy(['order_date', key], ['location', $customPartitionValue]),
                                    {
                                        Company : this.Company,
                                        ShipVia : this.ShipVia
                                    });
                                    "
                        }
                    },
                    CustomPartitionValue = "USA"
                };

                using (database.DocumentsStorage.ContextPool.AllocateOperationContext(out DocumentsOperationContext context))
                {
                    var testResult = OlapEtl.TestScript(new TestOlapEtlScript
                    {
                        DocumentId = "orders/1",
                        Configuration = configuration
                    }, database, database.ServerStore, context);
                    
                    var result = (OlapEtlTestScriptResult)testResult;

                    Assert.Equal(1, result.ItemsByPartition.Count);

                    Assert.Equal(4, result.ItemsByPartition[0].Columns.Count);

                    Assert.Equal("Orders/order_date=2020-01-01-00-00/location=USA", result.ItemsByPartition[0].Key);
                }

                configuration.CustomPartitionValue = null;

                using (database.DocumentsStorage.ContextPool.AllocateOperationContext(out DocumentsOperationContext context))
                {
                    var testResult = OlapEtl.TestScript(new TestOlapEtlScript
                    {
                        DocumentId = "orders/1",
                        Configuration = configuration
                    }, database, database.ServerStore, context);
                    
                    var result = (OlapEtlTestScriptResult)testResult;

                    Assert.Equal(1, result.ItemsByPartition.Count);

                    Assert.Equal(4, result.ItemsByPartition[0].Columns.Count);

                    Assert.Equal("Orders/order_date=2020-01-01-00-00/location=undefined", result.ItemsByPartition[0].Key);
                }

            }
        }
    }
}
