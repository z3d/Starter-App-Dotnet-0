using DockerLearningApi.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Configure Docker environment settings if needed
if (builder.Environment.EnvironmentName == "Docker")
{
    builder.Configuration.AddJsonFile("appsettings.Docker.json", optional: false);
}

// Add services to the container.
builder.Services.AddOpenApi();

// Add Database context
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Add controller support
builder.Services.AddControllers();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Apply migrations automatically during startup in non-production environments
if (!app.Environment.IsProduction())
{
    using (var scope = app.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.Database.Migrate();
    }
}

app.UseHttpsRedirection();
app.MapControllers();

// Health check endpoint
app.MapGet("/health", () => "Healthy");

app.Run();
