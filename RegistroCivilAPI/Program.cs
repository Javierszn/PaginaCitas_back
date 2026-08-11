using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using RegistroCivilAPI.Models;
using RegistroCivilAPI.Services;
using System;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Límite de peticiones anti-spam
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "Local",
            factory: partition => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1)
            }));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// Conexión a Base de Datos
builder.Services.AddDbContext<RegistroCivilCitasContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddControllers();

// Configuración de CORS permitiendo TODO
builder.Services.AddCors(options =>
{
    options.AddPolicy("PermitirTodo", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Inyección del Servicio de Correos
builder.Services.AddScoped<IEmailService, EmailService>();

// Configuración JWT
var jwtKey = builder.Configuration["JwtSettings:SecretKey"];
var keyBytes = Encoding.UTF8.GetBytes(jwtKey);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false;
        options.SaveToken = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["JwtSettings:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["JwtSettings:Audience"],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Si conoces la IP fija de tu proxy/nginx, agrégala aquí para mayor seguridad:
    // options.KnownProxies.Add(System.Net.IPAddress.Parse("IP_DE_TU_PROXY"));
    options.ForwardLimit = 1;
});

var app = builder.Build();
app.UseForwardedHeaders(); // debe ir muy al inicio, antes de CORS/RateLimiter/Auth


app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync("{\"mensaje\": \"Ha ocurrido un error interno en el servidor.\"}");
    });
});

// SE ELIMINÓ app.UseHttpsRedirection(); PARA EVITAR EL CHOQUE DE CORS

// EL ORDEN AQUÍ ES VITAL
app.UseCors("PermitirTodo"); // 1. Permiso a Angular
app.UseRateLimiter();        // 2. Anti-spam      
app.UseAuthentication();     // 3. Usuario      
app.UseAuthorization();      // 4. Permisos      

app.MapControllers();

app.Run();