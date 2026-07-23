using System;

namespace RegistroCivilAPI.Models
{
    public partial class RegistroAcceso
    {
        public int IdAcceso { get; set; }
        public string Username { get; set; } = null!;
        public DateTime? FechaLogin { get; set; }
        public DateTime? FechaLogout { get; set; }
    }
}