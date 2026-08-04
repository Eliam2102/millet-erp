namespace Millet.Catalogos.Domain;

/// <summary>
/// A qué tipo de persona aplica un uso CFDI (F-Admin-PR5.3). El SAT
/// define explícitamente qué usos son válidos para personas físicas,
/// morales o ambas.
/// </summary>
public enum AplicaTipoPersona : short
{
    AmbosFisicaMoral = 0,
    SoloFisica = 1,
    SoloMoral = 2,
}
