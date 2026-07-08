using System;
using System.Collections.Generic;

namespace RegistroCivilAPI.Models;

public partial class Role
{
    public int IdRol { get; set; }

    public string NombreRol { get; set; } = null!;

    public virtual ICollection<UsuariosInterno> UsuariosInternos { get; set; } = new List<UsuariosInterno>();
}
