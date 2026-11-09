namespace Millet.Integraciones.Aw.Application.Pinturas.Catalogos;

/// <summary>
/// Representa un cliente del catálogo publicado para Planta Pintura (<c>dbo.pinturas_cliente</c>).
/// Clave numérica propia de Pinturas.
/// </summary>
public sealed record FilaPinturasCliente(
    int Clave,
    string RazonSocial,
    string? NombreComercial = null,
    string? Contacto = null,
    string? Telefono = null,
    string? Celular = null,
    string? Email = null,
    bool Activo = true);

/// <summary>
/// Representa un servicio de recubrimiento del catálogo publicado para Planta Pintura (<c>dbo.pinturas_servicio</c>).
/// Clave de SAP/ERP (p. ej. SER95224). El precio se administra localmente en Pinturas.
/// </summary>
public sealed record FilaPinturasServicio(
    string Clave,
    string Nombre,
    bool Activo = true);

/// <summary>
/// Representa una obra del catálogo compartido publicado para Planta Pintura (<c>dbo.pinturas_obra</c>).
/// Origen: 'AW' (KA_OBJEKT) o 'PINTURAS' (alta manual).
/// </summary>
public sealed record FilaPinturasObra(
    string Clave,
    string Nombre,
    string Origen = FilaPinturasObra.OrigenPinturas,
    string? ReferenciaAw = null,
    bool Activo = true)
{
    public const string OrigenAw = "AW";
    public const string OrigenPinturas = "PINTURAS";

    public string Origen { get; init; } = NormalizarOrigen(Origen);

    public bool EsOrigenAw => string.Equals(Origen, OrigenAw, StringComparison.OrdinalIgnoreCase);
    public bool EsOrigenPinturas => string.Equals(Origen, OrigenPinturas, StringComparison.OrdinalIgnoreCase);

    public static string NormalizarOrigen(string? origen)
    {
        if (string.IsNullOrWhiteSpace(origen))
        {
            return OrigenPinturas;
        }

        var upper = origen.Trim().ToUpperInvariant();
        return upper switch
        {
            OrigenAw => OrigenAw,
            OrigenPinturas => OrigenPinturas,
            _ => throw new ArgumentException($"Origen de obra no válido: '{origen}'. Valores permitidos: '{OrigenAw}', '{OrigenPinturas}'.", nameof(origen))
        };
    }
}

/// <summary>
/// Representa un empleado del catálogo publicado para Planta Pintura (<c>dbo.pinturas_empleado</c>).
/// </summary>
public sealed record FilaPinturasEmpleado(
    string Clave,
    string Nombre,
    bool Activo = true);

/// <summary>
/// Lote contenedor de los 4 catálogos maestros para sincronización hacia Planta Pintura.
/// </summary>
public sealed record PinturasCatalogosBatch(
    IReadOnlyList<FilaPinturasCliente> Clientes,
    IReadOnlyList<FilaPinturasServicio> Servicios,
    IReadOnlyList<FilaPinturasObra> Obras,
    IReadOnlyList<FilaPinturasEmpleado> Empleados)
{
    public IReadOnlyList<FilaPinturasCliente> Clientes { get; init; } = Clientes ?? Array.Empty<FilaPinturasCliente>();
    public IReadOnlyList<FilaPinturasServicio> Servicios { get; init; } = Servicios ?? Array.Empty<FilaPinturasServicio>();
    public IReadOnlyList<FilaPinturasObra> Obras { get; init; } = Obras ?? Array.Empty<FilaPinturasObra>();
    public IReadOnlyList<FilaPinturasEmpleado> Empleados { get; init; } = Empleados ?? Array.Empty<FilaPinturasEmpleado>();

    public PinturasCatalogosBatch()
        : this([], [], [], [])
    {
    }

    public static readonly PinturasCatalogosBatch Empty = new();

    public int TotalElementos => Clientes.Count + Servicios.Count + Obras.Count + Empleados.Count;
    public bool EstaVacio => TotalElementos == 0;
}

/// <summary>
/// Resultado del proceso de publicación de catálogos hacia la base de integración.
/// </summary>
public sealed record PinturasPublicacionResultado(
    int ClientesPublicados,
    int ServiciosPublicados,
    int ObrasPublicadas,
    int EmpleadosPublicados,
    DateTimeOffset PublicadoAt)
{
    public int TotalPublicados => ClientesPublicados + ServiciosPublicados + ObrasPublicadas + EmpleadosPublicados;

    public static PinturasPublicacionResultado Vacio(DateTimeOffset? publicadoAt = null) =>
        new(0, 0, 0, 0, publicadoAt ?? DateTimeOffset.UtcNow);
}
