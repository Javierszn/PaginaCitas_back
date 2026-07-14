using System;
using System.Collections.Generic;

namespace RegistroCivilAPI.Models;

public partial class BitacoraAuditorium
{
    public int IdBitacora { get; set; }
    public int IdUsuarioInterno { get; set; }
    public string TablaAfectada { get; set; } = null!;
    public string AccionRealizada { get; set; } = null!;

    // --- CORRECCIÓN: AHORA ES STRING PARA SOPORTAR LOS FOLIOS DE LAS CITAS ---
    public string RegistroId { get; set; } = null!;

    public string? ValorAnterior { get; set; }
    public string? ValorNuevo { get; set; }
    public DateTime? FechaCambio { get; set; }

    public virtual UsuariosInterno IdUsuarioInternoNavigation { get; set; } = null!;
}