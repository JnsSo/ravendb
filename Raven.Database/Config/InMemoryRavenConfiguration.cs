//-----------------------------------------------------------------------
// <copyright file="InMemoryRavenConfiguration.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.ComponentModel.Composition.Hosting;
using System.ComponentModel.Composition.Primitives;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Caching;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Raven.Abstractions.Data;
using Raven.Abstractions.Util.Encryptors;
using Raven.Database.Extensions;
using Raven.Database.Indexing;
using Raven.Database.Plugins.Catalogs;
using Raven.Database.Server;
using Raven.Database.FileSystem.Util;
using Raven.Database.Storage;
using Raven.Database.Util;
using Raven.Imports.Newtonsoft.Json;
using Enum = System.Enum;
using Raven.Abstractions;
using Raven.Abstractions.Extensions;
using Raven.Client.Util;
using Raven.Database.Config.Attributes;
using Raven.Database.Config.Settings;

namespace Raven.Database.Config
{
    public class InMemoryRavenConfiguration
    {
        public const string VoronTypeName = "voron";

        private CompositionContainer container;
        private bool containerExternallySet;

        public CoreConfiguration Core { get; }

        public ReplicationConfiguration Replication { get; }

        public PrefetcherConfiguration Prefetcher { get; }

        public StorageConfiguration Storage { get; }

        public FileSystemConfiguration FileSystem { get; }

        public CounterConfiguration Counter { get; }
        
        public TimeSeriesConfiguration TimeSeries { get; }

        public EncryptionConfiguration Encryption { get; }

        public IndexingConfiguration Indexing { get; set; }

        public ClusterConfiguration Cluster { get; }

        public MonitoringConfiguration Monitoring { get; }

        public WebSocketsConfiguration WebSockets { get; set; }

        public QueryConfiguration Queries { get; }

        public PatchingConfiguration Patching { get;  }

        public BulkInsertConfiguration BulkInsert { get; }

        public ServerConfiguration Server { get; }

        public MemoryConfiguration Memory { get; }

        public FacetsConfiguration Facets { get; }

        public InMemoryRavenConfiguration()
        {
            
            Replication = new ReplicationConfiguration();
            Prefetcher = new PrefetcherConfiguration();
            Storage = new StorageConfiguration();
            FileSystem = new FileSystemConfiguration();
            Counter = new CounterConfiguration();
            TimeSeries = new TimeSeriesConfiguration();
            Encryption = new EncryptionConfiguration();
            Indexing = new IndexingConfiguration();
            WebSockets = new WebSocketsConfiguration();
            Cluster = new ClusterConfiguration();
            Monitoring = new MonitoringConfiguration();
            Queries = new QueryConfiguration();
            Patching = new PatchingConfiguration();
            BulkInsert = new BulkInsertConfiguration();
            Server = new ServerConfiguration();
            Memory = new MemoryConfiguration();

            Settings = new NameValueCollection(StringComparer.OrdinalIgnoreCase);
            Core = new CoreConfiguration(this);

            CreatePluginsDirectoryIfNotExisting = true;
            CreateAnalyzersDirectoryIfNotExisting = true;

            IndexingClassifier = new DefaultIndexingClassifier();

            Catalog = new AggregateCatalog(CurrentAssemblyCatalog);

            Catalog.Changed += (sender, args) => ResetContainer();
        }

        public string DatabaseName { get; set; }

        public string FileSystemName { get; set; }

        public string CounterStorageName { get; set; }

        public string TimeSeriesName { get; set; }

        public void PostInit()
        {
            CheckDirectoryPermissions();

            FilterActiveBundles();

            SetupOAuth();

            SetupGC();
        }

        public InMemoryRavenConfiguration Initialize()
        {
            int defaultMaxNumberOfItemsToIndexInSingleBatch = -1;
            int defaultInitialNumberOfItemsToIndexInSingleBatch = Environment.Is64BitProcess ? 512 : 256;

            var ravenSettings = new StronglyTypedRavenSettings(Settings);
            ravenSettings.Setup(defaultMaxNumberOfItemsToIndexInSingleBatch, defaultInitialNumberOfItemsToIndexInSingleBatch);
            
            

            var configurations = GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(x => x.Type().BaseType == typeof(ConfigurationBase));

            //foreach (var configuration in configurations)
            //{
            //	configuration.
            //}

            Core.Initialize(Settings);
            Replication.Initialize(Settings);
            Queries.Initialize(Settings);
            Patching.Initialize(Settings);
            BulkInsert.Initialize(Settings);
            Server.Initialize(Settings);
            Memory.Initialize(Settings);
            Indexing.Initialize(Settings);
            Prefetcher.Initialize(Settings);
            
            FileSystem.InitializeFrom(this);
            Counter.InitializeFrom(this);
            TimeSeries.InitializeFrom(this);

            if (ConcurrentMultiGetRequests == null)
                ConcurrentMultiGetRequests = new SemaphoreSlim(Server.MaxConcurrentMultiGetRequests);

            // Discovery
            DisableClusterDiscovery = ravenSettings.DisableClusterDiscovery.Value;

            ServerName = ravenSettings.ServerName.Value;

            MaxStepsForScript = ravenSettings.MaxStepsForScript.Value;
            AdditionalStepsForScriptBasedOnDocumentSize = ravenSettings.AdditionalStepsForScriptBasedOnDocumentSize.Value;
            TurnOffDiscoveryClient = ravenSettings.TurnOffDiscoveryClient.Value;

            // Index settings
            
            FlushIndexToDiskSizeInMb = ravenSettings.FlushIndexToDiskSizeInMb.Value;

            

            MaxIndexCommitPointStoreTimeInterval = ravenSettings.MaxIndexCommitPointStoreTimeInterval.Value;

            MinIndexingTimeIntervalToStoreCommitPoint = ravenSettings.MinIndexingTimeIntervalToStoreCommitPoint.Value;

            MaxNumberOfStoredCommitPoints = ravenSettings.MaxNumberOfStoredCommitPoints.Value;

            // Data settings

            if (string.IsNullOrEmpty(DefaultStorageTypeName))
            {
                DefaultStorageTypeName = ravenSettings.DefaultStorageTypeName.Value;
            }

            DatabaseOperationTimeout = ravenSettings.DatbaseOperationTimeout.Value;

            TimeToWaitBeforeRunningIdleIndexes = ravenSettings.TimeToWaitBeforeRunningIdleIndexes.Value;
            TimeToWaitBeforeMarkingAutoIndexAsIdle = ravenSettings.TimeToWaitBeforeMarkingAutoIndexAsIdle.Value;

            TimeToWaitBeforeMarkingIdleIndexAsAbandoned = ravenSettings.TimeToWaitBeforeMarkingIdleIndexAsAbandoned.Value;
            TimeToWaitBeforeRunningAbandonedIndexes = ravenSettings.TimeToWaitBeforeRunningAbandonedIndexes.Value;

            SetupTransactionMode();

            MaxRecentTouchesToRemember = ravenSettings.MaxRecentTouchesToRemember.Value;

            // HTTP settings
            
            if (string.IsNullOrEmpty(DatabaseName)) // we only use this for root database
            {
                Encryption.UseSsl = ravenSettings.Encryption.UseSsl.Value;
                Encryption.UseFips = ravenSettings.Encryption.UseFips.Value;
            }

            SetVirtualDirectory();
            

            AnonymousUserAccessMode = GetAnonymousUserAccessMode();
            
            // Misc settings

            AllowLocalAccessWithoutAuthorization = ravenSettings.AllowLocalAccessWithoutAuthorization.Value;
            RejectClientsMode = ravenSettings.RejectClientsModeEnabled.Value;

            Storage.Voron.MaxBufferPoolSize = Math.Max(2, ravenSettings.Voron.MaxBufferPoolSize.Value);
            Storage.Voron.InitialFileSize = ravenSettings.Voron.InitialFileSize.Value;
            Storage.Voron.MaxScratchBufferSize = ravenSettings.Voron.MaxScratchBufferSize.Value;
            Storage.Voron.ScratchBufferSizeNotificationThreshold = ravenSettings.Voron.ScratchBufferSizeNotificationThreshold.Value;
            Storage.Voron.AllowIncrementalBackups = ravenSettings.Voron.AllowIncrementalBackups.Value;
            Storage.Voron.TempPath = ravenSettings.Voron.TempPath.Value;
            Storage.Voron.JournalsStoragePath = ravenSettings.Voron.JournalsStoragePath.Value;
            Storage.Voron.AllowOn32Bits = ravenSettings.Voron.AllowOn32Bits.Value;

            Storage.PreventSchemaUpdate = ravenSettings.FileSystem.PreventSchemaUpdate.Value;
            
            Prefetcher.FetchingDocumentsFromDiskTimeoutInSeconds = ravenSettings.Prefetcher.FetchingDocumentsFromDiskTimeoutInSeconds.Value;
            Prefetcher.MaximumSizeAllowedToFetchFromStorageInMb = ravenSettings.Prefetcher.MaximumSizeAllowedToFetchFromStorageInMb.Value;

            Replication.FetchingFromDiskTimeoutInSeconds = ravenSettings.Replication.FetchingFromDiskTimeoutInSeconds.Value;
            Replication.ReplicationRequestTimeoutInMilliseconds = ravenSettings.Replication.ReplicationRequestTimeoutInMilliseconds.Value;
            Replication.ForceReplicationRequestBuffering = ravenSettings.Replication.ForceReplicationRequestBuffering.Value;
            Replication.MaxNumberOfItemsToReceiveInSingleBatch = ravenSettings.Replication.MaxNumberOfItemsToReceiveInSingleBatch.Value;

            FileSystem.MaximumSynchronizationInterval = ravenSettings.FileSystem.MaximumSynchronizationInterval.Value;
            FileSystem.DataDirectory = ravenSettings.FileSystem.DataDir.Value;
            FileSystem.IndexStoragePath = ravenSettings.FileSystem.IndexStoragePath.Value;
            if (string.IsNullOrEmpty(FileSystem.DefaultStorageTypeName))
                FileSystem.DefaultStorageTypeName = ravenSettings.FileSystem.DefaultStorageTypeName.Value;

            Counter.DataDirectory = ravenSettings.Counter.DataDir.Value;
            Counter.TombstoneRetentionTime = ravenSettings.Counter.TombstoneRetentionTime.Value;
            Counter.DeletedTombstonesInBatch = ravenSettings.Counter.DeletedTombstonesInBatch.Value;
            Counter.ReplicationLatencyInMs = ravenSettings.Counter.ReplicationLatencyInMs.Value;

            TimeSeries.DataDirectory = ravenSettings.TimeSeries.DataDir.Value;
            TimeSeries.TombstoneRetentionTime = ravenSettings.TimeSeries.TombstoneRetentionTime.Value;
            TimeSeries.DeletedTombstonesInBatch = ravenSettings.TimeSeries.DeletedTombstonesInBatch.Value;
            TimeSeries.ReplicationLatencyInMs = ravenSettings.TimeSeries.ReplicationLatencyInMs.Value;

            Encryption.EncryptionKeyBitsPreference = ravenSettings.Encryption.EncryptionKeyBitsPreference.Value;

            Indexing.MaxNumberOfItemsToProcessInTestIndexes = ravenSettings.Indexing.MaxNumberOfItemsToProcessInTestIndexes.Value;
            Indexing.MaxNumberOfStoredIndexingBatchInfoElements = ravenSettings.Indexing.MaxNumberOfStoredIndexingBatchInfoElements.Value;
            Indexing.UseLuceneASTParser = ravenSettings.Indexing.UseLuceneASTParser.Value;
            Indexing.DisableIndexingFreeSpaceThreshold = ravenSettings.Indexing.DisableIndexingFreeSpaceThreshold.Value;
            Indexing.DisableMapReduceInMemoryTracking = ravenSettings.Indexing.DisableMapReduceInMemoryTracking.Value;

            Cluster.ElectionTimeout = ravenSettings.Cluster.ElectionTimeout.Value;
            Cluster.HeartbeatTimeout = ravenSettings.Cluster.HeartbeatTimeout.Value;
            Cluster.MaxLogLengthBeforeCompaction = ravenSettings.Cluster.MaxLogLengthBeforeCompaction.Value;
            Cluster.MaxEntriesPerRequest = ravenSettings.Cluster.MaxEntriesPerRequest.Value;
            Cluster.MaxStepDownDrainTime = ravenSettings.Cluster.MaxStepDownDrainTime.Value;

            TombstoneRetentionTime = ravenSettings.TombstoneRetentionTime.Value;

            ImplicitFetchFieldsFromDocumentMode = ravenSettings.ImplicitFetchFieldsFromDocumentMode.Value;

            IgnoreSslCertificateErrors = GetIgnoreSslCertificateErrorModeMode();

            WebSockets.InitialBufferPoolSize = ravenSettings.WebSockets.InitialBufferPoolSize.Value;

            TempPath = ravenSettings.TempPath.Value;

            FillMonitoringSettings(ravenSettings);

            PostInit();

            // TODO arek

            return this;
        }

        private void FillMonitoringSettings(StronglyTypedRavenSettings settings)
        {
            Monitoring.Snmp.Enabled = settings.Monitoring.Snmp.Enabled.Value;
            Monitoring.Snmp.Community = settings.Monitoring.Snmp.Community.Value;
            Monitoring.Snmp.Port = settings.Monitoring.Snmp.Port.Value;
        }

        /// <summary>
        /// Determines how long replication and periodic backup tombstones will be kept by a database. After the specified time they will be automatically
        /// purged on next database startup. Default: 14 days.
        /// </summary>
        public TimeSpan TombstoneRetentionTime { get; set; }

        /// <summary>
        /// This limits the number of concurrent multi get requests,
        /// Note that this plays with the max number of requests allowed as well as the max number
        /// of sessions
        /// </summary>
        [JsonIgnore]
        public SemaphoreSlim ConcurrentMultiGetRequests;

        /// <summary>
        /// The time to wait before canceling a database operation such as load (many) or query
        /// </summary>
        public TimeSpan DatabaseOperationTimeout { get; private set; }

        public TimeSpan TimeToWaitBeforeRunningIdleIndexes { get; internal set; }

        public TimeSpan TimeToWaitBeforeRunningAbandonedIndexes { get; private set; }

        public TimeSpan TimeToWaitBeforeMarkingAutoIndexAsIdle { get; private set; }

        public TimeSpan TimeToWaitBeforeMarkingIdleIndexAsAbandoned { get; private set; }

        private void CheckDirectoryPermissions()
        {
            var tempPath = TempPath;
            var tempFileName = Guid.NewGuid().ToString("N");
            var tempFilePath = Path.Combine(tempPath, tempFileName);

            try
            {
                IOExtensions.CreateDirectoryIfNotExists(tempPath);
                File.WriteAllText(tempFilePath, string.Empty);
                File.Delete(tempFilePath);
            }
            catch (Exception e)
            {
                throw new InvalidOperationException(string.Format("Could not access temp path '{0}'. Please check if you have sufficient privileges to access this path or change 'Raven/TempPath' value.", tempPath), e);
            }
        }

        private void FilterActiveBundles()
        {
            if (container != null)
                container.Dispose();
            container = null;

            var catalog = GetUnfilteredCatalogs(Catalog.Catalogs);
            Catalog = new AggregateCatalog(new List<ComposablePartCatalog> { new BundlesFilteredCatalog(catalog, ActiveBundles.ToArray()) });
        }

        public IEnumerable<string> ActiveBundles
        {
            get
            {
                var activeBundles = Settings[Constants.ActiveBundles] ?? string.Empty;

                return BundlesHelper.ProcessActiveBundles(activeBundles)
                    .GetSemicolonSeparatedValues()
                    .Distinct();
            }
        }

        private HashSet<string> headersToIgnore;
        public HashSet<string> HeadersToIgnore
        {
            get
            {
                if (headersToIgnore != null)
                    return headersToIgnore;

                var headers = Settings["Raven/Headers/Ignore"] ?? string.Empty;
                return headersToIgnore = new HashSet<string>(headers.GetSemicolonSeparatedValues(), StringComparer.OrdinalIgnoreCase);
            }
        } 

        internal static ComposablePartCatalog GetUnfilteredCatalogs(ICollection<ComposablePartCatalog> catalogs)
        {
            if (catalogs.Count != 1)
                return new AggregateCatalog(catalogs.Select(GetUnfilteredCatalog));
            return GetUnfilteredCatalog(catalogs.First());
        }

        private static ComposablePartCatalog GetUnfilteredCatalog(ComposablePartCatalog x)
        {
            var filteredCatalog = x as BundlesFilteredCatalog;
            if (filteredCatalog != null)
                return GetUnfilteredCatalog(filteredCatalog.CatalogToFilter);
            return x;
        }

        public TaskScheduler CustomTaskScheduler { get; set; }

        private void SetupTransactionMode()
        {
            var transactionMode = Settings["Raven/TransactionMode"];
            TransactionMode result;
            if (Enum.TryParse(transactionMode, true, out result) == false)
                result = TransactionMode.Safe;
            TransactionMode = result;
        }

        private void SetVirtualDirectory()
        {
            var defaultVirtualDirectory = "/";
            try
            {
                if (HttpContext.Current != null)
                    defaultVirtualDirectory = HttpContext.Current.Request.ApplicationPath;
            }
            catch (HttpException)
            {
                // explicitly ignoring this because we might be running in embedded mode
                // inside IIS during init stages, in which case we can't access the HttpContext
                // nor do we actually care
            }

            VirtualDirectory = Settings["Raven/VirtualDirectory"] ?? defaultVirtualDirectory;

        }

        public bool UseDefaultOAuthTokenServer
        {
            get { return Settings["Raven/OAuthTokenServer"] == null;  }
        }

        private void SetupOAuth()
        {
            OAuthTokenServer = Settings["Raven/OAuthTokenServer"] ??
                               (ServerUrl.EndsWith("/") ? ServerUrl + "OAuth/API-Key" : ServerUrl + "/OAuth/API-Key");
            OAuthTokenKey = GetOAuthKey();
        }

        private void SetupGC()
        {
            //GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
        }

        private static readonly Lazy<byte[]> DefaultOauthKey = new Lazy<byte[]>(() =>
            {
            using (var rsa = Encryptor.Current.CreateAsymmetrical())
            {
                return rsa.ExportCspBlob(true);
            }
        });

        private byte[] GetOAuthKey()
        {
            var key = Settings["Raven/OAuthTokenCertificate"];
            if (string.IsNullOrEmpty(key) == false)
            {
                return Convert.FromBase64String(key);
            }
            return DefaultOauthKey.Value; // ensure we only create this once per process
        }

        public NameValueCollection Settings { get; set; }

        public string ServerUrl
        {
            get
            {
                HttpRequest httpRequest = null;
                try
                {
                    if (HttpContext.Current != null)
                        httpRequest = HttpContext.Current.Request;
                }
                catch (Exception)
                {
                    // the issue is probably Request is not available in this context
                    // we can safely ignore this, at any rate
                }
                if (httpRequest != null)// running in IIS, let us figure out how
                {
                    var url = httpRequest.Url;
                    return new UriBuilder(url)
                    {
                        Path = httpRequest.ApplicationPath,
                        Query = ""
                    }.Uri.ToString();
                }
                return new UriBuilder(Encryption.UseSsl ? "https" : "http", (Core.HostName ?? Environment.MachineName), Core.Port, VirtualDirectory).Uri.ToString();
            }
        }

        #region Index settings

        /// <summary>
        /// The indexing scheduler to use
        /// </summary>
        public IIndexingClassifier IndexingClassifier { get; set; }

        #endregion

        #region HTTP settings


        private string virtualDirectory;

        /// <summary>
        /// The virtual directory to use when creating the http listener. 
        /// Default: / 
        /// </summary>
        public string VirtualDirectory
        {
            get { return virtualDirectory; }
            set
            {
                virtualDirectory = value;

                if (virtualDirectory.EndsWith("/"))
                    virtualDirectory = virtualDirectory.Substring(0, virtualDirectory.Length - 1);
                if (virtualDirectory.StartsWith("/") == false)
                    virtualDirectory = "/" + virtualDirectory;
            }
        }

        /// <summary>
        /// Defines which operations are allowed for anonymous users.
        /// Allowed values: All, Get, None
        /// Default: Get
        /// </summary>
        public AnonymousUserAccessMode AnonymousUserAccessMode { get; set; }

        /// <summary>
        /// If set local request don't require authentication
        /// Allowed values: true/false
        /// Default: false
        /// </summary>
        public bool AllowLocalAccessWithoutAuthorization { get; set; }

        /// <summary>
        /// If set all client request to the server will be rejected with 
        /// the http 503 response.
        /// Other servers or the studio could still access the server.
        /// </summary>
        public bool RejectClientsMode { get; set; }

        /// <summary>
        /// The certificate to use when verifying access token signatures for OAuth
        /// </summary>
        public byte[] OAuthTokenKey { get; set; }

        public IgnoreSslCertificateErrorsMode IgnoreSslCertificateErrors { get; set; }

        #endregion

        #region Data settings

        /// <summary>
        /// What storage type to use (see: RavenDB Storage engines)
        /// Allowed values: voron
        /// Default: voron
        /// </summary>
        public string DefaultStorageTypeName
        {
            get { return defaultStorageTypeName; }
            set { if (!string.IsNullOrEmpty(value)) defaultStorageTypeName = value; }
        }
        private string defaultStorageTypeName;

        /// <summary>
        /// What sort of transaction mode to use. 
        /// Allowed values: 
        /// Lazy - faster, but can result in data loss in the case of server crash. 
        /// Safe - slower, but will never lose data 
        /// Default: Safe 
        /// </summary>
        public TransactionMode TransactionMode { get; set; }

        #endregion

        #region Misc settings

        public bool CreatePluginsDirectoryIfNotExisting { get; set; }
        public bool CreateAnalyzersDirectoryIfNotExisting { get; set; }

        public string OAuthTokenServer { get; set; }

        #endregion

        [JsonIgnore]
        public CompositionContainer Container
        {
            get { return container ?? (container = new CompositionContainer(Catalog)); }
            set
            {
                containerExternallySet = true;
                container = value;
            }
        }

        [JsonIgnore]
        public AggregateCatalog Catalog { get; set; }

        public bool RunInUnreliableYetFastModeThatIsNotSuitableForProduction { get; set; }
        
        private int? maxNumberOfParallelIndexTasks;

        //this is static so repeated initializations in the same process would not trigger reflection on all MEF plugins
        private readonly static AssemblyCatalog CurrentAssemblyCatalog = new AssemblyCatalog(typeof (DocumentDatabase).Assembly);

        /// <summary>
        /// Maximum time interval for storing commit points for map indexes when new items were added.
        /// The commit points are used to restore index if unclean shutdown was detected.
        /// Default: 00:05:00 
        /// </summary>
        public TimeSpan MaxIndexCommitPointStoreTimeInterval { get; set; }

        /// <summary>
        /// Minumum interval between between successive indexing that will allow to store a  commit point
        /// Default: 00:01:00
        /// </summary>
        public TimeSpan MinIndexingTimeIntervalToStoreCommitPoint { get; set; }

        /// <summary>
        /// Maximum number of kept commit points to restore map index after unclean shutdown
        /// Default: 5
        /// </summary>
        public int MaxNumberOfStoredCommitPoints { get; set; }

        internal bool IsTenantDatabase { get; set; }
        
        /// <summary>
        /// If True, cluster discovery will be disabled. Default is False
        /// </summary>
        public bool DisableClusterDiscovery { get; set; }

        /// <summary>
        /// If True, turns off the discovery client.
        /// </summary>
        public bool TurnOffDiscoveryClient { get; set; }

        /// <summary>
        /// The server name
        /// </summary>
        public string ServerName { get; set; }
        
        /// <summary>
        /// The maximum number of steps (instructions) to give a script before timing out.
        /// Default: 10,000
        /// </summary>
        public int MaxStepsForScript { get; set; }

        /// <summary>
        /// The maximum number of recent document touches to store (i.e. updates done in
        /// order to initiate indexing rather than because something has actually changed).
        /// </summary>
        public int MaxRecentTouchesToRemember { get; set; }

        /// <summary>
        /// The number of additional steps to add to a given script based on the processed document's quota.
        /// Set to 0 to give use a fixed size quota. This value is multiplied with the doucment size.
        /// Default: 5
        /// </summary>
        public int AdditionalStepsForScriptBasedOnDocumentSize { get; set; }

        /// <summary>
        /// Indexes are flushed to a disk only if their in-memory size exceed the specified value. Default: 5MB
        /// </summary>
        public long FlushIndexToDiskSizeInMb { get; set; }

        public bool EnableResponseLoggingForEmbeddedDatabases { get; set; }

        /// <summary>
        /// How FieldsToFetch are extracted from the document.
        /// Default: Enabled. 
        /// Other values are: 
        ///     DoNothing (fields are not fetched from the document)
        ///     Exception (an exception is thrown if we need to fetch fields from the document itself)
        /// </summary>
        public ImplicitFetchFieldsMode ImplicitFetchFieldsFromDocumentMode { get; set; }

        /// <summary>
        /// Path to temporary directory used by server.
        /// Default: Current user's temporary directory
        /// </summary>
        public string TempPath { get; set; }

        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public void SetSystemDatabase()
        {
            IsTenantDatabase = false;
        }

        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool IsSystemDatabase()
        {
            return IsTenantDatabase == false;
        }

        public static string GetKey(Expression<Func<InMemoryRavenConfiguration, object>> getKey)
        {
            var prop = getKey.ToProperty();
            return prop.GetCustomAttributes<ConfigurationEntryAttribute>().First().Key;
        }

        public abstract class ConfigurationBase
        {
            public const string DefaultValueSetInConstructor = "default-value-set-in-constructor";

            public virtual void Initialize(NameValueCollection settings)
            {
                var properties = GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance); //TODO arek

                foreach (var property in properties)
                {
                    var entries = property.GetCustomAttributes<ConfigurationEntryAttribute>().ToList();

                    if (entries.Count == 0)
                        continue;

                    TimeUnitAttribute timeUnit = null;
                    SizeUnitAttribute sizeUnit = null;

                    if (property.PropertyType == TimeSetting.TypeOf)
                    {
                        timeUnit = property.GetCustomAttribute<TimeUnitAttribute>();
                        Debug.Assert(timeUnit != null);
                    }
                    else if (property.PropertyType == SizeSetting.TypeOf)
                    {
                        sizeUnit = property.GetCustomAttribute<SizeUnitAttribute>();
                        Debug.Assert(sizeUnit != null);
                    }

                    var configuredValueSet = false;

                    foreach (var entry in entries)
                    {
                        var value = settings[entry.Key];

                        if (value == null)
                            continue;

                        try
                        {
                            if (timeUnit != null)
                            {
                                property.SetValue(this, new TimeSetting(Convert.ToInt64(value), timeUnit.Unit));
                            }
                            else if (sizeUnit != null)
                            {
                                property.SetValue(this, new SizeSetting(Convert.ToInt64(value), sizeUnit.Unit));
                            }
                            else
                            {
                                var minValue = property.GetCustomAttribute<MinValueAttribute>();

                                if (minValue == null)
                                {
                                    property.SetValue(this, Convert.ChangeType(value, property.PropertyType));
                                }
                                else
                                {
                                    if (property.PropertyType == typeof(int))
                                    {
                                        var currentValue = property.GetValue(this);
                                        property.SetValue(this, Math.Max(Convert.ToInt32(currentValue), minValue.Int32Value));
                                    }
                                    else
                                    {
                                        throw new NotSupportedException("Min value for " + property.PropertyType + " is not supported. Property name: " + property.Name);
                                    }
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            throw new InvalidOperationException("Could not set configuration value given under the following setting: " + entry.Key, e);
                        }

                        configuredValueSet = true;
                        break;
                    }

                    if (configuredValueSet)
                        continue;

                    var defaultValue = property.GetCustomAttribute<DefaultValueAttribute>().Value;
                    
                    if (DefaultValueSetInConstructor.Equals(defaultValue))
                        continue;

                    if (timeUnit != null)
                    {
                        property.SetValue(this, new TimeSetting(Convert.ToInt64(defaultValue), timeUnit.Unit));
                    }
                    else if (sizeUnit != null)
                    {
                        property.SetValue(this, new SizeSetting(Convert.ToInt64(defaultValue), sizeUnit.Unit));
                    }
                    else
                    {
                        property.SetValue(this, defaultValue);
                    }
                }
            }

            protected object GetDefaultValue<T>(Expression<Func<T, object>> getValue)
            {
                var prop = getValue.ToProperty();
                var value = prop.GetCustomAttributes<DefaultValueAttribute>().First().Value;

                if (DefaultValueSetInConstructor.Equals(value))
                {
                    return prop.GetValue(this);
                }

                return value;
            }
        }

        public class CoreConfiguration : ConfigurationBase
        {
            private readonly InMemoryRavenConfiguration parent; // TODO arek - remove
            internal static readonly int DefaultMaxNumberOfItemsToProcessInSingleBatch = Environment.Is64BitProcess ? 128 * 1024 : 16 * 1024;

            private bool runInMemory;

            private string indexStoragePath;
            private readonly int defaultInitialNumberOfItemsToProcessInSingleBatch = Environment.Is64BitProcess ? 512 : 256;
            private string pluginsDirectory;

            private int? maxNumberOfParallelIndexTasks;

            /// <summary>
            /// The hostname to use when creating the http listener (null to accept any hostname or address)
            /// Default: none, binds to all host names
            /// </summary>
            [DefaultValue((string)null)]
            [ConfigurationEntry("Raven/HostName")]
            public string HostName { get; set; }

            /// <summary>
            /// Where to cache the compiled indexes. Absolute path or relative to TEMP directory.
            /// Default: ~\CompiledIndexCache
            /// </summary>
            [DefaultValue(@"~\CompiledIndexCache")]
            [ConfigurationEntry("Raven/CompiledIndexCacheDirectory")]
            public string CompiledIndexCacheDirectory
            {
                get
                {
                    return compiledIndexCacheDirectory;
                }
                set
                {
                    compiledIndexCacheDirectory = value == null ? null : FilePathTools.ApplyWorkingDirectoryToPathAndMakeSureThatItEndsWithSlash(WorkingDirectory, value);
                }
            }

            /// <summary>
            /// The initial number of items to take when reducing a batch
            /// Default: 256 or 128 depending on CPU architecture
            /// </summary>
            //TODO arek
            public int InitialNumberOfItemsToReduceInSingleBatch { get; set; }

            /// <summary>
            /// The initial number of items to take when processing a batch
            /// Default: 512 or 256 depending on CPU architecture
            /// </summary>
            // TODO arek
            //[ConfigurationEntry("Raven/InitialNumberOfItemsToProcessInSingleBatch")]
            //[ConfigurationEntry("Raven/InitialNumberOfItemsToIndexInSingleBatch")]
            public int InitialNumberOfItemsToProcessInSingleBatch { get; set; }

            private static string CalculateWorkingDirectory(string workingDirectory)
            {
                if (string.IsNullOrEmpty(workingDirectory)) 
                    workingDirectory = @"~\";

                if (workingDirectory.StartsWith("APPDRIVE:", StringComparison.OrdinalIgnoreCase))
                {
                    var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                    var rootPath = Path.GetPathRoot(baseDirectory);
                    if (string.IsNullOrEmpty(rootPath) == false)
                        workingDirectory = Regex.Replace(workingDirectory, "APPDRIVE:", rootPath.TrimEnd('\\'), RegexOptions.IgnoreCase);
                }

                return FilePathTools.MakeSureEndsWithSlash(workingDirectory.ToFullPath());
            }

            /// <summary>
            /// Where we search for embedded files.
            /// Default: null
            /// </summary>
            [DefaultValue((string) null)]
            [ConfigurationEntry("Raven/EmbeddedFilesDirectory")]
            public string EmbeddedFilesDirectory
            {
                get { return embeddedFilesDirectory; }
                set { embeddedFilesDirectory = value.ToFullPath(); }
            }

            /// <summary>
            /// Where the internal assemblies will be extracted to.
            /// Default: ~\Assemblies
            /// </summary>
            [DefaultValue(@"~\Assemblies")]
            [ConfigurationEntry("Raven/AssembliesDirectory")]
            public string AssembliesDirectory
            {
                get
                {
                    return assembliesDirectory;
                }
                set
                {
                    assembliesDirectory = value == null ? null : FilePathTools.ApplyWorkingDirectoryToPathAndMakeSureThatItEndsWithSlash(WorkingDirectory, value);
                }
            }

            /// <summary>
            /// Where to look for plugins for RavenDB. 
            /// Default: ~\Plugins
            /// </summary>
            [DefaultValue(@"~\Plugins")]
            [ConfigurationEntry("Raven/PluginsDirectory")]
            public string PluginsDirectory
            {
                get { return pluginsDirectory; }
                set
                {
                    parent.ResetContainer();
                    // remove old directory catalog
                    var matchingCatalogs = parent.Catalog.Catalogs.OfType<DirectoryCatalog>()
                        .Concat(parent.Catalog.Catalogs.OfType<Plugins.Catalogs.FilteredCatalog>()
                                    .Select(x => x.CatalogToFilter as DirectoryCatalog)
                                    .Where(x => x != null)
                        )
                        .Where(c => c.Path == pluginsDirectory)
                        .ToArray();
                    foreach (var cat in matchingCatalogs)
                    {
                        parent.Catalog.Catalogs.Remove(cat);
                    }

                    pluginsDirectory = FilePathTools.ApplyWorkingDirectoryToPathAndMakeSureThatItEndsWithSlash(WorkingDirectory, value);

                    // add new one
                    if (Directory.Exists(pluginsDirectory))
                    {
                        var patterns = parent.Settings["Raven/BundlesSearchPattern"] ?? "*.dll";
                        foreach (var pattern in patterns.Split(new[] { ";" }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            parent.Catalog.Catalogs.Add(new BuiltinFilteringCatalog(new DirectoryCatalog(pluginsDirectory, pattern)));
                        }
                    }
                }
            }

            /// <summary>
            /// The directory to search for RavenDB's WebUI. 
            /// This is usually only useful if you are debugging RavenDB's WebUI. 
            /// Default: ~/Raven/WebUI 
            /// </summary>
            [DefaultValue(DefaultValueSetInConstructor)]
            [ConfigurationEntry("Raven/WebDir")]
            public string WebDir { get; set; }

            /// <summary>
            /// Allow to get config information over the wire.
            /// Applies to endpoints: /debug/config, /debug...
            /// Default: Open. You can set it to AdminOnly.
            /// </summary>
            [DefaultValue("Open")]
            [ConfigurationEntry("Raven/ExposeConfigOverTheWire")]
            public string ExposeConfigOverTheWire { get; set; }

            public int Port { get; set; }

            [DefaultValue((string)null)]
            [ConfigurationEntry("Raven/IndexStoragePath")] // TODO arek - add initialization order
            public string IndexStoragePath
            {
                get
                {
                    if (string.IsNullOrEmpty(indexStoragePath))
                        indexStoragePath = Path.Combine(DataDirectory, "Indexes");
                    return indexStoragePath;
                }
                set
                {
                    if (string.IsNullOrEmpty(value))
                        return;
                    indexStoragePath = value.ToFullPath();
                }
            }

            /// <summary>
            /// The directory for the RavenDB database. 
            /// You can use the ~\ prefix to refer to RavenDB's base directory. 
            /// Default: ~\Databases\System
            /// </summary>
            [DefaultValue(@"~\Databases\System")]
            [ConfigurationEntry("Raven/DataDir")]
            public string DataDirectory
            {
                get { return dataDirectory; }
                set { dataDirectory = value == null ? null : FilePathTools.ApplyWorkingDirectoryToPathAndMakeSureThatItEndsWithSlash(WorkingDirectory, value); }
            }

            [DefaultValue(@"~\")]
            [ConfigurationEntry("Raven/WorkingDir")]
            public string WorkingDirectory
            {
                get { return workingDirectory; }
                set { workingDirectory = CalculateWorkingDirectory(value); }
            }

            /// <summary>
            /// Should RavenDB's storage be in-memory. If set to true, Voron would be used as the
            /// storage engine, regardless of what was specified for StorageTypeName
            /// Allowed values: true/false
            /// Default: false
            /// </summary>
            [DefaultValue(false)]
            [ConfigurationEntry("Raven/RunInMemory")]
            public bool RunInMemory
            {
                get { return runInMemory; }
                set
                {
                    runInMemory = value;
                    parent.Settings[Constants.RunInMemory] = value.ToString(); //TODO arek - that is needed for DatabaseLandlord.CreateConfiguration - Settings = new NameValueCollection(parentConfiguration.Settings),
                }
            }

            /// <summary>
            /// The maximum number of indexing, replication and sql replication tasks allowed to run in parallel
            /// Default: The number of processors in the current machine
            /// </summary>
            [DefaultValue(DefaultValueSetInConstructor)]
            [MinValue(1)]
            [ConfigurationEntry("Raven/MaxNumberOfParallelProcessingTasks")]
            [ConfigurationEntry("Raven/MaxNumberOfParallelIndexTasks")]
            public int MaxNumberOfParallelProcessingTasks
            {
                get
                {
                    if (MemoryStatistics.MaxParallelismSet)
                        return Math.Min(maxNumberOfParallelIndexTasks ?? MemoryStatistics.MaxParallelism, MemoryStatistics.MaxParallelism);
                    return maxNumberOfParallelIndexTasks ?? Environment.ProcessorCount;
                }
                set
                {
                    if (value == 0)
                        throw new ArgumentException("You cannot set the number of parallel tasks to zero");
                    maxNumberOfParallelIndexTasks = value;
                }
            }

            /// <summary>
            /// The number that controls the if single step reduce optimization is performed.
            /// If the count of mapped results if less than this value then the reduce is executed in single step.
            /// Default: 1024
            /// </summary>
            [DefaultValue(1024)]
            [ConfigurationEntry("Raven/NumberOfItemsToExecuteReduceInSingleStep")]
            public int NumberOfItemsToExecuteReduceInSingleStep { get; set; }

            /// <summary>
            /// Max number of items to take for reducing in a batch
            /// Minimum: 128
            /// </summary>
            [DefaultValue(DefaultValueSetInConstructor)]
            [MinValue(128)]
            [ConfigurationEntry("Raven/MaxNumberOfItemsToReduceInSingleBatch")]
            public int MaxNumberOfItemsToReduceInSingleBatch { get; set; }

            /// <summary>
            /// Max number of items to take for indexing in a batch
            /// Minimum: 128
            /// </summary>
            [DefaultValue(DefaultValueSetInConstructor)]
            [MinValue(128)]
            [ConfigurationEntry("Raven/MaxNumberOfItemsToProcessInSingleBatch")]
            [ConfigurationEntry("Raven/MaxNumberOfItemsToIndexInSingleBatch")]
            public int MaxNumberOfItemsToProcessInSingleBatch { get; set; }

            [DefaultValue(5)]
            [TimeUnit(TimeUnit.Minutes)]
            [ConfigurationEntry("Raven/MaxProcessingRunLatency")]
            [ConfigurationEntry("Raven/MaxIndexingRunLatency")]
            public TimeSetting MaxProcessingRunLatency { get; set; }

            /// <summary>
            /// The maximum allowed page size for queries. 
            /// Default: 1024
            /// Minimum: 10
            /// </summary>
            [DefaultValue(1024)]
            [MinValue(10)]
            [ConfigurationEntry("Raven/MaxPageSize")]
            public int MaxPageSize { get; set; }

            private string assembliesDirectory;
            private string workingDirectory;
            private string embeddedFilesDirectory;
            private string dataDirectory;

            private string compiledIndexCacheDirectory;

            public CoreConfiguration(InMemoryRavenConfiguration parent)
            {
                this.parent = parent;
                MaxNumberOfItemsToProcessInSingleBatch = DefaultMaxNumberOfItemsToProcessInSingleBatch;
                MaxNumberOfItemsToReduceInSingleBatch = DefaultMaxNumberOfItemsToProcessInSingleBatch / 2;
                MaxNumberOfParallelProcessingTasks = Environment.ProcessorCount;
                WebDir = GetDefaultWebDir();
            }

            /// <summary>
            /// The port to use when creating the http listener. 
            /// Default: 8080. You can set it to *, in which case it will find the first available port from 8080 and upward.
            /// </summary>
            [DefaultValue("*")]
            [ConfigurationEntry("Raven/Port")]
            public string PortStringValue { get; set; }

            [DefaultValue((string)null)]
            [ConfigurationEntry("Raven/TaskScheduler")]
            public string TaskScheduler { get; set; }

            public override void Initialize(NameValueCollection settings)
            {
                base.Initialize(settings);

                var initialNumberOfItemsToIndexInSingleBatch = settings["Raven/InitialNumberOfItemsToProcessInSingleBatch"] ?? settings["Raven/InitialNumberOfItemsToIndexInSingleBatch"];
                if (initialNumberOfItemsToIndexInSingleBatch != null)
                {
                    InitialNumberOfItemsToProcessInSingleBatch = Math.Min(int.Parse(initialNumberOfItemsToIndexInSingleBatch), MaxNumberOfItemsToProcessInSingleBatch);
                }
                else
                {
                    InitialNumberOfItemsToProcessInSingleBatch = MaxNumberOfItemsToProcessInSingleBatch == (int) GetDefaultValue<CoreConfiguration>(x => x.MaxNumberOfItemsToProcessInSingleBatch) ?
                     defaultInitialNumberOfItemsToProcessInSingleBatch :
                     Math.Max(16, Math.Min(MaxNumberOfItemsToProcessInSingleBatch / 256, defaultInitialNumberOfItemsToProcessInSingleBatch));
                }

                var initialNumberOfItemsToReduceInSingleBatch = settings["Raven/InitialNumberOfItemsToReduceInSingleBatch"];
                if (initialNumberOfItemsToReduceInSingleBatch != null)
                {
                    InitialNumberOfItemsToReduceInSingleBatch = Math.Min(int.Parse(initialNumberOfItemsToReduceInSingleBatch),
                        MaxNumberOfItemsToReduceInSingleBatch);
                }
                else
                {
                    InitialNumberOfItemsToReduceInSingleBatch = MaxNumberOfItemsToReduceInSingleBatch == (int) GetDefaultValue<CoreConfiguration>(x => x.MaxNumberOfItemsToReduceInSingleBatch) ?
                     defaultInitialNumberOfItemsToProcessInSingleBatch / 2 :
                     Math.Max(16, Math.Min(MaxNumberOfItemsToReduceInSingleBatch / 256, defaultInitialNumberOfItemsToProcessInSingleBatch / 2));
                }

                if (string.IsNullOrEmpty(parent.DatabaseName)) // we only use this for root database
                {
                    Port = PortUtil.GetPort(PortStringValue, RunInMemory);
                }

                if (string.IsNullOrEmpty(TaskScheduler) == false)
                {
                    var type = Type.GetType(TaskScheduler);
                    parent.CustomTaskScheduler = (TaskScheduler)Activator.CreateInstance(type);
                }
            }

            private string GetDefaultWebDir()
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Raven/WebUI");
            }
        }

        protected void ResetContainer()
        {
            if (Container != null && containerExternallySet == false)
            {
                Container.Dispose();
                Container = null;
                containerExternallySet = false;
            }
        }

        public class ServerConfiguration : ConfigurationBase
        {
            [DefaultValue(512)]
            [ConfigurationEntry("Raven/Server/MaxConcurrentRequests")]
            [ConfigurationEntry("Raven/MaxConcurrentServerRequests")] // TODO arek - remove legacy keys
            public int MaxConcurrentRequests { get; set; }

            [DefaultValue(50)]
            [ConfigurationEntry("Raven/Server/MaxConcurrentRequestsForDatabaseDuringLoad")]
            [ConfigurationEntry("Raven/MaxConcurrentRequestsForDatabaseDuringLoad")]
            public int MaxConcurrentRequestsForDatabaseDuringLoad { get; set; }

            /// <summary>
            /// Whether to use http compression or not. 
            /// Allowed values: true/false; 
            /// Default: true
            /// </summary>
            [DefaultValue(true)]
            [ConfigurationEntry("Raven/Server/HttpCompression")]
            [ConfigurationEntry("Raven/HttpCompression")]
            public bool HttpCompression { get; set; } // TODO arek - 0 references?

            [DefaultValue((string)null)]
            [ConfigurationEntry("Raven/Server/RedirectStudioUrl")]
            [ConfigurationEntry("Raven/RedirectStudioUrl")]
            public string RedirectStudioUrl { get; set; }

            /// <summary>
            /// Determine the value of the Access-Control-Request-Headers header sent by the server.
            /// Indicates which HTTP headers are permitted for requests from allowed cross-domain origins.
            /// Ignored if AccessControlAllowOrigin is not specified.
            /// Allowed values: null (allow whatever headers are being requested), HTTP header field name
            /// </summary>
            [DefaultValue((string)null)]
            [ConfigurationEntry("Raven/Server/AccessControlRequestHeaders")]
            [ConfigurationEntry("Raven/AccessControlRequestHeaders")]
            public string AccessControlRequestHeaders { get; set; }

            /// <summary>
            /// Determine the value of the Access-Control-Allow-Methods header sent by the server.
            /// Indicates which HTTP methods (verbs) are permitted for requests from allowed cross-domain origins.
            /// Ignored if AccessControlAllowOrigin is not specified.
            /// Default: PUT,PATCH,GET,DELETE,POST
            /// </summary>
            [DefaultValue("PUT,PATCH,GET,DELETE,POST")]
            [ConfigurationEntry("Raven/Server/AccessControlAllowMethods")]
            [ConfigurationEntry("Raven/AccessControlAllowMethods")]
            public string AccessControlAllowMethods { get; set; }

            /// <summary>
            /// Determine the value of the Access-Control-Max-Age header sent by the server.
            /// Indicates how long (seconds) the browser should cache the Access Control settings.
            /// Ignored if AccessControlAllowOrigin is not specified.
            /// Default: 1728000 (20 days)
            /// </summary>
            [DefaultValue("1728000" /* 20 days */)]
            [ConfigurationEntry("Raven/Server/AccessControlMaxAge")]
            [ConfigurationEntry("Raven/AccessControlMaxAge")]
            public string AccessControlMaxAge { get; set; }

            public HashSet<string> AccessControlAllowOrigin { get; set; }

            [DefaultValue(192)]
            [ConfigurationEntry("Raven/Server/MaxConcurrentMultiGetRequests")]
            [ConfigurationEntry("Raven/MaxConcurrentMultiGetRequests")]
            public int MaxConcurrentMultiGetRequests { get; set; }

            [DefaultValue(5)]
            [TimeUnit(TimeUnit.Seconds)]
            [ConfigurationEntry("Raven/Server/MaxTimeForTaskToWaitForDatabaseToLoadInSec")]
            [ConfigurationEntry("Raven/MaxSecondsForTaskToWaitForDatabaseToLoad")]
            public TimeSetting MaxTimeForTaskToWaitForDatabaseToLoad { get; set; }

            /// <summary>
            /// Determine the value of the Access-Control-Allow-Origin header sent by the server. 
            /// Indicates the URL of a site trusted to make cross-domain requests to this server.
            /// Allowed values: null (don't send the header), *, http://example.org (space separated if multiple sites)
            /// </summary>
            [DefaultValue((string)null)]
            [ConfigurationEntry("Raven/Server/AccessControlAllowOrigin")]
            [ConfigurationEntry("Raven/AccessControlAllowOrigin")]
            public string AccessControlAllowOriginStringValue { get; set; }

            public override void Initialize(NameValueCollection settings)
            {
                base.Initialize(settings);

                AccessControlAllowOrigin = string.IsNullOrEmpty(AccessControlAllowOriginStringValue) ? new HashSet<string>() : new HashSet<string>(AccessControlAllowOriginStringValue.Split());
            }
        }

        protected AnonymousUserAccessMode GetAnonymousUserAccessMode()
        {
            if (string.IsNullOrEmpty(Settings["Raven/AnonymousAccess"]) == false)
            {
                var val = Enum.Parse(typeof(AnonymousUserAccessMode), Settings["Raven/AnonymousAccess"]);
                return (AnonymousUserAccessMode)val;
            }
            return AnonymousUserAccessMode.Admin;
        }

        public class MemoryConfiguration : ConfigurationBase
        {
            public MemoryConfiguration()
            {
                // we allow 1 GB by default, or up to 75% of available memory on startup, if less than that is available
                LimitForProcessing = new SizeSetting(Math.Min(1024, (int)(MemoryStatistics.AvailableMemoryInMb * 0.75)), SizeUnit.Megabytes);

                LowMemoryForLinuxDetection = new SizeSetting(Math.Min(16, (int)(MemoryStatistics.AvailableMemoryInMb * 0.10)), SizeUnit.Megabytes);

                MemoryCacheLimit = new SizeSetting(GetDefaultMemoryCacheLimitMegabytes(), SizeUnit.Megabytes);

                MemoryCacheLimitCheckInterval = new TimeSetting((long) MemoryCache.Default.PollingInterval.TotalSeconds, TimeUnit.Seconds);

                AvailableMemoryForRaisingBatchSizeLimit = new SizeSetting(Math.Min(768, MemoryStatistics.TotalPhysicalMemory / 2), SizeUnit.Megabytes);
            }

            /// <summary>
            /// Limit of how much memory a batch processing can take (in MBytes)
            /// </summary>
            [DefaultValue(DefaultValueSetInConstructor)]
            [SizeUnit(SizeUnit.Megabytes)]
            [ConfigurationEntry("Raven/Memory/LimitForProcessing")]
            [ConfigurationEntry("Raven/MemoryLimitForProcessing")]
            [ConfigurationEntry("Raven/MemoryLimitForIndexing")]
            public SizeSetting LimitForProcessing { get; set; }

            public SizeSetting DynamicLimitForProcessing
            {
                get
                {
                    var availableMemory = MemoryStatistics.AvailableMemoryInMb;
                    var minFreeMemory = (LimitForProcessing.Megabytes * 2L);
                    // we have more memory than the twice the limit, we can use the default limit
                    if (availableMemory > minFreeMemory)
                        return new SizeSetting(LimitForProcessing.Megabytes * 1024L * 1024L, SizeUnit.Bytes);

                    // we don't have enough room to play with, if two databases will request the max memory limit
                    // at the same time, we'll start paging because we'll run out of free memory. 
                    // Because of that, we'll dynamically adjust the amount
                    // of memory available for processing based on the amount of memory we actually have available,
                    // assuming that we have multiple concurrent users of memory at the same time.
                    // we limit that at 16 MB, if we have less memory than that, we can't really do much anyway
                    return new SizeSetting(Math.Min(availableMemory * 1024L * 1024L / 4, 16 * 1024 * 1024), SizeUnit.Bytes);
                }
            }

            // <summary>
            /// Limit for low mem detection in linux
            /// </summary>
            [DefaultValue(DefaultValueSetInConstructor)]
            [SizeUnit(SizeUnit.Megabytes)]
            [ConfigurationEntry("Raven/Memory/LowMemoryLimitForLinuxDetectionInMB")]
            [ConfigurationEntry("Raven/LowMemoryLimitForLinuxDetectionInMB")]
            public SizeSetting LowMemoryForLinuxDetection { get; set; }

            [DefaultValue(DefaultValueSetInConstructor)]
            [SizeUnit(SizeUnit.Megabytes)]
            [ConfigurationEntry("Raven/Memory/AvailableMemoryForRaisingBatchSizeLimit")]
            [ConfigurationEntry("Raven/AvailableMemoryForRaisingBatchSizeLimit")]
            [ConfigurationEntry("Raven/AvailableMemoryForRaisingIndexBatchSizeLimit")]
            public SizeSetting AvailableMemoryForRaisingBatchSizeLimit { get; set; }

            /// <summary>
            /// Interval for checking the memory cache limits
            /// Allowed values: max precision is 1 second
            /// Default: 00:02:00 (or value provided by system.runtime.caching app config)
            /// </summary>
            [DefaultValue(DefaultValueSetInConstructor)]
            [TimeUnit(TimeUnit.Seconds)]
            [ConfigurationEntry("Raven/Memory/MemoryCacheLimitCheckIntervalInSec")]
            [ConfigurationEntry("Raven/MemoryCacheLimitCheckInterval")]
            public TimeSetting MemoryCacheLimitCheckInterval { get; set; }

            /// <summary>
            /// Percentage of physical memory used for caching
            /// Allowed values: 0-99 (0 = autosize)
            /// </summary>
            [DefaultValue(0 /* auto size */)]
            [ConfigurationEntry("Raven/Memory/MemoryCacheLimitPercentage")]
            [ConfigurationEntry("Raven/MemoryCacheLimitPercentage")]
            public int MemoryCacheLimitPercentage { get; set; }

            /// <summary>
            /// The expiration value for documents in the internal managed cache
            /// </summary>
            [DefaultValue(360)]
            [TimeUnit(TimeUnit.Seconds)]
            [ConfigurationEntry("Raven/Memory/MemoryCacheExpirationInSec")]
            [ConfigurationEntry("Raven/MemoryCacheExpiration")]
            public TimeSetting MemoryCacheExpiration { get; set; }

            /// <summary>
            /// An integer value that specifies the maximum allowable size, in megabytes, that caching 
            /// document instances will use
            /// </summary>
            [DefaultValue(DefaultValueSetInConstructor)]
            [SizeUnit(SizeUnit.Megabytes)]
            [ConfigurationEntry("Raven/Memory/MemoryCacheLimitInMB")]
            [ConfigurationEntry("Raven/MemoryCacheLimitMegabytes")]
            public SizeSetting MemoryCacheLimit { get; set; }

            private int GetDefaultMemoryCacheLimitMegabytes()
            {
                // TODO: This used to use an esent key. Ensure that this is not needed anymore and kill this method. 
                var cacheSizeMaxSetting = 1024;

                // we need to leave ( a lot ) of room for other things as well, so we min the cache size
                var val = (MemoryStatistics.TotalPhysicalMemory / 2) -
                                        // reduce the unmanaged cache size from the default min
                                        cacheSizeMaxSetting;

                if (val < 0)
                    return 128; // if machine has less than 1024 MB, then only use 128 MB 

                return val;
            }
        }

        protected IgnoreSslCertificateErrorsMode GetIgnoreSslCertificateErrorModeMode()
        {
            if (string.IsNullOrEmpty(Settings["Raven/IgnoreSslCertificateErrors"]) == false)
            {
                var val = Enum.Parse(typeof(IgnoreSslCertificateErrorsMode), Settings["Raven/IgnoreSslCertificateErrors"]);
                return (IgnoreSslCertificateErrorsMode)val;
            }
            return IgnoreSslCertificateErrorsMode.None;
        }

        public Uri GetFullUrl(string baseUrl)
        {
            baseUrl = Uri.EscapeUriString(baseUrl);

            if (baseUrl.StartsWith("/"))
                baseUrl = baseUrl.Substring(1);

            var url = VirtualDirectory.EndsWith("/") ? VirtualDirectory + baseUrl : VirtualDirectory + "/" + baseUrl;
            return new Uri(url, UriKind.RelativeOrAbsolute);
        }

        public T? GetConfigurationValue<T>(string configName) where T : struct
        {
            // explicitly fail if we can't convert it
            if (string.IsNullOrEmpty(Settings[configName]) == false)
                return (T)Convert.ChangeType(Settings[configName], typeof(T));
            return null;
        }

        [CLSCompliant(false)]
        public ITransactionalStorage CreateTransactionalStorage(string storageEngine, Action notifyAboutWork, Action handleStorageInaccessible, Action onNestedTransactionEnter = null, Action onNestedTransactionExit = null)
        {
            if (EnvironmentUtils.RunningOnPosix)
                storageEngine = "voron";
            storageEngine = StorageEngineAssemblyNameByTypeName(storageEngine);
            var type = Type.GetType(storageEngine);

            if (type == null)
                throw new InvalidOperationException("Could not find transactional storage type: " + storageEngine);
            Action dummyAction = () => { };

            return (ITransactionalStorage)Activator.CreateInstance(type, this, notifyAboutWork, handleStorageInaccessible, onNestedTransactionEnter ?? dummyAction, onNestedTransactionExit ?? dummyAction);
        }


        public static string StorageEngineAssemblyNameByTypeName(string typeName)
        {
            switch (typeName.ToLowerInvariant())
            {
                case VoronTypeName:
                    typeName = typeof(Raven.Storage.Voron.TransactionalStorage).AssemblyQualifiedName;
                    break;
                default:
                    throw new ArgumentException("Invalid storage engine type name: " + typeName);
            }
            return typeName;
        }	  

        public class QueryConfiguration : ConfigurationBase
        {
            [DefaultValue(1024)] //1024 is Lucene.net default - so if the setting is not set it will be the same as not touching Lucene's settings at all
            [ConfigurationEntry("Raven/Query/MaxClauseCount")]
            [ConfigurationEntry("Raven/MaxClauseCount")]
            public int MaxClauseCount { get; set; }
        }

        public string SelectStorageEngineAndFetchTypeName()
        {
            if (Core.RunInMemory)
            {
                return VoronTypeName;                
            }

            if (String.IsNullOrEmpty(Core.DataDirectory) == false && Directory.Exists(Core.DataDirectory))
            {
                if (File.Exists(Path.Combine(Core.DataDirectory, Voron.Impl.Constants.DatabaseFilename)))
                {
                    return VoronTypeName;
                }
            }

            return DefaultStorageTypeName;
        }

        public class FacetsConfiguration : ConfigurationBase
        {
            // TODO arek - both options seem to be unused

            /// <summary>
            /// The time we should wait for pre-warming the facet cache from existing query after an indexing batch
            /// in a syncronous manner (after that, the pre warm still runs, but it will do so in a background thread).
            /// Facet queries that will try to use it will have to wait until it is over
            /// </summary>
            [DefaultValue(3)]
            [TimeUnit(TimeUnit.Seconds)]
            [ConfigurationEntry("Raven/Facets/PrewarmSyncronousWaitTimeInSec")]
            [ConfigurationEntry("Raven/PrewarmFacetsSyncronousWaitTime")]
            public TimeSetting PrewarmSyncronousWaitTime { get; set; }

            /// <summary>
            /// What is the maximum age of a facet query that we should consider when prewarming
            /// the facet cache when finishing an indexing batch
            /// </summary>
            [Browsable(false)]
            [DefaultValue(10)]
            [TimeUnit(TimeUnit.Minutes)]
            [ConfigurationEntry("Raven/Facets/PrewarmOnIndexingMaxAgeInMin")]
            [ConfigurationEntry("Raven/PrewarmFacetsOnIndexingMaxAge")]
            public TimeSetting PrewarmFacetsOnIndexingMaxAge { get; set; }
        }

        public void Dispose()
        {
            if (containerExternallySet)
                return;
            if (container == null)
                return;

            container.Dispose();
            container = null;
        }

        public class PatchingConfiguration : ConfigurationBase
        {
            [DefaultValue(false)]
            [ConfigurationEntry("Raven/Patching/AllowScriptsToAdjustNumberOfSteps")]
            [ConfigurationEntry("Raven/AllowScriptsToAdjustNumberOfSteps")]
            public bool AllowScriptsToAdjustNumberOfSteps { get; set; }
        }

        public class BulkInsertConfiguration : ConfigurationBase
        {
            [DefaultValue(60000)]
            [TimeUnit(TimeUnit.Milliseconds)]
            [ConfigurationEntry("Raven/BulkImport/BatchTimeoutInMs")]
            [ConfigurationEntry("Raven/BulkImport/BatchTimeout")]
            public TimeSetting ImportBatchTimeout { get; set; }
        }

        private ExtensionsLog GetExtensionsFor(Type type)
        {
            var enumerable =
                Container.GetExports(new ImportDefinition(x => true, type.FullName, ImportCardinality.ZeroOrMore, false, false)).
                    ToArray();
            if (enumerable.Length == 0)
                return null;
            return new ExtensionsLog
            {
                Name = type.Name,
                Installed = enumerable.Select(export => new ExtensionsLogDetail
                {
                    Assembly = export.Value.GetType().Assembly.GetName().Name,
                    Name = export.Value.GetType().Name
                }).ToArray()
            };
        }

        public IEnumerable<ExtensionsLog> ReportExtensions(params Type[] types)
        {
            return types.Select(GetExtensionsFor).Where(extensionsLog => extensionsLog != null);
        }

        public void CustomizeValuesForDatabaseTenant(string tenantId)
        {
            if (string.IsNullOrEmpty(Settings["Raven/IndexStoragePath"]) == false)
                Settings["Raven/IndexStoragePath"] = Path.Combine(Settings["Raven/IndexStoragePath"], "Databases", tenantId);

            if (string.IsNullOrEmpty(Settings["Raven/Esent/LogsPath"]) == false)
                Settings["Raven/Esent/LogsPath"] = Path.Combine(Settings["Raven/Esent/LogsPath"], "Databases", tenantId);

            if (string.IsNullOrEmpty(Settings[Constants.RavenTxJournalPath]) == false)
                Settings[Constants.RavenTxJournalPath] = Path.Combine(Settings[Constants.RavenTxJournalPath], "Databases", tenantId);

            if (string.IsNullOrEmpty(Settings["Raven/Voron/TempPath"]) == false)
                Settings["Raven/Voron/TempPath"] = Path.Combine(Settings["Raven/Voron/TempPath"], "Databases", tenantId, "VoronTemp");
        }

        public void CustomizeValuesForFileSystemTenant(string tenantId)
        {                                             
            if (string.IsNullOrEmpty(Settings[Constants.FileSystem.DataDirectory]) == false)
                Settings[Constants.FileSystem.DataDirectory] = Path.Combine(Settings[Constants.FileSystem.DataDirectory], "FileSystems", tenantId);
        }

        public void CustomizeValuesForCounterStorageTenant(string tenantId)
        {
            if (string.IsNullOrEmpty(Settings[Constants.Counter.DataDirectory]) == false)
                Settings[Constants.Counter.DataDirectory] = Path.Combine(Settings[Constants.Counter.DataDirectory], "Counters", tenantId);
        }

        public void CustomizeValuesForTimeSeriesTenant(string tenantId)
        {
            if (string.IsNullOrEmpty(Settings[Constants.TimeSeries.DataDirectory]) == false)
                Settings[Constants.TimeSeries.DataDirectory] = Path.Combine(Settings[Constants.TimeSeries.DataDirectory], "TimeSeries", tenantId);
        }

        public void CopyParentSettings(InMemoryRavenConfiguration defaultConfiguration)
        {
            Core.Port = defaultConfiguration.Core.Port;
            OAuthTokenKey = defaultConfiguration.OAuthTokenKey;
            OAuthTokenServer = defaultConfiguration.OAuthTokenServer;

            FileSystem.MaximumSynchronizationInterval = defaultConfiguration.FileSystem.MaximumSynchronizationInterval;

            Encryption.UseSsl = defaultConfiguration.Encryption.UseSsl;
            Encryption.UseFips = defaultConfiguration.Encryption.UseFips;

            Core.AssembliesDirectory = defaultConfiguration.Core.AssembliesDirectory;
            Storage.Voron.AllowOn32Bits = defaultConfiguration.Storage.Voron.AllowOn32Bits;
        }

        public IEnumerable<string> GetConfigOptionsDocs()
        {
            return ConfigOptionDocs.OptionsDocs;
        }

        public class StorageConfiguration
        {
            public StorageConfiguration()
            {
                Voron = new VoronConfiguration();
            }
            public bool PreventSchemaUpdate { get; set; }

            public VoronConfiguration Voron { get; private set; }

            public class VoronConfiguration
            {
                /// <summary>
                /// You can use this setting to specify a maximum buffer pool size that can be used for transactional storage (in gigabytes). 
                /// By default it is 4.
                /// Minimum value is 2.
                /// </summary>
                public int MaxBufferPoolSize { get; set; }

                /// <summary>
                /// You can use this setting to specify an initial file size for data file (in bytes).
                /// </summary>
                public int? InitialFileSize { get; set; }

                /// <summary>
                /// The maximum scratch buffer size that can be used by Voron. The value is in megabytes. 
                /// Default: 6144.
                /// </summary>
                public int MaxScratchBufferSize { get; set; }

                /// <summary>
                /// The minimum number of megabytes after which each scratch buffer size increase will create a notification. Used for indexing batch size tuning.
                /// Default: 
                /// 1024 when MaxScratchBufferSize > 1024, 
                /// 512 when MaxScratchBufferSize > 512
                /// -1 otherwise (disabled) 
                /// </summary>
                public int ScratchBufferSizeNotificationThreshold { get; set; }

                /// <summary>
                /// If you want to use incremental backups, you need to turn this to true, but then journal files will not be deleted after applying them to the data file. They will be deleted only after a successful backup. 
                /// Default: false.
                /// </summary>
                public bool AllowIncrementalBackups { get; set; }

                /// <summary>
                /// You can use this setting to specify a different path to temporary files. By default it is empty, which means that temporary files will be created at same location as data file.
                /// </summary>
                public string TempPath { get; set; }

                public string JournalsStoragePath { get; set; }

                /// <summary>
                /// Whether to allow Voron to run in 32 bits process.
                /// </summary>
                public bool AllowOn32Bits { get; set; }
            }
        }

        public class PrefetcherConfiguration : ConfigurationBase
        {
            public PrefetcherConfiguration()
            {
                MaxNumberOfItemsToPreFetch = CoreConfiguration.DefaultMaxNumberOfItemsToProcessInSingleBatch;
            }

            [DefaultValue(5000)]
            [TimeUnit(TimeUnit.Milliseconds)]
            [ConfigurationEntry("Raven/Prefetching/DurationLimitInMs")]
            [ConfigurationEntry("Raven/Prefetching/DurationLimit")]
            public TimeSetting DurationLimit { get; set; }

            [DefaultValue(false)]
            [ConfigurationEntry("Raven/Prefetching/Disable")]
            [ConfigurationEntry("Raven/DisableDocumentPreFetching")]
            [ConfigurationEntry("Raven/DisableDocumentPreFetchingForIndexing")]
            public bool Disabled { get; set; }

            [DefaultValue(DefaultValueSetInConstructor)]
            [MinValue(128)]
            [ConfigurationEntry("Raven/Prefetching/MaxNumberOfItemsToPreFetch")]
            [ConfigurationEntry("Raven/MaxNumberOfItemsToPreFetch")]
            [ConfigurationEntry("Raven/MaxNumberOfItemsToPreFetchForIndexing")]
            public int MaxNumberOfItemsToPreFetch { get; set; }
            /// <summary>
            /// Number of seconds after which prefetcher will stop reading documents from disk. Default: 5.
            /// </summary>
            public int FetchingDocumentsFromDiskTimeoutInSeconds { get; set; }

            /// <summary>
            /// Maximum number of megabytes after which prefetcher will stop reading documents from disk. Default: 256.
            /// </summary>
            public int MaximumSizeAllowedToFetchFromStorageInMb { get; set; }
        }

        public class ReplicationConfiguration : ConfigurationBase
        {
            [DefaultValue(600)]
            [TimeUnit(TimeUnit.Seconds)]
            [ConfigurationEntry("Raven/Replication/IndexAndTransformerReplicationLatencyInSec")]
            [ConfigurationEntry("Raven/Replication/IndexAndTransformerReplicationLatency")]
            public TimeSetting IndexAndTransformerReplicationLatency { get; set; }

            /// <summary>
            /// Number of seconds after which replication will stop reading documents from disk. Default: 30.
            /// </summary>
            public int FetchingFromDiskTimeoutInSeconds { get; set; }

            /// <summary>
            /// Number of milliseconds before replication requests will timeout. Default: 60 * 1000.
            /// </summary>
            public int ReplicationRequestTimeoutInMilliseconds { get; set; }

            /// <summary>
            /// Force us to buffer replication requests (useful if using windows auth under certain scenarios).
            /// </summary>
            public bool ForceReplicationRequestBuffering { get; set; }

            /// <summary>
            /// Maximum number of items replication will receive in single batch. Min: 512. Default: null (let source server decide).
            /// </summary>
            public int? MaxNumberOfItemsToReceiveInSingleBatch { get; set; }
        }

        public class FileSystemConfiguration
        {
            public void InitializeFrom(InMemoryRavenConfiguration configuration)
            {
                workingDirectory = configuration.Core.WorkingDirectory;
            }

            private string fileSystemDataDirectory;

            private string fileSystemIndexStoragePath;

            private string defaultFileSystemStorageTypeName;

            private string workingDirectory;

            public TimeSpan MaximumSynchronizationInterval { get; set; }

            /// <summary>
            /// The directory for the RavenDB file system. 
            /// You can use the ~\ prefix to refer to RavenDB's base directory. 
            /// </summary>
            public string DataDirectory
            {
                get { return fileSystemDataDirectory; }
                set { fileSystemDataDirectory = value == null ? null : FilePathTools.ApplyWorkingDirectoryToPathAndMakeSureThatItEndsWithSlash(workingDirectory, value); }
            }

            public string IndexStoragePath
            {
                get
                {
                    if (string.IsNullOrEmpty(fileSystemIndexStoragePath))
                        fileSystemIndexStoragePath = Path.Combine(DataDirectory, "Indexes");
                    return fileSystemIndexStoragePath;
                }
                set { fileSystemIndexStoragePath = value.ToFullPath(); }
            }

            /// <summary>
            /// What storage type to use in RavenFS (see: RavenFS Storage engines)
            /// Allowed values: voron
            /// Default: voron
            /// </summary>
            public string DefaultStorageTypeName
            {
                get { return defaultFileSystemStorageTypeName; }
                set { if (!string.IsNullOrEmpty(value)) defaultFileSystemStorageTypeName = value; }
            }
        }

        public class CounterConfiguration
        {
            public void InitializeFrom(InMemoryRavenConfiguration configuration)
            {
                workingDirectory = configuration.Core.WorkingDirectory;
            }

            private string workingDirectory;

            private string countersDataDirectory;

            /// <summary>
            /// The directory for the RavenDB counters. 
            /// You can use the ~\ prefix to refer to RavenDB's base directory. 
            /// </summary>
            public string DataDirectory
            {
                get { return countersDataDirectory; }
                set { countersDataDirectory = value == null ? null : FilePathTools.ApplyWorkingDirectoryToPathAndMakeSureThatItEndsWithSlash(workingDirectory, value); }
            }

            /// <summary>
            /// Determines how long tombstones will be kept by a counter storage. After the specified time they will be automatically
            /// Purged on next counter storage startup. Default: 14 days.
            /// </summary>
            public TimeSpan TombstoneRetentionTime { get; set; }

            public int DeletedTombstonesInBatch { get; set; }

            public int ReplicationLatencyInMs { get; set; }
        }

        public class TimeSeriesConfiguration
        {
            public void InitializeFrom(InMemoryRavenConfiguration configuration)
            {
                workingDirectory = configuration.Core.WorkingDirectory;
            }

            private string workingDirectory;

            private string timeSeriesDataDirectory;

            /// <summary>
            /// The directory for the RavenDB time series. 
            /// You can use the ~\ prefix to refer to RavenDB's base directory. 
            /// </summary>
            public string DataDirectory
            {
                get { return timeSeriesDataDirectory; }
                set { timeSeriesDataDirectory = value == null ? null : FilePathTools.ApplyWorkingDirectoryToPathAndMakeSureThatItEndsWithSlash(workingDirectory, value); }
            }

            /// <summary>
            /// Determines how long tombstones will be kept by a time series. After the specified time they will be automatically
            /// Purged on next time series startup. Default: 14 days.
            /// </summary>
            public TimeSpan TombstoneRetentionTime { get; set; }

            public int DeletedTombstonesInBatch { get; set; }

            public int ReplicationLatencyInMs { get; set; }
        }

        public class EncryptionConfiguration
        {
            /// <summary>
            /// Whatever we should use FIPS compliant encryption algorithms
            /// </summary>
            public bool UseFips { get; set; }

            public int EncryptionKeyBitsPreference { get; set; }

            /// <summary>
            /// Whatever we should use SSL for this connection
            /// </summary>
            public bool UseSsl { get; set; }
        }

        public class IndexingConfiguration : ConfigurationBase
        {
            [DefaultValue(256 * 1024)]
            [ConfigurationEntry("Raven/Indexing/MaxWritesBeforeRecreate")]
            [ConfigurationEntry("Raven/MaxIndexWritesBeforeRecreate")]
            public int MaxWritesBeforeRecreate { get; set; }

            /// <summary>
            /// How long can we keep the new index in memory before we have to flush it
            /// </summary>
            [DefaultValue(15)]
            [TimeUnit(TimeUnit.Minutes)]
            [ConfigurationEntry("Raven/Indexing/NewIndexInMemoryMaxTimeInMin")]
            [ConfigurationEntry("Raven/NewIndexInMemoryMaxTime")]
            public TimeSetting NewIndexInMemoryMaxTime { get; set; }

            /// <summary>
            /// Prevent index from being kept in memory. Default: false
            /// </summary>
            [DefaultValue(false)]
            [ConfigurationEntry("Raven/Indexing/DisableInMemory")]
            [ConfigurationEntry("Raven/DisableInMemoryIndexing")]
            public bool DisableInMemoryIndexing { get; set; }

            /// <summary>
            /// When the database is shut down rudely, determine whatever to reset the index or to check it.
            /// Checking the index may take some time on large databases
            /// </summary>
            [DefaultValue(false)]
            [ConfigurationEntry("Raven/Indexing/ResetIndexOnUncleanShutdown")]
            [ConfigurationEntry("Raven/ResetIndexOnUncleanShutdown")]
            public bool ResetIndexOnUncleanShutdown { get; set; }

            /// <summary>
            /// Controls whatever RavenDB will create temporary indexes 
            /// for queries that cannot be directed to standard indexes
            /// </summary>
            [DefaultValue(true)]
            [ConfigurationEntry("Raven/Indexing/CreateAutoIndexesForAdHocQueriesIfNeeded")]
            [ConfigurationEntry("Raven/CreateAutoIndexesForAdHocQueriesIfNeeded")]
            public bool CreateAutoIndexesForAdHocQueriesIfNeeded { get; set; }

            /// <summary>
            /// Limits the number of map outputs that a map-reduce index is allowed to create for a one source document. If a map operation applied to the one document
            /// produces more outputs than this number then an index definition will be considered as a suspicious, the indexing of this document will be skipped and
            /// the appropriate error message will be added to the indexing errors.
            /// Default value: 50. In order to disable this check set value to -1.
            /// </summary>
            [DefaultValue(50)]
            [ConfigurationEntry("Raven/Indexing/MaxMapReduceIndexOutputsPerDocument")]
            [ConfigurationEntry("Raven/MaxMapReduceIndexOutputsPerDocument")]
            public int MaxMapReduceIndexOutputsPerDocument { get; set; }

            /// <summary>
            /// Limits the number of map outputs that a simple index is allowed to create for a one source document. If a map operation applied to the one document
            /// produces more outputs than this number then an index definition will be considered as a suspicious, the indexing of this document will be skipped and
            /// the appropriate error message will be added to the indexing errors.
            /// Default value: 15. In order to disable this check set value to -1.
            /// </summary>
            [DefaultValue(15)]
            [ConfigurationEntry("Raven/Indexing/MaxSimpleIndexOutputsPerDocument")]
            [ConfigurationEntry("Raven/MaxSimpleIndexOutputsPerDocument")]
            public int MaxSimpleIndexOutputsPerDocument { get; set; }

            /// <summary>
            /// New indexes are kept in memory until they reach this integer value in bytes or until they're non-stale
            /// Default: 64 MB
            /// Minimum: 1 MB
            /// </summary>
            [DefaultValue(64)]
            [MinValue(1)]
            [SizeUnit(SizeUnit.Megabytes)]
            [ConfigurationEntry("Raven/Indexing/NewIndexInMemoryMaxInMB")]
            [ConfigurationEntry("Raven/NewIndexInMemoryMaxMB")]
            public SizeSetting NewIndexInMemoryMaxSize { get; set; }
            public int MaxNumberOfItemsToProcessInTestIndexes { get; set; }

            public int DisableIndexingFreeSpaceThreshold { get; set; }

            public bool DisableMapReduceInMemoryTracking { get; set; }
            public int MaxNumberOfStoredIndexingBatchInfoElements { get; set; }
            public bool UseLuceneASTParser
            {
                get { return useLuceneASTParser; }
                set
                {
                    if (value == useLuceneASTParser)
                        return;
                    QueryBuilder.UseLuceneASTParser = useLuceneASTParser = value;
        }
            }
            private bool useLuceneASTParser = true;
        }

        public class ClusterConfiguration
        {
            public int ElectionTimeout { get; set; }
            public int HeartbeatTimeout { get; set; }
            public int MaxLogLengthBeforeCompaction { get; set; }
            public TimeSpan MaxStepDownDrainTime { get; set; }
            public int MaxEntriesPerRequest { get; set; }
        }

        public class MonitoringConfiguration
        {
            public MonitoringConfiguration()
            {
                Snmp = new SnmpConfiguration();
            }

            public SnmpConfiguration Snmp { get; private set; }

            public class SnmpConfiguration
            {
                public bool Enabled { get; set; }

                public int Port { get; set; }

                public string Community { get; set; }
            }
        }

        public class WebSocketsConfiguration
        {
            public int InitialBufferPoolSize { get; set; }
        }

        public void UpdateDataDirForLegacySystemDb()
        {
            if (Core.RunInMemory)
                return;
            var legacyPath = Settings["Raven/DataDir/Legacy"];
            if (string.IsNullOrEmpty(legacyPath))
                return;
            var fullLegacyPath = FilePathTools.MakeSureEndsWithSlash(legacyPath.ToFullPath());

            // if we already have a system database in the legacy path, we want to keep it.
            // The idea is that we don't want to have the user experience "missing databases" because
            // we change the path to make it nicer.
            if (Directory.Exists(fullLegacyPath))
            {
                Core.DataDirectory = legacyPath;
            }
        }
    }
}
