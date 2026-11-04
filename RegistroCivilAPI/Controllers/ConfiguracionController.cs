using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RegistroCivilAPI.Models;
using RegistroCivilAPI.Services;

namespace RegistroCivilAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ConfiguracionController : ControllerBase
    {
        private readonly RegistroCivilCitasContext _context;
        private readonly IEmailService _emailService;

        public ConfiguracionController(RegistroCivilCitasContext context, IEmailService emailService)
        {
            _context = context;
            _emailService = emailService;
        }

        [HttpGet("ReglasCalendario")]
        public async Task<ActionResult> GetReglas()
        {
            var rango = await _context.ConfiguracionAgendas.FirstOrDefaultAsync(c => c.Id == 1);
            var dias = await _context.DiasInhabiles.Select(d => new { id = d.IdDiaInhabil, fecha = d.FechaBloqueada, motivo = d.Motivo }).ToListAsync();
            return Ok(new { rango = rango, diasInhabiles = dias });
        }
        [Authorize(Roles = "Administrador,Super Administrador")]
        [HttpPut("Rango")]
        public async Task<ActionResult> UpdateRango([FromBody] ConfiguracionAgenda dto)
        {
            if (dto.FechaInicio.Date >= dto.FechaFin.Date)
            {
                return BadRequest(new { mensaje = "La fecha de inicio debe ser anterior a la fecha de fin." });
            }

            var rango = await _context.ConfiguracionAgendas.FirstOrDefaultAsync(c => c.Id == 1);
            string valorAnterior = rango == null ? "No configurado" : $"Inicio: {rango.FechaInicio:yyyy-MM-dd}; Fin: {rango.FechaFin:yyyy-MM-dd}";
            if (rango == null)
            {
                rango = new ConfiguracionAgenda { Id = 1, FechaInicio = dto.FechaInicio, FechaFin = dto.FechaFin };
                _context.ConfiguracionAgendas.Add(rango);
            }
            else
            {
                rango.FechaInicio = dto.FechaInicio; rango.FechaFin = dto.FechaFin;
            }
            _context.BitacoraAuditoria.Add(await BitacoraHelper.CrearAsync(User, _context, "Configuracion_Agenda", "UPDATE", "1", valorAnterior, $"Inicio: {dto.FechaInicio:yyyy-MM-dd}; Fin: {dto.FechaFin:yyyy-MM-dd}"));
            await _context.SaveChangesAsync();
            return Ok(new { mensaje = "Rango de fechas de reservación actualizado." });
        }
        [Authorize(Roles = "Administrador,Super Administrador,Dirección")]
        [HttpPost("DiasInhabiles")]
        public async Task<ActionResult> AddDiaInhabil([FromBody] DiaInhabilDTO dto)
        {
            if (dto == null || dto.Fecha == default || string.IsNullOrWhiteSpace(dto.Motivo))
                return BadRequest(new { mensaje = "Indique una fecha y el motivo del cierre." });
            if (dto.Motivo.Trim().Length > 100)
                return BadRequest(new { mensaje = "El motivo no puede superar 100 caracteres." });

            var fecha = DateOnly.FromDateTime(dto.Fecha);
            await using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            try
            {
                if (await _context.DiasInhabiles.AnyAsync(d => d.FechaBloqueada == fecha))
                    return Conflict(new { mensaje = "Esa fecha ya está registrada como día inhábil." });

                var nuevoDia = new DiasInhabile { FechaBloqueada = fecha, Motivo = dto.Motivo.Trim(), IdSede = null };
                _context.DiasInhabiles.Add(nuevoDia);
                await _context.SaveChangesAsync();

                DateTime ahoraMexico = DateTime.UtcNow.AddHours(-6);
                var diasInhabiles = await _context.DiasInhabiles
                    .Where(d => d.IdSede == null)
                    .Select(d => d.FechaBloqueada)
                    .ToListAsync();
                DateTime fechaLimite = CalcularLimiteSieteDiasHabiles(ahoraMexico, diasInhabiles);

                var citasAfectadas = await _context.Citas
                    .Include(c => c.IdCiudadanoNavigation)
                    .Include(c => c.IdTramiteNavigation)
                    .Include(c => c.IdSedeNavigation)
                    .Where(c => c.FechaHoraInicio.Date == dto.Fecha.Date && c.FechaHoraInicio > ahoraMexico &&
                        (c.Estatus == "PROGRAMADA" || c.Estatus == "REPROGRAMADA"))
                    .ToListAsync();

                foreach (var cita in citasAfectadas)
                {
                    string anterior = cita.Estatus;
                    cita.Estatus = "CANCELADA_INHABIL";
                    cita.FechaLimiteResolucionInhabil = fechaLimite;
                    _context.BitacoraAuditoria.Add(await BitacoraHelper.CrearAsync(
                        User, _context, "Citas", "UPDATE", cita.IdCita,
                        $"Estatus: {anterior}",
                        $"Estatus: CANCELADA_INHABIL; Motivo: {dto.Motivo.Trim()}; Resolver antes de: {fechaLimite:dd/MM/yyyy}"));
                }

                _context.BitacoraAuditoria.Add(await BitacoraHelper.CrearAsync(
                    User, _context, "Dias_Inhabiles", "INSERT", dto.Fecha.ToString("yyyy-MM-dd"), null,
                    $"Motivo: {dto.Motivo.Trim()}; Citas afectadas: {citasAfectadas.Count}"));
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                foreach (var cita in citasAfectadas)
                {
                    string nombreCompleto = $"{cita.IdCiudadanoNavigation.Nombre} {cita.IdCiudadanoNavigation.PrimerApellido} {cita.IdCiudadanoNavigation.SegundoApellido}".Trim();
                    string identificador = !string.IsNullOrWhiteSpace(nombreCompleto)
                        ? nombreCompleto
                        : $"CURP: {cita.IdCiudadanoNavigation.Curp}";

                    await _emailService.EnviarAvisoCierreInhabilAsync(
                        cita.IdCiudadanoNavigation.Correo,
                        identificador,
                        cita.IdCita,
                        cita.FechaHoraInicio,
                        cita.IdTramiteNavigation.NombreTramite,
                        cita.IdSedeNavigation.Nombre,
                        fechaLimite);
                }
                return Ok(new
                {
                    mensaje = $"Día inhábil registrado. Se bloquearon {citasAfectadas.Count} cita(s); su estatus quedó en CANCELADA_INHABIL y no genera sanción. El plazo de siete días hábiles vence el {fechaLimite:dd/MM/yyyy}.",
                    citasAfectadas = citasAfectadas.Count,
                    fechaLimiteResolucion = fechaLimite
                });
            }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync();
                return Conflict(new { mensaje = "Esa fecha ya está registrada o no se pudo guardar el cierre." });
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        [Authorize(Roles = "Administrador,Super Administrador,Dirección")]
        [HttpDelete("DiasInhabiles/{id}")]
        public async Task<ActionResult> DeleteDiaInhabil(int id)
        {
            var dia = await _context.DiasInhabiles.FindAsync(id);
            if (dia == null) return NotFound();
            _context.DiasInhabiles.Remove(dia);
            _context.BitacoraAuditoria.Add(await BitacoraHelper.CrearAsync(User, _context, "Dias_Inhabiles", "DELETE", id.ToString(), $"Fecha: {dia.FechaBloqueada:yyyy-MM-dd}; Motivo: {dia.Motivo}", null));
            await _context.SaveChangesAsync();
            return Ok(new { mensaje = "Día inhábil eliminado. El día vuelve a estar disponible." });
        }
    internal static DateTime CalcularLimiteSieteDiasHabiles(DateTime desde, IEnumerable<DateOnly> fechasInhabiles)
    {
        var inhábiles = new HashSet<DateOnly>(fechasInhabiles);
        DateOnly fecha = DateOnly.FromDateTime(desde);
        int contados = 0;
        while (contados < 7)
        {
            fecha = fecha.AddDays(1);
            var diaSemana = fecha.DayOfWeek;
            if (diaSemana == DayOfWeek.Saturday || diaSemana == DayOfWeek.Sunday || inhábiles.Contains(fecha))
                continue;
            contados++;
        }
        return fecha.ToDateTime(new TimeOnly(23, 59, 59));
    }

    }

    public class DiaInhabilDTO { public DateTime Fecha { get; set; } public string Motivo { get; set; } }
}
