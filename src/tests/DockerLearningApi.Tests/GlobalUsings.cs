// Essential .NET namespaces
global using System;
global using System.Linq;
global using System.Threading;
global using System.Threading.Tasks;
global using System.Collections.Generic;
global using System.ComponentModel.DataAnnotations;
global using System.Net;
global using System.Net.Http;
global using System.Net.Http.Json;

// Testing frameworks
global using Xunit;
global using Xunit.Abstractions;
global using Moq;

// Application libraries
global using MediatR;

// Logging with Serilog
global using Serilog;
global using Serilog.Events;

// Domain references
global using DockerLearning.Domain.Entities;
global using DockerLearning.Domain.Interfaces;
global using DockerLearning.Domain.ValueObjects;

// Application references
global using DockerLearningApi.Application.Commands;
global using DockerLearningApi.Application.Queries;
global using DockerLearningApi.Application.DTOs;
global using DockerLearningApi.Application.Interfaces;