using System.Text;
using FluentAssertions;
using Millet.Tesoreria.Domain.Conciliaciones.Actas;

namespace Millet.Tesoreria.UnitTests.Conciliaciones;

public class ActaConciliacionPdfTests
{
    [Fact]
    public void GenerarActa_produce_documento_pdf_valido_con_encabezado_y_firmas()
    {
        var datos = new DatosActaConciliacion(
            Guid.NewGuid(),
            "Vidrios Millet de México S.A. de C.V.",
            "VMI900101XYZ",
            "BBVA",
            "****5678",
            "MXN",
            2026,
            9,
            SaldoExtracto: 250_000.00m,
            SaldoLibros: 250_000.00m,
            TotalCargosBanco: 45_000.00m,
            TotalAbonosBanco: 80_000.00m,
            TotalMovimientosExtracto: 120,
            TotalEmparejados: 118,
            PartidasTransito: new[]
            {
                new DatosPartidaTransito(
                    new DateOnly(2026, 9, 30),
                    "Cheque no cobrado #4050",
                    "CHQ-4050",
                    2_500.00m,
                    "Egreso",
                    "ChequeNoCobrado",
                    "Javier Canché")
            },
            FirmanteNombre: "Lic. Miguel Ángel Millet",
            FirmantePuesto: "Gerente de Tesorería",
            FechaFirma: new DateTimeOffset(2026, 10, 8, 10, 30, 0, TimeSpan.Zero));

        byte[] pdf = ActaConciliacionPdfGenerator.Generar(datos);

        pdf.Should().NotBeNullOrEmpty();
        pdf.Length.Should().BeGreaterThan(1000);

        string header = Encoding.ASCII.GetString(pdf[..4]);
        header.Should().Be("%PDF");
    }
}
