using Raven.Abstractions.Data;

using Raven.Abstractions.MEF;
using Raven.Database.Config;
using System;
using Raven.Database.Extensions;
using System.Collections.Generic;
using Raven.Database.FileSystem.Infrastructure;
using System.Collections.Specialized;
using Raven.Database.FileSystem.Plugins;
using System.IO;
using Raven.Database.FileSystem.Storage;
using System.Threading;

namespace Raven.Tests.FileSystem.Storage
{
    public abstract class StorageAccessorTestBase : IDisposable
    {
        private static int pathCount;

        private readonly IList<string> pathsToDelete;

        private readonly IList<ITransactionalStorage> storages;

        public static IEnumerable<object[]> Storages
        {
            get
            {
                return new[]
                {
                    new object[] {"voron"},
                };
            }
        }

        protected StorageAccessorTestBase()
        {
            pathsToDelete = new List<string>();
            storages = new List<ITransactionalStorage>();
        }

        protected string NewDataPath(string prefix = null, bool forceCreateDir = false)
        {
            if (prefix != null)
                prefix = prefix.Replace("<", "").Replace(">", "");

            var newDataDir = Path.GetFullPath(string.Format(@".\{1}-{0}-{2}\", DateTime.Now.ToString("yyyy-MM-dd,HH-mm-ss"), prefix ?? "TestDatabase", Interlocked.Increment(ref pathCount)));
            if (forceCreateDir && Directory.Exists(newDataDir) == false)
                Directory.CreateDirectory(newDataDir);
            pathsToDelete.Add(newDataDir);
            return newDataDir;
        }

        protected ITransactionalStorage NewTransactionalStorage(string requestedStorage, bool runInMemory = true, string path = null)
        {
            path = path ?? NewDataPath();

            var configuration = new InMemoryRavenConfiguration
            {
                FileSystem =
                {
                    DataDirectory = path
                },
                Settings = new NameValueCollection
                           {
                               { Constants.RunInMemory, runInMemory.ToString() }
                           }
            };

            ITransactionalStorage storage;

            switch (requestedStorage)
            {
                case "voron":
                    storage = new Raven.Database.FileSystem.Storage.Voron.TransactionalStorage(configuration);
                    break;
                default:
                    throw new NotSupportedException(string.Format("Given storage type ({0}) is not supported.", requestedStorage));
            }

            storages.Add(storage);
            storage.Initialize(new UuidGenerator(), new OrderedPartCollection<AbstractFileCodec>());

            return storage;
        }

        public void Dispose()
        {
            var exceptions = new List<Exception>();

            foreach (var storage in storages)
            {
                try
                {
                    storage.Dispose();
                }
                catch (Exception e)
                {
                    exceptions.Add(e);
                }
            }

            foreach (var path in pathsToDelete)
            {
                try
                {
                    IOExtensions.DeleteDirectory(path);
                }
                catch (Exception e)
                {
                    exceptions.Add(e);
                }
            }

            if (exceptions.Count > 0)
                throw new AggregateException("There was an error during test disposal.", exceptions);
        }
    }
}
