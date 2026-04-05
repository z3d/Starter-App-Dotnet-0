// Essential .NET namespaces
// Third-party libraries
global using Dapper;
global using Microsoft.AspNetCore.Diagnostics.HealthChecks;
// Microsoft namespaces
global using Microsoft.AspNetCore.RateLimiting;
global using Microsoft.EntityFrameworkCore;
// Logging with Serilog
global using Serilog;
global using StarterApp.Api.Application;
global using StarterApp.Api.Application.Commands;
// Application references
global using StarterApp.Api.Application.DTOs;
global using StarterApp.Api.Application.Interfaces;
global using StarterApp.Api.Application.Mapping;
global using StarterApp.Api.Application.Queries;
global using StarterApp.Api.Application.ReadModels;
global using StarterApp.Api.Data;
global using StarterApp.Api.Infrastructure;
global using StarterApp.Api.Infrastructure.Caching;
global using StarterApp.Api.Infrastructure.Mediator;
global using StarterApp.Api.Infrastructure.Validation;
global using StarterApp.Domain.Abstractions;
// Domain references
global using StarterApp.Domain.Entities;
global using StarterApp.Domain.Enums;
global using StarterApp.Domain.ValueObjects;
global using System.Data;
global using System.Reflection;
global using System.Threading.RateLimiting;

