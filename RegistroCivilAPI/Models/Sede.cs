using System;
using System.Collections.Generic;

namespace RegistroCivilAPI.Models;

public partial class Sede
{
    public int IdSede { get; set; }

    public string Nombre { get; set; } = null!;

    public string Municipio { get; set; } = null!;

    public string? Direccion { get; set; }

    public bool? Activa { get; set; }

    public virtual ICollection<Cita> Cita { get; set; } = new List<Cita>();

    public virtual ICollection<DiasInhabile> DiasInhabiles { get; set; } = new List<DiasInhabile>();

    public virtual ICollection<HorariosSede> HorariosSedes { get; set; } = new List<HorariosSede>();

    public virtual ICollection<UsuariosInterno> UsuariosInternos { get; set; } = new List<UsuariosInterno>();
}
