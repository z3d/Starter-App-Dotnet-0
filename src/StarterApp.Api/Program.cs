using StarterApp.Api.Data;
using StarterApp.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

// Add Aspire service defaults
builder.AddServiceDefaults();

// Configure Docker environment settings if needed
if (builder.Environment.EnvironmentName == "Docker")
    builder.Configuration.AddJsonFile("appsettings.Docker.json", optional: false);

// Configure Serilog using settings from appsettings.json
builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");

    // Add Seq sink if URL is provided
    var seqUrl = context.Configuration["SEQ_URL"] ?? context.Configuration["SeqUrl"];
    if (!string.IsNullOrEmpty(seqUrl))
    {
        configuration.WriteTo.Seq(seqUrl);
    }
});

// Add services to the container
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();

// Add native OpenAPI support with .NET 9
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Info.Title = "Starter App API";
        document.Info.Version = "v1";
        document.Info.Description = "A sample API for the Starter App built with .NET 9 Minimal APIs";
        document.Info.Contact = new()
        {
            Name = "Starter App Team"
        };

        // Configure API endpoint tags for better Swagger organization
        document.Tags = new List<Microsoft.OpenApi.Models.OpenApiTag>
        {
            new() {
                Name = "Customers",
                Description = "Customer management operations including CRUD functionality for customer data"
            },
            new() {
                Name = "Orders",
                Description = "Order processing and management including creation, status updates, and cancellation"
            },
            new() {
                Name = "Products",
                Description = "Product catalog management with full CRUD operations for product inventory"
            }
        };

        return Task.CompletedTask;
    });
});

// Add Database context - use "database" connection string from Aspire
var connectionString = builder.Configuration.GetConnectionString("database")
    ?? throw new InvalidOperationException("Connection string 'database' not found. Ensure Aspire is configured correctly.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString)
           .EnableSensitiveDataLogging(false)); // Never log sensitive data

// Register IDbConnection for Dapper queries
builder.Services.AddScoped<System.Data.IDbConnection>(provider =>
    new Microsoft.Data.SqlClient.SqlConnection(connectionString));

// Register mediator for CQRS pattern - handlers are auto-registered via reflection
builder.Services.AddMediator(Assembly.GetExecutingAssembly());

// Add CORS (restrict in production)
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (builder.Environment.IsDevelopment())
            policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
        else
            policy.WithOrigins(builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? Array.Empty<string>())
                  .WithMethods("GET", "POST", "PUT", "DELETE")
                  .WithHeaders("Authorization", "Content-Type");
    });
});

// Add Health checks and rate limiting
builder.Services.AddHealthChecks();
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("fixed", options =>
    {
        options.PermitLimit = 100;
        options.Window = TimeSpan.FromMinutes(1);
        options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        options.QueueLimit = 5;
    });

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

var app = builder.Build();

try
{
    Log.Information("Starting up application");

    // Log connection status
    if (app.Environment.IsDevelopment())
    {
        Log.Information("Database connection configured: {ConnectionString}", MaskConnectionStringPassword(connectionString));
    }
    else
    {
        Log.Information("Database connection configured successfully");
    }

    // Configure the HTTP request pipeline
    if (app.Environment.IsDevelopment() || app.Environment.EnvironmentName == "Docker")
    {
        // Use the new native .NET 9 OpenAPI endpoints
        app.MapOpenApi(); // Serves the OpenAPI document at /openapi/v1.json

        // Configure SwaggerUI to use the new OpenAPI endpoint
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/openapi/v1.json", "Docker Learning API v1");
            options.RoutePrefix = "swagger";
        });
    }
    else
    {
        app.UseHsts();
    }

    // Use .NET 9 Problem Details with StatusCodeSelector for centralized exception handling
    app.UseExceptionHandler(new ExceptionHandlerOptions
    {
        StatusCodeSelector = ex => ex switch
        {
            ArgumentNullException => StatusCodes.Status400BadRequest,
            ArgumentOutOfRangeException => StatusCodes.Status400BadRequest,
            ArgumentException => StatusCodes.Status400BadRequest,
            KeyNotFoundException => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status500InternalServerError
        }
    });

    app.UseStatusCodePages();

    // Security headers middleware
    app.Use(async (context, next) =>
    {
        context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
        context.Response.Headers.Append("X-Frame-Options", "DENY");
        context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
        context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");

        if (!app.Environment.IsDevelopment())
            context.Response.Headers.Append("Content-Security-Policy",
                "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'");

        await next();
    });

    // Use DbUp for database migrations
    if (connectionString != null)
    {
        Log.Information("Starting database migrations...");
        var migrationResult = DatabaseMigrator.MigrateDatabase(connectionString);
        if (migrationResult)
        {
            Log.Information("Database migrations completed successfully.");
        }
        else
        {
            Log.Error("Database migrations failed!");
            if (!app.Environment.IsDevelopment())
                Environment.Exit(1);
        }
    }
    else
    {
        Log.Warning("Connection string 'database' is missing. Skipping database migration.");
    }

    // Enable middlewares
    app.UseHttpsRedirection();
    app.UseCors();
    app.UseRateLimiter();
    app.UseRouting();

    // Map API endpoints using the new minimal API pattern
    app.MapApiEndpoints();

    app.MapHealthChecks("/health");

    // Map Aspire service defaults endpoints
    app.MapDefaultEndpoints();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application start-up failed: {ExMessage}", ex.Message);
    Environment.Exit(1);
}
finally
{
    Log.CloseAndFlush();
}

// Helper method to mask passwords in connection strings
static string? MaskConnectionStringPassword(string? connectionString)
{
    if (string.IsNullOrEmpty(connectionString))
        return connectionString;

    return System.Text.RegularExpressions.Regex.Replace(
        connectionString,
        @"(password|pwd)\s*=\s*[^;]+",
        "$1=***MASKED***",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
}




