using System;
using System.Collections.Generic;

namespace RegistroCivilAPI.Models;

public partial class Cita
{
    public string IdCita { get; set; } = null!;

    public int IdCiudadano { get; set; }

    public int IdTramite { get; set; }

    public int IdSede { get; set; }

    public DateTime FechaHoraInicio { get; set; }

    public DateTime FechaHoraFin { get; set; }

    public string Estatus { get; set; } = null!;

    public virtual Ciudadano IdCiudadanoNavigation { get; set; } = null!;

    public virtual Sede IdSedeNavigation { get; set; } = null!;

    public virtual Tramite IdTramiteNavigation { get; set; } = null!;
}
