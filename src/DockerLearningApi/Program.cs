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

// Use DbUp for database migrations instead of EF Core
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (!DatabaseMigrator.MigrateDatabase(connectionString))
{
    // If migrations fail, we might want to stop the application from fully starting
    if (!app.Environment.IsDevelopment())
    {
        return 1;
    }
}

app.UseHttpsRedirection();
app.MapControllers();

// Health check endpoint
app.MapGet("/health", () => "Healthy");

app.Run();
return 0;
