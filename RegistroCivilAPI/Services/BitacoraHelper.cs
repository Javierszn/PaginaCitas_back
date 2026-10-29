using System;
using System.Security.Claims;
using RegistroCivilAPI.Models;

namespace RegistroCivilAPI.Services
{
    public static class BitacoraHelper
    {
        public static BitacoraAuditorium Crear(ClaimsPrincipal actor, string tabla, string accion, string registroId, string? anterior, string? nuevo)
        {
            // JWT inbound claim mapping differs between hosting configurations. Accept
            // both the URI claim and the compact JWT/custom claim forms.
            var idClaim = actor.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? actor.FindFirst("nameid")?.Value
                ?? actor.FindFirst("nameidentifier")?.Value
                ?? actor.FindFirst("id_usuario")?.Value
                ?? actor.FindFirst("IdUsuario")?.Value;
            if (!int.TryParse(idClaim, out var idUsuario))
                throw new InvalidOperationException("No se pudo identificar al usuario que realiza el cambio.");

            return new BitacoraAuditorium
            {
                IdUsuarioInterno = idUsuario,
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
