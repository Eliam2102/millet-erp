import type { ReporteParaExportar } from '@/components/erp/reportes/exporters/ReporteDocument';
import type { Poliza } from '../api/polizas-types';
import type { PlantillaRecurrente } from '../api/recurrentes-types';

const columnas: ReporteParaExportar<Record<string, unknown>>['columnas'] = [
  { clave: 'linea', etiqueta: '#', tipo: 'numero', anchoPdf: 0.4 },
  { clave: 'cuenta', etiqueta: 'Cuenta', tipo: 'texto', anchoPdf: 1.5 },
  { clave: 'concepto', etiqueta: 'Concepto', tipo: 'texto', anchoPdf: 2 },
  { clave: 'sucursal', etiqueta: 'Sucursal', tipo: 'texto' },
  { clave: 'referencias', etiqueta: 'Centros / proyecto / auxiliares', tipo: 'texto', anchoPdf: 2 },
  { clave: 'cargo', etiqueta: 'Cargo MXN', tipo: 'moneda' },
  { clave: 'abono', etiqueta: 'Abono MXN', tipo: 'moneda' },
  { clave: 'divisa', etiqueta: 'Origen / tipo cambio', tipo: 'texto' },
];

export function reportePoliza(p: Poliza): ReporteParaExportar<Record<string, unknown>> {
  return {
    titulo: `Póliza ${p.folio ?? 'Borrador'}`,
    generadoEn: new Date().toISOString(),
    filtrosAplicados: {
      Tipo: p.tipoNombre ?? p.tipoClave,
      Estado: p.estado,
      Fecha: p.fecha,
      Periodo: `${p.periodoAnio}-${p.periodoNumero}`,
      Concepto: p.concepto,
      Origen: p.origen,
      Documento: p.documentoOrigen?.folio ?? null,
      UUID: p.documentoOrigen?.uuid ?? null,
      Capturó: p.capturadaPor,
      Autorizó: p.autorizadaPor,
      'Fecha autorización': p.autorizadaEn,
      'Motivo anulación': p.motivoAnulacion,
      'Anulada por': p.anuladaPorFolio,
      'Póliza relacionada': p.polizaRelacionadaFolio ?? null,
    },
    columnas,
    filas: p.partidas.map((x) => ({
      ...x,
      cuenta: [x.cuentaCodigo, x.cuentaNombre].filter(Boolean).join(' · '),
      sucursal: x.sucursalNombre ?? x.sucursalId,
      referencias: [
        x.dim1?.nombre,
        x.dim2?.nombre,
        x.dim3?.nombre,
        x.proyecto,
        x.cliente?.nombre,
        x.proveedor?.nombre,
        x.cuentaBancaria?.nombre,
      ]
        .filter(Boolean)
        .join(' · '),
      divisa: `${x.importeOriginal ?? ''} ${x.moneda} / TC ${x.tipoCambio}`,
    })),
    totales: { cargo: p.totalCargos, abono: p.totalAbonos },
  };
}

export function reportePlantilla(
  p: PlantillaRecurrente,
  cuenta: (id: string) => string,
  sucursal: (id: string) => string,
): ReporteParaExportar<Record<string, unknown>> {
  return {
    titulo: `Plantilla recurrente ${p.nombre}`,
    generadoEn: new Date().toISOString(),
    filtrosAplicados: {
      Estado: p.estado,
      Tipo: p.tipoClave,
      Concepto: p.concepto,
      Inicio: `${p.anioInicio}-${p.mesInicio}`,
      Periodos: String(p.numeroPeriodos),
      Día: String(p.diaDelMes),
      'Reversa automática': p.reversaAutomatica ? 'Sí' : 'No',
      'Nace autorizada': p.naceAutorizada ? 'Sí' : 'No',
      Preparó: p.creadaPor,
      Autorizó: p.activadaPor,
    },
    notas: p.aplicaciones.map(a => `Aplicación ${a.anio}-${a.mes}: ${a.folio ?? 'Pendiente'} (${a.estado ?? 'Sin generar'})${a.reversaFolio ? ` / Reversa ${a.reversaFolio}` : ''}`),
    columnas,
    filas: p.partidas.map((x, i) => ({
      ...x,
      linea: i + 1,
      cuenta: x.cuentaCodigo ? `${x.cuentaCodigo} ${x.cuentaNombre ?? ''}` : cuenta(x.cuentaId),
      sucursal: x.sucursalNombre ?? sucursal(x.sucursalId),
      referencias: [
        x.dim1?.nombre ?? x.dim1Id,
        x.dim2?.nombre ?? x.dim2Id,
        x.dim3?.nombre ?? x.dim3Id,
        x.proyecto,
        x.cliente?.nombre ?? x.clienteId,
        x.proveedor?.nombre ?? x.proveedorId,
        x.cuentaBancaria?.nombre ?? x.cuentaBancariaId,
      ]
        .filter(Boolean)
        .join(' · '),
      divisa: `${x.importeOriginal ?? ''} ${x.moneda ?? 'MXN'} / TC ${x.tipoCambio ?? 1}`,
    })),
    totales: {
      cargo: p.partidas.reduce((s, x) => s + x.cargo, 0),
      abono: p.partidas.reduce((s, x) => s + x.abono, 0),
    },
  };
}
