// -----------------------------------------------------------------------
//  <copyright file="RavenDB_1595.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------
using System.IO;
using Raven.Abstractions.Data;
using Raven.Client.Document;
using Raven.Database.Extensions;
using Raven.Server;
using Raven.Tests.Common;

using Xunit;

namespace Raven.Tests.Issues
{
    public class RavenDB_1595 : RavenTest
    {
        private RavenDbServer server1;
        private DocumentStore store1;

        [Fact]
        public void ProperlyHandleDataDirectoryWhichEndsWithSlash()
        {
            server1 = CreateServer(8079, "D1");

            store1 = new DocumentStore
            {
                DefaultDatabase = "Northwind",
                Url = "http://localhost:8079"
            };

            store1.Initialize(ensureDatabaseExists: false);

            store1.DatabaseCommands.GlobalAdmin.CreateDatabase(
                new DatabaseDocument
                {
                    Id = "Northwind",
                    Settings = { { "Raven/DataDir", @"~\D1\N" } }
                });

            store1.DatabaseCommands.ForDatabase("Northwind").Get("force/load/of/db");

            Assert.True(
                Directory.Exists(Path.Combine(server1.SystemDatabase.Configuration.Core.DataDirectory,
                                              "N")));


        }

        private RavenDbServer CreateServer(int port, string dataDirectory, bool removeDataDirectory = true)
        {
            Database.Server.NonAdminHttp.EnsureCanListenToWhenInNonAdminContext(port);

            var serverConfiguration = new Database.Config.RavenConfiguration
            {
                AnonymousUserAccessMode = Database.Server.AnonymousUserAccessMode.Admin,
                RunInUnreliableYetFastModeThatIsNotSuitableForProduction = true,
                Core =
                {
                    RunInMemory = false,
                    DataDirectory = dataDirectory,
                    Port = port,
                },
                DefaultStorageTypeName = "voron"
            };

            if (removeDataDirectory)
                IOExtensions.DeleteDirectory(serverConfiguration.Core.DataDirectory);

            var server = new RavenDbServer(serverConfiguration)
            {
                UseEmbeddedHttpServer = true
            };
            server.Initialize();
            serverConfiguration.PostInit();

            return server;
        }
        public override void Dispose()
        {
            if (server1 != null)
            {
                server1.Dispose();
                if (server1.SystemDatabase != null)
                    IOExtensions.DeleteDirectory(server1.SystemDatabase.Configuration.Core.DataDirectory);
            }

            if (store1 != null)
            {
                store1.Dispose();
            }
        }
    }

}
