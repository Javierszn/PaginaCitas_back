using System;
using System.Collections.Generic;

namespace RegistroCivilAPI.Models;

public partial class HorariosSede
{
    public int IdHorario { get; set; }

    public int IdSede { get; set; }

    public byte DiaSemana { get; set; }

    public TimeOnly HoraApertura { get; set; }

    public TimeOnly HoraCierre { get; set; }

    public virtual Sede IdSedeNavigation { get; set; } = null!;
}
