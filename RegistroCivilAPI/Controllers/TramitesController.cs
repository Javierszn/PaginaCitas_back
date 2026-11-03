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
        public async Task<ActionResult> GetTramitesActivos()
        {
            var categorias = await _context.CategoriasTramites
                .Where(c => c.Activa == true)
                .Select(c => new {
                    c.IdCategoria,
                    c.NombreCategoria,
                    c.Descripcion,
                    Tramites = _context.Tramites.Where(t => t.IdCategoria == c.IdCategoria && t.Activo == true).ToList()
                }).ToListAsync();

            return Ok(categorias.Where(c => c.Tramites.Any()));
        }

    
        [HttpGet("Admin")]
        [Authorize(Roles = "Super Administrador,Dirección")]
        public async Task<ActionResult> GetTramitesAdmin()
        {
            var categorias = await _context.CategoriasTramites
                .Select(c => new {
                    c.IdCategoria,
                    c.NombreCategoria,
                    c.Descripcion,
                    c.Activa,
                    Tramites = _context.Tramites.Where(t => t.IdCategoria == c.IdCategoria).ToList()
                }).ToListAsync();

            return Ok(categorias);
        }

     
        [HttpPut("Categoria/{idCategoria}")]
        [Authorize(Roles = "Super Administrador,Dirección")]
        public async Task<ActionResult> UpdateCategoria(int idCategoria, [FromBody] CategoriaUpdateDTO dto)
        {
            var categoria = await _context.CategoriasTramites.FindAsync(idCategoria);
            if (categoria == null) return NotFound(new { mensaje = "Categoría no encontrada." });

       
            var activaAnterior = categoria.Activa;
            categoria.Activa = dto.Activo;

            var tramites = await _context.Tramites.Where(t => t.IdCategoria == idCategoria).ToListAsync();
            var primerTramite = tramites.FirstOrDefault();
            string valorAnterior = primerTramite == null
                ? $"Activa: {activaAnterior}"
                : $"Activo: {primerTramite.Activo}; Duración: {primerTramite.DuracionMinutos}; Precio: {primerTramite.Costo}; Límite: {primerTramite.LimiteDiarioSede}; Desde: {primerTramite.FechaInicioPermitida:yyyy-MM-dd}; Hasta: {primerTramite.FechaFinPermitida:yyyy-MM-dd}";

           
            foreach (var t in tramites)
            {
                t.DuracionMinutos = dto.DuracionMinutos;
                t.Costo = dto.Costo;
                t.Activo = dto.Activo;
                t.LimiteDiarioSede = dto.LimiteDiario;
                t.FechaInicioPermitida = dto.FechaInicio; 
                t.FechaFinPermitida = dto.FechaFin;       
            }

            _context.BitacoraAuditoria.Add(await BitacoraHelper.CrearAsync(User, _context, "Tramites", "UPDATE", idCategoria.ToString(), valorAnterior, $"Activo: {dto.Activo}; Duración: {dto.DuracionMinutos}; Precio: {dto.Costo}; Límite: {dto.LimiteDiario}; Desde: {dto.FechaInicio:yyyy-MM-dd}; Hasta: {dto.FechaFin:yyyy-MM-dd}"));
            await _context.SaveChangesAsync();
            return Ok(new { mensaje = "Configuración aplicada exitosamente a todos los servicios de esta categoría." });
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
