using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RegistroCivilAPI.Models;

namespace RegistroCivilAPI.Services
{
    public static class BitacoraHelper
    {
        public static async Task<BitacoraAuditorium> CrearAsync(ClaimsPrincipal actor, RegistroCivilCitasContext context, string tabla, string accion, string registroId, string? anterior, string? nuevo)
        {
            // A JWT can contain both `sub` (username) and a numeric name identifier.
            // ASP.NET may map `sub` to NameIdentifier and place it first, so do not
            // stop at the first matching claim: select the first numeric candidate.
            var idClaimTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ClaimTypes.NameIdentifier,
                "nameid",
                "nameidentifier",
                "id_usuario",
                "IdUsuario"
            };
            int? idUsuario = actor.Claims
                .Where(claim => idClaimTypes.Contains(claim.Type))
                .Select(claim => int.TryParse(claim.Value, out var parsedId) ? (int?)parsedId : null)
                .FirstOrDefault(parsedId => parsedId.HasValue);

            // If the token has no numeric ID claim, resolve the authenticated username
            // from the database. The JWT subject in this application is the username.
            if (idUsuario is null)
            {
                var username = actor.FindFirst(ClaimTypes.Name)?.Value
                    ?? actor.FindFirst("unique_name")?.Value
                    ?? actor.FindFirst("sub")?.Value;

                if (!string.IsNullOrWhiteSpace(username))
                {
                    idUsuario = await context.UsuariosInternos
                        .AsNoTracking()
                        .Where(user => user.Username == username)
                        .Select(user => (int?)user.IdUsuario)
                        .SingleOrDefaultAsync();
                }
            }

            if (idUsuario is null)
                throw new InvalidOperationException("No se pudo identificar al usuario autenticado para registrar el cambio en bitácora.");

            return new BitacoraAuditorium
            {
                IdUsuarioInterno = idUsuario.Value,
                TablaAfectada = tabla,
                AccionRealizada = accion,
                RegistroId = registroId,
                ValorAnterior = anterior,
                ValorNuevo = nuevo,
                FechaCambio = DateTime.Now
            };
        }
    }
}
