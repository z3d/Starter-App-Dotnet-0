// Essential .NET namespaces
global using System.Text.Json;
global using System.Text.Json.Serialization;

// Azure Service Bus
global using Azure.Messaging.ServiceBus;
global using Azure.Messaging.ServiceBus.Administration;

// Microsoft Extensions
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Hosting;
global using Microsoft.Extensions.Logging;
global using Microsoft.Extensions.Options;

// Logging with Serilog
global using Serilog;
global using Serilog.Events;

// Domain references
global using DockerLearning.Domain.Entities;
global using DockerLearning.Domain.ValueObjects;

// ServiceBus references
global using DockerLearning.ServiceBus.Models;
global using DockerLearning.ServiceBus.Services;
global using DockerLearning.ServiceBus.Configuration;