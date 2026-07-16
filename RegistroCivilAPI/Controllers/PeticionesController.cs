using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RegistroCivilAPI.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RegistroCivilAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PeticionesController : ControllerBase
    {
        private readonly RegistroCivilCitasContext _context;

        public PeticionesController(RegistroCivilCitasContext context)
        {
            _context = context;
        }

        // Obtener peticiones para el Super Admin
        [HttpGet]
        public async Task<ActionResult> GetPeticiones()
        {
            var peticiones = new List<object>();
            using (var command = _context.Database.GetDbConnection().CreateCommand())
            {
                command.CommandText = "SELECT id_peticion, username_solicitante, tipo_peticion, descripcion, estatus, fecha_solicitud FROM Peticiones_Soporte ORDER BY fecha_solicitud DESC";
                await _context.Database.OpenConnectionAsync();

                using (var result = await command.ExecuteReaderAsync())
                {
                    while (await result.ReadAsync())
                    {
                        peticiones.Add(new
                        {
                            idPeticion = result.GetInt32(0),
                            username = result.GetString(1),
                            tipo = result.GetString(2),
                            descripcion = result.GetString(3),
                            estatus = result.GetString(4),
                            fecha = result.GetDateTime(5)
                        });
                    }
                }
            }
            return Ok(peticiones);
        }

        // Crear una nueva petición (Desde Login o Dashboard)
        [HttpPost]
        public async Task<ActionResult> CreatePeticion([FromBody] NuevaPeticionDTO dto)
        {
            // Usamos parámetros {0} para evitar Inyección SQL
            await _context.Database.ExecuteSqlRawAsync(
                "INSERT INTO Peticiones_Soporte (username_solicitante, tipo_peticion, descripcion) VALUES ({0}, {1}, {2})",
                dto.Username, dto.Tipo, dto.Descripcion);

            return Ok(new { mensaje = "Tu solicitud ha sido enviada al departamento de Sistemas. Pronto será atendida." });
        }

        // Marcar como resuelta (Super Admin)
        [HttpPut("{id}/resolver")]
        public async Task<ActionResult> ResolverPeticion(int id)
        {
            await _context.Database.ExecuteSqlRawAsync("UPDATE Peticiones_Soporte SET estatus = 'RESUELTA' WHERE id_peticion = {0}", id);
            return Ok(new { mensaje = "Petición marcada como resuelta." });
        }
    }

    public class NuevaPeticionDTO
    {
        public string Username { get; set; }
        public string Tipo { get; set; }
        public string Descripcion { get; set; }
    }
}