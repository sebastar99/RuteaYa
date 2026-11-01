
using RuteaYa.Web.Services;
using System.Text.Json.Serialization;

namespace RuteaYa.Web.Models
{
    public class LoginApiRespuesta
    {
        [JsonPropertyName("token")]
        public string Token { get; set; } = string.Empty;

        [JsonPropertyName("tipo")]
        public string Tipo { get; set; } = string.Empty;

        [JsonPropertyName("expiraEn")]
        public DateTimeOffset ExpiraEn { get; set; }

        [JsonPropertyName("usuario")]
        public UsuarioApiRespuesta? Usuario { get; set; }
    }
}
