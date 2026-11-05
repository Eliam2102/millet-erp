import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Badge } from '@/components/ui/badge';
import { FormCampo, FormGrid } from '@/components/erp/forms/FormGrid';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import { useMutarContenedor } from '../api/hooks';
import { totalesPacking } from '../lib/packing';
import type { ContenedoresDetalle, ExpedienteDetalle, PartidaEmbarcada } from '../api/types';

export function SalidaPanel({ data, d }: { data: ExpedienteDetalle; d: ContenedoresDetalle }) {
  const mutar = useMutarContenedor(data.expediente.id);
  const autorizar =
    useHasPermission(P.ComercioExteriorEmbarquesAutorizarSalida) && data.expediente.estado === 1;
  const [motivo, setMotivo] = useState('');
  const [errorPdf, setErrorPdf] = useState('');
  const [generando, setGenerando] = useState(false);
  async function packing() {
    setGenerando(true);
    setErrorPdf('');
    try {
      const { descargarReportePdf } =
        await import('@/components/erp/reportes/exporters/reporte-pdf-exporter');
      await descargarReportePdf(
        {
          titulo: 'Packing list',
          generadoEn: new Date().toISOString(),
          filtrosAplicados: {
            Expediente: data.expediente.folio,
            'Versión del embarque': String(d.versionEmbarque),
          },
          columnas: [
            { clave: 'contenedor', etiqueta: 'Contenedor', tipo: 'texto' },
            { clave: 'pedido', etiqueta: 'Pedido', tipo: 'texto' },
            { clave: 'partida', etiqueta: 'Partida', tipo: 'numero' },
            { clave: 'descripcion', etiqueta: 'Descripción', tipo: 'texto' },
            { clave: 'cantidad', etiqueta: 'Cantidad', tipo: 'numero' },
            { clave: 'unidad', etiqueta: 'Unidad', tipo: 'texto' },
            { clave: 'pesoKg', etiqueta: 'Peso (kg)', tipo: 'numero' },
            { clave: 'bultos', etiqueta: 'Bultos', tipo: 'numero' },
          ],
          filas: d.partidas
            .filter(
              (p) =>
                p.estado !== 3 &&
                d.contenedores.some((c) => c.id === p.contenedorId && c.estado === 1),
            )
            .map((p) => ({
              contenedor:
                d.contenedores.find((c) => c.id === p.contenedorId)?.numero ?? 'Sin número',
              pedido:
                d.pedidos.find((o) => o.id === p.pedidoFacturableId)?.numeroPedido ?? 'Sin número',
              partida: p.posicion,
              descripcion:
                d.pedidos
                  .find((o) => o.id === p.pedidoFacturableId)
                  ?.lineas.find((l) => l.posicion === p.posicion)?.descripcion ?? '',
              cantidad: p.cantidad,
              unidad: p.unidad,
              pesoKg: p.pesoKg ?? null,
              bultos: p.bultos ?? null,
            })),
        },
        `Packing-${data.expediente.folio}-v${d.versionEmbarque}`,
      );
    } catch {
      setErrorPdf(
        'No se pudo generar el packing list. Revise las partidas y vuelva a descargarlo.',
      );
    } finally {
      setGenerando(false);
    }
  }
  return (
    <section className="space-y-3 rounded-lg bg-surface-card p-4 shadow-card-flat">
      <h3 className="font-semibold">Salida del embarque · Versión {d.versionEmbarque}</h3>
      <Badge variant={d.salidaAutorizada ? 'success' : 'warning'}>
        {d.salidaAutorizada ? 'Salida autorizada' : 'Salida pendiente de autorización'}
      </Badge>
      {d.contenedores
        .filter((c) => c.estado === 1)
        .map((c) => {
          const t = totalesPacking(d, c.id);
          return (
            <p key={c.id} className={t.cuadra ? 'text-ink-muted' : 'text-warning-note-fg'}>
              {c.numero ?? 'Sin número'} · Partidas: {t.pesoKg} kg y {t.bultos} bultos
              {!t.cuadra &&
                ' · El total de las partidas no coincide con el peso o los bultos declarados; revise la captura.'}
            </p>
          );
        })}
      <Button
        variant="outline"
        onClick={() => {
          void packing();
        }}
        disabled={generando}
        title={generando ? 'Generando packing list.' : undefined}
      >
        Descargar packing list PDF
      </Button>
      {autorizar && (
        <div className="space-y-3">
          <Label htmlFor="ce-salida-motivo">Motivo del rechazo</Label>
          <Input
            id="ce-salida-motivo"
            value={motivo}
            maxLength={500}
            onChange={(e) => setMotivo(e.target.value)}
          />
          <div className="flex gap-2">
            <Button
              disabled={mutar.isPending}
              title={mutar.isPending ? 'Guardando autorización.' : undefined}
              onClick={() => {
                void mutar.mutateAsync({ ruta: 'autorizar-salida' }).catch(() => {});
              }}
            >
              Autorizar salida
            </Button>
            <Button
              variant="outline"
              disabled={mutar.isPending || motivo.trim().length < 5}
              title={
                motivo.trim().length < 5
                  ? 'Capture un motivo de al menos cinco caracteres.'
                  : mutar.isPending
                    ? 'Guardando rechazo.'
                    : undefined
              }
              onClick={() => {
                void mutar
                  .mutateAsync({ ruta: 'rechazar-salida', body: { motivo } })
                  .catch(() => {});
              }}
            >
              Rechazar salida
            </Button>
          </div>
        </div>
      )}
      {(d.autorizaciones ?? []).map((a, i) => (
        <p key={i} className="text-xs text-ink-muted">
          Versión {a.versionEmbarque} · {a.autorizada ? 'Autorizada' : 'Rechazada'} ·{' '}
          {new Date(a.fecha).toLocaleString('es-MX')}
          {a.motivo && ` · ${a.motivo}`}
        </p>
      ))}
      {mutar.isError && (
        <p role="alert" className="text-danger-fg">
          {esApiError(mutar.error)
            ? (mutar.error.problem.detail ?? mutar.error.problem.title)
            : 'No se pudo registrar la autorización.'}
        </p>
      )}
      {errorPdf && (
        <p role="alert" className="text-danger-fg">
          {errorPdf}
        </p>
      )}
    </section>
  );
}

export function PackingPartida({ id, p }: { id: string; p: PartidaEmbarcada }) {
  const mutar = useMutarContenedor(id);
  const [peso, setPeso] = useState(p.pesoKg == null ? '' : String(p.pesoKg));
  const [bultos, setBultos] = useState(p.bultos == null ? '' : String(p.bultos));
  return (
    <form
      className="space-y-2"
      onSubmit={(e) => {
        e.preventDefault();
        void mutar
          .mutateAsync({
            ruta: `partidas/${p.id}/packing`,
            method: 'PUT',
            body: { pesoKg: Number(peso), bultos: Number(bultos) },
          })
          .catch(() => {});
      }}
    >
      <FormGrid>
        <FormCampo span={6}>
          <Label htmlFor={`peso-${p.id}`}>Peso (kg) *</Label>
          <Input
            id={`peso-${p.id}`}
            type="number"
            min="0"
            step="0.000001"
            required
            value={peso}
            onChange={(e) => setPeso(e.target.value)}
          />
        </FormCampo>
        <FormCampo span={6}>
          <Label htmlFor={`bultos-${p.id}`}>Bultos *</Label>
          <Input
            id={`bultos-${p.id}`}
            type="number"
            min="0"
            step="1"
            required
            value={bultos}
            onChange={(e) => setBultos(e.target.value)}
          />
        </FormCampo>
      </FormGrid>
      <Button
        size="sm"
        variant="outline"
        type="submit"
        disabled={mutar.isPending}
        title={mutar.isPending ? 'Guardando packing.' : undefined}
      >
        Guardar packing de partida
      </Button>
      {mutar.isError && (
        <p role="alert" className="text-danger-fg">
          {esApiError(mutar.error)
            ? (mutar.error.problem.detail ?? mutar.error.problem.title)
            : 'No se pudo guardar el packing de la partida.'}
        </p>
      )}
    </form>
  );
}

export function AduanalesPartida({
  id,
  p,
  abierto,
}: {
  id: string;
  p: PartidaEmbarcada;
  abierto: boolean;
}) {
  const mutar = useMutarContenedor(id);
  const permiso = useHasPermission(P.ComercioExteriorExpedientesDocumentosRevisar) && abierto;
  const [fraccion, setFraccion] = useState(p.fraccionArancelaria ?? '');
  const [unidad, setUnidad] = useState(p.unidadAduana ?? '');
  return (
    <form
      className="space-y-2"
      onSubmit={(e) => {
        e.preventDefault();
        void mutar
          .mutateAsync({
            ruta: `partidas/${p.id}/aduanales`,
            method: 'PUT',
            body: { fraccionArancelaria: fraccion, unidadAduana: unidad, revisar: true },
          })
          .catch(() => {});
      }}
    >
      <p>
        {p.aduanalesRevisadoPorId
          ? 'Datos aduanales revisados'
          : 'Datos aduanales pendientes de revisión'}
      </p>
      <Label htmlFor={`fraccion-${p.id}`}>Fracción arancelaria *</Label>
      <Input
        id={`fraccion-${p.id}`}
        required
        maxLength={20}
        disabled={!permiso}
        value={fraccion}
        onChange={(e) => setFraccion(e.target.value)}
      />
      <Label htmlFor={`unidad-aduana-${p.id}`}>Unidad aduana *</Label>
      <Input
        id={`unidad-aduana-${p.id}`}
        required
        maxLength={20}
        disabled={!permiso}
        value={unidad}
        onChange={(e) => setUnidad(e.target.value)}
      />
      {permiso && (
        <Button
          size="sm"
          variant="outline"
          type="submit"
          disabled={mutar.isPending}
          title={mutar.isPending ? 'Guardando revisión.' : undefined}
        >
          Confirmar datos aduanales revisados
        </Button>
      )}
      {mutar.isError && (
        <p role="alert" className="text-danger-fg">
          {esApiError(mutar.error)
            ? (mutar.error.problem.detail ?? mutar.error.problem.title)
            : 'No se pudieron revisar los datos aduanales.'}
        </p>
      )}
    </form>
  );
}
