﻿using System.Linq;
using System.Threading;
using FastTests;
using Raven.Client.Documents.Operations;
using Raven.Server.Config;
using Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace SlowTests.Server.Documents.Indexing.Auto
{
    public class RavenDB_8713 : RavenTestBase
    {
        public RavenDB_8713(ITestOutputHelper output) : base(output)
        {
        }

        [RavenTheory(RavenTestCategory.Querying)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.All, DatabaseMode = RavenDatabaseMode.All)]
        public void CanQueryOnCaseSensitiveFields(Options options)
        {
            using (var store = GetDocumentStore(options))
            {
                using (var session = store.OpenSession())
                {
                    session.Store(new Item
                    {
                        Name = "joe",
                        name = "doe"
                    });

                    session.Store(new Item
                    {
                        Name = "ja",
                        name = "da"
                    });

                    session.SaveChanges();

                    var count = session.Query<Item>().Statistics(out var stats).Count(x => x.Name == "joe" || x.name == "da");

                    Assert.Equal(2, count);
                    Assert.Equal("Auto/Items/ByNameAndname", stats.IndexName);
                }
            }
        }

        [RavenTheory(RavenTestCategory.Querying)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.All, DatabaseMode = RavenDatabaseMode.All)]
        public void ShouldExtendMappingDueToCaseSensitiveFields(Options options)
        {
            options.ModifyDatabaseRecord = record => record.Settings[RavenConfiguration.GetKey(x => x.Indexing.TimeBeforeDeletionOfSupersededAutoIndex)] = "0";

            using (var store = GetDocumentStore(options))
            {
                using (var session = store.OpenSession())
                {
                    session.Store(new Item
                    {
                        Name = "joe",
                        name = "doe"
                    });

                    session.Store(new Item
                    {
                        Name = "ja",
                        name = "da"
                    });

                    session.SaveChanges();

                    var count = session.Query<Item>().Statistics(out var stats).Count(x => x.Name == "joe");

                    Assert.Equal(1, count);
                    Assert.Equal("Auto/Items/ByName", stats.IndexName);

                    // should extend mapping and remove prev index

                    var results = session.Query<Item>().Statistics(out stats).Count(x => x.Name == "joe" || x.name == "da");

                    Assert.Equal(2, results);
                    Assert.Equal("Auto/Items/ByNameAndname", stats.IndexName);
                }

                IndexInformation[] indexes = null;

                Assert.True(SpinWait.SpinUntil(() => (indexes = store.Maintenance.Send(new GetStatisticsOperation()).Indexes).Length == 1, 1000));

                Assert.Equal(1, indexes.Length);
                Assert.Equal("Auto/Items/ByNameAndname", indexes[0].Name);
            }
        }

        [RavenTheory(RavenTestCategory.Querying)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.All, DatabaseMode = RavenDatabaseMode.All)]
        public void CanQueryOnCaseSensitiveGroupByFields(Options options)
        {
            using (var store = GetDocumentStore(options))
            {
                using (var session = store.OpenSession())
                {
                    session.Store(new Item
                    {
                        Name = "joe",
                        name = "doe"
                    });

                    session.Store(new Item
                    {
                        Name = "joe",
                        name = "doe"
                    });


                    session.Store(new Item
                    {
                        Name = "ja",
                        name = "da"
                    });

                    session.SaveChanges();

                    var results = session.Query<Item>().Statistics(out var stats).GroupBy(x => new {x.name, x.Name}).Select(g => new
                    {
                        g.Key.name,
                        g.Key.Name,
                        Count = g.Count()
                    }).OrderBy(x => x.Count).ToList();

                    Assert.Equal(2, results.Count);

                    Assert.Equal(1, results[0].Count);
                    Assert.Equal("ja", results[0].Name);
                    Assert.Equal("da", results[0].name);

                    Assert.Equal(2, results[1].Count);
                    Assert.Equal("joe", results[1].Name);
                    Assert.Equal("doe", results[1].name);
                }
            }
        }

        [RavenTheory(RavenTestCategory.Querying)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.All, DatabaseMode = RavenDatabaseMode.All)]
        public void CanQueryOnCaseSensitiveFields_UsingSearch(Options options)
        {
            using (var store = GetDocumentStore(options))
            {
                using (var session = store.OpenSession())
                {
                    session.Store(new Item
                    {
                        Name = "joe",
                        name = "doe"
                    });

                    session.Store(new Item
                    {
                        Name = "ja",
                        name = "da"
                    });

                    session.SaveChanges();

                    var count = session.Advanced.DocumentQuery<Item>().WhereEquals(x => x.Name, "joe").OrElse().Search(x => x.name, "d*").Statistics(out var stats).Count();

                    Assert.Equal(2, count);
                    Assert.Equal("Auto/Items/ByNameAndSearch(name)", stats.IndexName);
                }
            }
        }

        private class Item
        {
            public string Name { get; set; }

            public string name { get; set; }

        }
    }
}
