using System;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.EntityFrameworkCore;
using Serilog;
using MediatR;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// Serilog
builder.Host.UseSerilog((ctx, lc) => lc.WriteTo.Console());

// Configuration
var conn = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(conn))
    throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

// Services
builder.Services.AddDbContext<ApplicationDbContext>(opt =>
    opt.UseNpgsql(conn));

builder.Services.AddSignalR();
builder.Services.AddControllers();

// MediatR
builder.Services.AddMediatR(Assembly.GetExecutingAssembly());

// JWT Auth (for SignalR we accept token via query)
var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey))
    throw new InvalidOperationException("JWT Key not configured (Jwt:Key).");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };

        // allow token in query string for SignalR
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"].FirstOrDefault();
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/forex"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            },
            OnAuthenticationFailed = context =>
            {
                // For testing purposes, allow connection without valid JWT
                // In production, remove this and ensure proper authentication
                if (context.HttpContext.Request.Path.StartsWithSegments("/hubs/forex"))
                {
                    var identity = new System.Security.Claims.ClaimsIdentity();
                    identity.AddClaim(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "test-user"));
                    context.Principal = new System.Security.Claims.ClaimsPrincipal(identity);
                    context.Success();
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// 🚀 CORS policy (allow all origins for dev)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
            .SetIsOriginAllowed(_ => true); // allow all origins
    });
});

// HTTP Client for price verification
builder.Services.AddHttpClient();

// Background services
builder.Services.AddHostedService<FinnhubIngestService>();
builder.Services.AddHostedService<BroadcastService>();

var app = builder.Build();

app.UseSerilogRequestLogging();

app.UseRouting();

// ✅ Enable CORS before auth
app.UseCors("AllowAll");

app.UseAuthentication();
app.UseAuthorization();

// Serve static files
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();
app.MapHub<ForexHub>("/hubs/forex");

app.Run();
