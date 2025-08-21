// Essential .NET namespaces
global using System.Reflection;
global using System.Net;
global using System.Text.Json;
global using System.Text.Json.Serialization;
global using System.ComponentModel.DataAnnotations;
global using System.ComponentModel.DataAnnotations.Schema;
global using System.Threading.RateLimiting;

// Microsoft namespaces
global using Microsoft.AspNetCore.Mvc;
global using Microsoft.AspNetCore.Diagnostics;
global using Microsoft.AspNetCore.RateLimiting;
global using Microsoft.EntityFrameworkCore;

// Third-party libraries
global using MediatR;
global using Dapper;
global using Microsoft.Data.SqlClient;

// Logging with Serilog
global using Serilog;
global using Serilog.Events;

// Domain references
global using StarterApp.Domain.Entities;
global using StarterApp.Domain.Enums;
global using StarterApp.Domain.ValueObjects;
global using StarterApp.Domain.Interfaces;

// Application references
global using StarterApp.Api.Application.DTOs;
global using StarterApp.Api.Application.Commands;
global using StarterApp.Api.Application.Queries;
global using StarterApp.Api.Application.ReadModels;
global using StarterApp.Api.Application.Interfaces;
global using StarterApp.Api.Infrastructure.Repositories;