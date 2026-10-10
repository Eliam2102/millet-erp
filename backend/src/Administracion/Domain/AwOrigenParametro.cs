namespace Millet.Administracion.Domain;

/// <summary>Selección global de A+W; solo se modifica por el caso de uso autorizado de Integraciones.Aw.</summary>
public static class AwOrigenParametro
{
    public const string Clave = "integraciones.aw.origen-activo";
    // 000b reservado; comprobado contra los seeds y las migraciones existentes.
    public static readonly Guid Id = Guid.Parse("00000006-0001-0000-0000-00000000000c");
}
