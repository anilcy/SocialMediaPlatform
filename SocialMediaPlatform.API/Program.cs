using System.IdentityModel.Tokens.Jwt;
using DotNetEnv;
using SocialMediaPlatform.Business.Interfaces;
using SocialMediaPlatform.Business.Services;
using SocialMediaPlatform.Data;
using SocialMediaPlatform.Entities.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using System.Text;
using SocialMediaPlatform.Business.Mappings;
using SocialMediaPlatform.Data.Interfaces;
using SocialMediaPlatform.Data.Repositories;
using Microsoft.OpenApi.Models;
using Scalar.AspNetCore;
using StackExchange.Redis;           
using SocialMediaPlatform.API.Realtime;      

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpContextAccessor();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Load .env
Env.Load();
builder.Configuration.AddEnvironmentVariables();

// HİBRİT GETTER: önce .env (Env.GetString), yoksa gerçek environment
string GetEnv(string k) =>
    Env.GetString(k) ?? Environment.GetEnvironmentVariable(k)
    ?? throw new Exception($"⚠️ {k} not found in .env or environment variables");


// Redis connection (env: REDIS_CONNECTION)
// Örn: Docker'da "redis:6379,abortConnect=false", lokalde "localhost:6379,abortConnect=false"
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var redisConn = GetEnv("REDIS_CONNECTION");
    return ConnectionMultiplexer.Connect(redisConn);
});


/*
// Presence service (Redis üstünden online/lastSeen)
builder.Services.AddSingleton<IPresenceService, PresenceService>();


// SignalR scale-out (çoklu API replika için)
builder.Services.AddSignalR().AddStackExchangeRedis(GetEnv("REDIS_CONNECTION"), opts =>
{
    opts.Configuration.ChannelPrefix = "socialmedia";
});
*/

builder.Services.AddSingleton<IPresenceService, InMemoryPresenceService>(); // In-memory

builder.Services.AddSignalR(o =>
{
    o.EnableDetailedErrors = true;
});


builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo 
    { 
        Title = "SocialMediaPlatform API", 
        Version = "v1" 
    });

    // JWT Bearer’ı Http tipiyle tanımlıyoruz:
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Bearer token. Sadece token kısmını girin.",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,    // ← Burayı ApiKey yerine Http yapıyoruz
        Scheme = "bearer",                 // ← küçük harf “bearer”
        BearerFormat = "JWT",
        Reference = new OpenApiReference
        {
            Type = ReferenceType.SecurityScheme,
            Id = "Bearer"
        }
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            // Burada da tanımladığımız Bearer’ı kullanıyoruz:
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference 
                { 
                    Type = ReferenceType.SecurityScheme, 
                    Id = "Bearer" 
                }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddAutoMapper(typeof(MappingProfile));

// Services
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<UserManager<AppUser>>();
builder.Services.AddScoped<SignInManager<AppUser>>();
builder.Services.AddScoped<IPostRepository, PostRepository>();
builder.Services.AddScoped<IPostService, PostService>();
builder.Services.AddScoped<IFileStorageService, FileStorageService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<ICommentRepository, CommentRepository>();
builder.Services.AddScoped<ICommentService, CommentService>();
builder.Services.AddScoped<IStoryRepository, StoryRepository>();
builder.Services.AddScoped<IStoryService, StoryService>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IFollowRepository, FollowRepository>();
builder.Services.AddScoped<IFollowService, FollowService>();
builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IPrivacyService, PrivacyService>();
builder.Services.AddScoped<IMessageRepository, MessageRepository>();
builder.Services.AddScoped<IMessageService, MessageService>();
builder.Services.AddScoped<IMediaRepository, MediaRepository>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();


builder.Services.AddIdentityCore<AppUser>(options => { })
    .AddRoles<AppRole>()
    .AddEntityFrameworkStores<SocialMediaDbContext>()
    .AddDefaultTokenProviders();


var connectionString =
    Environment.GetEnvironmentVariable("DB_CONNECTION_STRING")
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("DB_CONNECTION_STRING not found.");


// DbContext added to services
builder.Services.AddDbContext<SocialMediaDbContext>(options =>
    options.UseNpgsql(connectionString, b => b.MigrationsAssembly("SocialMediaPlatform.Data")));

// JWT Yapılandırması
var jwtKey      = GetEnv("JWT_KEY");
var jwtIssuer   = GetEnv("JWT_ISSUER");
var jwtAudience = GetEnv("JWT_AUDIENCE");
var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = key
        };

        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                Console.WriteLine($"Authentication failed: {context.Exception.Message}");
                return Task.CompletedTask;
            },
            OnChallenge = context =>
            {
                Console.WriteLine("OnChallenge triggered: " + context.ErrorDescription);
                return Task.CompletedTask;
            },
            OnMessageReceived = context =>
            {
                var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
                // 1) Normal Authorization header'dan al
                if (!string.IsNullOrEmpty(authHeader))
                {
                    if (authHeader.StartsWith("Bearer "))
                        context.Token = authHeader.Substring(7);
                    else
                        context.Token = authHeader;
                }
                
                // 2) SignalR için query-string'ten token’a izin ver (WebSocket/SSE)
                if (string.IsNullOrEmpty(context.Token))
                {
                    var accessToken = context.Request.Query["access_token"];
                    var path = context.HttpContext.Request.Path;
                    if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/chat"))
                    {
                        context.Token = accessToken;
                    }
                }
                
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
        
        options.AddPolicy("SignalRDev", p =>
            p.WithOrigins("http://localhost:8088")
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials());
    });
});

var app = builder.Build();

// Global exception handling — must be FIRST so it wraps the whole pipeline.
// (Found via E2E: this middleware existed but was never registered, so every
// service exception surfaced as a raw 500 instead of the mapped 400/403/404/409.)
app.UseMiddleware<SocialMediaPlatform.API.Middleware.GlobalExceptionHandlingMiddleware>();

// Apply migrations only in Development
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<SocialMediaDbContext>();
    db.Database.Migrate();
}

app.UseHttpsRedirection();

// Serve uploaded media (FileStorageService saves files here and returns "/uploads/<file>" urls)
var uploadsFolder = app.Configuration["UploadsFolder"] ?? "uploads";
var uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), uploadsFolder);
Directory.CreateDirectory(uploadsPath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsPath),
    RequestPath = "/" + uploadsFolder
});

// JWT Authentication
app.UseRouting();
app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// SignalR Chat Hub
app.MapHub<ChatHub>("/hubs/chat")
    .RequireAuthorization()
    .RequireCors("SignalRDev");

// PostgreSQL connection test (skipped under E2E tests: they replace the DbContext
// connection via DI, but this raw check would still use the .env connection string)
if (!app.Environment.IsEnvironment("Testing"))
{
    using var conn = new NpgsqlConnection(connectionString);
    conn.Open();
    using (var cmd = new NpgsqlCommand("SELECT version()", conn))
    {
        var version = cmd.ExecuteScalar()?.ToString();
        Console.WriteLine($"✅ PostgreSQL version: {version}");
    }
}


// Kök isteği / → /scalar/v1 yönlendir
app.MapGet("/", context =>
{
    context.Response.Redirect("/scalar/v1");
    return Task.CompletedTask;
});

if (app.Environment.IsDevelopment())
{
    // OpenAPI JSON’ınızı /openapi/v1.json altına taşıyın
    app.UseSwagger(options =>
    {
        options.RouteTemplate = "openapi/{documentName}.json";
    });
    
    // Scalar UI
    app.MapScalarApiReference(o =>
            o.WithTheme(ScalarTheme.DeepSpace)   // İstediğiniz temayı seçin
    );
}

app.Run();

// Makes the auto-generated Program class visible to WebApplicationFactory<Program>
// in the E2E tests ( its a standard pattern for minimal-hosting apps).
public partial class Program { }
