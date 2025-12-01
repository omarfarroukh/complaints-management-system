using CMS.Api.Filters;
using CMS.Api.Middleware;
using CMS.Application.Interfaces;
using CMS.Domain.Entities;
using CMS.Infrastructure.Hubs;
using CMS.Infrastructure.Persistence;
using CMS.Infrastructure.Services;
using Hangfire;
using Hangfire.MemoryStorage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Events;
using StackExchange.Redis;
using System.Text;
using CMS.Infrastructure.BackgroundJobs;
using System.Reflection;

// ======================================
//    1. SETUP SERILOG (BEFORE BUILDER)
// ======================================
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .MinimumLevel.Override("Hangfire", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore.Authentication", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "CMS.Api")
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .WriteTo.Async(a => a.File(
        path: "Logs/audit-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"
    ))
    .CreateLogger();

try
{
    Log.Information("🚀 Starting CMS API application...");

    var builder = WebApplication.CreateBuilder(args);

    // Use Serilog as the logging provider
    builder.Host.UseSerilog();

    // ======================================
    //      2. SERVICE REGISTRATION
    // ======================================

    // Database (PostgreSQL)
    builder.Services.AddDbContext<AppDbContext>(options =>
    {
        options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));

        // Enable sensitive data logging in development only
        if (builder.Environment.IsDevelopment())
        {
            options.EnableSensitiveDataLogging();
            options.EnableDetailedErrors();
        }
    });

    // Redis Configuration (Critical for AOP Caching & Rate Limiting)
    var redisConnectionString = builder.Configuration.GetConnectionString("Redis");

    if (string.IsNullOrWhiteSpace(redisConnectionString))
    {
        redisConnectionString = "localhost:6379";
        Log.Warning("Redis connection string is empty. Falling back to default: {RedisConnection}", redisConnectionString);
    }

    Log.Information("Configuring Redis connection: {RedisConnection}", redisConnectionString);

    // 1. Standard Distributed Cache (IDistributedCache) for general caching
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnectionString;
        options.InstanceName = "CMS:";
    });

    // 2. Raw Redis Connection (IConnectionMultiplexer) for advanced operations
    builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    {
        try
        {
            var config = ConfigurationOptions.Parse(redisConnectionString);
            config.AbortOnConnectFail = false;
            config.ConnectTimeout = 5000;
            config.SyncTimeout = 5000;

            var redis = ConnectionMultiplexer.Connect(config);

            Log.Information("✅ Redis connection established successfully");
            return redis;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "❌ Failed to connect to Redis. Caching will be disabled.");
            throw;
        }
    });

    // Identity Configuration
    builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequireDigit = true;
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;

        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedEmail = true;

        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.AllowedForNewUsers = true;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

    // JWT Authentication
    var jwtKey = builder.Configuration["Jwt:Key"];
    if (string.IsNullOrEmpty(jwtKey))
    {
        throw new InvalidOperationException("JWT Key is not configured in appsettings.json");
    }

    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.Zero,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                
                if (!string.IsNullOrEmpty(accessToken) && 
                    (path.StartsWithSegments("/notifications") || path.StartsWithSegments("/api/Dashboard")))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            },
            OnAuthenticationFailed = context =>
            {
                Log.Warning("JWT authentication failed: {Error}", context.Exception.Message);
                return Task.CompletedTask;
            }
        };
    });

    // Hangfire (Background Jobs)
    builder.Services.AddHangfire(config => config
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UseMemoryStorage());

    builder.Services.AddHangfireServer(options =>
    {
        options.WorkerCount = 2;
    });

    // HttpContext Access
    builder.Services.AddHttpContextAccessor();

    // Application Services (Dependency Injection)
    builder.Services.AddScoped<IAuthService, AuthService>();
    builder.Services.AddScoped<IEmailService, SmtpEmailService>();
    builder.Services.AddScoped<ISecurityService, SecurityService>();
    builder.Services.AddScoped<IUserService, UserService>();
    builder.Services.AddScoped<IFileStorageService, LocalFileStorageService>();
    builder.Services.AddScoped<IRateLimitService, RateLimitService>();
    builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
    builder.Services.AddScoped<IComplaintService, ComplaintService>();
    builder.Services.AddScoped<IComplaintLockService, ComplaintLockService>();
    builder.Services.AddScoped<INotificationService, NotificationService>();
    builder.Services.AddScoped<INotificationJob, NotificationJob>();
    builder.Services.AddScoped<IDashboardService, DashboardService>();
    builder.Services.AddScoped<IVirusScanService, ClamAvVirusScanService>();
    builder.Services.AddScoped<IAttachmentScanningJob, AttachmentScanningJob>();
    builder.Services.AddSingleton<ICacheService>(sp =>
    {
        var distributedCache = sp.GetRequiredService<Microsoft.Extensions.Caching.Distributed.IDistributedCache>();
        var connectionMultiplexer = sp.GetRequiredService<IConnectionMultiplexer>();
        return new RedisCacheService(distributedCache, connectionMultiplexer, "CMS:");
    });
    builder.Services.AddSingleton<SseService>();

    // SignalR with keep-alive configuration
    builder.Services.AddSignalR(options =>
    {
        options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
        options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        options.HandshakeTimeout = TimeSpan.FromSeconds(30);
    });

    // API Controllers with Global Filters
// API Controllers with Global Filters
    builder.Services.AddControllers(options =>
    {
        options.Filters.Add<ValidationFilterAttribute>();
    })
    .AddJsonOptions(options =>
    {
        // ⬇️ THIS IS THE FIX ⬇️
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DictionaryKeyPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

    builder.Services.AddEndpointsApiExplorer();

    // Swagger Configuration
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "CMS API",
            Version = "v1",
            Description = "Content Management System API with comprehensive documentation, AOP security features, and idempotency support",
            Contact = new OpenApiContact
            {
                Name = "CMS Development Team",
                Email = "support@cms-api.com"
            }
        });

        var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
        var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
        if (File.Exists(xmlPath))
        {
            c.IncludeXmlComments(xmlPath);
        }

        c.SchemaFilter<CMS.Api.Swagger.DateTimeExampleFilter>();
        c.OperationFilter<CMS.Api.Swagger.IdempotencyHeaderFilter>();

        c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token.",
            Name = "Authorization",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.ApiKey,
            Scheme = "Bearer"
        });

        c.AddSecurityRequirement(new OpenApiSecurityRequirement()
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    },
                    Scheme = "oauth2",
                    Name = "Bearer",
                    In = ParameterLocation.Header,
                },
                new List<string>()
            }
        });
    });

    // ======================================
    // CORS Configuration - CRITICAL FIX
    // ======================================
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowFrontend", policy =>
        {
            policy.WithOrigins(
                    "http://localhost:4200",      // Angular dev server
                    "http://localhost:4201",      // Backup port
                    "http://127.0.0.1:4200",      // Alternative localhost
                    "https://localhost:4200"      // HTTPS variant
                )
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials()
                .WithExposedHeaders("Content-Disposition", "X-Request-Id")
                .SetPreflightMaxAge(TimeSpan.FromMinutes(10)); // Cache preflight for 10 minutes
        });
    });

    var app = builder.Build();

    // ======================================
    //    3. MIDDLEWARE PIPELINE (ORDER IS CRITICAL!)
    // ======================================

    // 1. ERROR HANDLING - Must be first to catch all errors
    app.UseMiddleware<ErrorHandlerMiddleware>();

    // 2. REQUEST LOGGING - Log all requests early
    app.UseMiddleware<RequestLogMiddleware>();

    // 3. Development tools
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "CMS API v1");
            c.RoutePrefix = "swagger"; // Access at /swagger
        });
    }

    // 4. HTTPS Redirection (production only)
    if (app.Environment.IsProduction())
    {
        app.UseHttpsRedirection();
    }

    // ⚠️ 5. CORS - MUST COME BEFORE ROUTING AND STATIC FILES
    app.UseCors("AllowFrontend");
    Log.Information("✅ CORS enabled for: http://localhost:4200");

    // 6. STATIC FILES - Serve uploaded files
    var webRootPath = app.Environment.WebRootPath
        ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");

    if (!Directory.Exists(webRootPath))
    {
        Directory.CreateDirectory(webRootPath);
        Log.Information("Created wwwroot directory: {Path}", webRootPath);
    }

    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(webRootPath),
        RequestPath = "",
        OnPrepareResponse = ctx =>
        {
            // Add CORS headers to static files as well
            ctx.Context.Response.Headers.Append("Access-Control-Allow-Origin", "http://localhost:4200");
            ctx.Context.Response.Headers.Append("Access-Control-Allow-Credentials", "true");
        }
    });

    // 7. ROUTING
    app.UseRouting();

    // 8. SECURITY - IP Blacklist (after routing, before auth)
    app.UseMiddleware<IpBlacklistMiddleware>();

    // 9. RATE LIMITING
    app.UseMiddleware<RateLimitMiddleware>();

    // 10. AUTHENTICATION - Identify the user
    app.UseAuthentication();

    // 11. AUTHORIZATION - Check permissions
    app.UseAuthorization();

    // 12. IDEMPOTENCY - Handle duplicate POST/PUT/PATCH requests
    app.UseMiddleware<IdempotencyMiddleware>();

    // 13. HANGFIRE DASHBOARD - Background job monitoring (protected)
    app.UseHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = new[] { new HangfireAuthorizationFilter() },
        DashboardTitle = "CMS Background Jobs"
    });

    // 14. MAP CONTROLLERS AND HUBS
    app.MapControllers();
    app.MapHub<NotificationHub>("/notifications");

    // Health check endpoint
    app.MapGet("/health", () => Results.Ok(new
    {
        status = "healthy",
        timestamp = DateTime.UtcNow,
        environment = app.Environment.EnvironmentName
    }));

    // CORS test endpoint (for debugging)
    app.MapGet("/api/test-cors", () => Results.Ok(new
    {
        message = "CORS is working!",
        timestamp = DateTime.UtcNow
    }));

    // ======================================
    //    4. BACKGROUND JOBS SETUP
    // ======================================

    RecurringJob.AddOrUpdate<ISecurityService>(
        "cleanup-old-login-attempts",
        service => service.CleanupOldLoginAttemptsAsync(30),
        Cron.Daily);

    // ======================================
    //    5. DATABASE INITIALIZATION
    // ======================================

    if (app.Environment.IsDevelopment())
    {
        using (var scope = app.Services.CreateScope())
        {
            try
            {
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                Log.Information("Running database migrations...");
                await context.Database.MigrateAsync();

                Log.Information("Seeding database...");
                await DbSeeder.SeedUsersAsync(scope.ServiceProvider);

                Log.Information("✅ Database initialization complete");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "❌ Database initialization failed");
            }
        }
    }

    Log.Information("✅ CMS API started successfully");
    Log.Information("🌐 Listening on: {Urls}", string.Join(", ", app.Urls));
    Log.Information("📡 SignalR Hub: /notifications");
    Log.Information("🔧 Hangfire Dashboard: /hangfire");
    if (app.Environment.IsDevelopment())
    {
        Log.Information("📚 Swagger UI: /swagger");
    }

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "❌ Application start-up failed");
}
finally
{
    Log.CloseAndFlush();
}