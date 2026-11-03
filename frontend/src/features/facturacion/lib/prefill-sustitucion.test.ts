import { describe, expect, it } from 'vitest';
import { prefillSustitucion } from '@/features/facturacion/lib/prefill-sustitucion';
import type { DatosSustitucionResponse } from '@/features/facturacion/api/types';

const datos: DatosSustitucionResponse = {
  sustituyeComprobanteId: 'f-1',
  sustituyeFolio: 'A-1',
  sustituyeUuid: '11111111-1111-1111-1111-111111111111',
  sucursalId: 's-1',
  clienteId: null,
  receptorRfc: 'AAA010101AAA',
  receptorNombre: 'Cliente de prueba',
  receptorRegimenFiscal: '601',
  receptorCodigoPostal: '97000',
  receptorUsoCfdi: 'G03',
  metodoPago: 'PPD',
  formaPago: '99',
  moneda: 'USD',
  tipoCambio: 18.5,
  canalVenta: 3,
  comportamientoFiscal: 1,
  obraNombre: null,
  ordenCompraCliente: 'OC-9',
  referenciaCliente: null,
  condicionesPago: '30 DIAS',
  proforma: null,
  observaciones: 'Nota',
  lineas: [
    {
      productoId: null,
      claveProdServSat: '01010101',
      descripcion: 'Vidrio',
      claveUnidadSat: 'H87',
      cantidad: 2,
      valorUnitario: 50,
      descuento: 0,
      objetoImp: '02',
      tasaIvaTraslado: 0.16,
      tasaRetencionIva: null,
      tasaRetencionIsr: null,
      requierePedimento: false,
    },
  ],
};

describe('prefillSustitucion (FAC-08b)', () => {
  it('lleva la original como «sustituye» y no liga pedido', () => {
    const p = prefillSustitucion(datos);
    expect(p.sustituye).toEqual({ id: 'f-1', folio: 'A-1', uuid: '11111111-1111-1111-1111-111111111111' });
    expect(p.pedidoFacturableId).toBeUndefined();
    expect(p.clienteId).toBeUndefined();
  });

  it('copia receptor, pago, moneda y partidas de la original', () => {
    const p = prefillSustitucion(datos);
    expect(p).toMatchObject({
      receptorRfc: 'AAA010101AAA',
      metodoPago: 'PPD',
      formaPago: '99',
      moneda: 'USD',
      tipoCambio: 18.5,
      canalVenta: 3,
      ordenCompraCliente: 'OC-9',
      condicionesPago: '30 DIAS',
    });
    expect(p.lineas).toHaveLength(1);
    expect(p.lineas?.[0]).toMatchObject({ cantidad: 2, valorUnitario: 50, objetoImp: '02' });
  });
});
