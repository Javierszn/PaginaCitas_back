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


builder.Services.AddDbContext<RegistroCivilCitasContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddControllers();


builder.Services.AddCors(options =>
{
    options.AddPolicy("PermitirTodo", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.AddScoped<IEmailService, EmailService>();


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
    options.ForwardLimit = 1;
});
// Habilita el reciclaje inteligente de conexiones HTTP
builder.Services.AddHttpClient();

var app = builder.Build();


app.UseForwardedHeaders();

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync("{\"mensaje\": \"Ha ocurrido un error interno en el servidor.\"}");
    });
});

app.UseCors("PermitirTodo");
app.UseRateLimiter();             
app.UseAuthentication();      
app.UseAuthorization();

app.Use(async (context, next) =>
{
    // 1. Evita que disfracen archivos maliciosos (MIME Sniffing)
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";

    // 2. Bloquea que otra página meta tu sitio web en un Iframe (Clickjacking)
    context.Response.Headers["X-Frame-Options"] = "DENY";

    // 3. Filtro básico contra ataques XSS en navegadores antiguos
    context.Response.Headers["X-XSS-Protection"] = "1; mode=block";

    // 4. Protege la información de la URL al navegar a sitios externos
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

    // 5. Política de Seguridad de Contenido (CSP) básica para evitar Iframes no autorizados
    context.Response.Headers["Content-Security-Policy"] = "frame-ancestors 'none';";

    await next();
});

app.MapControllers();

app.Run();