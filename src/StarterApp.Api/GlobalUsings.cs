// Essential .NET namespaces
// Third-party libraries
global using Dapper;
// Microsoft namespaces
global using Microsoft.AspNetCore.Mvc;
global using Microsoft.AspNetCore.RateLimiting;
global using Microsoft.Data.SqlClient;
global using Microsoft.EntityFrameworkCore;
// Logging with Serilog
global using Serilog;
global using StarterApp.Api.Application.Commands;
// Application references
global using StarterApp.Api.Application.DTOs;
global using StarterApp.Api.Application.Interfaces;
global using StarterApp.Api.Application.Queries;
global using StarterApp.Api.Application.ReadModels;
global using StarterApp.Api.Infrastructure.Mediator;
// Domain references
global using StarterApp.Domain.Entities;
global using StarterApp.Domain.Enums;
global using StarterApp.Domain.ValueObjects;
global using System.ComponentModel.DataAnnotations;
global using System.Reflection;
global using System.Threading.RateLimiting;



