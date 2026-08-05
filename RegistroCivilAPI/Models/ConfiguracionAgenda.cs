using System;

namespace RegistroCivilAPI.Models
{
    public partial class ConfiguracionAgenda
    {
        public int Id { get; set; }
        public DateTime FechaInicio { get; set; }
        public DateTime FechaFin { get; set; }
    }
}