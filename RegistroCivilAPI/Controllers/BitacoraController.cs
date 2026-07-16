using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RegistroCivilAPI.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace RegistroCivilAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BitacoraController : ControllerBase
    {
        private readonly RegistroCivilCitasContext _context;

        public BitacoraController(RegistroCivilCitasContext context)
        {
            _context = context;
        }

        
        [HttpGet]
        public async Task<ActionResult> ObtenerBitacora([FromQuery] string? fecha = null, [FromQuery] string? busqueda = null)
        {
            var query = _context.BitacoraAuditoria
                .Include(b => b.IdUsuarioInternoNavigation)
                .AsQueryable();

            
            if (!string.IsNullOrWhiteSpace(busqueda))
            {
               
                busqueda = busqueda.ToLower();

                query = query.Where(b => b.IdUsuarioInternoNavigation.NombreCompleto.ToLower().Contains(busqueda) ||
                                         b.TablaAfectada.ToLower().Contains(busqueda) ||
                                         b.AccionRealizada.ToLower().Contains(busqueda) ||
                                         b.RegistroId.ToLower().Contains(busqueda) ||
                                       
                                         (b.ValorAnterior != null && b.ValorAnterior.Contains(busqueda)) ||
                                         (b.ValorNuevo != null && b.ValorNuevo.Contains(busqueda)));
            }
            else
            {
                
                if (!string.IsNullOrEmpty(fecha) && DateTime.TryParse(fecha, out DateTime parsedDate))
                {
                    var fechaFiltro = parsedDate.Date;
                    query = query.Where(b => b.FechaCambio.HasValue &&
                                             b.FechaCambio.Value.Year == fechaFiltro.Year &&
                                             b.FechaCambio.Value.Month == fechaFiltro.Month &&
                                             b.FechaCambio.Value.Day == fechaFiltro.Day);
                }
            }

            var registros = await query
                .OrderByDescending(b => b.FechaCambio)
                .Select(b => new {
                    idBitacora = b.IdBitacora,
                    empleado = b.IdUsuarioInternoNavigation.NombreCompleto,
                    tabla = b.TablaAfectada,
                    accion = b.AccionRealizada,
                    registroId = b.RegistroId,
                    valorAnterior = b.ValorAnterior,
                    valorNuevo = b.ValorNuevo,
                    fecha = b.FechaCambio
                })
                .ToListAsync();

            return Ok(registros);
        }

       
        [HttpPost("Deshacer/{idBitacora}")]
        public async Task<ActionResult> DeshacerCambio(int idBitacora, [FromBody] int idAdmin)
        {
            var log = await _context.BitacoraAuditoria.FindAsync(idBitacora);
            if (log == null) return NotFound(new { mensaje = "Registro no encontrado." });

            if (log.TablaAfectada == "Citas" && log.AccionRealizada == "UPDATE")
            {
                var cita = await _context.Citas.FirstOrDefaultAsync(c => c.IdCita == log.RegistroId);
                if (cita == null) return BadRequest(new { mensaje = "La cita original ya no existe." });

                string estatusAnterior = log.ValorAnterior?.Replace("Estatus: ", "").Trim();

                if (string.IsNullOrEmpty(estatusAnterior))
                    return BadRequest(new { mensaje = "No se pudo leer el valor anterior para restaurarlo." });

                string estatusActual = cita.Estatus;
                cita.Estatus = estatusAnterior;

                var nuevoLog = new BitacoraAuditorium
                {
                    IdUsuarioInterno = idAdmin,
                    TablaAfectada = "Citas",
                    AccionRealizada = "UPDATE",
                    RegistroId = cita.IdCita,
                    ValorAnterior = $"Estatus: {estatusActual}",
                    ValorNuevo = $"Estatus: {estatusAnterior} (RESTAURADO)",
                    FechaCambio = DateTime.Now
                };

                _context.BitacoraAuditoria.Add(nuevoLog);
                await _context.SaveChangesAsync();

                return Ok(new { mensaje = "El cambio ha sido deshecho y la cita volvió a su estado anterior." });
            }

            return BadRequest(new { mensaje = "Esta acción específica no se puede deshacer de forma automática." });
        }
    }
}