using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.Administracion.Application.Series;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.Compras.Application.Oc.CrearOrdenCompraVacia;

/// <summary>
/// Handler de <see cref="CrearOrdenCompraVaciaCommand"/>:
/// <list type="number">
///   <item>Resuelve <c>EmpresaId</c> y <c>CompradorTitularId</c> desde el
///         JWT (no del request) para evitar cross-tenant injection y
///         suplantación del comprador.</item>
///   <item>Resuelve <c>EncargadoComprasId</c>: si viene en el command, lo
///         usa; si no, default al comprador titular.</item>
///   <item>Valida el proveedor cross-table (existe + activo, C10 del
///         diseño aplicado desde la creación).</item>
///   <item>Genera el siguiente folio atómicamente vía upsert sobre
///         <c>compras.folio_secuencias_oc</c> con
///         <c>INSERT ... ON CONFLICT DO UPDATE ... RETURNING</c>.</item>
///   <item>Construye <see cref="Folio"/> con formato canónico §4.3
///         (<c>OC-{SucursalCodigo}{FolioAnio}-{secuencial:D6}</c>).</item>
///   <item>Crea el agregado <see cref="OrdenCompra"/> en estado
///         <see cref="EstadoOrdenCompra.Borrador"/> y persiste.</item>
/// </list>
///
/// Cuidado §6.1 [P0] (no bypass de interceptors): <see cref="FolioSecuenciaOc"/>
/// NO es <c>IAuditable</c>; el upsert SQL crudo es válido. La
/// <see cref="OrdenCompra"/> sí pasa por <c>SaveChanges</c> y todos los
/// interceptors (audit, empresa-aware, soft-delete, version bump).
///
/// PLATFORM-TODO(&lt;CatalogoSucursales&gt;): el cliente envía
/// <c>SucursalCodigo</c> en el request porque aún no existe el catálogo
/// de sucursales. Cuando exista, el handler hace lookup
/// <c>sucursal.Codigo</c> por <c>SucursalDestinoId</c> y borra
/// <c>SucursalCodigo</c> del command.
/// </summary>
public sealed class CrearOrdenCompraVaciaHandler
    : IRequestHandler<CrearOrdenCompraVaciaCommand, CrearOrdenCompraVaciaResponse>
{
    private readonly ComprasDbContext _db;
    private readonly CompartidoDbContext _compartido;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly IMediator _mediator;
    private readonly ILogger<CrearOrdenCompraVaciaHandler> _logger;

    public CrearOrdenCompraVaciaHandler(
        ComprasDbContext db,
        CompartidoDbContext compartido,
        ICurrentUserContext currentUser,
        ICurrentEmpresaContext currentEmpresa,
        IMediator mediator,
        ILogger<CrearOrdenCompraVaciaHandler> logger)
    {
        _db = db;
        _compartido = compartido;
        _currentUser = currentUser;
        _currentEmpresa = currentEmpresa;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task<CrearOrdenCompraVaciaResponse> Handle(
        CrearOrdenCompraVaciaCommand command,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not Guid userId)
        {
            throw new UnauthorizedAccessException("Sin usuario autenticado.");
        }

        if (_currentEmpresa.Current is not Guid empresaId)
        {
            throw new ForbiddenException(
                "EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada en el JWT actual.");
        }

        var encargadoId = command.EncargadoComprasId ?? userId;

        // C10: valid proveedor cross-table. Activo es invariante en cada
        // transición de estado; ya desde la creación validamos para
        // detectar OCs contra proveedores bloqueados antes de capturar.
        var proveedor = await _compartido.Proveedores
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == command.ProveedorId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "PROVEEDOR_NO_ENCONTRADO",
                $"No se encontró proveedor con id '{command.ProveedorId}'.");

        if (proveedor.Estatus != EstatusCatalogo.Activo)
        {
            throw new BusinessRuleException(
                "PROVEEDOR_INACTIVO",
                $"El proveedor '{proveedor.Clave}' está {proveedor.Estatus} y no puede usarse en OCs nuevas.");
        }

        // F-Admin-PR6.2: intentar reservar el folio via el nuevo sistema
        // de Series. Si no hay Serie configurada para
        // (Empresa, SucursalDestino, OrdenCompra), cae al sistema legacy
        // (compras.folio_secuencias_oc) que preserva el formato canónico
        // §4.3 y la compatibilidad con tests existentes.
        //
        // PLATFORM-TODO(<OcFolioMigrateToSeries>): cuando se decida el
        // formato unificado del nuevo sistema (¿incluye SucursalCodigo?
        // ¿Reinicio Anual o None?) y se actualice <see cref="Folio"/>
        // para aceptarlo, eliminar el fallback legacy y borrar la tabla
        // compras.folio_secuencias_oc + entidad FolioSecuenciaOc.
        var folioStr = await ReservarFolioConFallbackLegacyAsync(
            empresaId,
            command,
            cancellationToken);
        var folio = Folio.Parse(folioStr);

        var oc = new OrdenCompra(
            id: Guid.CreateVersion7(),
            empresaId: empresaId,
            folio: folio,
            folioAnio: command.FolioAnio,
            proveedorId: command.ProveedorId,
            sucursalDestinoId: command.SucursalDestinoId,
            condicionesPagoId: command.CondicionesPagoId,
            usoPrincipalId: command.UsoPrincipalId,
            compradorTitularId: userId,
            encargadoComprasId: encargadoId,
            fechaDocumento: command.FechaDocumento,
            moneda: command.Moneda,
            tipoCambio: command.TipoCambio,
            sinRequisicionPrevia: command.SinRequisicionPrevia,
            esImportacion: command.EsImportacion,
            cotizacionExcepcionada: command.CotizacionExcepcionada,
            observaciones: command.Observaciones,
            motivoSinRequisicion: command.MotivoSinRequisicion,
            fechaEntregaEsperada: command.FechaEntregaEsperada,
            ocOrigenId: command.OcOrigenId);

        _db.OrdenesCompra.Add(oc);
        await _db.SaveChangesAsync(cancellationToken);

        return new CrearOrdenCompraVaciaResponse(
            Id: oc.Id,
            Folio: oc.Folio.Valor,
            FolioAnio: oc.FolioAnio,
            Estado: oc.Estado,
            Version: oc.Version);
    }

    /// <summary>
    /// F-Admin-PR6.2: orquesta el path nuevo (Series) → legacy. Si
    /// existe una Serie activa para (Empresa, Sucursal, OrdenCompra) o
    /// la cross-sucursal, llama a <see cref="ReservarFolioCommand"/> y
    /// devuelve el folio formateado por el nuevo sistema. Si no, cae
    /// al path legacy con upsert sobre <c>compras.folio_secuencias_oc</c>.
    /// </summary>
    private async Task<string> ReservarFolioConFallbackLegacyAsync(
        Guid empresaId,
        CrearOrdenCompraVaciaCommand command,
        CancellationToken cancellationToken)
    {
        var fechaRef = command.FechaDocumento;

        try
        {
            var reservaResp = await _mediator.Send(
                new ReservarFolioCommand(
                    EmpresaId: empresaId,
                    SucursalId: command.SucursalDestinoId,
                    TipoDocumento: TipoDocumentoSerie.OrdenCompra,
                    FechaReferencia: fechaRef),
                cancellationToken);

            _logger.LogInformation(
                "OC folio reservado via Series: folio={Folio}, periodo={Periodo}, numero={Numero}",
                reservaResp.Folio, reservaResp.PeriodoClave, reservaResp.Numero);

            return reservaResp.Folio;
        }
        catch (BusinessRuleException ex) when (ex.Code == "SERIE_NO_CONFIGURADA")
        {
            // Fallback al sistema legacy. Log warning para que el
            // operador sepa que la empresa NO tiene Serie OC y está
            // dependiendo de compras.folio_secuencias_oc.
            _logger.LogWarning(
                "OC sin Serie configurada (empresa={EmpresaId}, sucursal={SucursalId}); " +
                "usando legacy compras.folio_secuencias_oc.",
                empresaId, command.SucursalDestinoId);

            var siguiente = await GetNextFolioSequenceLegacyAsync(
                empresaId,
                command.SucursalDestinoId,
                command.FolioAnio,
                cancellationToken);

            // Formato canónico §4.3: OC-{prefijoSucursal}{año}-{secuencial:6}
            // ej. OC-MID2026-000001.
            return $"OC-{command.SucursalCodigo}{command.FolioAnio}-{siguiente:D6}";
        }
    }

    /// <summary>
    /// PLATFORM-TODO(&lt;FolioSecuenciaDeprecate&gt;): el path legacy
    /// sigue activo hasta que F-Admin-PR6.x complete la migración formal
    /// de OC al nuevo sistema. Cuando el cutover ocurra, eliminar esta
    /// función, la entidad <c>FolioSecuenciaOc</c> y la tabla
    /// <c>compras.folio_secuencias_oc</c>.
    ///
    /// <para>Upsert atómico sobre <c>compras.folio_secuencias_oc</c>: si
    /// la fila no existe la inserta con <c>siguiente=2</c> (devolviendo
    /// 1 para esta OC); si ya existe, incrementa <c>siguiente</c> en 1
    /// y devuelve el valor previo. Unicidad garantizada por la PK
    /// compuesta.</para>
    /// </summary>
    private async Task<long> GetNextFolioSequenceLegacyAsync(
        Guid empresaId,
        Guid sucursalId,
        short anio,
        CancellationToken cancellationToken)
    {
        var result = await _db.Database
            .SqlQuery<long>($@"
                INSERT INTO compras.folio_secuencias_oc (empresa_id, sucursal_id, anio, siguiente)
                VALUES ({empresaId}, {sucursalId}, {anio}, 2)
                ON CONFLICT (empresa_id, sucursal_id, anio) DO UPDATE
                  SET siguiente = compras.folio_secuencias_oc.siguiente + 1
                RETURNING (siguiente - 1)::bigint AS ""Value""
            ")
            .ToListAsync(cancellationToken);

        return result.Single();
    }
}
