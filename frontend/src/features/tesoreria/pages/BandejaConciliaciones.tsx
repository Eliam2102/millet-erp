import { useState, useMemo } from 'react';
import {
  Scale,
  Plus,
  UploadCloud,
  RefreshCw,
  CheckCircle2,
  AlertCircle,
  Unlink,
  Link2,
  FileDown,
  Lock,
  Unlock,
  Clock,
} from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Textarea } from '@/components/ui/textarea';
import { Checkbox } from '@/components/ui/checkbox';
import { Badge } from '@/components/ui/badge';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import {
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
} from '@/components/ui/tabs';
import {
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { PageHeader } from '@/components/erp/PageHeader';
import { EmptyState, TableSkeleton } from '@/components/erp';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import {
  useCuentasBancarias,
  useConceptos,
  useConciliaciones,
  useConciliacionDetalle,
  useCrearConciliacion,
  useCargarExtracto,
  useEmparejarAutomatico,
  useEmparejarManual,
  useDesemparejarLinea,
  useAltaAsistida,
  useMarcarPartidaTransito,
  useCerrarConciliacion,
  useReabrirConciliacion,
  descargarActaConciliacion,
} from '@/features/tesoreria/api/useTesoreria';
import {
  EstadoConciliacion,
  EstadoExtractoLinea,
  PartidaTransitoTipo,
  ESTADO_CONCILIACION_HEADER_LABELS,
  ESTADO_EXTRACTO_LINEA_LABELS,
  PARTIDA_TRANSITO_LABELS,
  type ExtractoLineaResponse,
} from '@/features/tesoreria/api/types';

const MESES = [
  { valor: 1, label: 'Enero' },
  { valor: 2, label: 'Febrero' },
  { valor: 3, label: 'Marzo' },
  { valor: 4, label: 'Abril' },
  { valor: 5, label: 'Mayo' },
  { valor: 6, label: 'Junio' },
  { valor: 7, label: 'Julio' },
  { valor: 8, label: 'Agosto' },
  { valor: 9, label: 'Septiembre' },
  { valor: 10, label: 'Octubre' },
  { valor: 11, label: 'Noviembre' },
  { valor: 12, label: 'Diciembre' },
];

const ANIOS = [2024, 2025, 2026, 2027];

export function BandejaConciliaciones() {
  const ahora = new Date();
  const [cuentaId, setCuentaId] = useState<string>('');
  const [anio, setAnio] = useState<number>(ahora.getFullYear());
  const [mes, setMes] = useState<number>(ahora.getMonth() + 1);
  const [conciliacionSeleccionadaId, setConciliacionSeleccionadaId] = useState<string | null>(null);

  // Permisos
  const puedeOperar = useHasPermission(PermisosCanonicos.TesoreriaConciliacionOperar);
  const puedeCerrar = useHasPermission(PermisosCanonicos.TesoreriaConciliacionCerrar);

  // Queries
  const cuentasQuery = useCuentasBancarias(true);
  const conceptosQuery = useConceptos();
  const conciliacionesQuery = useConciliaciones({
    cuentaBancariaId: cuentaId || undefined,
    anio,
    mes,
  });

  // Preseleccionar primera cuenta si no hay ninguna
  if (!cuentaId && cuentasQuery.data && cuentasQuery.data.length > 0) {
    setCuentaId(cuentasQuery.data[0].id);
  }

  // Preseleccionar conciliación del periodo si existe
  const conciliaciones = conciliacionesQuery.data ?? [];
  const conciliacionActivaResumen = useMemo(() => {
    if (conciliacionSeleccionadaId) {
      return conciliaciones.find((c) => c.id === conciliacionSeleccionadaId) ?? conciliaciones[0] ?? null;
    }
    return conciliaciones[0] ?? null;
  }, [conciliaciones, conciliacionSeleccionadaId]);

  const activoId = conciliacionActivaResumen?.id ?? null;
  const detalleQuery = useConciliacionDetalle(activoId ?? '', !!activoId);
  const conciliacion = detalleQuery.data ?? null;

  // Modales
  const [modalNueva, setModalNueva] = useState(false);
  const [modalCargarExtracto, setModalCargarExtracto] = useState(false);
  const [modalAutoEmparejar, setModalAutoEmparejar] = useState(false);
  const [modalAltaAsistida, setModalAltaAsistida] = useState<ExtractoLineaResponse | null>(null);
  const [modalDesemparejar, setModalDesemparejar] = useState<ExtractoLineaResponse | null>(null);
  const [modalPartidaTransito, setModalPartidaTransito] = useState<ExtractoLineaResponse | null>(null);
  const [modalCerrar, setModalCerrar] = useState(false);
  const [modalReabrir, setModalReabrir] = useState(false);

  // Selección manual para matching
  const [lineaExtractoSeleccionadaId, setLineaExtractoSeleccionadaId] = useState<string | null>(null);
  const [movimientosSeleccionadosIds, setMovimientosSeleccionadosIds] = useState<string[]>([]);
  const [filtroEstadoLinea, setFiltroEstadoLinea] = useState<string>('todos');

  // Mutations
  const crearMutation = useCrearConciliacion();
  const cargarExtractoMutation = useCargarExtracto();
  const autoEmparejarMutation = useEmparejarAutomatico();
  const emparejarManualMutation = useEmparejarManual();
  const desemparejarMutation = useDesemparejarLinea();
  const altaAsistidaMutation = useAltaAsistida();
  const transitoMutation = useMarcarPartidaTransito();
  const cerrarMutation = useCerrarConciliacion();
  const reabrirMutation = useReabrirConciliacion();

  // Estados de formularios
  const [nuevoSaldoExtracto, setNuevoSaldoExtracto] = useState<string>('0.00');
  const [contenidoExtracto, setContenidoExtracto] = useState<string>('');
  const [formatoExtracto, setFormatoExtracto] = useState<string>('BBVA');
  const [saldoFinalExtracto, setSaldoFinalExtracto] = useState<string>('');
  const [autoEmparejarAlCargar, setAutoEmparejarAlCargar] = useState<boolean>(true);
  const [toleranciaImporte, setToleranciaImporte] = useState<string>('1.00');
  const [ventanaDias, setVentanaDias] = useState<string>('3');
  const [motivoDesemparejar, setMotivoDesemparejar] = useState<string>('');
  const [motivoReabrir, setMotivoReabrir] = useState<string>('');
  const [firmanteNombre, setFirmanteNombre] = useState<string>('Gerente de Tesorería');
  const [firmantePuesto, setFirmantePuesto] = useState<string>('Gerente de Tesorería');

  // Formulario alta asistida
  const [altaConceptoId, setAltaConceptoId] = useState<string>('');
  const [altaIncluyeIva, setAltaIncluyeIva] = useState<boolean>(true);
  const [altaConceptoIvaId, setAltaConceptoIvaId] = useState<string>('');
  const [altaMontoBase, setAltaMontoBase] = useState<string>('');
  const [altaMontoIva, setAltaMontoIva] = useState<string>('');

  // Formulario partida en tránsito
  const [transitoTipo, setTransitoTipo] = useState<PartidaTransitoTipo>(PartidaTransitoTipo.ChequeNoCobrado);
  const [transitoResponsable, setTransitoResponsable] = useState<string>('');

  // Manejo de apertura de alta asistida
  const abrirAltaAsistida = (linea: ExtractoLineaResponse) => {
    setModalAltaAsistida(linea);
    const montoAbs = Math.abs(linea.monto);
    if (linea.sentido === 2) {
      // Cargo -> sugerir comisión
      // Buscar concepto de comisión bancaria
      const cCom = conceptosQuery.data?.find((c) => c.nombre.toLowerCase().includes('comisi'));
      const cIva = conceptosQuery.data?.find((c) => c.nombre.toLowerCase().includes('impuesto') || c.nombre.toLowerCase().includes('iva'));
      if (cCom) setAltaConceptoId(cCom.id);
      if (cIva) setAltaConceptoIvaId(cIva.id);
      setAltaIncluyeIva(true);
      const base = Math.round((montoAbs / 1.16) * 100) / 100;
      const iva = Math.round((montoAbs - base) * 100) / 100;
      setAltaMontoBase(base.toFixed(2));
      setAltaMontoIva(iva.toFixed(2));
    } else {
      // Abono -> sugerir interés o depósito
      const cInt = conceptosQuery.data?.find((c) => c.nombre.toLowerCase().includes('inter') || c.nombre.toLowerCase().includes('rend'));
      if (cInt) setAltaConceptoId(cInt.id);
      setAltaIncluyeIva(false);
      setAltaMontoBase(montoAbs.toFixed(2));
      setAltaMontoIva('0.00');
    }
  };

  const handleAltaAsistidaSubmit = async () => {
    if (!conciliacion || !modalAltaAsistida || !altaConceptoId) {
      toast.error('Selecciona el concepto para registrar el movimiento');
      return;
    }
    try {
      await altaAsistidaMutation.mutateAsync({
        id: conciliacion.id,
        extractoLineaId: modalAltaAsistida.id,
        conceptoId: altaConceptoId,
        incluyeIva: altaIncluyeIva,
        tasaIva: 0.16,
        conceptoIvaId: altaIncluyeIva ? altaConceptoIvaId : undefined,
        montoBase: parseFloat(altaMontoBase),
        montoIva: altaIncluyeIva ? parseFloat(altaMontoIva) : undefined,
      });
      toast.success('Movimiento registrado y emparejado con éxito (póliza emitida)');
      setModalAltaAsistida(null);
    } catch (err: unknown) {
      toast.error(esApiError(err) ? err.problem.detail : 'Error al registrar alta asistida');
    }
  };

  // Manejo de desemparejar
  const handleDesemparejarSubmit = async () => {
    if (!conciliacion || !modalDesemparejar || !motivoDesemparejar.trim()) {
      toast.error('El motivo de desemparejamiento es obligatorio para la bitácora');
      return;
    }
    try {
      await desemparejarMutation.mutateAsync({
        id: conciliacion.id,
        extractoLineaId: modalDesemparejar.id,
        motivo: motivoDesemparejar.trim(),
      });
      toast.success('Línea desemparejada y registrada en bitácora de auditoría');
      setModalDesemparejar(null);
      setMotivoDesemparejar('');
    } catch (err: unknown) {
      toast.error(esApiError(err) ? err.problem.detail : 'Error al desemparejar');
    }
  };

  // Manejo de partida en tránsito
  const handlePartidaTransitoSubmit = async () => {
    if (!conciliacion || !modalPartidaTransito || !transitoResponsable.trim()) {
      toast.error('El responsable de la partida en tránsito es obligatorio');
      return;
    }
    try {
      await transitoMutation.mutateAsync({
        id: conciliacion.id,
        extractoLineaId: modalPartidaTransito.id,
        tipo: transitoTipo,
        responsable: transitoResponsable.trim(),
      });
      toast.success('Partida marcada en tránsito');
      setModalPartidaTransito(null);
      setTransitoResponsable('');
    } catch (err: unknown) {
      toast.error(esApiError(err) ? err.problem.detail : 'Error al marcar en tránsito');
    }
  };

  // Emparejamiento manual
  const lineaSeleccionada = useMemo(() => {
    if (!conciliacion || !lineaExtractoSeleccionadaId) return null;
    return conciliacion.lineas.find((l) => l.id === lineaExtractoSeleccionadaId) ?? null;
  }, [conciliacion, lineaExtractoSeleccionadaId]);

  const movimientosSeleccionados = useMemo(() => {
    if (!conciliacion) return [];
    return conciliacion.movimientosPeriodo.filter((m) => movimientosSeleccionadosIds.includes(m.id));
  }, [conciliacion, movimientosSeleccionadosIds]);

  const totalMovimientosSeleccionados = useMemo(() => {
    return movimientosSeleccionados.reduce((sum, m) => sum + (m.sentido === 1 ? m.monto : -m.monto), 0);
  }, [movimientosSeleccionados]);

  const handleEmparejarManualSubmit = async () => {
    if (!conciliacion || !lineaSeleccionada || movimientosSeleccionadosIds.length === 0) {
      toast.error('Selecciona una línea de extracto y al menos un movimiento contable');
      return;
    }
    try {
      await emparejarManualMutation.mutateAsync({
        id: conciliacion.id,
        extractoLineaId: lineaSeleccionada.id,
        movimientoIds: movimientosSeleccionadosIds,
      });
      toast.success('Emparejamiento manual realizado correctamente');
      setLineaExtractoSeleccionadaId(null);
      setMovimientosSeleccionadosIds([]);
    } catch (err: unknown) {
      toast.error(esApiError(err) ? err.problem.detail : 'Error al realizar emparejamiento manual');
    }
  };

  // Emparejamiento automático
  const handleAutoEmparejarSubmit = async () => {
    if (!conciliacion) return;
    try {
      const res = await autoEmparejarMutation.mutateAsync({
        id: conciliacion.id,
        toleranciaImporte: parseFloat(toleranciaImporte) || 1.0,
        ventanaDias: parseInt(ventanaDias, 10) || 3,
      });
      toast.success(
        `Emparejamiento completado: ${res.totalEmparejadas} emparejadas (P1: ${res.paso1Emparejadas}, P2: ${res.paso2Emparejadas}, P3: ${res.paso3Emparejadas}). En revisión: ${res.enRevision}. Pendientes: ${res.pendientes}.`
      );
      setModalAutoEmparejar(false);
    } catch (err: unknown) {
      toast.error(esApiError(err) ? err.problem.detail : 'Error en emparejamiento automático');
    }
  };

  // Cierre
  const handleCerrarSubmit = async () => {
    if (!conciliacion) return;
    if (conciliacion.diferencia !== 0) {
      toast.error('La conciliación solo puede cerrarse con diferencia $0.00 (RN-7)');
      return;
    }
    if (!firmanteNombre.trim() || !firmantePuesto.trim()) {
      toast.error('El nombre y puesto del firmante son obligatorios');
      return;
    }
    try {
      await cerrarMutation.mutateAsync({
        id: conciliacion.id,
        firmanteNombre: firmanteNombre.trim(),
        firmantePuesto: firmantePuesto.trim(),
      });
      toast.success('Conciliación cerrada con éxito. Descargando acta oficial en PDF...');
      setModalCerrar(false);
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : 'Error al cerrar conciliación');
    }
  };

  // Reapertura
  const handleReabrirSubmit = async () => {
    if (!conciliacion || !motivoReabrir.trim()) {
      toast.error('El motivo de reapertura es obligatorio');
      return;
    }
    try {
      await reabrirMutation.mutateAsync({
        id: conciliacion.id,
        motivo: motivoReabrir.trim(),
      });
      toast.success('Conciliación reabierta');
      setModalReabrir(false);
      setMotivoReabrir('');
    } catch (err: unknown) {
      toast.error(esApiError(err) ? err.problem.detail : 'Error al reabrir conciliación');
    }
  };

  // Filtrado de líneas
  const lineasFiltradas = useMemo(() => {
    if (!conciliacion) return [];
    if (filtroEstadoLinea === 'todos') return conciliacion.lineas;
    const est = parseInt(filtroEstadoLinea, 10);
    return conciliacion.lineas.filter((l) => l.estado === est);
  }, [conciliacion, filtroEstadoLinea]);

  return (
    <div className="flex h-full min-h-0 flex-col gap-5 text-sm text-ink p-6 overflow-y-auto">
      <PageHeader
        titulo="Conciliación bancaria"
        descripcion="Cruce mensual de extracto BBVA ↔ movimientos internos por cuenta con emparejamiento multinivel en 3 pasos, partidas en tránsito, alta asistida de comisiones/intereses y cierre cuadrado con acta en PDF."
        acciones={
          puedeOperar ? (
            <Button onClick={() => setModalNueva(true)} size="sm">
              <Plus className="mr-1.5 h-4 w-4" />
              Nueva conciliación
            </Button>
          ) : undefined
        }
      />

      {/* Barra de Filtros / Selector de Periodo y Cuenta */}
      <div className="grid grid-cols-1 md:grid-cols-4 gap-4 p-4 rounded-lg bg-surface border border-edge">
        <div>
          <Label className="text-xs text-ink-muted">Cuenta Bancaria</Label>
          <Select value={cuentaId} onValueChange={(val) => setCuentaId(val)}>
            <SelectTrigger className="mt-1">
              <SelectValue placeholder="Seleccione cuenta..." />
            </SelectTrigger>
            <SelectContent>
              {cuentasQuery.data?.map((c) => (
                <SelectItem key={c.id} value={c.id}>
                  {c.banco} · {c.numeroCuenta} ({c.moneda})
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        <div>
          <Label className="text-xs text-ink-muted">Año</Label>
          <Select value={anio.toString()} onValueChange={(val) => setAnio(parseInt(val, 10))}>
            <SelectTrigger className="mt-1">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {ANIOS.map((a) => (
                <SelectItem key={a} value={a.toString()}>
                  {a}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        <div>
          <Label className="text-xs text-ink-muted">Mes</Label>
          <Select value={mes.toString()} onValueChange={(val) => setMes(parseInt(val, 10))}>
            <SelectTrigger className="mt-1">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {MESES.map((m) => (
                <SelectItem key={m.valor} value={m.valor.toString()}>
                  {m.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        <div className="flex items-end">
          {conciliaciones.length > 1 && (
            <div className="w-full">
              <Label className="text-xs text-ink-muted">Conciliación Activa</Label>
              <Select
                value={activoId ?? ''}
                onValueChange={(val) => setConciliacionSeleccionadaId(val)}
              >
                <SelectTrigger className="mt-1">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {conciliaciones.map((c) => (
                    <SelectItem key={c.id} value={c.id}>
                      {c.banco} ({c.periodoAnio}/{c.periodoMes}) - {ESTADO_CONCILIACION_HEADER_LABELS[c.estado]}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          )}
        </div>
      </div>

      {/* Contenido Principal */}
      {conciliacionesQuery.isLoading || detalleQuery.isLoading ? (
        <TableSkeleton rows={6} />
      ) : !conciliacion ? (
        <EmptyState
          icon={<Scale className="h-10 w-10 text-ink-muted" />}
          title="Sin conciliación para este período"
          description="No existe una conciliación abierta para la cuenta y período seleccionados. Haz clic en 'Nueva conciliación' para abrir el período e importar el extracto."
          action={
            puedeOperar ? (
              <Button onClick={() => setModalNueva(true)} size="sm">
                <Plus className="mr-1.5 h-4 w-4" />
                Abrir período {anio}/{mes}
              </Button>
            ) : undefined
          }
        />
      ) : (
        <div className="flex flex-col gap-6">
          {/* Tarjetas de Control Financiero y Estado */}
          <div className="grid grid-cols-1 md:grid-cols-4 gap-4">
            {/* Saldo Banco */}
            <div className="p-4 rounded-lg bg-surface border border-edge flex flex-col justify-between">
              <span className="text-xs font-medium text-ink-muted uppercase tracking-wider">
                Saldo Extracto Banco
              </span>
              <div className="mt-2 text-2xl font-bold tracking-tight">
                <MoneyDisplay amount={conciliacion.saldoExtracto} currency={conciliacion.moneda} />
              </div>
              <span className="text-xs text-ink-muted mt-1">Corte fin de mes</span>
            </div>

            {/* Saldo Libros */}
            <div className="p-4 rounded-lg bg-surface border border-edge flex flex-col justify-between">
              <span className="text-xs font-medium text-ink-muted uppercase tracking-wider">
                Saldo Libros (ERP)
              </span>
              <div className="mt-2 text-2xl font-bold tracking-tight">
                <MoneyDisplay amount={conciliacion.saldoLibros} currency={conciliacion.moneda} />
              </div>
              <span className="text-xs text-ink-muted mt-1">Movimientos conciliados</span>
            </div>

            {/* Diferencia */}
            <div
              className={`p-4 rounded-lg border flex flex-col justify-between ${
                conciliacion.diferencia === 0
                  ? 'bg-emerald-500/10 border-emerald-500/30 text-emerald-900 dark:text-emerald-300'
                  : 'bg-rose-500/10 border-rose-500/30 text-rose-900 dark:text-rose-300'
              }`}
            >
              <div className="flex items-center justify-between">
                <span className="text-xs font-medium uppercase tracking-wider">
                  Diferencia
                </span>
                {conciliacion.diferencia === 0 ? (
                  <CheckCircle2 className="h-4 w-4 text-emerald-600 dark:text-emerald-400" />
                ) : (
                  <AlertCircle className="h-4 w-4 text-rose-600 dark:text-rose-400" />
                )}
              </div>
              <div className="mt-2 text-2xl font-bold tracking-tight">
                <MoneyDisplay amount={conciliacion.diferencia} currency={conciliacion.moneda} />
              </div>
              <span className="text-xs mt-1 font-medium">
                {conciliacion.diferencia === 0
                  ? '✓ Conciliación cuadrada ($0.00)'
                  : '⚠ Descuadre pendiente'}
              </span>
            </div>

            {/* Estado y Acciones Rápidas */}
            <div className="p-4 rounded-lg bg-surface border border-edge flex flex-col justify-between">
              <div className="flex items-center justify-between">
                <span className="text-xs font-medium text-ink-muted uppercase tracking-wider">
                  Estado
                </span>
                <Badge
                  variant={conciliacion.estado === EstadoConciliacion.Cerrada ? 'default' : 'outline'}
                  className={
                    conciliacion.estado === EstadoConciliacion.Cerrada
                      ? 'bg-emerald-600 text-white'
                      : 'border-amber-500 text-amber-600'
                  }
                >
                  {conciliacion.estado === EstadoConciliacion.Cerrada ? (
                    <Lock className="mr-1 h-3 w-3 inline" />
                  ) : (
                    <Unlock className="mr-1 h-3 w-3 inline" />
                  )}
                  {ESTADO_CONCILIACION_HEADER_LABELS[conciliacion.estado]}
                </Badge>
              </div>

              <div className="mt-2 text-xs text-ink-muted">
                {conciliacion.estado === EstadoConciliacion.Cerrada ? (
                  <div>
                    <p className="font-medium text-ink">Cerrada por: {conciliacion.firmanteNombre}</p>
                    <p>{conciliacion.firmantePuesto}</p>
                  </div>
                ) : (
                  <div>
                    <p>{conciliacion.lineas.filter((l) => l.estado === EstadoExtractoLinea.Emparejada).length} de {conciliacion.lineas.length} líneas emparejadas</p>
                    <p className="text-amber-600">
                      {conciliacion.lineas.filter((l) => l.estado === EstadoExtractoLinea.EnRevision).length} en revisión
                    </p>
                  </div>
                )}
              </div>

              {/* Botón de cierre o acta */}
              <div className="mt-3 flex gap-2">
                {conciliacion.estado === EstadoConciliacion.Abierta ? (
                  puedeCerrar && (
                    <Button
                      size="sm"
                      className="w-full"
                      disabled={conciliacion.diferencia !== 0}
                      onClick={() => setModalCerrar(true)}
                    >
                      <Lock className="mr-1.5 h-3.5 w-3.5" />
                      Cerrar con Acta
                    </Button>
                  )
                ) : (
                  <div className="flex gap-2 w-full">
                    <Button
                      variant="outline"
                      size="sm"
                      className="flex-1"
                      onClick={() => descargarActaConciliacion(conciliacion.id)}
                    >
                      <FileDown className="mr-1.5 h-3.5 w-3.5" />
                      Acta PDF
                    </Button>
                    {puedeCerrar && (
                      <Button
                        variant="secondary"
                        size="sm"
                        onClick={() => setModalReabrir(true)}
                      >
                        <Unlock className="h-3.5 w-3.5" />
                      </Button>
                    )}
                  </div>
                )}
              </div>
            </div>
          </div>

          {/* Barra de Herramientas de Operación */}
          {conciliacion.estado === EstadoConciliacion.Abierta && puedeOperar && (
            <div className="flex flex-wrap items-center justify-between gap-3 p-3 bg-surface border border-edge rounded-lg">
              <div className="flex flex-wrap items-center gap-2">
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => setModalCargarExtracto(true)}
                >
                  <UploadCloud className="mr-1.5 h-4 w-4" />
                  Cargar Extracto BBVA
                </Button>

                <Button
                  variant="secondary"
                  size="sm"
                  onClick={() => setModalAutoEmparejar(true)}
                  disabled={conciliacion.lineas.length === 0}
                >
                  <RefreshCw className="mr-1.5 h-4 w-4" />
                  Emparejamiento 3 Pasos
                </Button>
              </div>

              {lineaSeleccionada && (
                <div className="flex items-center gap-3 bg-primary/10 border border-primary/20 px-3 py-1.5 rounded-md">
                  <span className="text-xs font-medium">
                    Línea #{lineaSeleccionada.numeroLinea} seleccionada ({lineaSeleccionada.monto >= 0 ? '+' : ''}
                    {lineaSeleccionada.monto}) ↔ {movimientosSeleccionadosIds.length} movs (Total: {totalMovimientosSeleccionados.toFixed(2)})
                  </span>
                  <Button
                    size="sm"
                    disabled={movimientosSeleccionadosIds.length === 0}
                    onClick={handleEmparejarManualSubmit}
                  >
                    <Link2 className="mr-1 h-3.5 w-3.5" />
                    Emparejar Manual
                  </Button>
                  <Button
                    variant="ghost"
                    size="sm"
                    onClick={() => {
                      setLineaExtractoSeleccionadaId(null);
                      setMovimientosSeleccionadosIds([]);
                    }}
                  >
                    Cancelar
                  </Button>
                </div>
              )}
            </div>
          )}

          {/* Tabs: Workspace de Emparejamiento / Bitácora */}
          <Tabs defaultValue="matching" className="w-full">
            <TabsList>
              <TabsTrigger value="matching">Emparejamiento Extracto ↔ Libros</TabsTrigger>
              <TabsTrigger value="bitacora">
                Bitácora de Auditoría ({conciliacion.bitacora.length})
              </TabsTrigger>
            </TabsList>

            {/* TAB MATCHING: SPLIT WORKSPACE */}
            <TabsContent value="matching" className="mt-4">
              <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
                {/* COLUMNA IZQUIERDA: LÍNEAS DEL EXTRACTO BANCARIO */}
                <div className="flex flex-col gap-3 rounded-lg border border-edge bg-surface p-4">
                  <div className="flex items-center justify-between">
                    <div>
                      <h3 className="text-base font-semibold">Extracto Bancario</h3>
                      <p className="text-xs text-ink-muted">
                        Líneas reportadas por el banco ({conciliacion.lineas.length})
                      </p>
                    </div>

                    <Select value={filtroEstadoLinea} onValueChange={setFiltroEstadoLinea}>
                      <SelectTrigger className="w-36 h-8 text-xs">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="todos">Todos ({conciliacion.lineas.length})</SelectItem>
                        <SelectItem value={EstadoExtractoLinea.Pendiente.toString()}>
                          Pendientes ({conciliacion.lineas.filter((l) => l.estado === EstadoExtractoLinea.Pendiente).length})
                        </SelectItem>
                        <SelectItem value={EstadoExtractoLinea.EnRevision.toString()}>
                          En revisión ({conciliacion.lineas.filter((l) => l.estado === EstadoExtractoLinea.EnRevision).length})
                        </SelectItem>
                        <SelectItem value={EstadoExtractoLinea.Emparejada.toString()}>
                          Emparejadas ({conciliacion.lineas.filter((l) => l.estado === EstadoExtractoLinea.Emparejada).length})
                        </SelectItem>
                        <SelectItem value={EstadoExtractoLinea.EnTransito.toString()}>
                          En tránsito ({conciliacion.lineas.filter((l) => l.estado === EstadoExtractoLinea.EnTransito).length})
                        </SelectItem>
                      </SelectContent>
                    </Select>
                  </div>

                  <TableContainer className="max-h-[550px] overflow-y-auto">
                    <Table>
                      <TableHeader>
                        <TableRow>
                          <TableHead className="w-10">#</TableHead>
                          <TableHead className="w-20">Fecha</TableHead>
                          <TableHead>Concepto / Ref</TableHead>
                          <TableHead className="text-right w-24">Monto</TableHead>
                          <TableHead className="w-28 text-center">Estado</TableHead>
                          <TableHead className="w-20 text-right">Acción</TableHead>
                        </TableRow>
                      </TableHeader>
                      <TableBody>
                        {lineasFiltradas.length === 0 ? (
                          <TableRow>
                            <TableCell colSpan={6} className="text-center py-8 text-ink-muted text-xs">
                              No hay líneas con el filtro seleccionado
                            </TableCell>
                          </TableRow>
                        ) : (
                          lineasFiltradas.map((linea) => {
                            const esSeleccionada = lineaExtractoSeleccionadaId === linea.id;
                            return (
                              <TableRow
                                key={linea.id}
                                className={esSeleccionada ? 'bg-primary/5 font-medium' : undefined}
                              >
                                <TableCell className="text-xs text-ink-muted">
                                  {linea.numeroLinea}
                                </TableCell>
                                <TableCell className="text-xs whitespace-nowrap">
                                  {linea.fecha.substring(0, 10)}
                                </TableCell>
                                <TableCell className="text-xs">
                                  <div className="font-medium truncate max-w-[200px]" title={linea.descripcion}>
                                    {linea.descripcion}
                                  </div>
                                  {linea.referencia && (
                                    <div className="text-[11px] text-ink-muted truncate max-w-[200px]" title={linea.referencia}>
                                      Ref: {linea.referencia}
                                    </div>
                                  )}
                                  {linea.candidatosRevisionIds?.length > 1 && (
                                    <div className="text-[10px] text-amber-600 font-medium">
                                      ⚠ {linea.candidatosRevisionIds.length} candidatos posibles
                                    </div>
                                  )}
                                </TableCell>
                                <TableCell className="text-right text-xs whitespace-nowrap font-mono">
                                  <span className={linea.monto < 0 ? 'text-rose-600' : 'text-emerald-600'}>
                                    {linea.monto < 0 ? '-' : '+'}
                                    <MoneyDisplay amount={Math.abs(linea.monto)} currency={conciliacion.moneda} />
                                  </span>
                                </TableCell>
                                <TableCell className="text-center text-xs">
                                  <Badge
                                    variant="outline"
                                    className={`text-[10px] px-1.5 py-0.5 ${
                                      linea.estado === EstadoExtractoLinea.Emparejada
                                        ? 'border-emerald-500 text-emerald-700 bg-emerald-50 dark:bg-emerald-950/20'
                                        : linea.estado === EstadoExtractoLinea.EnRevision
                                          ? 'border-amber-500 text-amber-700 bg-amber-50 dark:bg-amber-950/20'
                                          : linea.estado === EstadoExtractoLinea.EnTransito
                                            ? 'border-blue-500 text-blue-700 bg-blue-50 dark:bg-blue-950/20'
                                            : 'border-edge text-ink-muted'
                                    }`}
                                  >
                                    {ESTADO_EXTRACTO_LINEA_LABELS[linea.estado]}
                                  </Badge>
                                </TableCell>
                                <TableCell className="text-right text-xs">
                                  {conciliacion.estado === EstadoConciliacion.Abierta && puedeOperar && (
                                    <div className="flex items-center justify-end gap-1">
                                      {linea.estado === EstadoExtractoLinea.Emparejada ? (
                                        <Button
                                          variant="ghost"
                                          size="icon"
                                          className="h-7 w-7 text-rose-600 hover:text-rose-700 hover:bg-rose-50"
                                          title="Desemparejar (requiere motivo)"
                                          onClick={() => setModalDesemparejar(linea)}
                                        >
                                          <Unlink className="h-3.5 w-3.5" />
                                        </Button>
                                      ) : (
                                        <>
                                          <Button
                                            variant="ghost"
                                            size="icon"
                                            className="h-7 w-7 text-blue-600 hover:text-blue-700 hover:bg-blue-50"
                                            title="Alta asistida (comisión / interés / depósito)"
                                            onClick={() => abrirAltaAsistida(linea)}
                                          >
                                            <Plus className="h-3.5 w-3.5" />
                                          </Button>
                                          <Button
                                            variant="ghost"
                                            size="icon"
                                            className="h-7 w-7 text-ink-muted hover:text-ink hover:bg-surface"
                                            title="Marcar en tránsito"
                                            onClick={() => {
                                              setModalPartidaTransito(linea);
                                              setTransitoResponsable('');
                                            }}
                                          >
                                            <Clock className="h-3.5 w-3.5" />
                                          </Button>
                                          <Button
                                            variant={esSeleccionada ? 'default' : 'outline'}
                                            size="icon"
                                            className="h-7 w-7"
                                            title="Seleccionar para matching manual"
                                            onClick={() => {
                                              if (esSeleccionada) {
                                                setLineaExtractoSeleccionadaId(null);
                                              } else {
                                                setLineaExtractoSeleccionadaId(linea.id);
                                              }
                                            }}
                                          >
                                            <Link2 className="h-3.5 w-3.5" />
                                          </Button>
                                        </>
                                      )}
                                    </div>
                                  )}
                                </TableCell>
                              </TableRow>
                            );
                          })
                        )}
                      </TableBody>
                    </Table>
                  </TableContainer>
                </div>

                {/* COLUMNA DERECHA: MOVIMIENTOS INTERNOS DE LIBROS */}
                <div className="flex flex-col gap-3 rounded-lg border border-edge bg-surface p-4">
                  <div className="flex items-center justify-between">
                    <div>
                      <h3 className="text-base font-semibold">Movimientos del Libro Auxiliar</h3>
                      <p className="text-xs text-ink-muted">
                        Movimientos registrados en Tesorería durante el período ({conciliacion.movimientosPeriodo.length})
                      </p>
                    </div>

                    {lineaSeleccionada && (
                      <span className="text-xs text-primary font-medium">
                        Modo emparejamiento activo
                      </span>
                    )}
                  </div>

                  <TableContainer className="max-h-[550px] overflow-y-auto">
                    <Table>
                      <TableHeader>
                        <TableRow>
                          {lineaSeleccionada && <TableHead className="w-8"></TableHead>}
                          <TableHead className="w-20">Fecha</TableHead>
                          <TableHead>Concepto / Ref</TableHead>
                          <TableHead className="text-right w-24">Importe</TableHead>
                          <TableHead className="w-24 text-center">Estado</TableHead>
                        </TableRow>
                      </TableHeader>
                      <TableBody>
                        {conciliacion.movimientosPeriodo.length === 0 ? (
                          <TableRow>
                            <TableCell colSpan={lineaSeleccionada ? 5 : 4} className="text-center py-8 text-ink-muted text-xs">
                              No hay movimientos registrados para este período
                            </TableCell>
                          </TableRow>
                        ) : (
                          conciliacion.movimientosPeriodo.map((mov) => {
                            const checked = movimientosSeleccionadosIds.includes(mov.id);
                            const esConciliado = mov.estadoConciliacion === 2; // Conciliado
                            return (
                              <TableRow
                                key={mov.id}
                                className={checked ? 'bg-primary/5 font-medium' : undefined}
                              >
                                {lineaSeleccionada && (
                                  <TableCell className="w-8 p-2">
                                    <Checkbox
                                      checked={checked}
                                      disabled={esConciliado}
                                      onCheckedChange={(val) => {
                                        if (val) {
                                          setMovimientosSeleccionadosIds((prev) => [...prev, mov.id]);
                                        } else {
                                          setMovimientosSeleccionadosIds((prev) => prev.filter((id) => id !== mov.id));
                                        }
                                      }}
                                    />
                                  </TableCell>
                                )}
                                <TableCell className="text-xs whitespace-nowrap">
                                  {mov.fechaValor.substring(0, 10)}
                                </TableCell>
                                <TableCell className="text-xs">
                                  <div className="font-medium truncate max-w-[200px]">
                                    {mov.conceptoNombre || 'Sin concepto'}
                                  </div>
                                  {mov.referenciaBancaria && (
                                    <div className="text-[11px] text-ink-muted truncate max-w-[200px]">
                                      Ref: {mov.referenciaBancaria}
                                    </div>
                                  )}
                                </TableCell>
                                <TableCell className="text-right text-xs whitespace-nowrap font-mono">
                                  <span className={mov.sentido === 2 ? 'text-rose-600' : 'text-emerald-600'}>
                                    {mov.sentido === 2 ? '-' : '+'}
                                    <MoneyDisplay amount={mov.monto} currency={mov.moneda} />
                                  </span>
                                </TableCell>
                                <TableCell className="text-center text-xs">
                                  <Badge
                                    variant="outline"
                                    className={`text-[10px] px-1.5 py-0.5 ${
                                      esConciliado
                                        ? 'border-emerald-500 text-emerald-700 bg-emerald-50 dark:bg-emerald-950/20'
                                        : 'border-edge text-ink-muted'
                                    }`}
                                  >
                                    {esConciliado ? 'Conciliado' : 'Pendiente'}
                                  </Badge>
                                </TableCell>
                              </TableRow>
                            );
                          })
                        )}
                      </TableBody>
                    </Table>
                  </TableContainer>
                </div>
              </div>
            </TabsContent>

            {/* TAB BITÁCORA */}
            <TabsContent value="bitacora" className="mt-4">
              <div className="rounded-lg border border-edge bg-surface p-4">
                <h3 className="text-base font-semibold mb-3">Bitácora de Auditoría</h3>
                {conciliacion.bitacora.length === 0 ? (
                  <p className="text-xs text-ink-muted py-6 text-center">
                    No hay registros en la bitácora aún. Todas las desemparejaciones, aperturas, cierres y reaperturas se auditan aquí.
                  </p>
                ) : (
                  <TableContainer>
                    <Table>
                      <TableHeader>
                        <TableRow>
                          <TableHead className="w-36">Fecha y Hora</TableHead>
                          <TableHead className="w-32">Acción</TableHead>
                          <TableHead>Motivo / Justificación</TableHead>
                          <TableHead className="w-40">Usuario</TableHead>
                        </TableRow>
                      </TableHeader>
                      <TableBody>
                        {conciliacion.bitacora.map((b) => (
                          <TableRow key={b.id}>
                            <TableCell className="text-xs">
                              <DateTimeDisplay value={b.fecha} variant="datetime" />
                            </TableCell>
                            <TableCell className="text-xs font-semibold">
                              {b.accion}
                            </TableCell>
                            <TableCell className="text-xs text-ink">
                              {b.motivo}
                            </TableCell>
                            <TableCell className="text-xs text-ink-muted font-mono">
                              {b.usuarioId.substring(0, 8)}...
                            </TableCell>
                          </TableRow>
                        ))}
                      </TableBody>
                    </Table>
                  </TableContainer>
                )}
              </div>
            </TabsContent>
          </Tabs>
        </div>
      )}

      {/* MODAL: NUEVA CONCILIACIÓN */}
      <Dialog open={modalNueva} onOpenChange={setModalNueva}>
        <DialogContent className="sm:max-w-[425px]">
          <DialogHeader>
            <DialogTitle>Abrir nueva conciliación mensual</DialogTitle>
            <DialogDescription>
              Inicia el período de conciliación para la cuenta bancaria seleccionada.
            </DialogDescription>
          </DialogHeader>
          <div className="grid gap-4 py-4">
            <div>
              <Label className="text-xs">Cuenta Bancaria</Label>
              <Select value={cuentaId} onValueChange={setCuentaId}>
                <SelectTrigger className="mt-1">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {cuentasQuery.data?.map((c) => (
                    <SelectItem key={c.id} value={c.id}>
                      {c.banco} · {c.numeroCuenta} ({c.moneda})
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className="grid grid-cols-2 gap-4">
              <div>
                <Label className="text-xs">Año</Label>
                <Select value={anio.toString()} onValueChange={(v) => setAnio(parseInt(v, 10))}>
                  <SelectTrigger className="mt-1">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {ANIOS.map((a) => (
                      <SelectItem key={a} value={a.toString()}>
                        {a}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              <div>
                <Label className="text-xs">Mes</Label>
                <Select value={mes.toString()} onValueChange={(v) => setMes(parseInt(v, 10))}>
                  <SelectTrigger className="mt-1">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {MESES.map((m) => (
                      <SelectItem key={m.valor} value={m.valor.toString()}>
                        {m.label}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
            </div>
            <div>
              <Label className="text-xs">Saldo Extracto Inicial (Opcional)</Label>
              <Input
                type="number"
                step="0.01"
                className="mt-1 font-mono"
                value={nuevoSaldoExtracto}
                onChange={(e) => setNuevoSaldoExtracto(e.target.value)}
              />
            </div>
          </div>
          <DialogFooter>
            <Button variant="ghost" onClick={() => setModalNueva(false)}>
              Cancelar
            </Button>
            <Button
              disabled={crearMutation.isPending || !cuentaId}
              onClick={async () => {
                try {
                  const res = await crearMutation.mutateAsync({
                    cuentaBancariaId: cuentaId,
                    periodoAnio: anio,
                    periodoMes: mes,
                    saldoExtracto: parseFloat(nuevoSaldoExtracto) || 0,
                  });
                  toast.success('Conciliación abierta con éxito');
                  setModalNueva(false);
                  setConciliacionSeleccionadaId(res.id);
                } catch (err: unknown) {
                  toast.error(esApiError(err) ? err.problem.detail : 'Error al abrir conciliación');
                }
              }}
            >
              Abrir Conciliación
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* MODAL: CARGAR EXTRACTO */}
      <Dialog open={modalCargarExtracto} onOpenChange={setModalCargarExtracto}>
        <DialogContent className="sm:max-w-[550px]">
          <DialogHeader>
            <DialogTitle>Cargar extracto bancario BBVA</DialogTitle>
            <DialogDescription>
              Pega el texto del estado de cuenta o sube un archivo CSV/TXT exportado de BBVA Bancomer.
            </DialogDescription>
          </DialogHeader>
          <div className="grid gap-4 py-4">
            <div>
              <Label className="text-xs">Formato de extracto</Label>
              <Select value={formatoExtracto} onValueChange={setFormatoExtracto}>
                <SelectTrigger className="mt-1">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="BBVA">BBVA Bancomer (CSV / Tabulado)</SelectItem>
                </SelectContent>
              </Select>
            </div>
            <div>
              <Label className="text-xs">Saldo Final del Extracto (Opcional si viene en encabezado)</Label>
              <Input
                type="number"
                step="0.01"
                placeholder="Ej. 125430.50"
                className="mt-1 font-mono"
                value={saldoFinalExtracto}
                onChange={(e) => setSaldoFinalExtracto(e.target.value)}
              />
            </div>
            <div>
              <Label className="text-xs">Contenido del extracto (Pegar texto o arrastrar archivo)</Label>
              <Textarea
                rows={8}
                placeholder="01/10/2026,PAGO PROVEEDOR,REF12345,1500.00,0.00&#10;02/10/2026,DEPOSITO CLIENTE,CLI999,0.00,25000.00"
                className="mt-1 font-mono text-xs"
                value={contenidoExtracto}
                onChange={(e) => setContenidoExtracto(e.target.value)}
              />
            </div>
            <div className="flex items-center space-x-2">
              <Checkbox
                id="autoemp"
                checked={autoEmparejarAlCargar}
                onCheckedChange={(val) => setAutoEmparejarAlCargar(!!val)}
              />
              <label htmlFor="autoemp" className="text-xs font-medium cursor-pointer">
                Ejecutar emparejamiento automático (3 pasos) al terminar de cargar
              </label>
            </div>
          </div>
          <DialogFooter>
            <Button variant="ghost" onClick={() => setModalCargarExtracto(false)}>
              Cancelar
            </Button>
            <Button
              disabled={cargarExtractoMutation.isPending || !contenidoExtracto.trim() || !conciliacion}
              onClick={async () => {
                if (!conciliacion) return;
                try {
                  await cargarExtractoMutation.mutateAsync({
                    id: conciliacion.id,
                    contenidoArchivo: contenidoExtracto.trim(),
                    formato: formatoExtracto,
                    saldoExtracto: saldoFinalExtracto ? parseFloat(saldoFinalExtracto) : undefined,
                    autoEmparejar: autoEmparejarAlCargar,
                  });
                  toast.success('Extracto cargado y procesado correctamente');
                  setModalCargarExtracto(false);
                  setContenidoExtracto('');
                } catch (err: unknown) {
                  toast.error(esApiError(err) ? err.problem.detail : 'Error al cargar extracto');
                }
              }}
            >
              Cargar y Procesar
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* MODAL: EMPAREJAMIENTO AUTOMÁTICO (3 PASOS) */}
      <Dialog open={modalAutoEmparejar} onOpenChange={setModalAutoEmparejar}>
        <DialogContent className="sm:max-w-[425px]">
          <DialogHeader>
            <DialogTitle>Emparejamiento Automático (3 Pasos)</DialogTitle>
            <DialogDescription>
              Ejecuta el motor de cruce multinivel: (1) Referencia exacta + importe, (2) Importe + fecha exacta, (3) Tolerancia de importe y ventana de fechas.
            </DialogDescription>
          </DialogHeader>
          <div className="grid gap-4 py-4">
            <div className="grid grid-cols-2 gap-4">
              <div>
                <Label className="text-xs">Tolerancia Importe (±$)</Label>
                <Input
                  type="number"
                  step="0.01"
                  className="mt-1 font-mono"
                  value={toleranciaImporte}
                  onChange={(e) => setToleranciaImporte(e.target.value)}
                />
                <span className="text-[10px] text-ink-muted">Default: ±$1.00</span>
              </div>
              <div>
                <Label className="text-xs">Ventana de Días (±)</Label>
                <Input
                  type="number"
                  className="mt-1 font-mono"
                  value={ventanaDias}
                  onChange={(e) => setVentanaDias(e.target.value)}
                />
                <span className="text-[10px] text-ink-muted">Default: ±3 días</span>
              </div>
            </div>
            <div className="text-xs text-ink-muted bg-amber-500/10 border border-amber-500/20 p-2.5 rounded text-amber-900 dark:text-amber-300">
              <span className="font-semibold">Regla de seguridad:</span> Si una línea encuentra más de 1 movimiento candidato idéntico, NO se empareja sola; se envía a <b>En revisión</b> para validación humana.
            </div>
          </div>
          <DialogFooter>
            <Button variant="ghost" onClick={() => setModalAutoEmparejar(false)}>
              Cancelar
            </Button>
            <Button
              disabled={autoEmparejarMutation.isPending}
              onClick={handleAutoEmparejarSubmit}
            >
              Iniciar Emparejamiento
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* MODAL: ALTA ASISTIDA */}
      <Dialog open={!!modalAltaAsistida} onOpenChange={(val) => !val && setModalAltaAsistida(null)}>
        <DialogContent className="sm:max-w-[500px]">
          <DialogHeader>
            <DialogTitle>Alta asistida desde extracto</DialogTitle>
            <DialogDescription>
              Crea el movimiento bancario correspondiente en libros con su concepto, queda emparejado automáticamente y emite el evento contable.
            </DialogDescription>
          </DialogHeader>
          {modalAltaAsistida && (
            <div className="grid gap-4 py-3">
              <div className="bg-surface border border-edge p-3 rounded-md text-xs">
                <p className="font-semibold text-ink">{modalAltaAsistida.descripcion}</p>
                <p className="text-ink-muted">
                  Fecha: {modalAltaAsistida.fecha.substring(0, 10)} · Monto: {modalAltaAsistida.monto >= 0 ? '+' : ''}
                  {modalAltaAsistida.monto} {conciliacion?.moneda}
                </p>
              </div>

              <div>
                <Label className="text-xs">Concepto Principal</Label>
                <Select value={altaConceptoId} onValueChange={setAltaConceptoId}>
                  <SelectTrigger className="mt-1">
                    <SelectValue placeholder="Seleccione concepto..." />
                  </SelectTrigger>
                  <SelectContent>
                    {conceptosQuery.data?.map((c) => (
                      <SelectItem key={c.id} value={c.id}>
                        {c.nombre}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>

              {modalAltaAsistida.sentido === 2 && (
                <div className="flex flex-col gap-3 p-3 bg-surface border border-edge rounded-md">
                  <div className="flex items-center space-x-2">
                    <Checkbox
                      id="altaiva"
                      checked={altaIncluyeIva}
                      onCheckedChange={(val) => {
                        const checked = !!val;
                        setAltaIncluyeIva(checked);
                        const total = Math.abs(modalAltaAsistida.monto);
                        if (checked) {
                          const base = Math.round((total / 1.16) * 100) / 100;
                          const iva = Math.round((total - base) * 100) / 100;
                          setAltaMontoBase(base.toFixed(2));
                          setAltaMontoIva(iva.toFixed(2));
                        } else {
                          setAltaMontoBase(total.toFixed(2));
                          setAltaMontoIva('0.00');
                        }
                      }}
                    />
                    <label htmlFor="altaiva" className="text-xs font-medium cursor-pointer">
                      Desglosar IVA (16%)
                    </label>
                  </div>

                  {altaIncluyeIva && (
                    <>
                      <div>
                        <Label className="text-xs">Concepto de IVA</Label>
                        <Select value={altaConceptoIvaId} onValueChange={setAltaConceptoIvaId}>
                          <SelectTrigger className="mt-1">
                            <SelectValue placeholder="Seleccione concepto IVA..." />
                          </SelectTrigger>
                          <SelectContent>
                            {conceptosQuery.data?.map((c) => (
                              <SelectItem key={c.id} value={c.id}>
                                {c.nombre}
                              </SelectItem>
                            ))}
                          </SelectContent>
                        </Select>
                      </div>

                      <div className="grid grid-cols-2 gap-3">
                        <div>
                          <Label className="text-xs">Monto Base</Label>
                          <Input
                            type="number"
                            step="0.01"
                            className="mt-1 font-mono text-xs"
                            value={altaMontoBase}
                            onChange={(e) => setAltaMontoBase(e.target.value)}
                          />
                        </div>
                        <div>
                          <Label className="text-xs">Monto IVA</Label>
                          <Input
                            type="number"
                            step="0.01"
                            className="mt-1 font-mono text-xs"
                            value={altaMontoIva}
                            onChange={(e) => setAltaMontoIva(e.target.value)}
                          />
                        </div>
                      </div>
                    </>
                  )}
                </div>
              )}
            </div>
          )}
          <DialogFooter>
            <Button variant="ghost" onClick={() => setModalAltaAsistida(null)}>
              Cancelar
            </Button>
            <Button
              disabled={altaAsistidaMutation.isPending || !altaConceptoId}
              onClick={handleAltaAsistidaSubmit}
            >
              Crear Movimiento y Emparejar
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* MODAL: DESEMPAREJAR LÍNEA */}
      <Dialog open={!!modalDesemparejar} onOpenChange={(val) => !val && setModalDesemparejar(null)}>
        <DialogContent className="sm:max-w-[425px]">
          <DialogHeader>
            <DialogTitle>Desemparejar línea</DialogTitle>
            <DialogDescription>
              La línea y sus movimientos quedarán libres nuevamente. Se requiere un motivo para la bitácora de auditoría (CA9.5 / T9.2-a).
            </DialogDescription>
          </DialogHeader>
          <div className="py-3">
            <Label className="text-xs">Motivo de desemparejamiento *</Label>
            <Textarea
              rows={3}
              placeholder="Ej. El movimiento fue emparejado por coincidencia de monto pero pertenece a otra factura..."
              className="mt-1 text-xs"
              value={motivoDesemparejar}
              onChange={(e) => setMotivoDesemparejar(e.target.value)}
            />
          </div>
          <DialogFooter>
            <Button variant="ghost" onClick={() => setModalDesemparejar(null)}>
              Cancelar
            </Button>
            <Button
              variant="destructive"
              disabled={desemparejarMutation.isPending || !motivoDesemparejar.trim()}
              onClick={handleDesemparejarSubmit}
            >
              Desemparejar y Registrar
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* MODAL: MARCAR PARTIDA EN TRÁNSITO */}
      <Dialog open={!!modalPartidaTransito} onOpenChange={(val) => !val && setModalPartidaTransito(null)}>
        <DialogContent className="sm:max-w-[425px]">
          <DialogHeader>
            <DialogTitle>Marcar como Partida en Tránsito</DialogTitle>
            <DialogDescription>
              Identifica movimientos bancarios que corresponden a operaciones del período pero aún no reflejadas.
            </DialogDescription>
          </DialogHeader>
          <div className="grid gap-4 py-3">
            <div>
              <Label className="text-xs">Tipo de Partida en Tránsito</Label>
              <Select
                value={transitoTipo.toString()}
                onValueChange={(val) => setTransitoTipo(parseInt(val, 10) as PartidaTransitoTipo)}
              >
                <SelectTrigger className="mt-1">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={PartidaTransitoTipo.ChequeNoCobrado.toString()}>
                    {PARTIDA_TRANSITO_LABELS[PartidaTransitoTipo.ChequeNoCobrado]}
                  </SelectItem>
                  <SelectItem value={PartidaTransitoTipo.DepositoUltimoDia.toString()}>
                    {PARTIDA_TRANSITO_LABELS[PartidaTransitoTipo.DepositoUltimoDia]}
                  </SelectItem>
                  <SelectItem value={PartidaTransitoTipo.Otro.toString()}>
                    {PARTIDA_TRANSITO_LABELS[PartidaTransitoTipo.Otro]}
                  </SelectItem>
                </SelectContent>
              </Select>
            </div>
            <div>
              <Label className="text-xs">Responsable *</Label>
              <Input
                placeholder="Ej. Juan Pérez (Cobranza)"
                className="mt-1 text-xs"
                value={transitoResponsable}
                onChange={(e) => setTransitoResponsable(e.target.value)}
              />
            </div>
          </div>
          <DialogFooter>
            <Button variant="ghost" onClick={() => setModalPartidaTransito(null)}>
              Cancelar
            </Button>
            <Button
              disabled={transitoMutation.isPending || !transitoResponsable.trim()}
              onClick={handlePartidaTransitoSubmit}
            >
              Guardar en Tránsito
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* MODAL: CERRAR CONCILIACIÓN */}
      <Dialog open={modalCerrar} onOpenChange={setModalCerrar}>
        <DialogContent className="sm:max-w-[450px]">
          <DialogHeader>
            <DialogTitle>Cierre cuadrado de conciliación</DialogTitle>
            <DialogDescription>
              La conciliación se encuentra cuadrada (Diferencia $0.00). El cierre generará el Acta de Conciliación Bancaria oficial con firma en PDF.
            </DialogDescription>
          </DialogHeader>
          <div className="grid gap-4 py-3">
            <div>
              <Label className="text-xs">Nombre del Firmante *</Label>
              <Input
                className="mt-1 text-xs"
                value={firmanteNombre}
                onChange={(e) => setFirmanteNombre(e.target.value)}
              />
            </div>
            <div>
              <Label className="text-xs">Puesto del Firmante *</Label>
              <Select value={firmantePuesto} onValueChange={setFirmantePuesto}>
                <SelectTrigger className="mt-1">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="Gerente de Tesorería">Gerente de Tesorería</SelectItem>
                  <SelectItem value="Jefe de Tesorería">Jefe de Tesorería</SelectItem>
                </SelectContent>
              </Select>
            </div>
          </div>
          <DialogFooter>
            <Button variant="ghost" onClick={() => setModalCerrar(false)}>
              Cancelar
            </Button>
            <Button
              disabled={cerrarMutation.isPending || !firmanteNombre.trim()}
              onClick={handleCerrarSubmit}
            >
              Firmar, Cerrar y Descargar Acta
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* MODAL: REABRIR CONCILIACIÓN */}
      <Dialog open={modalReabrir} onOpenChange={setModalReabrir}>
        <DialogContent className="sm:max-w-[425px]">
          <DialogHeader>
            <DialogTitle>Reabrir conciliación cerrada</DialogTitle>
            <DialogDescription>
              Se requiere justificación obligatoria para reabrir un período cerrado. Esta acción se audita en la bitácora.
            </DialogDescription>
          </DialogHeader>
          <div className="py-3">
            <Label className="text-xs">Motivo de reapertura *</Label>
            <Textarea
              rows={3}
              placeholder="Ej. Ajuste contable requerido por auditoría externa..."
              className="mt-1 text-xs"
              value={motivoReabrir}
              onChange={(e) => setMotivoReabrir(e.target.value)}
            />
          </div>
          <DialogFooter>
            <Button variant="ghost" onClick={() => setModalReabrir(false)}>
              Cancelar
            </Button>
            <Button
              variant="destructive"
              disabled={reabrirMutation.isPending || !motivoReabrir.trim()}
              onClick={handleReabrirSubmit}
            >
              Reabrir Período
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
