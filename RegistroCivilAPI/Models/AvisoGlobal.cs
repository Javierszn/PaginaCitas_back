using System;

namespace RegistroCivilAPI.Models;

public partial class AvisoGlobal
{
    public int IdAviso { get; set; }
    public string Titulo { get; set; } = null!;
    public string Mensaje { get; set; } = null!;
    public bool? Activo { get; set; }
}