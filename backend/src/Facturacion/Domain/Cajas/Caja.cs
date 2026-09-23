using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Facturacion.Domain.Cajas;

/// <summary>
/// Caja del módulo Facturación (12-cajas.md). Entidad compartida por las dos
/// capas del concepto: **Capa A** — alcance de datos (sus relaciones N:M con
/// sucursales, canales de venta y usuarios definen qué documentos ve/opera un
/// cajero, resolución dinámica `[Decisión 12-2]`) — y **Capa B** — sesión de
/// efectivo (las sesiones y cobros de mostrador entran en CAJAS-PR3/PR4).
///
/// <para>
/// Configuración operativa propia del módulo: el CRUD vive en Facturación,
/// no en Administración (excepción declarada al ADR-0034, 12-cajas.md §8).
/// Las relaciones NO son roles de I&amp;A.
/// </para>
///
/// <para>
/// Semántica del alcance: sucursales × canales (producto cartesiano). Sin
/// sucursales asignadas = todas las sucursales; sin canales = todos los
/// canales. Solapamientos entre cajas permitidos (visibilidad compartida,
/// `[Decisión 12-B]`).
/// </para>
/// </summary>
public sealed class Caja : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }

    public string Nombre { get; private set; } = string.Empty;
    public string? Descripcion { get; private set; }
    public EstatusCatalogo Estatus { get; private set; } = EstatusCatalogo.Activo;

    private readonly List<CajaSucursal> _sucursales = [];
    private readonly List<CajaCanal> _canales = [];
    private readonly List<CajaUsuario> _usuarios = [];

    /// <summary>Sucursales del alcance (vacío = todas).</summary>
    public IReadOnlyCollection<CajaSucursal> Sucursales => _sucursales.AsReadOnly();

    /// <summary>Canales de venta del alcance (vacío = todos).</summary>
    public IReadOnlyCollection<CajaCanal> Canales => _canales.AsReadOnly();

    /// <summary>Cajeros relacionados (pueden abrir sesión sin autorización de supervisor).</summary>
    public IReadOnlyCollection<CajaUsuario> Usuarios => _usuarios.AsReadOnly();

    private Caja() { }

    private Caja(Guid id, Guid empresaId, string nombre, string? descripcion) : base(id)
    {
        EmpresaId = empresaId;
        Nombre = nombre;
        Descripcion = descripcion;
        Estatus = EstatusCatalogo.Activo;
    }

    public static Caja Crear(Guid empresaId, string nombre, string? descripcion)
    {
        ValidarNombre(nombre);
        return new Caja(Guid.CreateVersion7(), empresaId, nombre.Trim(), Normalizar(descripcion));
    }

    public void Actualizar(string nombre, string? descripcion)
    {
        ValidarNombre(nombre);
        Nombre = nombre.Trim();
        Descripcion = Normalizar(descripcion);
    }

    /// <summary>Reactiva la caja. Idempotente.</summary>
    public void Activar() => Estatus = EstatusCatalogo.Activo;

    /// <summary>
    /// Desactiva la caja. Idempotente. Una caja inactiva deja de aportar
    /// combinaciones al alcance de sus usuarios y no admite sesiones nuevas;
    /// sus sesiones/cobros históricos no se tocan (registro financiero).
    /// </summary>
    public void Desactivar() => Estatus = EstatusCatalogo.Inactivo;

    /// <summary>
    /// Reemplaza el conjunto de sucursales del alcance (replace-set del PUT).
    /// Conserva las filas existentes que sobreviven (estabilidad de ids para
    /// auditoría) y es idempotente ante duplicados en el input.
    ///
    /// La invariante "sucursales de la misma empresa que la caja" es latente:
    /// el catálogo de sucursales es cross-empresa hoy (MVP mono-empresa).
    /// PLATFORM-TODO(&lt;SucursalEmpresaId&gt;): al agregar EmpresaId a
    /// compartido.sucursales, validar aquí la pertenencia.
    /// </summary>
    public void ReemplazarSucursales(IReadOnlyCollection<Guid> sucursalIds)
    {
        ValidarSinVacios(sucursalIds, "CAJA_SUCURSAL_INVALIDA", "SucursalId no puede ser vacío.");
        var deseadas = sucursalIds.Distinct().ToHashSet();
        _sucursales.RemoveAll(s => !deseadas.Contains(s.SucursalId));
        foreach (var id in deseadas.Where(id => _sucursales.All(s => s.SucursalId != id)))
            _sucursales.Add(new CajaSucursal(Guid.CreateVersion7(), Id, id));
    }

    /// <summary>Reemplaza el conjunto de canales de venta del alcance (replace-set del PUT).</summary>
    public void ReemplazarCanales(IReadOnlyCollection<short> canalVentaIds)
    {
        if (canalVentaIds.Any(c => c <= 0))
            throw new BusinessRuleException("CAJA_CANAL_INVALIDO", "CanalVentaId debe ser positivo.");
        var deseados = canalVentaIds.Distinct().ToHashSet();
        _canales.RemoveAll(c => !deseados.Contains(c.CanalVentaId));
        foreach (var id in deseados.Where(id => _canales.All(c => c.CanalVentaId != id)))
            _canales.Add(new CajaCanal(Guid.CreateVersion7(), Id, id));
    }

    /// <summary>
    /// Reemplaza el conjunto de cajeros relacionados (replace-set del PUT).
    /// El UsuarioId se guarda opaco, mismo trato que
    /// <c>Comprobante.UsuarioEmisorId</c> (sin FK cross-schema a Identidad);
    /// la UI lo alimenta desde el picker de usuarios.
    /// </summary>
    public void ReemplazarUsuarios(IReadOnlyCollection<Guid> usuarioIds)
    {
        ValidarSinVacios(usuarioIds, "CAJA_USUARIO_INVALIDO", "UsuarioId no puede ser vacío.");
        var deseados = usuarioIds.Distinct().ToHashSet();
        _usuarios.RemoveAll(u => !deseados.Contains(u.UsuarioId));
        foreach (var id in deseados.Where(id => _usuarios.All(u => u.UsuarioId != id)))
            _usuarios.Add(new CajaUsuario(Guid.CreateVersion7(), Id, id));
    }

    private static void ValidarNombre(string nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre))
            throw new BusinessRuleException("CAJA_NOMBRE_INVALIDO", "El nombre de la caja es obligatorio.");
        if (nombre.Trim().Length > 100)
            throw new BusinessRuleException("CAJA_NOMBRE_INVALIDO", "El nombre de la caja no puede exceder 100 caracteres.");
    }

    private static void ValidarSinVacios(IReadOnlyCollection<Guid> ids, string codigo, string mensaje)
    {
        if (ids.Any(id => id == Guid.Empty))
            throw new BusinessRuleException(codigo, mensaje);
    }

    private static string? Normalizar(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}

/// <summary>
/// Sucursal del alcance de una <see cref="Caja"/>. Tabla
/// <c>facturacion.caja_sucursal</c>, UNIQUE (caja_id, sucursal_id). La FK a
/// <c>compartido.sucursales</c> es lógica (sin FK física cross-schema, mismo
/// precedente que <c>comprobante.sucursal_id</c>); se valida vía
/// <c>ISucursalesReadPort</c> en el handler.
/// </summary>
public sealed class CajaSucursal : BaseEntity, IAuditable
{
    public Guid CajaId { get; private set; }
    public Guid SucursalId { get; private set; }

    private CajaSucursal() { }

    internal CajaSucursal(Guid id, Guid cajaId, Guid sucursalId) : base(id)
    {
        CajaId = cajaId;
        SucursalId = sucursalId;
    }
}

/// <summary>
/// Canal de venta del alcance de una <see cref="Caja"/>. Tabla
/// <c>facturacion.caja_canal</c>, UNIQUE (caja_id, canal_venta_id). Referencia
/// lógica a <c>compartido.canales_venta</c> (FAC-ING-PR2); se valida activo
/// vía <c>ICanalesVentaReadPort</c> en el handler.
/// </summary>
public sealed class CajaCanal : BaseEntity, IAuditable
{
    public Guid CajaId { get; private set; }
    public short CanalVentaId { get; private set; }

    private CajaCanal() { }

    internal CajaCanal(Guid id, Guid cajaId, short canalVentaId) : base(id)
    {
        CajaId = cajaId;
        CanalVentaId = canalVentaId;
    }
}

/// <summary>
/// Cajero relacionado a una <see cref="Caja"/>. Tabla
/// <c>facturacion.caja_usuario</c>, UNIQUE (caja_id, usuario_id). Administrado
/// por UI en Facturación — NO es un rol de I&amp;A (12-cajas.md §8). UsuarioId
/// opaco (sin FK a Identidad, mismo trato que <c>UsuarioEmisorId</c>).
/// </summary>
public sealed class CajaUsuario : BaseEntity, IAuditable
{
    public Guid CajaId { get; private set; }
    public Guid UsuarioId { get; private set; }

    private CajaUsuario() { }

    internal CajaUsuario(Guid id, Guid cajaId, Guid usuarioId) : base(id)
    {
        CajaId = cajaId;
        UsuarioId = usuarioId;
    }
}
