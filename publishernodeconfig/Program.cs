
using Mono.Options;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PubisherConfig
{
    using Newtonsoft.Json;
    using OpcPublisher;
    using System.Linq;
    using System.Net;
    using System.Reflection;

    public class Program
    {
        public static Serilog.Core.Logger Logger = null;

        // long wait time
        public const int MAX_LONG_WAIT_SEC = 10;
        // short wait time
        public const int MAX_SHORT_WAIT_SEC = 5;

        /// <summary>
        /// Synchronous main method of the app.
        /// </summary>
        public static void Main(string[] args)
        {
            MainAsync(args).Wait();
        }

        /// <summary>
        /// Asynchronous part of the main method of the app.
        /// </summary>
        public async static Task MainAsync(string[] args)
        {
            Logger = new LoggerConfiguration()
                .WriteTo.Console()
                .MinimumLevel.Debug()
                .CreateLogger();

            Logger.Information($"OPC Publisher node configuration tool");

            // command line options
            bool showHelp = false;
            string iotHubConnectionString = string.Empty;
            string iotHubPublisherDeviceName = string.Empty;
            string iotHubPublisherModuleName = string.Empty;

            Mono.Options.OptionSet options = new Mono.Options.OptionSet {
                { "h|help", "show this message and exit", h => showHelp = h != null },

                { "ic|iotHubConnectionString=", "IoTHub owner or service connectionstring", (string s) => iotHubConnectionString = s },
                { "id|iothubdevicename=", "IoTHub device name of the OPC Publisher", (string s) => iotHubPublisherDeviceName = s },
                { "im|iothubmodulename=", "IoT Edge module name of the OPC Publisher which runs in the IoT Edge device specified by id/iothubdevicename", (string s) => iotHubPublisherModuleName = s },

                { "pc|purgeconfig", "remove all configured nodes before pushing new ones",  b => _purgeConfig = b != null },
                { "bf|backupfile=", $"the filename to store the existing node configuration of OPC Publisher\nDefault: './{_backupFileName}'", (string l) => _backupFileName = l },
                { "nc|nodeconfigfile=", $"the filename of the new node configuration to be set", (string l) => _nodeConfigFileName = l },

                { "lf|logfile=", $"the filename of the logfile to use\nDefault: './{_logFileName}'", (string l) => _logFileName = l },
                { "ll|loglevel=", $"the loglevel to use (allowed: fatal, error, warn, info, debug, verbose).\nDefault: info", (string l) => {
                        List<string> logLevels = new List<string> {"fatal", "error", "warn", "info", "debug", "verbose"};
                        if (logLevels.Contains(l.ToLowerInvariant()))
                        {
                            _logLevel = l.ToLowerInvariant();
                        }
                        else
                        {
                            throw new OptionException("The loglevel must be one of: fatal, error, warn, info, debug, verbose", "loglevel");
                        }
                    }
                }
            };

            IList<string> extraArgs = null;
            try
            {
                extraArgs = options.Parse(args);
            }
            catch (OptionException e)
            {
                // initialize logging
                InitLogging();

                // show message
                Logger.Fatal(e, "Error in command line options");

                // show usage
                Usage(options, args);
                return;
            }

            // initialize logging
            InitLogging();

            // show usage if requested
            if (showHelp)
            {
                Usage(options);
                return;
            }

            // no extra options
            if (extraArgs.Count > 0)
            {
                for (int i = 1; i < extraArgs.Count; i++)
                {
                    Logger.Error("Error: Unknown option: {0}", extraArgs[i]);
                }
                Usage(options, args);
                return;
            }

            // sanity check parameters
            if (string.IsNullOrEmpty(iotHubConnectionString) || string.IsNullOrEmpty(iotHubPublisherDeviceName))
            {
                Logger.Fatal("For IoTHub communication an IoTHub connection string and the publisher devicename (and modulename) must be specified.");
                return;
            }
            Logger.Information($"IoTHub connectionstring: {iotHubConnectionString}");
            if (string.IsNullOrEmpty(iotHubPublisherModuleName))
            {
                Logger.Information($"OPC Publisher not running in IoT Edge.");
                Logger.Information($"IoTHub OPC Publisher device name: {iotHubPublisherDeviceName}");
            }
            else
            {
                Logger.Information($"OPC Publisher running as IoT Edge module.");
                Logger.Information($"IoT Edge device name: {iotHubPublisherDeviceName}");
                Logger.Information($"OPC Publisher module name: {iotHubPublisherModuleName}");
            }
            CancellationTokenSource cts = new CancellationTokenSource();
            CancellationToken ct = cts.Token;

            // read new configuration
            if (!string.IsNullOrEmpty(_nodeConfigFileName))
            {
                try
                {
                    _configurationFileEntries = JsonConvert.DeserializeObject<List<PublisherConfigurationFileEntryLegacyModel>>(File.ReadAllText(_nodeConfigFileName));
                }
                catch (Exception e)
                {
                    Logger.Fatal(e, $"Error reading configuration file. Exiting...");
                    return;
                }
                Logger.Information($"The configuration file '{_nodeConfigFileName}' to be applied contains {_configurationFileEntries.Count} entries.");
            }

            // instantiate OPC Publisher interface
            _publisher = new Publisher(iotHubConnectionString, iotHubPublisherDeviceName, iotHubPublisherModuleName, MAX_SHORT_WAIT_SEC, MAX_LONG_WAIT_SEC, ct);

            // validate OPC Publisher version
            if (!await ValidatePublisherVersionAsync(ct))
            {
                Environment.Exit(1);
            }

            // read existing configuration
            List<PublisherConfigurationFileEntryModel> currentConfiguration = new List<PublisherConfigurationFileEntryModel>();
            List<string> configuredEndpoints = _publisher.GetConfiguredEndpoints(ct);
            if (configuredEndpoints.Count > 0)
            {
                Logger.Information($"OPC Publisher has the following node configuration:");
            }
            else
            {
                Logger.Information($"OPC Publisher is not publishing any data.");
            }
            foreach (var configuredEndpoint in configuredEndpoints)
            {
                List<NodeModel> configuredNodesOnEndpoint = _publisher.GetConfiguredNodesOnEndpoint(configuredEndpoint, ct);
                PublisherConfigurationFileEntryModel configEntry = new PublisherConfigurationFileEntryModel();
                configEntry.EndpointUrl = new Uri(configuredEndpoint);
                List<OpcNodeOnEndpointModel> nodesOnEndpoint = new List<OpcNodeOnEndpointModel>();
                Logger.Information($"For endpoint '{configuredEndpoint}' there are {configuredNodesOnEndpoint.Count} nodes configured.");
                foreach (var configuredNode in configuredNodesOnEndpoint)
                {
                    Logger.Debug($"Id '{configuredNode.Id}', " +
                        $"OpcPublishingInterval: {(configuredNode.OpcPublishingInterval == null ? "default" : configuredNode.OpcPublishingInterval.ToString())}, " +
                        $"OpcSamplingInterval: {(configuredNode.OpcSamplingInterval == null ? "default" : configuredNode.OpcSamplingInterval.ToString())}");
                    OpcNodeOnEndpointModel opcNodeOnEndpoint = new OpcNodeOnEndpointModel();
                    opcNodeOnEndpoint.Id = configuredNode.Id;
                    opcNodeOnEndpoint.OpcSamplingInterval = configuredNode.OpcSamplingInterval;
                    opcNodeOnEndpoint.OpcPublishingInterval = configuredNode.OpcPublishingInterval;
                    nodesOnEndpoint.Add(opcNodeOnEndpoint);
                }
                configEntry.OpcNodes = nodesOnEndpoint;
                currentConfiguration.Add(configEntry);
            }

            // save it on request
            if (!string.IsNullOrEmpty(_backupFileName) && currentConfiguration.Count > 0)
            {
                await File.WriteAllTextAsync(_backupFileName, JsonConvert.SerializeObject(currentConfiguration, Formatting.Indented));
                Logger.Information($"The existing OPC Publisher node configuration was saved in '{_backupFileName}'");
            }

            // remove existing configuration on request
            if (_purgeConfig)
            {
                _publisher.UnpublishAllConfiguredNodes(ct);
                Logger.Information($"The existing node configuration was purged. OPC Publisher should no longer publish any data.");
            }

            // push new configuration, if required
            if (_configurationFileEntries != null)
            {
                var uniqueEndpoints = _configurationFileEntries.Select(e => e.EndpointUrl).Distinct();
                Logger.Information($"The new node configuration will now be set in OPC Publisher.");
                foreach (var uniqueEndpoint in uniqueEndpoints)
                {
                    var endpointConfigurationfileEntries = _configurationFileEntries.Where(e => e.EndpointUrl == uniqueEndpoint);
                    List<NodeIdInfo> configurationNodeIdInfos = new List<NodeIdInfo>();
                    foreach (var endpointConfigurationFileEntry in endpointConfigurationfileEntries)
                    {
                        foreach (var opcNode in endpointConfigurationFileEntry.OpcNodes)
                        {
                            Logger.Debug($"Id '{opcNode.Id}', " +
                                $"OpcPublishingInterval: {(opcNode.OpcPublishingInterval == null ? "default" : opcNode.OpcPublishingInterval.ToString())}, " +
                                $"OpcSamplingInterval: {(opcNode.OpcSamplingInterval == null ? "default" : opcNode.OpcSamplingInterval.ToString())}");
                            NodeIdInfo nodeIdInfo = new NodeIdInfo(opcNode.Id);
                            configurationNodeIdInfos.Add(nodeIdInfo);
                        }
                    }
                    if (!_publisher.PublishNodes(configurationNodeIdInfos, ct, uniqueEndpoint.AbsoluteUri))
                    {
                        Logger.Error($"Not able to send the new node configuration to OPC Publisher.");
                    }
                }
            }

            // done
            Logger.Information($"Done. Exiting....");
            return;
        }

        /// <summary>
        /// Initialize logging.
        /// </summary>
        private static void InitLogging()
        {
            LoggerConfiguration loggerConfiguration = new LoggerConfiguration();

            // set the log level
            switch (_logLevel)
            {
                case "fatal":
                    loggerConfiguration.MinimumLevel.Fatal();
                    break;
                case "error":
                    loggerConfiguration.MinimumLevel.Error();
                    break;
                case "warn":
                    loggerConfiguration.MinimumLevel.Warning();
                    break;
                case "info":
                    loggerConfiguration.MinimumLevel.Information();
                    break;
                case "debug":
                    loggerConfiguration.MinimumLevel.Debug();
                    break;
                case "verbose":
                    loggerConfiguration.MinimumLevel.Verbose();
                    break;
            }

            // set logging sinks
            loggerConfiguration.WriteTo.Console();

            if (!string.IsNullOrEmpty(_logFileName))
            {
                // configure rolling file sink
                const int MAX_LOGFILE_SIZE = 1024 * 1024;
                const int MAX_RETAINED_LOGFILES = 2;
                loggerConfiguration.WriteTo.File(_logFileName, fileSizeLimitBytes: MAX_LOGFILE_SIZE, rollOnFileSizeLimit: true, retainedFileCountLimit: MAX_RETAINED_LOGFILES);
            }

            Logger = loggerConfiguration.CreateLogger();
            Logger.Information($"Current directory is: {System.IO.Directory.GetCurrentDirectory()}");
            Logger.Information($"Log file is: {_logFileName}");
            Logger.Information($"Log level is: {_logLevel}");
            return;
        }

        /// <summary>
        /// Usage message.
        /// </summary>
        private static void Usage(Mono.Options.OptionSet options, string[] args)
        {
            Logger.Information("");

            // show the args
            if (args != null)
            {
                string commandLine = string.Empty;
                foreach (var arg in args)
                {
                    commandLine = commandLine + " " + arg;
                }
                Logger.Information($"Command line: {commandLine}");
            }

            Logger.Information("");
            Logger.Information("");
            Logger.Information($"Usage: iot-edge-opc-publisher-nodeconfiguration [<options>]");
            Logger.Information("");

            // output the options
            Logger.Information("Options:");
            StringBuilder stringBuilder = new StringBuilder();
            StringWriter stringWriter = new StringWriter(stringBuilder);
            options.WriteOptionDescriptions(stringWriter);
            string[] helpLines = stringBuilder.ToString().Split("\r\n");
            foreach (var line in helpLines)
            {
                Logger.Information(line);
            }
        }

        /// <summary>
        /// Validates if the publisher is there and supports the method calls we need.
        /// </summary>
        /// <returns></returns>
        private static async Task<bool> ValidatePublisherVersionAsync(CancellationToken ct)
        {
            // fetch the information
            GetInfoMethodResponseModel info = await _publisher.GetInfoAsync(ct);
            if (info == null)
            {
                return false;
            }

            Logger.Information($"OPC Publisher V{info.VersionMajor}.{info.VersionMinor}.{info.VersionPatch} was detected.");
            return true;
        }


        /// <summary>
        /// Usage message.
        /// </summary>
        private static void Usage(Mono.Options.OptionSet options)
        {

            // show usage
            Logger.Information("");
            Logger.Information("Usage: {0}.exe [<options>]", Assembly.GetEntryAssembly().GetName().Name);
            Logger.Information("");
            Logger.Information("OPC Publisher node configuration tool.");
            Logger.Information("To exit the application, just press CTRL-C while it is running.");
            Logger.Information("");

            // output the options
            Logger.Information("Options:");
            StringBuilder stringBuilder = new StringBuilder();
            StringWriter stringWriter = new StringWriter(stringBuilder);
            options.WriteOptionDescriptions(stringWriter);
            string[] helpLines = stringBuilder.ToString().Split("\n");
            foreach (var line in helpLines)
            {
                Logger.Information(line);
            }
        }

        private static string _logFileName = $"{Dns.GetHostName()}-publishernodeconfig.log";
        private static string _backupFileName = $"{Dns.GetHostName()}-publishernodeconfig.bak";
        private static string _nodeConfigFileName = string.Empty;
        private static string _logLevel = "info";
        private static bool _purgeConfig = false;
        private static List<PublisherConfigurationFileEntryLegacyModel> _configurationFileEntries = null;
        private static Publisher _publisher;
    }
}
