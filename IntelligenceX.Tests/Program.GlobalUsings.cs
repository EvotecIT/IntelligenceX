global using System;
global using System.Collections.Generic;
global using System.Diagnostics;
global using System.IO;
global using System.Net;
global using System.Net.Sockets;
global using System.Reflection;
global using System.Text;
global using System.Threading;
global using System.Threading.Tasks;
#if !NET472
global using IntelligenceX.Analysis;
global using IntelligenceX.Cli;
global using IntelligenceX.Cli.Release;
global using IntelligenceX.Cli.Setup;
global using IntelligenceX.Cli.Setup.Host;
#endif
global using IntelligenceX.Copilot;
global using IntelligenceX.Json;
global using IntelligenceX.OpenAI;
global using IntelligenceX.OpenAI.AppServer;
global using IntelligenceX.OpenAI.AppServer.Models;
global using IntelligenceX.OpenAI.Chat;
global using IntelligenceX.OpenAI.Native;
global using IntelligenceX.OpenAI.ToolCalling;
global using IntelligenceX.OpenAI.Transport;
global using IntelligenceX.OpenAI.Usage;
global using IntelligenceX.Rpc;
global using IntelligenceX.Telemetry;
global using IntelligenceX.Tools;
global using IntelligenceX.Utils;
#if INTELLIGENCEX_REVIEWER
global using IntelligenceX.Reviewer;
#endif
