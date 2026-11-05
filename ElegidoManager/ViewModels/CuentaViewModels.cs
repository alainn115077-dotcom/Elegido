using System.ComponentModel.DataAnnotations;

namespace ElegidoManager.ViewModels
{
    public class LoginViewModel
    {
        [Required(ErrorMessage = "Escribe tu usuario.")]
        public string Usuario { get; set; } = string.Empty;

        [Required(ErrorMessage = "Escribe tu contraseña.")]
        [DataType(DataType.Password)]
        [Display(Name = "Contraseña")]
        public string Contrasena { get; set; } = string.Empty;

        [Display(Name = "Mantener sesión iniciada")]
        public bool Recordarme { get; set; }

        public string? ReturnUrl { get; set; }
    }

    public class NuevoUsuarioViewModel
    {
        [Required(ErrorMessage = "Escribe el nombre completo."), StringLength(100)]
        [Display(Name = "Nombre completo")]
        public string NombreCompleto { get; set; } = string.Empty;

        [Required(ErrorMessage = "Escribe un nombre de usuario."), StringLength(50, MinimumLength = 3, ErrorMessage = "Entre 3 y 50 caracteres.")]
        [RegularExpression(@"^[a-zA-Z0-9._-]+$", ErrorMessage = "Solo letras, números, punto, guion y guion bajo.")]
        public string Usuario { get; set; } = string.Empty;

        [Required(ErrorMessage = "Escribe una contraseña."), StringLength(100, MinimumLength = 8, ErrorMessage = "Mínimo 8 caracteres.")]
        [DataType(DataType.Password)]
        [Display(Name = "Contraseña")]
        public string Contrasena { get; set; } = string.Empty;

        [Required(ErrorMessage = "Confirma la contraseña.")]
        [DataType(DataType.Password)]
        [Compare(nameof(Contrasena), ErrorMessage = "Las contraseñas no coinciden.")]
        [Display(Name = "Confirmar contraseña")]
        public string ConfirmarContrasena { get; set; } = string.Empty;

        public string Rol { get; set; } = Models.Catalogos.RolAdministrador;

        [Display(Name = "¿Qué integrante es?")]
        public int? IntegranteId { get; set; }
    }

    public class CambiarContrasenaViewModel
    {
        [Required(ErrorMessage = "Escribe tu contraseña actual.")]
        [DataType(DataType.Password)]
        [Display(Name = "Contraseña actual")]
        public string Actual { get; set; } = string.Empty;

        [Required(ErrorMessage = "Escribe la nueva contraseña."), StringLength(100, MinimumLength = 8, ErrorMessage = "Mínimo 8 caracteres.")]
        [DataType(DataType.Password)]
        [Display(Name = "Nueva contraseña")]
        public string Nueva { get; set; } = string.Empty;

        [Required(ErrorMessage = "Confirma la contraseña.")]
        [DataType(DataType.Password)]
        [Compare(nameof(Nueva), ErrorMessage = "Las contraseñas no coinciden.")]
        [Display(Name = "Confirmar contraseña")]
        public string Confirmar { get; set; } = string.Empty;
    }
}
