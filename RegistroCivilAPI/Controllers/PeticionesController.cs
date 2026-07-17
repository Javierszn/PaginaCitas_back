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

        [HttpGet]
        public async Task<ActionResult> GetPeticiones()
        {
            var peticiones = new List<object>();
            using (var command = _context.Database.GetDbConnection().CreateCommand())
            {
                command.CommandText = "SELECT id_peticion, username_solicitante, tipo_peticion, descripcion, estatus, fecha_solicitud, respuesta, leido FROM Peticiones_Soporte ORDER BY fecha_solicitud DESC";
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
                            fecha = result.GetDateTime(5),
                            respuesta = result.IsDBNull(6) ? "" : result.GetString(6),
                            leido = result.IsDBNull(7) ? false : result.GetBoolean(7)
                        });
                    }
                }
            }
            return Ok(peticiones);
        }

        [HttpGet("MisPeticiones/{username}")]
        public async Task<ActionResult> GetMisPeticiones(string username)
        {
            var peticiones = new List<object>();
            using (var command = _context.Database.GetDbConnection().CreateCommand())
            {
                command.CommandText = $"SELECT id_peticion, tipo_peticion, descripcion, estatus, fecha_solicitud, respuesta, leido FROM Peticiones_Soporte WHERE username_solicitante = '{username}' ORDER BY fecha_solicitud DESC";
                await _context.Database.OpenConnectionAsync();

                using (var result = await command.ExecuteReaderAsync())
                {
                    while (await result.ReadAsync())
                    {
                        peticiones.Add(new
                        {
                            idPeticion = result.GetInt32(0),
                            tipo = result.GetString(1),
                            descripcion = result.GetString(2),
                            estatus = result.GetString(3),
                            fecha = result.GetDateTime(4),
                            respuesta = result.IsDBNull(5) ? "" : result.GetString(5),
                            leido = result.IsDBNull(6) ? false : result.GetBoolean(6)
                        });
                    }
                }
            }
            return Ok(peticiones);
        }

        [HttpPost]
        public async Task<ActionResult> CreatePeticion([FromBody] NuevaPeticionDTO dto)
        {
            await _context.Database.ExecuteSqlRawAsync(
                "INSERT INTO Peticiones_Soporte (username_solicitante, tipo_peticion, descripcion) VALUES ({0}, {1}, {2})",
                dto.Username, dto.Tipo, dto.Descripcion);

            return Ok(new { mensaje = "Tu solicitud ha sido enviada al departamento de Sistemas. Pronto será atendida." });
        }

        [HttpPut("{id}/resolver")]
        public async Task<ActionResult> ResolverPeticion(int id, [FromBody] RespuestaDTO dto)
        {
           
            await _context.Database.ExecuteSqlRawAsync(
                "UPDATE Peticiones_Soporte SET estatus = 'RESUELTA', respuesta = {0}, leido = 0 WHERE id_peticion = {1}",
                dto.Respuesta, id);
            return Ok(new { mensaje = "Petición marcada como resuelta y mensaje enviado al usuario." });
        }

        [HttpPut("MarcarLeidasAdmin")]
        public async Task<ActionResult> MarcarLeidasAdmin()
        {
            await _context.Database.ExecuteSqlRawAsync("UPDATE Peticiones_Soporte SET leido = 1 WHERE estatus = 'PENDIENTE'");
            return Ok();
        }

        [HttpPut("MarcarLeidasUsuario/{username}")]
        public async Task<ActionResult> MarcarLeidasUsuario(string username)
        {
            await _context.Database.ExecuteSqlRawAsync("UPDATE Peticiones_Soporte SET leido = 1 WHERE username_solicitante = {0} AND estatus = 'RESUELTA'", username);
            return Ok();
        }
    }

    public class NuevaPeticionDTO { public string Username { get; set; } public string Tipo { get; set; } public string Descripcion { get; set; } }
    public class RespuestaDTO { public string Respuesta { get; set; } }
}