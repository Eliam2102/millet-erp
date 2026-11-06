using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.Application.Salidas;

/// <summary>G-10: una salida no se confirma sin destinatario ni acuse (nombre de quien recibe); el momento lo fija el servidor.</summary>
internal static class SalidaAcuse
{
    public static void Exigir(Guid? personaDestinatariaId, string? acuseRecibeNombre)
    {
        if (personaDestinatariaId is null || personaDestinatariaId == Guid.Empty)
            throw new BusinessRuleException("SALIDA_SIN_DESTINATARIO", "Elige el destinatario de la salida.");
        if (string.IsNullOrWhiteSpace(acuseRecibeNombre))
            throw new BusinessRuleException("SALIDA_SIN_ACUSE", "Captura el nombre de quien recibe el material para confirmar la salida.");
        if (acuseRecibeNombre.Trim().Length > 150)
            throw new BusinessRuleException("SALIDA_ACUSE_LARGO", "El nombre de quien recibe no puede exceder 150 caracteres.");
    }
}
