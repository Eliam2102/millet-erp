using Microsoft.EntityFrameworkCore;
using Millet.ComercioExterior.Domain;
using Millet.ComercioExterior.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Infrastructure.Persistence;

namespace Millet.Api.Seed;
public sealed partial class DemoSesionSeedHostedService
{
    private static async Task SembrarContenedoresCeAsync(IServiceProvider sp, Guid sucursalId, Guid incotermId, CancellationToken ct)
    {
        var maestros = sp.GetRequiredService<CompartidoDbContext>();
        var facturacion = sp.GetRequiredService<FacturacionDbContext>();
        var db = sp.GetRequiredService<ComercioExteriorDbContext>();
        for (var i = 1; i <= 3; i++)
        {
            var clave = $"DEMO-CLI-EXP-{i}";
            if (!await maestros.Clientes.AnyAsync(c => c.Clave == clave, ct))
                maestros.Clientes.Add(new Cliente(Id(clave), clave, $"DEMO · Cliente extranjero {i}", referenciaExterna: clave, rfc: "XEXX010101000", regimenFiscal: "616",
                    codigoPostalFiscal: "42501", usoCfdiDefault: "S01", formaPagoDefault: "99", metodoPagoDefault: "PPD", monedaDefault: "USD",
                    esGenerico: true, numRegIdTrib: $"DEMO-TAX-{i}", paisResidencia: "USA", domicilioExtranjeroEstado: "DEMO", domicilioExtranjeroCodigoPostal: "00000"));
        }
        await maestros.SaveChangesAsync(ct);
        var pedidos = new Dictionary<string, PedidoFacturable>();
        foreach (var (numero, cliente, cantidad) in new[] { ("DEMO-PED-EXP-P1", 1, 60m), ("DEMO-PED-EXP-P2", 1, 40m), ("DEMO-PED-EXP-C2", 2, 10m), ("DEMO-PED-EXP-C3", 3, 20m), ("DEMO-PED-EXP-RT", 1, 5m) })
        {
            var p = await facturacion.PedidosFacturables.Include(p => p.Lineas).SingleOrDefaultAsync(p => p.NumeroPedido == numero && p.EmpresaId == EmpresaId, ct);
            if (p is null)
            {
                var cid = Id($"DEMO-CLI-EXP-{cliente}");
                p = PedidoFacturable.ImportarDesdeAw(EmpresaId, numero, sucursalId, cid, $"DEMO · Cliente extranjero {cliente}", 1,
                    ComportamientoFiscal.ExportacionConCce, "USD", null, null, "DEMO · Parcialidades discretas del mismo pedido A+W; sin conexión al origen.", 1, "15");
                p.AgregarLinea(null, "DEMO · Vidrio de exportación", "30171700", "H87", cantidad, 100, 0, false, tasaIva: 0);
                p.RecalcularTotal(); facturacion.PedidosFacturables.Add(p);
            }
            pedidos[numero] = p;
        }
        await facturacion.SaveChangesAsync(ct);
        foreach (var clave in new[] { "A1", "RT" })
        {
            var id = Id(clave == "A1" ? "DEMO-CE-EXPORTACION" : "DEMO-CE-EXPORTACION-RT");
            var e = await db.Expedientes.Include(e => e.Contenedores).Include(e => e.Partidas).Include(e => e.Participantes).SingleOrDefaultAsync(e => e.Id == id, ct);
            if (e is null)
            {
                e = new Expediente(id, EmpresaId, "CE-EXP-2026-900003", TipoExpediente.Exportacion, sucursalId, "DEMO · Exportación temporal RT", null, incotermId, "USD", CapturistaComprasDemoId, ["DEMO · Cliente extranjero 1"]);
                e.InicializarHitos(new(2026, 10, 12)); db.Expedientes.Add(e);
            }
            if (e.Contenedores.Count > 0 || e.Estado != EstadoExpediente.Abierto) continue;
            for (var i = 1; i <= (clave == "A1" ? 3 : 1); i++)
            {
                var cliente = Id($"DEMO-CLI-EXP-{i}");
                if (!e.Participantes.Any(p => p.ClienteId == cliente))
                    e.AgregarParticipante(Id($"DEMO-CE/{clave}/CLIENTE-{i}"), $"DEMO · Cliente extranjero {i}", TipoParticipante.Cliente, null, PapelParticipante.Captura, null, cliente);
            }
            e.RegistrarPedimentoExportacion($"DEMO-PEDIMENTO-{clave}", clave, new(2026, 10, 12), new(2026, 10, 16));
            for (var i = 1; i <= (clave == "A1" ? 2 : 1); i++)
            {
                var cid = Id($"DEMO-CE/{clave}/CONT-{i}");
                e.GuardarContenedor(cid, new($"DEMO-CONT-{clave}-{i}", "DEMO-SELLO", $"DEMO-BOOK-{clave}", $"DEMO-BL-{clave}-{i}", "DEMO-RUTA", new(2026, 10, 12), 100, 1));
                var numeros = clave == "RT" ? new[] { "DEMO-PED-EXP-RT" } : i == 1 ? new[] { "DEMO-PED-EXP-P1", "DEMO-PED-EXP-C2", "DEMO-PED-EXP-C3" } : new[] { "DEMO-PED-EXP-P2" };
                foreach (var numero in numeros)
                {
                    var p = pedidos[numero];
                    e.AsignarPartida(cid, new(p.Id, numero, p.ClienteId, p.ClienteNombre, sucursalId, true, p.Estado.ToString(), p.VersionOrigen,
                        p.Lineas.Select(l => new LineaEmbarcable(l.Posicion, l.ProductoId, l.ProductoDescripcion, l.Cantidad, l.ClaveUnidadSat!)).ToArray(), null, null), 1, p.Lineas.Single().Cantidad, CapturistaComprasDemoId);
                }
                if (i == 1) e.RegistrarEmbarque(cid, new(2026, 10, 12), new(2026, 10, 16));
            }
            await db.SaveChangesAsync(ct);
        }
    }
}
