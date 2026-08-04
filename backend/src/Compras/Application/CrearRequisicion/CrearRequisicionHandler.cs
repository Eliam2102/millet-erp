using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Application.Folios;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Ports.Administracion;
using Millet.Compras.Domain.Ports.Almacen;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.Persistence;

namespace Millet.Compras.Application.CrearRequisicion;

/// <summary>
/// Handler de <see cref="CrearRequisicionCommand"/>:
/// <list type="number">
///   <item>Resuelve <c>EmpresaId</c> y <c>CreadorId</c> desde el JWT
///         (no del request) para evitar cross-tenant injection y
///         suplantación del creador.</item>
///   <item>Resuelve <c>RequisitanteId</c>: si viene en el command, lo
///         usa; si no, default al current user. <b>El check del permiso
///         <c>compras.requisiciones.seleccionar-requisitante</c> para
///         delegación se hace en la capa Api (endpoint)</b>, donde se
///         tiene acceso al cache de permisos sin que Compras dependa
///         de Identidad.</item>
///   <item>Genera el siguiente folio atómicamente vía upsert sobre
///         <c>compras.folio_secuencias</c> con
///         <c>INSERT ... ON CONFLICT DO UPDATE ... RETURNING</c>.</item>
///   <item>Construye <see cref="Folio"/> con formato canónico
///         <c>{SucursalCodigo}{FolioAnio}-{secuencial:D6}</c>.</item>
///   <item>Crea el agregado <see cref="Requisicion"/> en estado
///         <see cref="EstadoRequisicion.Borrador"/> y persiste.</item>
/// </list>
///
/// Cuidado §6.1 [P0] (no bypass de interceptors): <see cref="FolioSecuencia"/>
/// NO es <c>IAuditable</c>; el upsert SQL crudo es válido. La
/// <see cref="Requisicion"/> sí pasa por <c>SaveChanges</c> y todos los
/// interceptors.
/// </summary>
public sealed class CrearRequisicionHandler : IRequestHandler<CrearRequisicionCommand, CrearRequisicionResponse>
{
    private readonly ComprasDbContext _db;
    private readonly CompartidoDbContext _compartido;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly ISucursalDepartamentoReadPort _sucursalDepartamento;
    private readonly IFolioSecuenciaService _folios;

    public CrearRequisicionHandler(
        ComprasDbContext db,
        CompartidoDbContext compartido,
        ICurrentUserContext currentUser,
        ICurrentEmpresaContext currentEmpresa,
        ISucursalDepartamentoReadPort sucursalDepartamento,
        IFolioSecuenciaService folios)
    {
        _db = db;
        _compartido = compartido;
        _currentUser = currentUser;
        _currentEmpresa = currentEmpresa;
        _sucursalDepartamento = sucursalDepartamento;
        _folios = folios;
    }

    public async Task<CrearRequisicionResponse> Handle(
        CrearRequisicionCommand command,
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

        var requisitanteId = command.RequisitanteId ?? userId;

        // F7-PR1: validar proveedor sugerido cross-table si viene en el request.
        if (command.ProveedorSugeridoId is Guid provId)
        {
            var prov = await _compartido.Proveedores
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == provId, cancellationToken)
                ?? throw new EntityNotFoundException(
                    "PROVEEDOR_NO_ENCONTRADO",
                    $"No se encontró proveedor sugerido con id '{provId}'.");

            if (prov.Estatus != EstatusCatalogo.Activo)
            {
                throw new BusinessRuleException(
                    "PROVEEDOR_INACTIVO",
                    $"El proveedor '{prov.Clave}' está {prov.Estatus} y no puede sugerirse en RQs nuevas.");
            }
        }

        // PR-A2: validación cross-table sucursal/depto (01-diseno Rev. 21).
        // Almacén-por-línea PR3: la RQ manual ya no captura almacén destino
        // (nace null), así que se retiró la validación de existencia +
        // pertenencia-a-sucursal del almacén. El reorden (RQ Sistema) valida
        // su propio almacén en ComprasCrearRqSistemaAdapter.
        var operaDepto = await _sucursalDepartamento.OperaAsync(
            command.SucursalId, command.DepartamentoId, cancellationToken);

        if (!operaDepto)
        {
            throw new BusinessRuleException(
                "RQ_DEPTO_NO_OPERA_EN_SUCURSAL",
                $"El departamento '{command.DepartamentoId}' no opera en la sucursal '{command.SucursalCodigo}' o la asignación está inactiva.");
        }

        // PLATFORM-TODO(<CatalogoSucursales>): el cliente envía
        // SucursalCodigo en el request porque aún no existe el catálogo
        // de sucursales. Cuando exista, el handler hace lookup
        // sucursal.Codigo por sucursalId y borra SucursalCodigo del
        // command; el cliente solo necesitará SucursalId.
        var folioStr = await _folios.SiguienteFolioRequisicionAsync(
            empresaId, command.SucursalId, command.SucursalCodigo, command.FolioAnio, cancellationToken);
        var folio = Folio.Parse(folioStr);

        var requisicion = new Requisicion(
            id: Guid.CreateVersion7(),
            empresaId: empresaId,
            folio: folio,
            folioAnio: command.FolioAnio,
            clasificacion: command.Clasificacion,
            sucursalId: command.SucursalId,
            departamentoId: command.DepartamentoId,
            almacenDestinoId: null,
            requisitanteId: requisitanteId,
            creadorId: userId,
            prioridad: command.Prioridad,
            fechaSolicitud: command.FechaSolicitud,
            fechaEntregaDeseada: command.FechaEntregaDeseada,
            proveedorSugeridoId: command.ProveedorSugeridoId,
            descripcion: command.Descripcion);

        _db.Requisiciones.Add(requisicion);
        await _db.SaveChangesAsync(cancellationToken);

        return new CrearRequisicionResponse(
            Id: requisicion.Id,
            Folio: requisicion.Folio.Valor,
            FolioAnio: requisicion.FolioAnio,
            Estado: requisicion.Estado,
            Version: requisicion.Version);
    }
}
