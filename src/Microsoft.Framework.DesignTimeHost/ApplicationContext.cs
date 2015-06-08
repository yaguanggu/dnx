// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Threading;
using Microsoft.Framework.DesignTimeHost.Models;
using Microsoft.Framework.DesignTimeHost.Models.IncomingMessages;
using Microsoft.Framework.DesignTimeHost.Models.OutgoingMessages;
using Microsoft.Framework.Runtime;
using Microsoft.Framework.Runtime.Caching;
using Microsoft.Framework.Runtime.Common.Impl;
using Microsoft.Framework.Runtime.Compilation;
using Microsoft.Framework.Runtime.Roslyn;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NuGet;

namespace Microsoft.Framework.DesignTimeHost
{
    public class ApplicationContext
    {
        private readonly IServiceProvider _hostServices;
        private readonly ICache _cache;
        private readonly ICacheContextAccessor _cacheContextAccessor;
        private readonly INamedCacheDependencyProvider _namedDependencyProvider;
        private readonly IApplicationEnvironment _appEnv;
        private readonly Queue<Message> _inbox = new Queue<Message>();
        private readonly object _processingLock = new object();

        private readonly Trigger<string> _appPath = new Trigger<string>();
        private readonly Trigger<string> _configuration = new Trigger<string>();
        private readonly Trigger<Void> _pluginWorkNeeded = new Trigger<Void>();
        private readonly Trigger<Void> _filesChanged = new Trigger<Void>();
        private readonly Trigger<Void> _rebuild = new Trigger<Void>();
        private readonly Trigger<Void> _refreshDependencies = new Trigger<Void>();
        private readonly Trigger<Void> _requiresCompilation = new Trigger<Void>();

        private World _remote = new World();
        private World _local = new World();

        private ConnectionContext _initializedContext;
        private readonly Dictionary<FrameworkName, List<CompiledAssemblyState>> _waitingForCompiledAssemblies = new Dictionary<FrameworkName, List<CompiledAssemblyState>>();
        private readonly List<ConnectionContext> _waitingForDiagnostics = new List<ConnectionContext>();
        private readonly Dictionary<FrameworkName, Trigger<Void>> _requiresAssemblies = new Dictionary<FrameworkName, Trigger<Void>>();
        private readonly Dictionary<FrameworkName, ProjectCompilation> _compilations = new Dictionary<FrameworkName, ProjectCompilation>();
        private readonly PluginHandler _pluginHandler;
        private readonly ProtocolManager _protocolManager;
        private int? _contextProtocolVersion;

        public ApplicationContext(IServiceProvider services,
                                  ICache cache,
                                  ICacheContextAccessor cacheContextAccessor,
                                  INamedCacheDependencyProvider namedDependencyProvider,
                                  ProtocolManager protocolManager,
                                  int id)
        {
            _hostServices = services;
            _appEnv = (IApplicationEnvironment)services.GetService(typeof(IApplicationEnvironment));
            _cache = cache;
            _cacheContextAccessor = cacheContextAccessor;
            _namedDependencyProvider = namedDependencyProvider;
            _pluginHandler = new PluginHandler(services, SendPluginMessage);
            _protocolManager = protocolManager;

            Id = id;
        }

        public int Id { get; private set; }

        public string ApplicationPath { get { return _appPath.Value; } }

        public int ProtocolVersion
        {
            get
            {
                if (_contextProtocolVersion.HasValue)
                {
                    return _contextProtocolVersion.Value;
                }
                else
                {
                    return _protocolManager.CurrentVersion;
                }
            }
        }

        public void OnReceive(Message message)
        {
            lock (_inbox)
            {
                _inbox.Enqueue(message);
            }

            ThreadPool.QueueUserWorkItem(ProcessLoop);
        }

        public void ProcessLoop(object state)
        {
            if (!Monitor.TryEnter(_processingLock))
            {
                return;
            }

            try
            {
                lock (_inbox)
                {
                    if (_inbox.IsEmpty())
                    {
                        return;
                    }
                }

                DoProcessLoop();
            }
            catch (Exception ex)
            {
                Logger.TraceError("[ApplicationContext]: Error occured: {0}", ex);

                // Unhandled errors
                var error = new ErrorMessage
                {
                    Message = ex.Message
                };

                var fileFormatException = ex as FileFormatException;
                if (fileFormatException != null)
                {
                    error.Path = fileFormatException.Path;
                    error.Line = fileFormatException.Line;
                    error.Column = fileFormatException.Column;
                }

                var message = new Message
                {
                    ContextId = Id,
                    MessageType = "Error",
                    Payload = JToken.FromObject(error)
                };

                _initializedContext.Transmit(message);

                // Notify anyone waiting for diagnostics
                foreach (var connection in _waitingForDiagnostics)
                {
                    connection.Transmit(message);
                }

                _waitingForDiagnostics.Clear();

                // Notify the runtime of errors
                foreach (var frameworkGroup in _waitingForCompiledAssemblies.Values)
                {
                    foreach (var connection in frameworkGroup)
                    {
                        if (connection.Version > 0)
                        {
                            connection.AssemblySent = true;
                            connection.Connection.Transmit(message);
                        }
                    }
                }

                _waitingForCompiledAssemblies.Clear();
                _requiresAssemblies.Clear();
            }
            finally
            {
                Monitor.Exit(_processingLock);
            }
        }

        public void DoProcessLoop()
        {
            while (true)
            {
                DrainInbox();

                if (ResolveDependencies())
                {
                    SendOutgoingMessages();
                }

                if (PerformCompilation())
                {
                    SendOutgoingMessages();
                }

                PerformPluginWork();

                lock (_inbox)
                {
                    // If there's no more messages queued then bail out.
                    if (_inbox.Count == 0)
                    {
                        return;
                    }
                }
            }
        }

        private void DrainInbox()
        {
            // Process all of the messages in the inbox
            while (ProcessMessage()) { }
        }

        private bool ProcessMessage()
        {
            Message message;

            lock (_inbox)
            {
                if (_inbox.IsEmpty())
                {
                    return false;
                }

                message = _inbox.Dequeue();

                // REVIEW: Can this ever happen?
                if (message == null)
                {
                    return false;
                }
            }

            Logger.TraceInformation("[ApplicationContext]: Received {0}", message.MessageType);

            switch (message.MessageType)
            {
                case "Initialize":
                    {
                        // This should only be sent once
                        if (_initializedContext == null)
                        {
                            _initializedContext = message.Sender;

                            var data = new InitializeMessage
                            {
                                Version = GetValue<int>(message.Payload, "Version"),
                                Configuration = GetValue(message.Payload, "Configuration"),
                                ProjectFolder = GetValue(message.Payload, "ProjectFolder")
                            };

                            _appPath.Value = data.ProjectFolder;
                            _configuration.Value = data.Configuration ?? "Debug";

                            // Therefore context protocol version is set only when the version is not 0 (meaning 'Version'
                            // protocol is not missing) and protocol version is not overridden by environment variable.
                            if (data.Version != 0 && !_protocolManager.EnvironmentOverridden)
                            {
                                _contextProtocolVersion = Math.Min(data.Version, _protocolManager.MaxVersion);
                                Logger.TraceInformation($"[{nameof(ApplicationContext)}]: Set context protocol version to {_contextProtocolVersion.Value}");
                            }
                        }
                        else
                        {
                            Logger.TraceInformation("[ApplicationContext]: Received Initialize message more than once for {0}", _appPath.Value);
                        }
                    }
                    break;
                case "Teardown":
                    {
                        // TODO: Implement
                    }
                    break;
                case "ChangeConfiguration":
                    {
                        var data = new ChangeConfigurationMessage
                        {
                            Configuration = GetValue(message.Payload, "Configuration")
                        };
                        _configuration.Value = data.Configuration;
                    }
                    break;
                case "RefreshDependencies":
                case "RestoreComplete":
                    {
                        _refreshDependencies.Value = default(Void);
                    }
                    break;
                case "Rebuild":
                    {
                        _rebuild.Value = default(Void);
                    }
                    break;
                case "FilesChanged":
                    {
                        _filesChanged.Value = default(Void);
                    }
                    break;
                case "GetCompiledAssembly":
                    {
                        var libraryKey = new RemoteLibraryKey
                        {
                            Name = GetValue(message.Payload, "Name"),
                            TargetFramework = GetValue(message.Payload, "TargetFramework"),
                            Configuration = GetValue(message.Payload, "Configuration"),
                            Aspect = GetValue(message.Payload, "Aspect"),
                            Version = GetValue<int>(message.Payload, nameof(RemoteLibraryKey.Version)),
                        };

                        var targetFramework = new FrameworkName(libraryKey.TargetFramework);

                        // Only set this the first time for the project
                        if (!_requiresAssemblies.ContainsKey(targetFramework))
                        {
                            _requiresAssemblies[targetFramework] = new Trigger<Void>();
                            _requiresAssemblies[targetFramework].Value = default(Void);
                        }

                        List<CompiledAssemblyState> waitingForCompiledAssemblies;
                        if (!_waitingForCompiledAssemblies.TryGetValue(targetFramework, out waitingForCompiledAssemblies))
                        {
                            waitingForCompiledAssemblies = new List<CompiledAssemblyState>();
                            _waitingForCompiledAssemblies[targetFramework] = waitingForCompiledAssemblies;
                        }

                        waitingForCompiledAssemblies.Add(new CompiledAssemblyState
                        {
                            Connection = message.Sender,
                            Version = libraryKey.Version
                        });
                    }
                    break;
                case "GetDiagnostics":
                    {
                        _requiresCompilation.Value = default(Void);

                        _waitingForDiagnostics.Add(message.Sender);
                    }
                    break;
                case "Plugin":
                    {
                        var pluginMessage = message.Payload.ToObject<PluginMessage>();
                        var result = _pluginHandler.OnReceive(pluginMessage);

                        _refreshDependencies.Value = default(Void);
                        _pluginWorkNeeded.Value = default(Void);

                        if (result == PluginHandlerOnReceiveResult.RefreshDependencies)
                        {
                            _refreshDependencies.Value = default(Void);
                        }
                    }
                    break;
            }

            return true;
        }

        private bool ResolveDependencies()
        {
            State state = null;

            if (_appPath.WasAssigned ||
                _configuration.WasAssigned ||
                _filesChanged.WasAssigned ||
                _rebuild.WasAssigned ||
                _refreshDependencies.WasAssigned)
            {
                bool triggerBuildOutputs = _rebuild.WasAssigned || _filesChanged.WasAssigned;
                bool triggerDependencies = _refreshDependencies.WasAssigned || _rebuild.WasAssigned;

                _appPath.ClearAssigned();
                _configuration.ClearAssigned();
                _filesChanged.ClearAssigned();
                _rebuild.ClearAssigned();
                _refreshDependencies.ClearAssigned();

                // Trigger that the project outputs changes in case the runtime process
                // hasn't died yet
                TriggerProjectOutputsChanged();

                state = DoInitialWork(_appPath.Value, _configuration.Value, triggerBuildOutputs, triggerDependencies);
            }

            if (state == null)
            {
                return false;
            }

            _local = new World();
            _local.ProjectInformation = new ProjectMessage
            {
                Name = state.Name,

                // All target framework information
                Frameworks = state.Frameworks,

                // debug/release etc
                Configurations = state.Configurations,

                Commands = state.Commands,

                ProjectSearchPaths = state.ProjectSearchPaths,

                GlobalJsonPath = state.GlobalJsonPath
            };

            _local.ProjectDiagnostics = new DiagnosticsMessage(state.Diagnostics);

            foreach (var project in state.Projects)
            {
                var frameworkData = project.TargetFramework;

                var projectWorld = new ProjectWorld
                {
                    ApplicationHostContext = project.DependencyInfo.HostContext,
                    TargetFramework = project.FrameworkName,
                    Sources = new SourcesMessage
                    {
                        Framework = frameworkData,
                        Files = project.SourceFiles
                    },
                    CompilerOptions = new CompilationOptionsMessage
                    {
                        Framework = frameworkData,
                        CompilationOptions = project.CompilationSettings
                    },
                    Dependencies = new DependenciesMessage
                    {
                        Framework = frameworkData,
                        RootDependency = state.Name,
                        Dependencies = project.DependencyInfo.Dependencies
                    },
                    References = new ReferencesMessage
                    {
                        Framework = frameworkData,
                        ProjectReferences = project.DependencyInfo.ProjectReferences,
                        FileReferences = project.DependencyInfo.References,
                        RawReferences = project.DependencyInfo.RawReferences
                    },
                    DependencyDiagnostics = new DiagnosticsMessage(project.Diagnostics, frameworkData)
                };

                _local.Projects[project.FrameworkName] = projectWorld;
            }

            if (_pluginHandler.FaultedPluginRegistrations)
            {
                var assemblyLoadContext = GetAppRuntimeLoadContext();

                _pluginHandler.TryRegisterFaultedPlugins(assemblyLoadContext);
            }

            return true;
        }

        private bool PerformCompilation()
        {
            bool calculateDiagnostics = _requiresCompilation.WasAssigned;

            if (calculateDiagnostics)
            {
                _requiresCompilation.ClearAssigned();
            }

            foreach (var pair in _local.Projects)
            {
                var project = pair.Value;
                var projectCompilationChanged = false;
                ProjectCompilation compilation = null;

                if (calculateDiagnostics)
                {
                    projectCompilationChanged = UpdateProjectCompilation(project, out compilation);

                    project.CompilationDiagnostics = new DiagnosticsMessage(
                        compilation.Diagnostics,
                        project.Sources.Framework);
                }

                Trigger<Void> requiresAssemblies;
                if ((_requiresAssemblies.TryGetValue(pair.Key, out requiresAssemblies) &&
                    requiresAssemblies.WasAssigned))
                {
                    requiresAssemblies.ClearAssigned();

                    // If we didn't already update the compilation then do it on demand
                    if (compilation == null)
                    {
                        projectCompilationChanged = UpdateProjectCompilation(project, out compilation);
                    }

                    // Only emit the assembly if there are no errors and
                    // this is the very first time or there were changes
                    if (!compilation.Diagnostics.HasErrors() &&
                        (!compilation.HasOutputs || projectCompilationChanged))
                    {
                        var engine = new NonLoadingLoadContext();

                        compilation.ProjectReference.Load(engine);

                        compilation.AssemblyBytes = engine.AssemblyBytes ?? new byte[0];
                        compilation.PdbBytes = engine.PdbBytes ?? new byte[0];
                        compilation.AssemblyPath = engine.AssemblyPath;
                    }

                    project.Outputs = new OutputsMessage
                    {
                        FrameworkData = project.Sources.Framework,
                        AssemblyBytes = compilation.AssemblyBytes ?? new byte[0],
                        PdbBytes = compilation.PdbBytes ?? new byte[0],
                        AssemblyPath = compilation.AssemblyPath,
                        EmbeddedReferences = compilation.EmbeddedReferences
                    };

                    if (project.CompilationDiagnostics == null)
                    {
                        project.CompilationDiagnostics = new DiagnosticsMessage(
                            compilation.Diagnostics,
                            project.Sources.Framework);
                    }
                }
            }

            return true;
        }

        private void PerformPluginWork()
        {
            if (_pluginWorkNeeded.WasAssigned)
            {
                _pluginWorkNeeded.ClearAssigned();

                var assemblyLoadContext = GetAppRuntimeLoadContext();

                _pluginHandler.ProcessMessages(assemblyLoadContext);
            }
        }

        private IAssemblyLoadContext GetAppRuntimeLoadContext()
        {
            Project project;
            if (!Project.TryGetProject(_appPath.Value, out project))
            {
                throw new InvalidOperationException(
                    Resources.FormatPlugin_UnableToFindProjectJson(_appPath.Value));
            }

            var loadContextFactory = GetRuntimeLoadContextFactory(project);

            var assemblyLoadContext = loadContextFactory.Create(_hostServices);

            return assemblyLoadContext;
        }

        private bool UpdateProjectCompilation(ProjectWorld project, out ProjectCompilation compilation)
        {
            var export = project.ApplicationHostContext.LibraryManager.GetLibraryExport(_local.ProjectInformation.Name);

            ProjectCompilation oldCompilation;
            if (!_compilations.TryGetValue(project.TargetFramework, out oldCompilation) ||
                export != oldCompilation?.Export)
            {
                compilation = new ProjectCompilation();
                compilation.Export = export;
                compilation.EmbeddedReferences = new Dictionary<string, byte[]>();
                foreach (var reference in compilation.Export.MetadataReferences)
                {
                    if (compilation.ProjectReference == null)
                    {
                        compilation.ProjectReference = reference as IMetadataProjectReference;
                    }

                    var embedded = reference as IMetadataEmbeddedReference;
                    if (embedded != null)
                    {
                        compilation.EmbeddedReferences[embedded.Name] = embedded.Contents;
                    }
                }

                var diagnostics = compilation.ProjectReference.GetDiagnostics();
                compilation.Diagnostics = diagnostics.Diagnostics.ToList();

                _compilations[project.TargetFramework] = compilation;

                return true;
            }

            compilation = oldCompilation;
            return false;
        }

        private void SendOutgoingMessages()
        {
            if (IsDifferent(_local.ProjectInformation, _remote.ProjectInformation))
            {
                Logger.TraceInformation("[ApplicationContext]: OnTransmit(ProjectInformation)");

                _initializedContext.Transmit(new Message
                {
                    ContextId = Id,
                    MessageType = "ProjectInformation",
                    Payload = JToken.FromObject(_local.ProjectInformation)
                });

                _remote.ProjectInformation = _local.ProjectInformation;
            }

            var allDiagnostics = new List<DiagnosticsMessage>();

            if (_local.ProjectDiagnostics != null)
            {
                allDiagnostics.Add(_local.ProjectDiagnostics);
            }

            if (IsDifferent(_local.ProjectDiagnostics, _remote.ProjectDiagnostics))
            {
                _initializedContext.Transmit(new Message
                {
                    ContextId = Id,
                    MessageType = "Diagnostics",
                    Payload = _local.ProjectDiagnostics.ConvertToJson(ProtocolVersion)
                });

                _remote.ProjectDiagnostics = _local.ProjectDiagnostics;
            }

            var unprocessedFrameworks = new HashSet<FrameworkName>(_remote.Projects.Keys);

            foreach (var pair in _local.Projects)
            {
                ProjectWorld localProject = pair.Value;
                ProjectWorld remoteProject;

                if (!_remote.Projects.TryGetValue(pair.Key, out remoteProject))
                {
                    remoteProject = new ProjectWorld();
                    _remote.Projects[pair.Key] = remoteProject;
                }

                if (localProject.DependencyDiagnostics != null)
                {
                    allDiagnostics.Add(localProject.DependencyDiagnostics);
                }

                if (localProject.CompilationDiagnostics != null)
                {
                    allDiagnostics.Add(localProject.CompilationDiagnostics);
                }

                unprocessedFrameworks.Remove(pair.Key);

                if (IsDifferent(localProject.DependencyDiagnostics, remoteProject.DependencyDiagnostics))
                {
                    Logger.TraceInformation("[ApplicationContext]: OnTransmit(DependencyDiagnostics)");

                    _initializedContext.Transmit(new Message
                    {
                        ContextId = Id,
                        MessageType = "DependencyDiagnostics",
                        Payload = localProject.DependencyDiagnostics.ConvertToJson(ProtocolVersion)
                    });

                    remoteProject.DependencyDiagnostics = localProject.DependencyDiagnostics;
                }

                if (IsDifferent(localProject.Dependencies, remoteProject.Dependencies))
                {
                    Logger.TraceInformation("[ApplicationContext]: OnTransmit(Dependencies)");

                    _initializedContext.Transmit(new Message
                    {
                        ContextId = Id,
                        MessageType = "Dependencies",
                        Payload = JToken.FromObject(localProject.Dependencies)
                    });

                    remoteProject.Dependencies = localProject.Dependencies;
                }

                if (IsDifferent(localProject.CompilerOptions, remoteProject.CompilerOptions))
                {
                    Logger.TraceInformation("[ApplicationContext]: OnTransmit(CompilerOptions)");

                    _initializedContext.Transmit(new Message
                    {
                        ContextId = Id,
                        MessageType = "CompilerOptions",
                        Payload = JToken.FromObject(localProject.CompilerOptions)
                    });

                    remoteProject.CompilerOptions = localProject.CompilerOptions;
                }

                if (IsDifferent(localProject.References, remoteProject.References))
                {
                    Logger.TraceInformation("[ApplicationContext]: OnTransmit(References)");

                    _initializedContext.Transmit(new Message
                    {
                        ContextId = Id,
                        MessageType = "References",
                        Payload = JToken.FromObject(localProject.References)
                    });

                    remoteProject.References = localProject.References;
                }

                if (IsDifferent(localProject.Sources, remoteProject.Sources))
                {
                    Logger.TraceInformation("[ApplicationContext]: OnTransmit(Sources)");

                    _initializedContext.Transmit(new Message
                    {
                        ContextId = Id,
                        MessageType = "Sources",
                        Payload = JToken.FromObject(localProject.Sources)
                    });

                    remoteProject.Sources = localProject.Sources;
                }

                SendCompiledAssemblies(localProject);
            }

            SendDiagnostics(allDiagnostics);

            // Remove all processed frameworks from the remote view
            foreach (var framework in unprocessedFrameworks)
            {
                _remote.Projects.Remove(framework);
            }
        }

        private void SendDiagnostics(IList<DiagnosticsMessage> diagnostics)
        {
            if (diagnostics.Count == 0)
            {
                return;
            }

            // Group all of the diagnostics into group by target framework

            var messages = new List<DiagnosticsMessage>();

            foreach (var g in diagnostics.GroupBy(g => g.Framework))
            {
                var messageGroup = g.SelectMany(d => d.Diagnostics).ToList();
                messages.Add(new DiagnosticsMessage(messageGroup, g.Key));
            }

            var payload = JToken.FromObject(messages.Select(d => d.ConvertToJson(ProtocolVersion)));

            // Send all diagnostics back
            foreach (var connection in _waitingForDiagnostics)
            {
                connection.Transmit(new Message
                {
                    ContextId = Id,
                    MessageType = "AllDiagnostics",
                    Payload = payload
                });
            }

            _waitingForDiagnostics.Clear();
        }

        public void SendPluginMessage(object data)
        {
            SendMessage(data, messageType: "Plugin");
        }

        public void SendMessage(object data, string messageType)
        {
            var message = new Message
            {
                ContextId = Id,
                MessageType = messageType,
                Payload = JToken.FromObject(data)
            };

            _initializedContext.Transmit(message);
        }

        private void TriggerProjectOutputsChanged()
        {
            foreach (var pair in _waitingForCompiledAssemblies)
            {
                var waitingForCompiledAssemblies = pair.Value;

                _requiresAssemblies[pair.Key].Value = default(Void);

                for (int i = waitingForCompiledAssemblies.Count - 1; i >= 0; i--)
                {
                    var waitingForCompiledAssembly = waitingForCompiledAssemblies[i];

                    if (waitingForCompiledAssembly.AssemblySent)
                    {
                        Logger.TraceInformation("[ApplicationContext]: OnTransmit(ProjectChanged)");

                        waitingForCompiledAssembly.Connection.Transmit(writer =>
                        {
                            if (waitingForCompiledAssembly.Version == 0)
                            {
                                writer.Write("ProjectChanged");
                                writer.Write(Id);
                            }
                            else
                            {
                                var obj = new JObject();
                                obj["MessageType"] = "ProjectChanged";
                                obj["ContextId"] = Id;
                                writer.Write(obj.ToString(Formatting.None));
                            }
                        });

                        waitingForCompiledAssemblies.Remove(waitingForCompiledAssembly);
                    }
                }
            }
        }

        private void SendCompiledAssemblies(ProjectWorld localProject)
        {
            if (localProject.Outputs == null)
            {
                return;
            }

            List<CompiledAssemblyState> waitingForCompiledAssemblies;
            if (_waitingForCompiledAssemblies.TryGetValue(localProject.TargetFramework, out waitingForCompiledAssemblies))
            {
                foreach (var waitingForCompiledAssembly in waitingForCompiledAssemblies)
                {
                    if (!waitingForCompiledAssembly.AssemblySent)
                    {
                        Logger.TraceInformation("[ApplicationContext]: OnTransmit(Assembly)");

                        int version = waitingForCompiledAssembly.Version;

                        waitingForCompiledAssembly.Connection.Transmit(writer =>
                        {
                            WriteProjectSources(version, localProject, writer);
                            WriteAssembly(version, localProject, writer);
                        });

                        waitingForCompiledAssembly.AssemblySent = true;
                    }
                }
            }
        }

        private void WriteProjectSources(int version, ProjectWorld project, BinaryWriter writer)
        {
            if (version == 0)
            {
                writer.Write("Sources");
                writer.Write(project.Sources.Files.Count);
                foreach (var file in project.Sources.Files)
                {
                    writer.Write(file);
                }
            }
            else
            {
                var obj = new JObject();
                obj["MessageType"] = "Sources";
                obj["Files"] = new JArray(project.Sources.Files);
                writer.Write(obj.ToString(Formatting.None));
            }
        }

        private void WriteAssembly(int version, ProjectWorld project, BinaryWriter writer)
        {
            if (version == 0)
            {
                writer.Write("Assembly");
                writer.Write(Id);

                writer.Write(project.CompilationDiagnostics.Warnings.Count);
                foreach (var warning in project.CompilationDiagnostics.Warnings)
                {
                    writer.Write(warning.FormattedMessage);
                }

                writer.Write(project.CompilationDiagnostics.Errors.Count);
                foreach (var error in project.CompilationDiagnostics.Errors)
                {
                    writer.Write(error.FormattedMessage);
                }

                WriteAssembly(project, writer);
            }
            else
            {
                var obj = new JObject();
                obj["MessageType"] = "Assembly";
                obj["ContextId"] = Id;
                obj[nameof(CompileResponse.Diagnostics)] = ConvertToJArray(project.CompilationDiagnostics.Diagnostics);
                obj[nameof(CompileResponse.AssemblyPath)] = project.Outputs.AssemblyPath;
                obj["Blobs"] = 2;
                writer.Write(obj.ToString(Formatting.None));

                WriteAssembly(project, writer);
            }
        }

        private static JArray ConvertToJArray(IList<ICompilationMessage> diagnostics)
        {
            var values = diagnostics.Select(diagnostic => new JObject
            {
                [nameof(ICompilationMessage.SourceFilePath)] = diagnostic.SourceFilePath,
                [nameof(ICompilationMessage.Message)] = diagnostic.Message,
                [nameof(ICompilationMessage.FormattedMessage)] = diagnostic.FormattedMessage,
                [nameof(ICompilationMessage.Severity)] = (int)diagnostic.Severity,
                [nameof(ICompilationMessage.StartColumn)] = diagnostic.StartColumn,
                [nameof(ICompilationMessage.StartLine)] = diagnostic.StartLine,
                [nameof(ICompilationMessage.EndColumn)] = diagnostic.EndColumn,
                [nameof(ICompilationMessage.EndLine)] = diagnostic.EndLine,
            });

            return new JArray(values);
        }

        private static void WriteAssembly(ProjectWorld project, BinaryWriter writer)
        {
            writer.Write(project.Outputs.EmbeddedReferences.Count);
            foreach (var pair in project.Outputs.EmbeddedReferences)
            {
                writer.Write(pair.Key);
                writer.Write(pair.Value.Length);
                writer.Write(pair.Value);
            }
            writer.Write(project.Outputs.AssemblyBytes.Length);
            writer.Write(project.Outputs.AssemblyBytes);
            writer.Write(project.Outputs.PdbBytes.Length);
            writer.Write(project.Outputs.PdbBytes);
        }

        private bool IsDifferent<T>(T local, T remote) where T : class
        {
            // If no value was ever produced, then don't even bother
            if (local == null)
            {
                return false;
            }

            return !object.Equals(local, remote);
        }

        private State DoInitialWork(string appPath, string configuration, bool triggerBuildOutputs, bool triggerDependencies)
        {
            var state = new State
            {
                Frameworks = new List<FrameworkData>(),
                Projects = new List<ProjectInfo>(),
                Diagnostics = new List<ICompilationMessage>()
            };

            Project project;
            if (!Project.TryGetProject(appPath, out project, state.Diagnostics))
            {
                throw new InvalidOperationException(string.Format("Unable to find project.json in '{0}'", appPath));
            }

            if (triggerBuildOutputs)
            {
                // Trigger the build outputs for this project
                _namedDependencyProvider.Trigger(project.Name + "_BuildOutputs");
            }

            if (triggerDependencies)
            {
                _namedDependencyProvider.Trigger(project.Name + "_Dependencies");
            }

            state.Name = project.Name;
            state.Configurations = project.GetConfigurations().ToList();
            state.Commands = project.Commands;

            var frameworks = new List<FrameworkName>(
                project.GetTargetFrameworks()
                .Select(tf => tf.FrameworkName));

            if (!frameworks.Any())
            {
                frameworks.Add(VersionUtility.ParseFrameworkName(FrameworkNames.ShortNames.Dnx451));
            }

            var sourcesProjectWideSources = project.Files.SourceFiles.ToList();

            foreach (var frameworkName in frameworks)
            {
                var dependencyInfo = ResolveProjectDepencies(project, configuration, frameworkName);
                var dependencySources = new List<string>(sourcesProjectWideSources);

                var frameworkResolver = dependencyInfo.HostContext.FrameworkReferenceResolver;

                var frameworkData = new FrameworkData
                {
                    ShortName = VersionUtility.GetShortFrameworkName(frameworkName),
                    FrameworkName = frameworkName.ToString(),
                    FriendlyName = frameworkResolver.GetFriendlyFrameworkName(frameworkName),
                    RedistListPath = frameworkResolver.GetFrameworkRedistListPath(frameworkName)
                };

                state.Frameworks.Add(frameworkData);

                // Add shared files from packages
                dependencySources.AddRange(dependencyInfo.ExportedSourcesFiles);

                // Add shared files from projects
                foreach (var reference in dependencyInfo.ProjectReferences)
                {
                    // Only add direct dependencies as sources
                    if (!project.Dependencies.Any(d => string.Equals(d.Name, reference.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    Project referencedProject;
                    if (Project.TryGetProject(reference.Path, out referencedProject))
                    {
                        dependencySources.AddRange(referencedProject.Files.SharedFiles);
                    }
                }

                var projectInfo = new ProjectInfo()
                {
                    Path = appPath,
                    Configuration = configuration,
                    TargetFramework = frameworkData,
                    FrameworkName = frameworkName,
                    // TODO: This shouldn't be roslyn specific compilation options
                    CompilationSettings = project.GetCompilerOptions(frameworkName, configuration)
                                                 .ToCompilationSettings(frameworkName),
                    SourceFiles = dependencySources,
                    Diagnostics = dependencyInfo.HostContext.DependencyWalker.GetDependencyDiagnostics(project.ProjectFilePath),
                    DependencyInfo = dependencyInfo
                };

                state.Projects.Add(projectInfo);

                if (state.ProjectSearchPaths == null)
                {
                    state.ProjectSearchPaths = dependencyInfo.HostContext.ProjectResolver.SearchPaths.ToList();
                }

                if (state.GlobalJsonPath == null)
                {
                    GlobalSettings settings;
                    if (GlobalSettings.TryGetGlobalSettings(dependencyInfo.HostContext.RootDirectory, out settings))
                    {
                        state.GlobalJsonPath = settings.FilePath;
                    }
                }
            }

            return state;
        }

        private ApplicationHostContext GetApplicationHostContext(Project project, string configuration, FrameworkName frameworkName, bool useRuntimeLoadContextFactory = true)
        {
            var cacheKey = Tuple.Create("ApplicationContext", project.Name, configuration, frameworkName);

            return _cache.Get<ApplicationHostContext>(cacheKey, ctx =>
            {
                var applicationHostContext = new ApplicationHostContext(_hostServices,
                                                                        project.ProjectDirectory,
                                                                        packagesDirectory: null,
                                                                        configuration: configuration,
                                                                        targetFramework: frameworkName,
                                                                        cache: _cache,
                                                                        cacheContextAccessor: _cacheContextAccessor,
                                                                        namedCacheDependencyProvider: _namedDependencyProvider,
                                                                        loadContextFactory: GetRuntimeLoadContextFactory(project),
                                                                        skipLockFileValidation: true);

                applicationHostContext.DependencyWalker.Walk(project.Name, project.Version, frameworkName);

                // Watch all projects for project.json changes
                foreach (var library in applicationHostContext.DependencyWalker.Libraries)
                {
                    if (string.Equals(library.Type, "Project"))
                    {
                        ctx.Monitor(new FileWriteTimeCacheDependency(library.Path));
                    }
                }

                // Add a cache dependency on restore complete to reevaluate dependencies
                ctx.Monitor(_namedDependencyProvider.GetNamedDependency(project.Name + "_Dependencies"));

                return applicationHostContext;
            });
        }

        private IAssemblyLoadContextFactory GetRuntimeLoadContextFactory(Project project)
        {
            return new DesignTimeAssemblyLoadContextFactory(
                project,
                _appEnv,
                _cache,
                _cacheContextAccessor,
                _namedDependencyProvider);
        }

        private DependencyInfo ResolveProjectDepencies(Project project, string configuration, FrameworkName frameworkName)
        {
            var cacheKey = Tuple.Create("DependencyInfo", project.Name, configuration, frameworkName);

            return _cache.Get<DependencyInfo>(cacheKey, ctx =>
            {
                var applicationHostContext = GetApplicationHostContext(project, configuration, frameworkName);

                var libraryManager = applicationHostContext.LibraryManager;
                var frameworkResolver = applicationHostContext.FrameworkReferenceResolver;

                var info = new DependencyInfo
                {
                    Dependencies = new Dictionary<string, DependencyDescription>(),
                    ProjectReferences = new List<ProjectReference>(),
                    HostContext = applicationHostContext,
                    References = new List<string>(),
                    RawReferences = new Dictionary<string, byte[]>(),
                    ExportedSourcesFiles = new List<string>()
                };

                foreach (var library in applicationHostContext.DependencyWalker.Libraries)
                {
                    var description = CreateDependencyDescription(library);
                    info.Dependencies[description.Name] = description;

                    // Skip unresolved libraries
                    if (!library.Resolved)
                    {
                        continue;
                    }

                    if (string.Equals(library.Type, "Project") &&
                       !string.Equals(library.Identity.Name, project.Name))
                    {
                        Project referencedProject;
                        if (!Project.TryGetProject(library.Path, out referencedProject))
                        {
                            // Should never happen
                            continue;
                        }

                        var targetFrameworkInformation = referencedProject.GetTargetFramework(library.Framework);

                        // If this is an assembly reference then treat it like a file reference
                        if (!string.IsNullOrEmpty(targetFrameworkInformation.AssemblyPath) &&
                            string.IsNullOrEmpty(targetFrameworkInformation.WrappedProject))
                        {
                            string assemblyPath = GetProjectRelativeFullPath(referencedProject, targetFrameworkInformation.AssemblyPath);
                            info.References.Add(assemblyPath);

                            description.Path = assemblyPath;
                            description.Type = "Assembly";
                        }
                        else
                        {
                            string wrappedProjectPath = null;

                            if (!string.IsNullOrEmpty(targetFrameworkInformation.WrappedProject))
                            {
                                wrappedProjectPath = GetProjectRelativeFullPath(referencedProject, targetFrameworkInformation.WrappedProject);
                            }

                            info.ProjectReferences.Add(new ProjectReference
                            {
                                Name = referencedProject.Name,
                                Framework = new FrameworkData
                                {
                                    ShortName = VersionUtility.GetShortFrameworkName(library.Framework),
                                    FrameworkName = library.Framework.ToString(),
                                    FriendlyName = frameworkResolver.GetFriendlyFrameworkName(library.Framework)
                                },
                                Path = library.Path,
                                WrappedProjectPath = wrappedProjectPath
                            });
                        }
                    }
                }

                var exportWithoutProjects = ProjectExportProviderHelper.GetExportsRecursive(
                     _cache,
                     applicationHostContext.LibraryManager,
                     applicationHostContext.LibraryExportProvider,
                     new LibraryKey
                     {
                         Configuration = configuration,
                         TargetFramework = frameworkName,
                         Name = project.Name
                     },
                     library => library.Type != "Project");

                foreach (var reference in exportWithoutProjects.MetadataReferences)
                {
                    var fileReference = reference as IMetadataFileReference;
                    if (fileReference != null)
                    {
                        info.References.Add(fileReference.Path);
                    }

                    var embedded = reference as IMetadataEmbeddedReference;
                    if (embedded != null)
                    {
                        info.RawReferences[embedded.Name] = embedded.Contents;
                    }
                }

                foreach (var sourceFileReference in exportWithoutProjects.SourceReferences.OfType<ISourceFileReference>())
                {
                    info.ExportedSourcesFiles.Add(sourceFileReference.Path);
                }

                return info;
            });
        }

        private static string GetProjectRelativeFullPath(Project referencedProject, string path)
        {
            return Path.GetFullPath(Path.Combine(referencedProject.ProjectDirectory, path));
        }

        private static DependencyDescription CreateDependencyDescription(LibraryDescription library)
        {
            return new DependencyDescription
            {
                Name = library.Identity.Name,
                Version = library.Identity.Version?.ToString(),
                Type = library.Resolved ? library.Type : "Unresolved",
                Path = library.Path,
                Dependencies = library.Dependencies.Select(dependency => new DependencyItem
                {
                    Name = dependency.Name,
                    Version = dependency.Library?.Version?.ToString()
                })
            };
        }

        private static string GetValue(JToken token, string name)
        {
            return GetValue<string>(token, name);
        }

        private static TVal GetValue<TVal>(JToken token, string name)
        {
            var value = token?[name];
            if (value != null)
            {
                return value.Value<TVal>();
            }

            return default(TVal);
        }

        private class Trigger<TValue>
        {
            private TValue _value;

            public bool WasAssigned { get; private set; }

            public void ClearAssigned()
            {
                WasAssigned = false;
            }

            public TValue Value
            {
                get { return _value; }
                set
                {
                    WasAssigned = true;
                    _value = value;
                }
            }
        }

        private class State
        {
            public string Name { get; set; }

            public IList<string> ProjectSearchPaths { get; set; }

            public string GlobalJsonPath { get; set; }

            public IList<string> Configurations { get; set; }

            public IList<FrameworkData> Frameworks { get; set; }

            public IDictionary<string, string> Commands { get; set; }

            public IList<ProjectInfo> Projects { get; set; }

            public IList<ICompilationMessage> Diagnostics { get; set; }
        }

        // Represents a project that should be used for intellisense
        private class ProjectInfo
        {
            public string Path { get; set; }

            public string Configuration { get; set; }

            public FrameworkName FrameworkName { get; set; }

            public FrameworkData TargetFramework { get; set; }

            public CompilationSettings CompilationSettings { get; set; }

            public IList<string> SourceFiles { get; set; }

            public IList<ICompilationMessage> Diagnostics { get; set; }

            public DependencyInfo DependencyInfo { get; set; }
        }

        private class DependencyInfo
        {
            public ApplicationHostContext HostContext { get; set; }

            public IDictionary<string, byte[]> RawReferences { get; set; }

            public IDictionary<string, DependencyDescription> Dependencies { get; set; }

            public IList<string> References { get; set; }

            public IList<ProjectReference> ProjectReferences { get; set; }

            public IList<string> ExportedSourcesFiles { get; set; }
        }

        private class ProjectCompilation
        {
            public ILibraryExport Export { get; set; }

            public IMetadataProjectReference ProjectReference { get; set; }

            public IDictionary<string, byte[]> EmbeddedReferences { get; set; }

            public IList<ICompilationMessage> Diagnostics { get; set; }

            public bool HasOutputs
            {
                get
                {
                    return AssemblyBytes != null || AssemblyPath != null;
                }
            }

            public byte[] AssemblyBytes { get; set; }

            public byte[] PdbBytes { get; set; }

            public string AssemblyPath { get; set; }
        }

        private class CompiledAssemblyState
        {
            public ConnectionContext Connection { get; set; }

            public bool AssemblySent { get; set; }

            public int Version { get; set; }
        }

        private class RemoteLibraryKey
        {
            public int Version { get; set; }

            public string Name { get; set; }

            public string TargetFramework { get; set; }

            public string Configuration { get; set; }

            public string Aspect { get; set; }
        }

        private class LibraryKey : ILibraryKey
        {
            public string Name { get; set; }

            public FrameworkName TargetFramework { get; set; }

            public string Configuration { get; set; }

            public string Aspect { get; set; }
        }

        private struct Void
        {
        }
    }
}
