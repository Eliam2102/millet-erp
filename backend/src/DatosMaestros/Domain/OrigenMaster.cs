namespace Millet.DatosMaestros.Domain;

/// <summary>
/// Origen de un registro de master de datos (ADR-0048 D5/D6): quién lo dio
/// de alta. <c>Aw</c> = auto-provisión desde las vistas de A+W durante la
/// ingesta de pedidos; <c>Manual</c> = captura en el ERP. Se persiste como
/// <c>smallint</c> (convención enums del repo).
/// </summary>
public enum OrigenMaster : short
{
    Manual = 0,
    Aw = 1,
}
