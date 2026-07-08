using Microsoft.EntityFrameworkCore;
using RegistroCivilAPI.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<RegistroCivilCitasContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));


builder.Services.AddControllers();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();


builder.Services.AddCors(options =>
{
    options.AddPolicy("PermitirAngular", policy =>
    {
        policy.WithOrigins("http://localhost:4200") // El puerto exacto donde corre Angular
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

//app.UseHttpsRedirection();

app.UseCors("PermitirAngular");

app.UseAuthorization();

app.MapControllers();

app.Run();