using System.Net.Mail;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Domain;

/// <summary>
/// I13.2 (TI de Millet, Jorge Toache, 10-nov): correos que reciben el aviso cuando el tipo de cambio hacia A+W
/// se publica tarde o falla. Se editan en Administración → Parámetros sin desplegar (la gente cambia de puesto).
/// Lista separada por coma o punto y coma; vacía apaga el correo (la alerta sigue en el log).
/// </summary>
public static class AvisoTipoCambioAwParametro
{
    public const string Clave = "integraciones.aw.tipo-cambio.correos-aviso";
    public static readonly Guid Id = Guid.Parse("00000006-0001-0000-0000-00000000000e");
    public const string ValorInicial = "soportemillet@millet.com.mx, contadorgeneral@millet.com.mx, falejos@millet.com.mx";

    public static IReadOnlyList<string> LeerCorreos(string valor)
    {
        var correos = valor.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var correo in correos)
        {
            if (!MailAddress.TryCreate(correo, out var m) || m.Address != correo)
                throw new BusinessRuleException("AW_TC_AVISO_CORREO_INVALIDO",
                    $"'{correo}' no es un correo válido. Separe los correos con coma.");
        }
        return correos;
    }
}
