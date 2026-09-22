namespace RegistroCivilAPI.DTOs
{
    public class CitaResponseDTO
    {
        public int Id { get; set; }
        public DateTime FechaCita { get; set; }
        public string TramiteNombre { get; set; } 
        public string SedeNombre { get; set; }
        
    }
}
