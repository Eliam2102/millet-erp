using Millet.ComercioExterior.Domain;
using Millet.CuentasPorPagar.Application.PublicPorts;
namespace Millet.ComercioExterior.Application;

public sealed class CierreServiciosCeServicio(IFacturaGastoReadPort facturas)
{
    public async Task<IReadOnlyList<string>> MotivosAsync(Expediente e, CancellationToken ct)
    {
        if (e.Tipo != TipoExpediente.Importacion) return [];
        var gastos = e.Gastos.Where(g => !g.ImpuestoPeca).ToArray();
        if (gastos.Length == 0) return [];
        var saldos = (await facturas.ObtenerSaldosAsync(e.EmpresaId,
            gastos.Select(g => g.FacturaProveedorId).OfType<Guid>().Distinct().ToArray(), ct)).ToDictionary(f => f.Id);
        List<string> motivos = [];
        foreach (var g in gastos)
        {
            if (g.FacturaProveedorId is not Guid id || !saldos.TryGetValue(id, out var f)
                || f.EmpresaId != e.EmpresaId || f.ProveedorId != g.ProveedorId || f.SucursalId != e.SucursalId || f.Moneda != g.Moneda)
                motivos.Add($"No se pudo verificar la factura del servicio {g.Factura}; revise su vínculo en Cuentas por Pagar.");
            else if (f.PagoPorConciliar)
                motivos.Add($"Servicio {f.Referencia}: hay un pago vigente en Tesorería pendiente de conciliar en Cuentas por Pagar. No registre otro pago.");
            else if (!f.Autorizada || f.SaldoPendiente != 0)
                motivos.Add($"Servicio {f.Referencia}: {(f.Autorizada ? "saldo pendiente " + f.Moneda + " " + f.SaldoPendiente.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("es-MX")) : "factura pendiente de autorización")}. Complete su pago o aplicación en Cuentas por Pagar.");
        }
        return motivos;
    }
}
