using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.Motor;
using Millet.Contabilidad.Application.Polizas;
using Millet.Contabilidad.Application.Ports;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Contabilidad.Application.Iva;

// ─── M-209 (86e3zr7mp) · CA4 · corrección del IVA de un documento contra su XML (CON-07-R) ─────────────────────────────────────

/// <summary>Diferencia de un tratamiento: lo contabilizado, lo del XML y la diferencia (la parte ya pagada va directo a pagado).</summary>
public sealed record DiferenciaIvaResponse(string Tratamiento, decimal BaseContabilizada, decimal IvaContabilizado, decimal BaseXml,
    decimal IvaXml, decimal BaseDiferencia, decimal IvaDiferencia, decimal IvaPagado);

/// <summary>
/// Resultado del recálculo. Sin <c>Aplicar</c>, solo la vista previa. Con él, la póliza de ajuste ligada al mismo documento (la original
/// no se anula) y el saldo de IVA corregido.
/// </summary>
public sealed record RecalculoIvaResponse(Guid SaldoId, string? Folio, string? Uuid, string Moneda, IReadOnlyList<DiferenciaIvaResponse> Diferencias,
    decimal IvaDiferencia, bool SinDiferencia, Guid? PolizaAjusteId, string? PolizaAjusteFolio);

/// <summary>Vuelve a leer el XML del documento y compara su IVA por tratamiento contra lo contabilizado; con <c>Aplicar</c>, corrige.</summary>
public sealed record RecalcularIvaDocumentoCommand(Guid SaldoId, string? Motivo, bool Aplicar) : IRequest<RecalculoIvaResponse>;

public sealed class RecalcularIvaDocumentoValidator : AbstractValidator<RecalcularIvaDocumentoCommand>
{
    public RecalcularIvaDocumentoValidator()
    {
        RuleFor(c => c.SaldoId).NotEqual(Guid.Empty);
        RuleFor(c => c.Motivo).Must(m => m is not null && m.Trim().Length is >= Poliza.MotivoMinimo and <= Poliza.MotivoMaximo)
            .When(c => c.Aplicar)
            .WithMessage($"Indique el motivo de la corrección (entre {Poliza.MotivoMinimo} y {Poliza.MotivoMaximo} caracteres).");
    }
}

public sealed class RecalcularIvaDocumentoHandler(ContabilidadDbContext db, ITrasladosIvaCfdiReadPort traslados, IPolizaContablePort polizas,
    IClock clock) : IRequestHandler<RecalcularIvaDocumentoCommand, RecalculoIvaResponse>
{
    public const string RolPendiente = "IVA_ACREDITABLE_PENDIENTE";
    public const string RolPagado = "IVA_ACREDITABLE_PAGADO";
    public const string RolProveedores = "PROVEEDORES";
    public static readonly (string Modulo, string Tipo) DocAjuste = ("contabilidad", "ajuste_iva");

    public async Task<RecalculoIvaResponse> Handle(RecalcularIvaDocumentoCommand request, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var saldo = await db.SaldosIvaDocumento.FirstOrDefaultAsync(s => s.Id == request.SaldoId, ct)
            ?? throw new EntityNotFoundException("CONTAB_IVA_SALDO_NO_ENCONTRADO", $"No existe el saldo de IVA '{request.SaldoId}'.");
        // Solo facturas de proveedor: la venta toma su IVA de sus propias líneas al timbrar (no hay captura que corregir).
        if (saldo.Lado != LadoIva.Acreditable || saldo.OrigenModulo != NormalizadorEventos.DocFacturaProveedor.Modulo)
            throw new BusinessRuleException("CONTAB_IVA_DOCUMENTO_NO_CORREGIBLE",
                "Solo se corrige contra su XML el IVA de una factura de proveedor.");
        if (saldo.Estado != EstadoSaldoIva.Vigente)
            throw new BusinessRuleException("CONTAB_IVA_DOCUMENTO_CANCELADO", "El documento está cancelado: su IVA no se corrige.");
        if (string.IsNullOrWhiteSpace(saldo.Uuid))
            throw new BusinessRuleException("CONTAB_IVA_SIN_UUID", "El documento no tiene UUID: no hay XML contra el cual recalcular el IVA.");
        var xml = await traslados.LeerAsync(saldo.Uuid, ct)
            ?? throw new BusinessRuleException("CONTAB_IVA_SIN_XML",
                $"No se encontró el XML del CFDI {saldo.Uuid} en el buzón de CxP: cárguelo para poder recalcular el IVA.");
        if (xml.Count == 0)
            throw new BusinessRuleException("CONTAB_IVA_XML_SIN_DESGLOSE",
                $"El XML del CFDI {saldo.Uuid} no desglosa los traslados de IVA por concepto: no se puede recalcular por tratamiento.");

        var diferencias = saldo.Diferencias(xml.Select(x => (x.Tratamiento, x.Base, x.Iva)), await ReducidoPorNotasAsync(saldo.Id, ct));
        var total = diferencias.Sum(d => d.IvaDiferencia);
        var respuesta = new RecalculoIvaResponse(saldo.Id, saldo.Folio, saldo.Uuid, saldo.Moneda,
            [.. diferencias.Select(d => new DiferenciaIvaResponse(d.Tratamiento, d.BaseContabilizada, d.IvaContabilizado, d.BaseXml, d.IvaXml,
                d.BaseDiferencia, d.IvaDiferencia, d.IvaPagado))],
            total, diferencias.Count == 0, null, null);
        if (!request.Aplicar) return respuesta;
        if (diferencias.Count == 0)
            throw new BusinessRuleException("CONTAB_IVA_SIN_DIFERENCIA", "El IVA contabilizado coincide con el XML: no hay nada que corregir.");

        // Sin diferencia de IVA (solo bases exentas o al 0 %), se corrige el saldo sin póliza.
        var poliza = diferencias.Any(d => d.IvaDiferencia != 0m) ? await PolizaAjusteAsync(saldo, diferencias, request.Motivo!.Trim(), ct) : null;
        var tc = saldo.Moneda == Poliza.MonedaNacional ? 1m : saldo.TipoCambio ?? 1m;
        db.ReclasificacionesIva.Add(saldo.Corregir(diferencias, poliza?.Id ?? Guid.CreateVersion7(), FechaContable.HoyLocal(clock.UtcNow),
            poliza?.Id ?? saldo.PolizaId, tc));
        await db.SaveChangesAsync(ct);
        return respuesta with { PolizaAjusteId = poliza?.Id, PolizaAjusteFolio = poliza?.Folio };
    }

    /// <summary>Lo que las notas de crédito o de cargo ya redujeron por tratamiento (también era IVA del documento original).</summary>
    private async Task<IReadOnlyDictionary<string, (decimal Base, decimal Iva)>> ReducidoPorNotasAsync(Guid saldoId, CancellationToken ct) =>
        (await db.ReclasificacionesIva.AsNoTracking()
            .Where(r => r.SaldoIvaDocumentoId == saldoId && r.Tipo == TipoMovimientoIva.Nota && r.AnuladaEn == null).ToListAsync(ct))
        .SelectMany(r => r.Tratamientos)
        .GroupBy(t => t.Tratamiento)
        .ToDictionary(g => g.Key, g => (g.Sum(t => t.Base), g.Sum(t => t.Iva)));

    /// <summary>
    /// Póliza de ajuste del mismo tipo que la del documento y ligada a él: la parte pendiente al IVA pendiente de su tratamiento (o al
    /// rol base), la parte ya pagada al IVA pagado y la contrapartida al proveedor (supuesto a validar). Idempotente por la versión del
    /// saldo (un reintento no duplica).
    /// </summary>
    private async Task<PolizaRegistrada> PolizaAjusteAsync(SaldoIvaDocumento saldo, IReadOnlyList<DiferenciaIvaDocumento> diferencias,
        string motivo, CancellationToken ct)
    {
        var sucursal = saldo.SucursalId ?? throw new BusinessRuleException("CONTAB_IVA_SIN_SUCURSAL",
            "El documento no tiene sucursal: no se puede generar la póliza de ajuste.");
        var tipoClave = await db.Polizas.IgnoreQueryFilters().AsNoTracking().Where(p => p.Id == saldo.PolizaId)
            .Select(p => p.TipoClave).FirstOrDefaultAsync(ct) ?? "TT";
        var fecha = FechaContable.HoyLocal(clock.UtcNow);
        var codigos = diferencias.Select(d => $"{RolPendiente}_{d.Tratamiento}").Append(RolPendiente).Append(RolPagado).Append(RolProveedores)
            .Distinct().ToList();
        var roles = await db.RolesCuenta.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.EmpresaId == saldo.EmpresaId && r.DeletedAt == null && codigos.Contains(r.Codigo))
            .ToListAsync(ct);
        Guid Cuenta(string rol, string? tratamiento = null) =>
            (tratamiento is null ? null : GenerarPolizaDesdeEventoHandler.Rol(roles, $"{rol}_{tratamiento}", sucursal, saldo.Moneda, fecha)?.CuentaId)
            ?? GenerarPolizaDesdeEventoHandler.Rol(roles, rol, sucursal, saldo.Moneda, fecha)?.CuentaId
            ?? throw new BusinessRuleException("CONTAB_IVA_ROL_SIN_CUENTA",
                $"El rol {rol} no tiene cuenta asignada: Contabilidad debe mapearlo en Roles de cuenta.");

        var tc = saldo.Moneda == Poliza.MonedaNacional ? 1m : saldo.TipoCambio ?? 1m;
        var partidas = new List<PartidaRequest>();
        var extranjera = saldo.Moneda != Poliza.MonedaNacional;
        void Agregar(Guid cuenta, string concepto, decimal importe)
        {
            if (importe == 0m) return;
            var mxn = Math.Round(importe * tc, 2, MidpointRounding.AwayFromZero);
            partidas.Add(new PartidaRequest(cuenta, concepto, mxn >= 0m ? mxn : 0m, mxn < 0m ? -mxn : 0m, sucursal,
                Moneda: saldo.Moneda, TipoCambio: tc, ImporteOriginal: extranjera ? Math.Abs(importe) : null, Origen: OrigenMovimiento.Automatica));
        }
        foreach (var d in diferencias)
        {
            Agregar(Cuenta(RolPendiente, d.Tratamiento), $"Ajuste de IVA pendiente ({d.Tratamiento})", d.IvaDiferencia - d.IvaPagado);
            Agregar(Cuenta(RolPagado), $"Ajuste de IVA pagado ({d.Tratamiento})", d.IvaPagado);
        }
        // Contrapartida al proveedor por el neto en MXN de las partidas de IVA (cuadra al centavo aunque haya tipo de cambio).
        var neto = partidas.Sum(p => p.Cargo - p.Abono);
        var proveedor = saldo.TerceroId ?? throw new BusinessRuleException("CONTAB_IVA_SIN_PROVEEDOR", "El documento no tiene proveedor.");
        if (neto != 0m)
            partidas.Add(new PartidaRequest(Cuenta(RolProveedores), "Proveedor", neto < 0m ? -neto : 0m, neto > 0m ? neto : 0m, sucursal,
                ProveedorId: proveedor, Moneda: saldo.Moneda, TipoCambio: tc,
                ImporteOriginal: extranjera ? Math.Abs(diferencias.Sum(d => d.IvaDiferencia)) : null, Origen: OrigenMovimiento.AuxiliarCxP));

        var referencia = saldo.Folio ?? saldo.Uuid;
        var concepto = $"Ajuste de IVA de {referencia}: {motivo}";
        return await polizas.RegistrarAutomaticaAsync(new SolicitudPolizaAutomatica(tipoClave, fecha, concepto.Length > 500 ? concepto[..500] : concepto,
            new DocumentoOrigenPoliza(saldo.OrigenModulo, saldo.OrigenTipo, saldo.DocumentoId, saldo.Folio, saldo.Uuid),
            $"{DocAjuste.Tipo}:{saldo.Id}:{saldo.Version}", partidas, OrigenMovimiento.Automatica, saldo.EmpresaId), ct);
    }
}
