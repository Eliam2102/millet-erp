import type { ReporteParaExportar } from '@/components/erp/reportes/exporters/ReporteDocument';
import { estadoToString, prioridadToString, type RequisicionResponse } from '../api/types';

export function reporteRequisicion(
  rq: RequisicionResponse,
  sucursal: string,
): ReporteParaExportar<Record<string, unknown>> {
  return {
    titulo: `Requisición ${rq.folio}`,
    generadoEn: new Date().toISOString(),
    filtrosAplicados: {
      Estado: estadoToString(rq.estado),
      Sucursal: sucursal,
      Departamento: rq.departamentoNombre,
      Solicitante: rq.requisitanteNombre,
      Solicitud: rq.fechaSolicitud,
      'Entrega deseada': rq.fechaEntregaDeseada,
      Prioridad: prioridadToString(rq.prioridad),
      Descripción: rq.descripcion,
      Obra: rq.obra ?? null,
      Proveedor: rq.proveedorSugeridoRazonSocial,
      Terminación: rq.motivoTerminacionTexto,
    },
    columnas: [
      { clave: 'posicion', etiqueta: '#', tipo: 'numero', anchoPdf: 0.4 },
      { clave: 'articulo', etiqueta: 'Artículo', tipo: 'texto', anchoPdf: 2 },
      { clave: 'cantidad', etiqueta: 'Cantidad', tipo: 'numero' },
      { clave: 'unidadMedida', etiqueta: 'Unidad', tipo: 'texto', anchoPdf: 0.6 },
      { clave: 'precio', etiqueta: 'Precio estimado / moneda', tipo: 'texto' },
      { clave: 'centro', etiqueta: 'Centro / proyecto', tipo: 'texto' },
      { clave: 'fechaRequerida', etiqueta: 'Requerida', tipo: 'fecha' },
      { clave: 'notas', etiqueta: 'Notas', tipo: 'texto', anchoPdf: 2 },
    ],
    filas: rq.lineas.map((l) => ({
      ...l,
      articulo: [l.articuloClave, l.articuloNombre].filter(Boolean).join(' · ') || l.articuloId,
      precio: `${l.precioEstimadoMonto.toLocaleString('es-MX')} ${l.precioEstimadoMoneda}`,
      centro: [l.centroCostoClave, l.centroCostoNombre, l.proyecto].filter(Boolean).join(' · '),
    })),
  };
}
