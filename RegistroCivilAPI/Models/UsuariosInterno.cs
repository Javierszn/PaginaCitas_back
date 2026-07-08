using System;
using System.Collections.Generic;

namespace RegistroCivilAPI.Models;

public partial class UsuariosInterno
{
    public int IdUsuario { get; set; }

    public string Username { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public string NombreCompleto { get; set; } = null!;

    public int IdRol { get; set; }

    public int IdSede { get; set; }

    public bool? Activo { get; set; }

    public virtual ICollection<BitacoraAuditorium> BitacoraAuditoria { get; set; } = new List<BitacoraAuditorium>();

    public virtual Role IdRolNavigation { get; set; } = null!;

    public virtual Sede IdSedeNavigation { get; set; } = null!;
}
