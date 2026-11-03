using Microsoft.EntityFrameworkCore;
using RegistroCivilAPI.Models;

namespace RegistroCivilAPI.Services;

public sealed class CitasEstadoBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CitasEstadoBackgroundService> _logger;

    public CitasEstadoBackgroundService(IServiceScopeFactory scopeFactory, ILogger<CitasEstadoBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        await ActualizarCitasNoConfirmadasAsync(stoppingToken);

        while (await timer.WaitForNextTickAsync(stoppingToken))
            await ActualizarCitasNoConfirmadasAsync(stoppingToken);
    }

    private async Task ActualizarCitasNoConfirmadasAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<RegistroCivilCitasContext>();
            var limiteMexico = DateTime.UtcNow.AddHours(-6).AddMinutes(-15);
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE Citas SET estatus = 'NO_CONFIRMADA' WHERE estatus IN ('PROGRAMADA', 'REPROGRAMADA') AND fecha_hora_inicio <= {0}",
                new object[] { limiteMexico }, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // El servicio se está apagando.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudieron marcar las citas pendientes como NO_CONFIRMADA.");
        }
    }
}
