using Microsoft.EntityFrameworkCore;
using Millet.ComercioExterior.Infrastructure.Persistence;
using System.Globalization;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.ComercioExterior.Domain;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.ComercioExterior.Application;
public static class ValidacionesFinancieras
{
    public static async Task PeriodoAbiertoAsync(IPeriodoContableConsultaPort periodos, DateOnly fecha, CancellationToken ct)
    {
        if (!(await periodos.ConsultarPorFechaAsync(fecha, ct)).AdmiteMovimientos)
            throw new BusinessRuleException("CE_PERIODO_CERRADO", $"El periodo contable de {fecha.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("es-MX"))} está cerrado");
    }
    public static async Task FacturaUnicaAsync(ComercioExteriorDbContext db, Guid empresa, Guid factura, Guid gastoId, CancellationToken ct)
    {
        var duplicado = await (from gasto in db.Set<GastoExpediente>()
            join expediente in db.Expedientes on gasto.ExpedienteId equals expediente.Id
            where gasto.EmpresaId == empresa && gasto.FacturaProveedorId == factura && gasto.Id != gastoId
            select expediente.Folio).FirstOrDefaultAsync(ct);
        if (duplicado is not null)
            throw new BusinessRuleException("CE_FACTURA_DUPLICADA", $"Esta factura ya está en el expediente {duplicado}");
    }
    public static void FacturaRequerida(DatosGasto gasto, ConceptoGasto concepto)
    {
        if (gasto.ImpuestoPeca && (concepto.Codigo is not ("IVA" or "IGI" or "PRV" or "DTA") || string.IsNullOrWhiteSpace(gasto.Pedimento)))
            throw new BusinessRuleException("CE_PECA_INVALIDO", "La excepción PECA solo admite impuestos con pedimento.");
        if (!gasto.ImpuestoPeca && gasto.FacturaProveedorId is null)
            throw new BusinessRuleException("CE_FACTURA_REQUERIDA", "Seleccione la factura de Cuentas por Pagar (UUID).");
    }
}
