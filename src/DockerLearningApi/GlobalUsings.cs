global using System.Text.Json;
global using System.Text.Json.Serialization;
global using System.ComponentModel.DataAnnotations;
global using System.ComponentModel.DataAnnotations.Schema;

global using Microsoft.AspNetCore.Mvc;
global using Microsoft.EntityFrameworkCore;
global using Microsoft.Extensions.Logging;

global using MediatR;

global using DockerLearningApi.Domain.Entities;
global using DockerLearningApi.Domain.ValueObjects;
global using DockerLearningApi.Domain.Interfaces;
global using DockerLearningApi.Application.DTOs;
global using DockerLearningApi.Application.Commands;
global using DockerLearningApi.Application.Queries;
global using DockerLearningApi.Infrastructure.Repositories;