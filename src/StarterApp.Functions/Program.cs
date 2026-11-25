using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StarterApp.Application.Infrastructure.Worker;
using StarterApp.Functions;

var builder = FunctionsApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddPayloadCapture();
builder.AddJobRunRecording();
builder.AddMessageInbox();
builder.AddWorkerOperations();
builder.AddWorkerHealth();
builder.Services.AddSingleton(provider => new IdempotencyRecordPurge(provider.GetService<Npgsql.NpgsqlDataSource>()));
builder.Services.AddOptions<JobWatchOptions>().BindConfiguration(JobWatchOptions.SectionName);

builder.Build().Run();
