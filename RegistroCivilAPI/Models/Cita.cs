using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema; 

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


    [Column("ip_origen")]
    public string? IpOrigen { get; set; }

    [Column("navegador")]
    public string? Navegador { get; set; }

    [Column("sistema_operativo")]
    public string? SistemaOperativo { get; set; }
}