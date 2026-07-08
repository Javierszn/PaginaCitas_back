using System;
using System.Collections.Generic;

namespace RegistroCivilAPI.Models;

public partial class Ciudadano
{
    public int IdCiudadano { get; set; }

    public string? Curp { get; set; }

    public string Nombre { get; set; } = null!;

    public string PrimerApellido { get; set; } = null!;

    public string? SegundoApellido { get; set; }

    public string Telefono { get; set; } = null!;

    public string Correo { get; set; } = null!;

    public string OrigenRegistro { get; set; } = null!;

    public virtual ICollection<Cita> Cita { get; set; } = new List<Cita>();
}
