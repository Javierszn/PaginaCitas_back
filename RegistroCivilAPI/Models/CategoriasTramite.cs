using System;
using System.Collections.Generic;

namespace RegistroCivilAPI.Models;

public partial class CategoriasTramite
{
    public int IdCategoria { get; set; }

    public string NombreCategoria { get; set; } = null!;

    public virtual ICollection<Tramite> Tramites { get; set; } = new List<Tramite>();
}
