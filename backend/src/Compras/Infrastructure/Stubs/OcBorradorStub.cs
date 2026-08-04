using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;

namespace Millet.Compras.Infrastructure.Stubs;

/// <summary>
/// Entidad provisional para el stub <c>InMemoryGenerarSolicitudCompraPort</c>.
/// Cada llamada a <c>GenerarBorradorAsync</c> persiste una fila aquí —
/// permite que los tests verifiquen que la bifurcación a OC se invocó
/// con el saldo esperado, sin depender de un submódulo OC real.
///
/// <para>
/// PLATFORM-TODO(<![CDATA[<StubsTeardown>]]>): cuando exista la
/// implementación real del puerto en el submódulo OC, eliminar:
/// <list type="bullet">
///   <item>Esta entidad y su <see cref="Configurations.OcBorradorStubConfiguration"/>.</item>
///   <item>La tabla <c>compras.oc_borrador_stub</c> (migración de drop).</item>
///   <item>Los 5 stubs <c>InMemory*</c> y la opción <c>Compras:UseStubs</c>.</item>
/// </list>
/// </para>
/// <para>
/// Vive en <c>Infrastructure/Stubs/</c> (no en <c>Domain/</c>) porque es
/// pura infraestructura provisional: no representa un concepto de
/// dominio. El módulo OC, cuando exista, tendrá sus propias entidades en
/// su propio schema.
/// </para>
/// </summary>
public sealed class OcBorradorStub : BaseEntity, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; }

    public Guid OrigenRequisicionId { get; private set; }

    /// <summary>
    /// Payload de las líneas de saldo serializado como JSON. Persiste como
    /// <c>jsonb</c> en Postgres. Estructura: lista de
    /// <see cref="Domain.Ports.OrdenCompra.LineaSaldo"/>.
    /// </summary>
    public string LineasJson { get; private set; } = "[]";

    private OcBorradorStub() { }

    public OcBorradorStub(Guid id, Guid empresaId, Guid origenRequisicionId, string lineasJson) : base(id)
    {
        EmpresaId = empresaId;
        OrigenRequisicionId = origenRequisicionId;
        LineasJson = lineasJson;
    }
}
