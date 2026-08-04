namespace Millet.Compras.Domain;

/// <summary>
/// Niveles de autorización (§4.4, §10.1). Persistido como <c>smallint</c>
/// con CHECK <c>nivel IN (1, 2)</c>. Empieza en 1 (no 0) para alinear con
/// la columna del DDL del diseño.
/// </summary>
public enum NivelAutorizacion : short
{
    Nivel1 = 1,
    Nivel2 = 2,
}
