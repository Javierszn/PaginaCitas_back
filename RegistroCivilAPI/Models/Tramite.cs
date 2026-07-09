using System;
using System.Collections.Generic;

namespace RegistroCivilAPI.Models;

public partial class Tramite
{
    public int IdTramite { get; set; }
    public int IdCategoria { get; set; }
    public string NombreTramite { get; set; } = null!;
    public string? Descripcion { get; set; }
    public string? RequisitosUrl { get; set; }
    public int DuracionMinutos { get; set; }
    public int LimiteDiarioSede { get; set; }
    public decimal Costo { get; set; }
    public bool? Activo { get; set; }
    public string? Requisitos { get; set; }
    public bool? Activa { get; set; }

    public virtual CategoriasTramite IdCategoriaNavigation { get; set; } = null!;
    public virtual ICollection<Cita> Citas { get; set; } = new List<Cita>();
}