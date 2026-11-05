using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RegistroCivilAPI.Models;
using RegistroCivilAPI.Services;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;

namespace RegistroCivilAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TramitesController : ControllerBase
    {
        private readonly RegistroCivilCitasContext _context;

        public TramitesController(RegistroCivilCitasContext context)
        {
            _context = context;
        }

        
        [HttpGet]
        public async Task<ActionResult> GetTramitesActivos([FromQuery] int? idSede)
        {
            var categorias = await _context.CategoriasTramites
                .Where(c => c.Activa == true)
                .Select(c => new {
                    c.IdCategoria,
                    c.NombreCategoria,
                    c.Descripcion,
                    Tramites = _context.Tramites.Where(t => t.IdCategoria == c.IdCategoria && t.Activo == true &&
                        (idSede.HasValue ? t.IdSede == idSede : t.IdSede == null)).ToList()
                }).ToListAsync();

            return Ok(categorias.Where(c => c.Tramites.Any()));
        }

    
        [HttpGet("Admin")]
        [Authorize(Roles = "Super Administrador,Dirección")]
        public async Task<ActionResult> GetTramitesAdmin([FromQuery] int idSede)
        {
            if (idSede <= 0) return BadRequest(new { mensaje = "Seleccione una sede para consultar su configuración." });
            var categorias = await _context.CategoriasTramites
                .Select(c => new {
                    c.IdCategoria,
                    c.NombreCategoria,
                    c.Descripcion,
                    c.Activa,
                    Tramites = _context.Tramites.Where(t => t.IdCategoria == c.IdCategoria && t.IdSede == idSede).ToList()
                }).ToListAsync();

            return Ok(categorias);
        }

     
        [HttpPut("Categoria/{idCategoria}")]
        [Authorize(Roles = "Super Administrador,Dirección")]
        public async Task<ActionResult> UpdateCategoria(int idCategoria, [FromQuery] int idSede, [FromBody] CategoriaUpdateDTO dto)
        {
            if (idSede <= 0) return BadRequest(new { mensaje = "Seleccione una sede antes de guardar." });
            var categoria = await _context.CategoriasTramites.FindAsync(idCategoria);
            if (categoria == null) return NotFound(new { mensaje = "Categoría no encontrada." });

            var tramites = await _context.Tramites.Where(t => t.IdCategoria == idCategoria && t.IdSede == idSede).ToListAsync();
            if (tramites.Count == 0) return NotFound(new { mensaje = "No existe configuración para esta categoría en la sede seleccionada. Aplique la actualización de base de datos local y vuelva a cargar." });
            var primerTramite = tramites.FirstOrDefault();
            string valorAnterior = $"Sede: {idSede}; Activo: {primerTramite!.Activo}; Duración: {primerTramite.DuracionMinutos}; Precio: {primerTramite.Costo}; Límite: {primerTramite.LimiteDiarioSede}; Desde: {primerTramite.FechaInicioPermitida:yyyy-MM-dd}; Hasta: {primerTramite.FechaFinPermitida:yyyy-MM-dd}";

           
            foreach (var t in tramites)
            {
                t.DuracionMinutos = dto.DuracionMinutos;
                t.Costo = dto.Costo;
                t.Activo = dto.Activo;
                t.LimiteDiarioSede = dto.LimiteDiario;
                t.FechaInicioPermitida = dto.FechaInicio; 
                t.FechaFinPermitida = dto.FechaFin;       
            }

            _context.BitacoraAuditoria.Add(await BitacoraHelper.CrearAsync(User, _context, "Tramites", "UPDATE", idCategoria.ToString(), valorAnterior, $"Sede: {idSede}; Activo: {dto.Activo}; Duración: {dto.DuracionMinutos}; Precio: {dto.Costo}; Límite: {dto.LimiteDiario}; Desde: {dto.FechaInicio:yyyy-MM-dd}; Hasta: {dto.FechaFin:yyyy-MM-dd}"));
            await _context.SaveChangesAsync();
            return Ok(new { mensaje = "Configuración aplicada a los servicios de esta categoría en la sede seleccionada." });
        }
    }

    public class CategoriaUpdateDTO
    {
        public int DuracionMinutos { get; set; }
        public decimal Costo { get; set; }
        public bool Activo { get; set; }
        public int LimiteDiario { get; set; }
        public DateTime? FechaInicio { get; set; }
        public DateTime? FechaFin { get; set; }
    }
}
