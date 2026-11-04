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
        await ActualizarEstadosCitasAsync(stoppingToken);

        while (await timer.WaitForNextTickAsync(stoppingToken))
            await ActualizarEstadosCitasAsync(stoppingToken);
    }

    private async Task ActualizarEstadosCitasAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<RegistroCivilCitasContext>();
            var limiteMexico = DateTime.UtcNow.AddHours(-6).AddMinutes(-25);
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE Citas SET estatus = 'NO_ASISTIO' WHERE estatus IN ('PROGRAMADA', 'REPROGRAMADA') AND fecha_hora_inicio <= {0}",
                new object[] { limiteMexico }, stoppingToken);

            var ahoraMexico = DateTime.UtcNow.AddHours(-6);
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE Citas SET estatus = 'CANCELADA' WHERE estatus IN ('CANCELADA_INHABIL', 'PRESENCIAL_PENDIENTE') AND fecha_limite_resolucion_inhabil < {0}",
                new object[] { ahoraMexico }, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // El servicio se está apagando.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudieron actualizar los estados de las citas vencidas.");
        }
    }
}
