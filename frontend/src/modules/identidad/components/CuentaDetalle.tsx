import { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { ArrowLeft, UserCheck } from 'lucide-react';
import { toast } from 'sonner';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Label } from '@/components/ui/label';
import {
  Dialog,
  DialogBody,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { ErrorState, PageHeader, TableSkeleton } from '@/components/erp';
import { TextAreaField } from '@/components/erp/forms/TextAreaField';
import { esApiError, codigoSoporte } from '@/lib/api';
import { useAuthStore } from '@/lib/auth/auth-store';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useConvertirEnCuentaDePersona, useUsuario } from '@/modules/identidad/api/usuarios';
import { RolesPorEmpresaPanel } from '@/modules/identidad/components/RolesPorEmpresaPanel';
import { PermisosPersonalizadosPanel } from '@/modules/identidad/components/PermisosPersonalizadosPanel';

/** Código del rol super administrador (alineado con el backend). */
const SUPER_ADMIN = 'super-admin';
export const MOTIVO_MINIMO = 15;

function fecha(iso: string | null | undefined) {
  return iso ? new Date(iso).toLocaleString('es-MX', { dateStyle: 'medium', timeStyle: 'short' }) : 'Nunca';
}

/**
 * Ficha de una cuenta sin empleado (86e3yxe9m): datos de la cuenta, su rol por
 * empresa (se puede cambiar con el panel de siempre) y sus permisos. Una cuenta
 * técnica se puede convertir en cuenta de persona; solo un super administrador,
 * con motivo, y queda en la bitácora.
 */
export function CuentaDetalle({ usuarioId }: { usuarioId: string }) {
  const detalle = useUsuario(usuarioId);
  const usuarioActualId = useAuthStore((s) => s.user?.id ?? null);
  const empresaId = useAuthStore((s) => s.currentEmpresaId);
  const actual = useUsuario(usuarioActualId);
  const puedeEditar = useHasPermission(PermisosCanonicos.IdentidadUsuariosEditar);
  const [convirtiendo, setConvirtiendo] = useState(false);

  if (detalle.isLoading) return <TableSkeleton rows={5} />;
  if (detalle.isError || !detalle.data) {
    return (
      <ErrorState
        title="No se pudo consultar la cuenta"
        problem={esApiError(detalle.error) ? detalle.error.problem : undefined}
        onRetry={() => detalle.refetch()}
      />
    );
  }

  const { usuario, asignaciones } = detalle.data;
  const tecnica = usuario.esCuentaTecnica === true;
  const actorEsSuperAdmin = (actual.data?.asignaciones ?? []).some((a) => a.rolCodigo === SUPER_ADMIN);
  const puedeConvertir = tecnica && puedeEditar && actorEsSuperAdmin;

  return (
    <div className="space-y-5">
      <Link
        to="/admin/empleados"
        search={{ vista: tecnica ? 'tecnicas' : 'sin-empleado' }}
        className="inline-flex items-center gap-1 text-xs text-ink-muted hover:text-ink"
      >
        <ArrowLeft className="h-3.5 w-3.5" />
        {tecnica ? 'Cuentas técnicas' : 'Cuentas sin empleado'}
      </Link>
      <PageHeader
        titulo={<>{usuario.nombre}</>}
        descripcion={
          <>
            <span className="font-mono">{usuario.email}</span> · Cuenta sin empleado
          </>
        }
        acciones={
          puedeConvertir ? (
            <Button variant="outline" onClick={() => setConvirtiendo(true)}>
              <UserCheck className="mr-1.5 h-4 w-4" />
              Convertir en cuenta de persona
            </Button>
          ) : undefined
        }
      />

      <section aria-label="Datos de la cuenta" className="rounded-md border bg-surface-card p-4 text-sm">
        <dl className="grid grid-cols-2 gap-x-4 gap-y-3 lg:grid-cols-4">
          <div>
            <dt className="text-xs text-ink-muted">Tipo</dt>
            <dd>
              <Badge variant={tecnica ? 'warning' : 'neutral'}>{tecnica ? 'Cuenta técnica' : 'Cuenta de persona'}</Badge>
            </dd>
          </div>
          <div>
            <dt className="text-xs text-ink-muted">Estado</dt>
            <dd>
              <Badge variant={usuario.activo ? 'success' : 'danger'}>{usuario.activo ? 'Activa' : 'Desactivada'}</Badge>
            </dd>
          </div>
          <div>
            <dt className="text-xs text-ink-muted">Primer acceso</dt>
            <dd>{fecha(usuario.primerAccesoEn)}</dd>
          </div>
          <div>
            <dt className="text-xs text-ink-muted">Empleado</dt>
            <dd>Sin empleado ligado</dd>
          </div>
        </dl>
        {tecnica && (
          <p className="mt-3 text-xs text-ink-muted">
            Las cuentas técnicas no corresponden a un empleado (administrador inicial, soporte, QA) y conservan el
            acceso aunque se exija empleado para entrar.
          </p>
        )}
      </section>

      <RolesPorEmpresaPanel usuarioId={usuario.id} asignaciones={asignaciones} />
      {empresaId != null && <PermisosPersonalizadosPanel usuarioId={usuario.id} empresaId={empresaId} />}

      <ConvertirDialog
        open={convirtiendo}
        onOpenChange={setConvirtiendo}
        usuarioId={usuario.id}
        version={usuario.version}
        nombre={usuario.nombre}
      />
    </div>
  );
}

function ConvertirDialog({
  open,
  onOpenChange,
  usuarioId,
  version,
  nombre,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  usuarioId: string;
  version: number;
  nombre: string;
}) {
  const [motivo, setMotivo] = useState('');
  const convertir = useConvertirEnCuentaDePersona();
  const motivoValido = motivo.trim().length >= MOTIVO_MINIMO;

  function cerrar(next: boolean) {
    if (!next) setMotivo('');
    onOpenChange(next);
  }

  function confirmar() {
    convertir.mutate(
      { id: usuarioId, versionEsperada: version, motivo: motivo.trim(), idempotencyKey: crypto.randomUUID() },
      {
        onSuccess: () => {
          toast.success(`${nombre} ahora es una cuenta de persona.`);
          cerrar(false);
        },
        onError: (error) => {
          if (esApiError(error))
            toast.error(error.problem.title, { description: error.problem.detail ?? codigoSoporte(error) });
          else toast.error('Error inesperado.');
        },
      },
    );
  }

  return (
    <Dialog open={open} onOpenChange={cerrar}>
      <DialogContent size="sm">
        <DialogHeader>
          <DialogTitle>Convertir en cuenta de persona</DialogTitle>
          <DialogDescription>
            La cuenta deja de ser técnica. Si se exige empleado para entrar, necesitará un empleado ligado para
            conservar el acceso. El motivo queda en la bitácora de auditoría.
          </DialogDescription>
        </DialogHeader>
        <DialogBody>
          <div className="space-y-1">
            <Label htmlFor="motivo-convertir" className="text-xs">
              Motivo * (mínimo {MOTIVO_MINIMO} caracteres)
            </Label>
            <TextAreaField
              value={motivo}
              onChange={(v) => setMotivo(v ?? '')}
              maxLength={500}
              textareaProps={{
                id: 'motivo-convertir',
                placeholder: 'p. ej. La cuenta es de una persona del área de sistemas, no de soporte',
              }}
            />
          </div>
        </DialogBody>
        <DialogFooter>
          <Button type="button" variant="ghost" onClick={() => cerrar(false)} disabled={convertir.isPending}>
            Cancelar
          </Button>
          <Button type="button" disabled={!motivoValido || convertir.isPending} onClick={confirmar}>
            {convertir.isPending ? 'Convirtiendo…' : 'Convertir'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
