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
global using System.Data.SqlClient;
global using System.Reflection;

// Testing frameworks
global using Xunit;
global using Xunit.Abstractions;
global using Moq;

// Microsoft ASP.NET Core
global using Microsoft.AspNetCore.Mvc;
global using Microsoft.Extensions.Configuration;

// Application libraries
global using MediatR;
global using DbUp;
global using DbUp.Engine;

// Logging with Serilog
global using Serilog;
global using Serilog.Events;

// Domain references
global using StarterApp.Domain.Entities;
global using StarterApp.Domain.Enums;
global using StarterApp.Domain.Interfaces;
global using StarterApp.Domain.ValueObjects;

// Application references
global using StarterApp.Api.Application.Commands;
global using StarterApp.Api.Application.Queries;
global using StarterApp.Api.Application.DTOs;
global using StarterApp.Api.Application.Interfaces;
global using StarterApp.Api.Application.ReadModels;