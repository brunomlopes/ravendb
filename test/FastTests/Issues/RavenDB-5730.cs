﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FastTests.Server.Replication;
using Raven.Abstractions.Data;
using Raven.Abstractions.Replication;
using Raven.NewClient.Client.Document;
using Xunit;

namespace FastTests.Issues
{
    public class RavenDB_5730 : ReplicationTestsBase
    {
        public class User
        {
            public string Name { get; set; }
        }

        [Fact]
        public void Whispace_at_the_beginning_of_replication_destination_url_should_not_cause_issues()
        {
            using (var storeA = GetDocumentStore())
            using (var storeB = GetDocumentStore())
            {
                var url = " " + storeB.Url;
                DoReplicationTest(storeA, storeB, url);
            }
        }

        [Fact]
        public void Whispace_at_the_end_of_replication_destination_url_should_not_cause_issues()
        {
            using (var storeA = GetDocumentStore())
            using (var storeB = GetDocumentStore())
            {
                var url = storeB.Url + " ";
                DoReplicationTest(storeA, storeB, url);
            }
        }

        [Fact]
        public void Whispace_at_the_beginning_and_end_of_replication_destination_url_should_not_cause_issues()
        {
            using (var storeA = GetDocumentStore())
            using (var storeB = GetDocumentStore())
            {
                var url = storeB.Url + " ";
                DoReplicationTest(storeA, storeB, url);
            }
        }       

        private void DoReplicationTest(DocumentStore storeA, DocumentStore storeB, string url)
        {
            using (var session = storeA.OpenSession())
            {
                var destinations = new List<ReplicationDestination>
                {
                    new ReplicationDestination
                    {
                        Database = storeB.DefaultDatabase,
                        Url = url, //whitespace at the start of url
                    }
                };

                session.Store(new ReplicationDocument
                {
                    Destinations = destinations
                }, Constants.Replication.DocumentReplicationConfiguration);
                session.SaveChanges();
            }

            using (var session = storeA.OpenSession())
            {
                session.Store(new User {Name = "foo/bar"}, "foo-id");
                session.SaveChanges();
            }

            Assert.True(WaitForDocument(storeB, "foo-id"));
        }
    }
}
