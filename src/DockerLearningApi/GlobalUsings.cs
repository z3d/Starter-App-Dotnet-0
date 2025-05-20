// Essential .NET namespaces
global using System.Text.Json;
global using System.Text.Json.Serialization;
global using System.ComponentModel.DataAnnotations;
global using System.ComponentModel.DataAnnotations.Schema;

// Microsoft namespaces
global using Microsoft.AspNetCore.Mvc;
global using Microsoft.EntityFrameworkCore;

// Third-party libraries
global using MediatR;

// Logging with Serilog
global using Serilog;
global using Serilog.Events;

// Domain references
global using DockerLearning.Domain.Entities;
global using DockerLearning.Domain.ValueObjects;
global using DockerLearning.Domain.Interfaces;

// Application references
global using DockerLearningApi.Application.DTOs;
global using DockerLearningApi.Application.Commands;
global using DockerLearningApi.Application.Queries;
global using DockerLearningApi.Infrastructure.Repositories;