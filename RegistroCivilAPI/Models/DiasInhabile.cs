using System;
using System.Collections.Generic;

namespace RegistroCivilAPI.Models;

public partial class DiasInhabile
{
    public int IdDiaInhabil { get; set; }

    public int? IdSede { get; set; }

    public DateOnly FechaBloqueada { get; set; }

    public string Motivo { get; set; } = null!;

    public virtual Sede? IdSedeNavigation { get; set; }
}
