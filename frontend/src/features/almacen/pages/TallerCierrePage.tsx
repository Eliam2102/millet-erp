import { errorUsuario } from '@/lib/error-usuario';
import { useMemo, useState } from 'react';
import { CheckCircle2, Pencil } from 'lucide-react';
import { toast } from 'sonner';
import { PageHeader } from '@/components/erp/PageHeader';
import { KpiCard } from '@/components/erp/display/KpiCard';
import { AvisoEnLinea } from '@/components/erp/feedback/AvisoEnLinea';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Field } from '@/features/almacen/components/internal/Field';
import {
  useGuardarCierreTaller,
  usePrellenadoCierreTaller,
  type CapturaCierreTaller,
  type PrellenadoCierreTaller,
} from '@/features/almacen/api/useTaller';
import { useSucursales } from '@/features/catalogos/api';
import { EstatusCatalogo } from '@/features/compras/api/types';
import { TipoSucursal } from '@/features/catalogos/api/types';
import { codigoSoporte, esApiError } from '@/lib/api';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { formatCantidad } from '@/lib/format-cantidad';

const hoyLocal = () => new Date().toLocaleDateString('en-CA'); // AAAA-MM-DD

const aNumero = (texto: string) => (texto.trim() === '' ? NaN : Number(texto));

/**
 * PT5.10 — Taller de sucursal: cierre de día del almacenista. Muestra lo que bajó del inventario del taller por los
 * pedidos internos Serie 9000 (no facturables), prellena hojas y pedidos servidos, y calcula el factor de optimización
 * = m² descargados ÷ m² cortados. Cerrado el día no se edita; solo quien administra almacenes lo corrige.
 * Permisos: leer <c>almacen.salidas.leer-todas</c>, cerrar <c>almacen.salidas.capturar</c>, corregir
 * <c>almacen.almacenes.administrar</c>.
 */
export function TallerCierrePage() {
  const sucursalesQuery = useSucursales();
  const talleres = useMemo(
    () =>
      (sucursalesQuery.data?.items ?? []).filter(
        (s) => s.tipo === TipoSucursal.Taller && s.estatus === EstatusCatalogo.Activo,
      ),
    [sucursalesQuery.data],
  );
  const [sucursalId, setSucursalId] = useState<string | null>(null);
  const [fecha, setFecha] = useState(hoyLocal);
  const sucursalActiva = sucursalId ?? talleres[0]?.id ?? null;
  const prellenado = usePrellenadoCierreTaller(sucursalActiva, fecha);
  const data = prellenado.data;

  return (
    <div className="space-y-6">
      <PageHeader
        titulo="Taller de sucursal"
        descripcion="Registra el cierre diario del taller con las hojas descargadas por pedidos internos Serie 9000, que no se facturan."
      />

      <section className="grid gap-4 rounded-lg border border-line-row bg-surface-card p-4 sm:grid-cols-2 lg:max-w-2xl">
        <Field label="Taller" required>
          <Select value={sucursalActiva ?? ''} onValueChange={setSucursalId}>
            <SelectTrigger aria-label="Taller">
              <SelectValue
                placeholder={talleres.length ? 'Elige el taller' : 'Sin talleres activos'}
              />
            </SelectTrigger>
            <SelectContent>
              {talleres.map((s) => (
                <SelectItem key={s.id} value={s.id}>
                  {s.clave} · {s.nombre}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </Field>
        <Field label="Fecha del cierre" required>
          <Input
            type="date"
            value={fecha}
            max={hoyLocal()}
            onChange={(e) => setFecha(e.target.value)}
          />
        </Field>
      </section>

      {prellenado.isError && (
        <AvisoEnLinea tono="error" titulo="No se pudo leer el taller">
          Recarga la página. Si el problema continúa, avisa a soporte.
        </AvisoEnLinea>
      )}

      {data && (
        <>
          <section
            aria-label="Descargado por Serie 9000"
            className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4"
          >
            <KpiCard etiqueta="Pedidos Serie 9000" valor={data.pedidosSerie9000} tono="info" />
            <KpiCard
              etiqueta="Hojas descargadas"
              valor={formatCantidad(data.hojasDescargadas, 0)}
              tono="info"
            />
            <KpiCard
              etiqueta="m² descargados"
              valor={formatCantidad(data.m2Descargados, 4)}
              tono="info"
            />
            <KpiCard
              etiqueta="En revisión"
              valor={data.enRevision}
              tono={data.enRevision > 0 ? 'warning' : 'success'}
              subdato={data.enRevision > 0 ? 'sin existencia o sin medida' : 'todo descargado'}
            />
          </section>

          <section className="overflow-hidden rounded-lg border border-line-row bg-surface-card">
            <div className="border-b border-line-row px-4 py-3">
              <h2 className="text-sm font-semibold text-ink-strong">
                Descargas del día por pedido interno
              </h2>
            </div>
            {data.descargas.length === 0 ? (
              <p className="px-4 py-6 text-sm text-ink-muted">
                Este taller no tiene pedidos Serie 9000 en la fecha elegida.
              </p>
            ) : (
              <table className="w-full text-sm">
                <thead className="bg-surface-muted text-left text-xs text-ink-muted">
                  <tr>
                    <th className="px-4 py-2 font-medium">Pedido</th>
                    <th className="px-4 py-2 font-medium">Pos.</th>
                    <th className="px-4 py-2 font-medium">Artículo A+W</th>
                    <th className="px-4 py-2 font-medium">Medida (mm)</th>
                    <th className="px-4 py-2 text-right font-medium">Hojas</th>
                    <th className="px-4 py-2 text-right font-medium">m²</th>
                    <th className="px-4 py-2 font-medium">Estado</th>
                    <th className="px-4 py-2 font-medium">Salida</th>
                  </tr>
                </thead>
                <tbody>
                  {data.descargas.map((d) => (
                    <tr
                      key={`${d.numeroPedido}-${d.posicion}`}
                      className="border-t border-line-row"
                    >
                      <td className="px-4 py-2 font-mono">{d.numeroPedido}</td>
                      <td className="px-4 py-2 tabular-nums">{d.posicion}</td>
                      <td className="px-4 py-2 font-mono">{d.articuloRef}</td>
                      <td className="px-4 py-2 tabular-nums">
                        {d.anchoMm && d.altoMm ? `${d.anchoMm} × ${d.altoMm}` : '—'}
                      </td>
                      <td className="px-4 py-2 text-right tabular-nums">
                        {formatCantidad(d.cantidad, 0)}
                      </td>
                      <td className="px-4 py-2 text-right tabular-nums">
                        {formatCantidad(d.m2, 4)}
                      </td>
                      <td className="px-4 py-2">
                        {d.estado === 'RECIBIDO' ? (
                          <Badge variant="success">Descargado</Badge>
                        ) : (
                          <span title="Revisa el pedido en A+W y vuelve a procesar la descarga.">
                            <Badge variant="warning">En revisión</Badge>
                          </span>
                        )}
                      </td>
                      <td className="px-4 py-2 font-mono text-xs">{d.folio ?? '—'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </section>

          <CierreForm
            key={`${data.sucursalId}-${data.fecha}-${data.cierre?.id ?? 'nuevo'}-${data.cierre?.corregidoAt ?? ''}`}
            data={data}
            sucursalId={sucursalActiva!}
            fecha={fecha}
          />
        </>
      )}
    </div>
  );
}

/** Formulario del cierre del día. Se remonta (key) al cambiar de taller, fecha o cierre, así el prellenado no usa efectos. */
function CierreForm({
  data,
  sucursalId,
  fecha,
}: {
  data: PrellenadoCierreTaller;
  sucursalId: string;
  fecha: string;
}) {
  const puedeCerrar = useHasPermission(PermisosCanonicos.AlmacenSalidasCapturar);
  const puedeCorregir = useHasPermission(PermisosCanonicos.AlmacenAlmacenesAdministrar);
  const guardar = useGuardarCierreTaller();
  const cierre = data.cierre;
  const m2 = (n: number | null | undefined) => formatCantidad(n, 4);
  const [hojas, setHojas] = useState(String(cierre ? cierre.hojasTomadas : data.hojasDescargadas));
  const [retazos, setRetazos] = useState(String(cierre ? cierre.retazosTomados : 0));
  const [m2Cortados, setM2Cortados] = useState(cierre ? String(cierre.m2Cortados) : '');
  const [servidos, setServidos] = useState(
    String(cierre ? cierre.pedidosServidos : data.pedidosServidos),
  );
  const [cortadores, setCortadores] = useState(cierre ? String(cierre.cortadores) : '');
  const [notas, setNotas] = useState(cierre?.notas ?? '');
  const [corrigiendo, setCorrigiendo] = useState(false);

  const captura: CapturaCierreTaller = {
    hojasTomadas: aNumero(hojas),
    retazosTomados: aNumero(retazos),
    m2Cortados: aNumero(m2Cortados),
    pedidosServidos: aNumero(servidos),
    cortadores: aNumero(cortadores),
    notas: notas.trim() || null,
  };
  const incompleto = [
    captura.hojasTomadas,
    captura.retazosTomados,
    captura.m2Cortados,
    captura.pedidosServidos,
    captura.cortadores,
  ].some((n) => !Number.isFinite(n) || n < 0);
  const factorVista =
    data && Number.isFinite(captura.m2Cortados) && captura.m2Cortados > 0
      ? data.m2Descargados / captura.m2Cortados
      : null;
  const editable = !cierre || corrigiendo;

  function enviar() {
    if (!sucursalId) return;
    guardar.mutate(
      { sucursalId: sucursalId, fecha, cierreId: corrigiendo ? cierre?.id : undefined, captura },
      {
        onSuccess: () => toast.success(corrigiendo ? 'Cierre corregido' : 'Día cerrado'),
        onError: (error) =>
          esApiError(error)
            ? toast.error(errorUsuario(error), {
                description: codigoSoporte(error),
              })
            : toast.error("No se pudo guardar el cierre. Revisa los datos del registro; si continúa, avisa a soporte."),
      },
    );
  }

  return (
    <section
      aria-labelledby="cierre-taller-titulo"
      className="rounded-lg border border-line-row bg-surface-card p-4"
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 id="cierre-taller-titulo" className="text-sm font-semibold text-ink-strong">
          Cierre del día
        </h2>
        {cierre && <Badge variant="success">Cerrado</Badge>}
      </div>
      <div className="mt-3 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        <Field label="Hojas tomadas" required hint="Prellenado con lo descargado por Serie 9000.">
          <Input
            type="number"
            min={0}
            step="any"
            value={hojas}
            disabled={!editable}
            onChange={(e) => setHojas(e.target.value)}
          />
        </Field>
        <Field label="Retazos tomados" required hint="Indica cuántos retazos se usaron.">
          <Input
            type="number"
            min={0}
            step="any"
            value={retazos}
            disabled={!editable}
            onChange={(e) => setRetazos(e.target.value)}
          />
        </Field>
        <Field
          label="m² cortados en el día"
          required
          hint="Vendidos o cortados; si Facturación no trae el m² por partida, se captura aquí."
        >
          <Input
            type="number"
            min={0}
            step="any"
            value={m2Cortados}
            disabled={!editable}
            onChange={(e) => setM2Cortados(e.target.value)}
          />
        </Field>
        <Field
          label="Pedidos servidos"
          required
          hint="Prellenado con los pedidos de taller (estatus 15) del día."
        >
          <Input
            type="number"
            min={0}
            step={1}
            value={servidos}
            disabled={!editable}
            onChange={(e) => setServidos(e.target.value)}
          />
        </Field>
        <Field label="Cortadores" required hint="Cuántos cortadores trabajaron ese día.">
          <Input
            type="number"
            min={0}
            step={1}
            value={cortadores}
            disabled={!editable}
            onChange={(e) => setCortadores(e.target.value)}
          />
        </Field>
        <Field label="Notas" optional>
          <Input
            value={notas}
            maxLength={500}
            disabled={!editable}
            onChange={(e) => setNotas(e.target.value)}
          />
        </Field>
      </div>

      <div
        className="mt-4 rounded-md bg-surface-muted p-3 text-sm"
        data-component="factor-optimizacion"
      >
        <p className="text-xs text-ink-muted">
          Factor de optimización = m² descargados por Serie 9000 ÷ m² cortados en el día
        </p>
        <p className="mt-1 tabular-nums text-ink-strong">
          {m2(cierre && !corrigiendo ? cierre.m2Descargados : data.m2Descargados)} ÷{' '}
          {cierre && !corrigiendo
            ? m2(cierre.m2Cortados)
            : Number.isFinite(captura.m2Cortados)
              ? m2(captura.m2Cortados)
              : '—'}{' '}
          ={' '}
          <strong className="text-base">
            {(() => {
              const f = cierre && !corrigiendo ? cierre.factorOptimizacion : factorVista;
              return f == null ? 'sin m² cortados' : formatCantidad(f, 4);
            })()}
          </strong>
        </p>
      </div>

      <div className="mt-4 flex flex-wrap items-center gap-3">
        {!cierre && (
          <Button onClick={enviar} disabled={!puedeCerrar || incompleto || guardar.isPending}>
            <CheckCircle2 className="mr-2 size-3.5" />
            Cerrar día
          </Button>
        )}
        {cierre && !corrigiendo && puedeCorregir && (
          <Button variant="outline" onClick={() => setCorrigiendo(true)}>
            <Pencil className="mr-2 size-3.5" />
            Corregir cierre
          </Button>
        )}
        {cierre && corrigiendo && (
          <>
            <Button onClick={enviar} disabled={incompleto || guardar.isPending}>
              Guardar corrección
            </Button>
            <Button variant="ghost" onClick={() => setCorrigiendo(false)}>
              Cancelar
            </Button>
          </>
        )}
        {!cierre && !puedeCerrar && (
          <p className="text-xs text-ink-muted">Tu rol no puede cerrar el día del taller.</p>
        )}
        {cierre && !corrigiendo && (
          <p className="text-xs text-ink-muted">
            Cerrado el {new Date(cierre.cerradoAt).toLocaleString('es-MX')}
            {puedeCorregir ? '' : '. Solo quien administra almacenes puede corregirlo'}.
          </p>
        )}
      </div>
    </section>
  );
}
