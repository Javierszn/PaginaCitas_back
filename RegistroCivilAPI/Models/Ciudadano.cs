using System;
using System.Collections.Generic;

namespace RegistroCivilAPI.Models;

public partial class Ciudadano
{
    public int IdCiudadano { get; set; }
    public string Curp { get; set; } = null!;
    public string Nombre { get; set; } = null!;
    public string PrimerApellido { get; set; } = null!;
    public string? SegundoApellido { get; set; }
    public string? Correo { get; set; }
    public string? Telefono { get; set; }
    public string? OrigenRegistro { get; set; }


    public virtual ICollection<Cita> Citas { get; set; } = new List<Cita>();
}