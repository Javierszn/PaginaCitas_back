using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RegistroCivilAPI.Models;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims; 

namespace RegistroCivilAPI.Controllers
{
    [Authorize(Roles = "Administrador,Super Administrador")]
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
        public async Task<ActionResult> ObtenerBitacora([FromQuery] string? fecha = null, [FromQuery] string? busqueda = null, [FromQuery] int pagina = 1, [FromQuery] int registrosPorPagina = 50)
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

           
            int totalRegistros = await query.CountAsync();

            
            int totalPaginas = (int)Math.Ceiling(totalRegistros / (double)registrosPorPagina);
            int registrosASaltar = (pagina - 1) * registrosPorPagina;

            var registros = await query
                .OrderByDescending(b => b.FechaCambio) 
                .Skip(registrosASaltar)
                .Take(registrosPorPagina)
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

            // Los registros antiguos guardaban la contraseña como "secreto omitido".
            // Mantener inmutable el dato almacenado, pero mostrar el nombre del usuario
            // afectado y una descripción clara en la respuesta de bitácora.
            var idsUsuariosConPasswordLegacy = registros
                .Where(r => r.tabla == "Usuarios_Internos"
                    && (r.valorNuevo?.Contains("secreto omitido", StringComparison.OrdinalIgnoreCase) == true
                        || r.valorNuevo?.Contains("valor secreto", StringComparison.OrdinalIgnoreCase) == true))
                .Select(r => int.TryParse(r.registroId, out var idUsuario) ? (int?)idUsuario : null)
                .Where(idUsuario => idUsuario.HasValue)
                .Select(idUsuario => idUsuario!.Value)
                .Distinct()
                .ToArray();
            var usuariosLegacy = idsUsuariosConPasswordLegacy.Length == 0
                ? new Dictionary<int, (string Username, string NombreCompleto)>()
                : await _context.UsuariosInternos
                    .AsNoTracking()
                    .Where(u => idsUsuariosConPasswordLegacy.Contains(u.IdUsuario))
                    .Select(u => new { u.IdUsuario, u.Username, u.NombreCompleto })
                    .ToDictionaryAsync(u => u.IdUsuario, u => (u.Username, u.NombreCompleto));

            var registrosCreacionUsuarioLegacy = registros
                .Where(r => r.tabla == "Usuarios_Internos"
                    && r.valorNuevo?.StartsWith("Usuario creado;", StringComparison.OrdinalIgnoreCase) == true)
                .ToArray();
            var idsRolesLegacy = registrosCreacionUsuarioLegacy
                .Select(r => LeerIdCampo(r.valorNuevo, "rol"))
                .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray();
            var idsSedesLegacy = registrosCreacionUsuarioLegacy
                .Select(r => LeerIdCampo(r.valorNuevo, "sede"))
                .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray();
            var rolesLegacy = idsRolesLegacy.Length == 0
                ? new Dictionary<int, string>()
                : await _context.Roles.AsNoTracking()
                    .Where(r => idsRolesLegacy.Contains(r.IdRol))
                    .ToDictionaryAsync(r => r.IdRol, r => r.NombreRol);
            var sedesLegacy = idsSedesLegacy.Length == 0
                ? new Dictionary<int, string>()
                : await _context.Sedes.AsNoTracking()
                    .Where(s => idsSedesLegacy.Contains(s.IdSede))
                    .ToDictionaryAsync(s => s.IdSede, s => s.Nombre);

            var foliosCita = registros
                .Where(r => r.tabla == "Citas" && r.accion == "UPDATE")
                .Select(r => r.registroId)
                .Distinct()
                .ToArray();
            var ultimoMovimientoPorCita = foliosCita.Length == 0
                ? new Dictionary<string, int>()
                : await _context.BitacoraAuditoria
                    .Where(b => b.TablaAfectada == "Citas" && b.AccionRealizada == "UPDATE" && foliosCita.Contains(b.RegistroId))
                    .GroupBy(b => b.RegistroId)
                    .Select(grupo => new { RegistroId = grupo.Key, IdBitacora = grupo.Max(b => b.IdBitacora) })
                    .ToDictionaryAsync(x => x.RegistroId, x => x.IdBitacora);
            var registrosConPermisoDeshacer = registros.Select(r => new
            {
                r.idBitacora,
                r.empleado,
                r.tabla,
                r.accion,
                r.registroId,
                r.valorAnterior,
                valorNuevo = FormatearValorNuevo(r.tabla, r.registroId, r.valorNuevo, usuariosLegacy, rolesLegacy, sedesLegacy),
                r.fecha,
                puedeDeshacer = r.tabla == "Citas" && r.accion == "UPDATE" &&
                    r.valorNuevo?.Contains("(RESTAURADO)", StringComparison.OrdinalIgnoreCase) != true &&
                    ultimoMovimientoPorCita.TryGetValue(r.registroId, out var ultimoId) && ultimoId == r.idBitacora
            }).ToList();

            
            return Ok(new
            {
                totalRegistros = totalRegistros,
                totalPaginas = totalPaginas,
                paginaActual = pagina,
                datos = registrosConPermisoDeshacer
            });
        }

        private static int? LeerIdCampo(string? valor, string campo)
        {
            if (string.IsNullOrWhiteSpace(valor)) return null;
            foreach (var segmento in valor.Split(';'))
            {
                var texto = segmento.Trim();
                var prefijo = campo + ":";
                if (texto.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(texto[prefijo.Length..].Trim(), out var id))
                    return id;
            }
            return null;
        }

        private static string? FormatearValorNuevo(
            string tabla,
            string registroId,
            string? valorNuevo,
            Dictionary<int, (string Username, string NombreCompleto)> usuarios,
            Dictionary<int, string> roles,
            Dictionary<int, string> sedes)
        {
            if (string.IsNullOrWhiteSpace(valorNuevo)) return valorNuevo;

            if (tabla == "Usuarios_Internos"
                && (valorNuevo.Contains("secreto omitido", StringComparison.OrdinalIgnoreCase)
                    || valorNuevo.Contains("valor secreto", StringComparison.OrdinalIgnoreCase))
                && int.TryParse(registroId, out var idUsuario)
                && usuarios.TryGetValue(idUsuario, out var usuario))
                return $"Contraseña actualizada para {usuario.Username}; nombre: {usuario.NombreCompleto}. La contraseña no se registra en bitácora.";

            if (tabla != "Usuarios_Internos"
                || !valorNuevo.StartsWith("Usuario creado;", StringComparison.OrdinalIgnoreCase))
                return valorNuevo;

            var idRol = LeerIdCampo(valorNuevo, "rol");
            var idSede = LeerIdCampo(valorNuevo, "sede");
            var partes = valorNuevo.Split(';').Select(parte => parte.Trim()).ToArray();
            for (var i = 0; i < partes.Length; i++)
            {
                if (idRol.HasValue && partes[i].StartsWith("rol:", StringComparison.OrdinalIgnoreCase)
                    && roles.TryGetValue(idRol.Value, out var nombreRol))
                    partes[i] = $"rol: {nombreRol}";
                else if (idSede.HasValue && partes[i].StartsWith("sede:", StringComparison.OrdinalIgnoreCase)
                    && sedes.TryGetValue(idSede.Value, out var nombreSede))
                    partes[i] = $"sede: {nombreSede}";
            }
            return string.Join("; ", partes);
        }

        [HttpPost("Deshacer/{idBitacora}")]
        [Authorize(Roles = "Administrador,Super Administrador")]
        public async Task<ActionResult> DeshacerCambio(int idBitacora)
        {
          
            var usernameClaim = User.FindFirst(ClaimTypes.Name)?.Value;
            if (string.IsNullOrEmpty(usernameClaim))
                return Unauthorized(new { mensaje = "Token inválido." });

           
            var usuarioReal = await _context.UsuariosInternos.FirstOrDefaultAsync(u => u.Username == usernameClaim);
            if (usuarioReal == null)
                return Unauthorized(new { mensaje = "Usuario no encontrado en el sistema." });

            int idAdmin = usuarioReal.IdUsuario;

            var log = await _context.BitacoraAuditoria.FindAsync(idBitacora);
            if (log == null) return NotFound(new { mensaje = "Registro no encontrado." });

            if (log.TablaAfectada == "Citas" && log.AccionRealizada == "UPDATE")
            {
                var cita = await _context.Citas.FirstOrDefaultAsync(c => c.IdCita == log.RegistroId);
                if (cita == null) return BadRequest(new { mensaje = "La cita original ya no existe." });

                bool tieneMovimientoPosterior = await _context.BitacoraAuditoria.AnyAsync(b =>
                    b.TablaAfectada == "Citas" && b.AccionRealizada == "UPDATE" &&
                    b.RegistroId == log.RegistroId && b.IdBitacora > log.IdBitacora);
                if (tieneMovimientoPosterior)
                    return Conflict(new { mensaje = "Este movimiento ya no es el más reciente y no se puede deshacer. Solo puedes revertir el último cambio de la cita." });

                if (log.ValorNuevo?.Contains("(RESTAURADO)", StringComparison.OrdinalIgnoreCase) == true)
                    return Conflict(new { mensaje = "Este movimiento ya fue deshecho y no se puede volver a aplicar." });

                var estatusEsperado = log.ValorNuevo?.Replace("Estatus: ", "", StringComparison.OrdinalIgnoreCase).Trim();
                if (string.IsNullOrWhiteSpace(estatusEsperado))
                    return BadRequest(new { mensaje = "No se pudo validar el estado actual de la cita contra este movimiento." });

                if (!string.Equals(cita.Estatus, estatusEsperado, StringComparison.OrdinalIgnoreCase))
                    return Conflict(new { mensaje = "La cita ya cambió desde este movimiento. Solo se puede deshacer el cambio más reciente que coincida con su estado actual." });

                string estatusAnterior = log.ValorAnterior?.Replace("Estatus: ", "").Trim();
                if (string.IsNullOrEmpty(estatusAnterior))
                    return BadRequest(new { mensaje = "No se pudo leer el valor anterior para restaurarlo." });

                
                var estatusValidos = new[] { "PROGRAMADA", "CONFIRMADA", "REPROGRAMADA", "NO_CONFIRMADA", "ATENDIDA", "NO_ASISTIO", "CANCELADA" };
                if (Array.IndexOf(estatusValidos, estatusAnterior) < 0)
                    return BadRequest(new { mensaje = $"El valor anterior registrado ('{estatusAnterior}') no es un estatus válido y no puede restaurarse automáticamente." });

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
