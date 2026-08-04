namespace Millet.Administracion.Domain;

/// <summary>
/// Tipo del valor de un <see cref="ParametroGlobal"/>. Determina cómo
/// el handler <c>ActualizarParametroCommand</c> valida el string
/// entrante antes de persistir.
/// </summary>
public enum TipoParametro : short
{
    Texto = 0,
    Numero = 1,
    Booleano = 2,
    Json = 3,
}
