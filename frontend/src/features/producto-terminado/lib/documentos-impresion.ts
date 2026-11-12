import type { ReporteParaExportar } from '@/components/erp/reportes/exporters/ReporteDocument';
import type { HojaSalidaDto, TraspasoDto } from '../api/types';

export function reporteHojaSalida(
  h: HojaSalidaDto,
  nombre: (id: string) => string,
): ReporteParaExportar<Record<string, unknown>> {
  return {
    titulo: `Hoja de salida ${h.folio}`,
    generadoEn: new Date().toISOString(),
    filtrosAplicados: {
      Sucursal: nombre(h.sucursalId),
      Destino: h.destino,
      Estado: h.estado,
      Fecha: h.emitidaAt ?? h.creadaAt,
      'Carta Porte': h.cartaPorteFolio,
      Pedidos: String(h.pedidos),
      Piezas: String(h.piezas),
    },
    columnas: [
      { clave: 'secuencia', etiqueta: '#', tipo: 'numero', anchoPdf: 0.4 },
      { clave: 'folio', etiqueta: 'Albarán', tipo: 'texto' },
      { clave: 'numeroPedido', etiqueta: 'Pedido', tipo: 'texto' },
      { clave: 'cliente', etiqueta: 'Cliente', tipo: 'texto', anchoPdf: 1.5 },
      { clave: 'destino', etiqueta: 'Obra o destino', tipo: 'texto', anchoPdf: 1.5 },
      { clave: 'cargado', etiqueta: 'Lo cargado', tipo: 'texto', anchoPdf: 2 },
      { clave: 'entregaTexto', etiqueta: 'Entrega', tipo: 'texto' },
    ],
    filas: h.albaranes.map((a) => ({
      ...a,
      destino: a.destino ?? h.destino,
      cargado: [
        ...a.lineas.map((l) => `${l.piezas} pzas · partida ${l.posicion} ${l.descripcion ?? ''}`),
        a.etiquetas.join(', '),
        ...(a.rotas?.length ? [`Rotas pendientes: ${a.rotas.join(', ')}`] : []),
      ].join('\n'),
      entregaTexto: a.entrega ? `${a.entrega.resultado} · recibió ${a.entrega.receptor}` : 'Pendiente',
    })),
  };
}

export function reporteTraspaso(
  t: TraspasoDto,
  nombre: (id: string) => string,
): ReporteParaExportar<Record<string, unknown>> {
  return {
    titulo: `Hoja de traslado ${t.folio}`,
    generadoEn: new Date().toISOString(),
    filtrosAplicados: {
      Origen: nombre(t.sucursalOrigenId),
      Destino: nombre(t.sucursalDestinoId),
      Estado: t.estado,
      Fecha: t.autorizadoAt ?? t.salidaAt,
      'Carta Porte': t.cartaPorteFolio,
      Total: String(t.total),
      Pendientes: String(t.pendientes),
    },
    columnas: [
      { clave: 'etiqueta', etiqueta: 'Pieza', tipo: 'texto' },
      { clave: 'numeroPedido', etiqueta: 'Pedido', tipo: 'texto' },
      { clave: 'estadoTexto', etiqueta: 'Estado de recepción', tipo: 'texto' },
      { clave: 'clasificacionTexto', etiqueta: 'Diferencia', tipo: 'texto' },
      { clave: 'motivo', etiqueta: 'Motivo', tipo: 'texto', anchoPdf: 2 },
    ],
    filas: t.piezas.map((p) => ({
      ...p,
      estadoTexto:
        p.resolucion ??
        (p.recibida ? 'Recibida' : t.estado === 'Preparado' ? 'Sin salir' : 'En tránsito'),
      clasificacionTexto:
        p.clasificacion === 'DanadaEnTransito'
          ? 'Dañada en tránsito'
          : p.clasificacion === 'EnCuarentena'
            ? 'En cuarentena'
            : p.clasificacion,
    })),
  };
}
