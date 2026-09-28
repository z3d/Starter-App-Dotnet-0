using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StarterApp.Functions;

var builder = FunctionsApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddPayloadCapture();
builder.AddJobRunRecording();
builder.AddMessageInbox();
builder.Services.AddOptions<JobWatchOptions>().BindConfiguration(JobWatchOptions.SectionName);
builder.Services.AddSingleton<JobWatchStart>();

builder.Build().Run();
