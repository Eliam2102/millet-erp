import { useMemo, useState } from 'react';
import { Settings2 } from 'lucide-react';
import { toast } from 'sonner';
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
import { Textarea } from '@/components/ui/textarea';
import {
  EmptyState,
  ErrorState,
  TableSkeleton,
} from '@/components/erp';
import { esApiError, useFormIdempotencyKey } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import {
  useActualizarParametro,
  useParametros,
} from '@/modules/administracion/api';
import {
  TipoParametro,
  type ParametroResponse,
} from '@/modules/administracion/api/types';
import { actualizarParametroSchema } from '@/modules/administracion/schemas/parametro';

const TODOS_LOS_MODULOS = '__todos__';
const SOLO_GLOBALES = '__globales__';

const TIPO_LABEL: Record<TipoParametro, string> = {
  [TipoParametro.Texto]: 'Texto',
  [TipoParametro.Numero]: 'Número',
  [TipoParametro.Booleano]: 'Booleano',
  [TipoParametro.Json]: 'JSON',
};

/**
 * <c>&lt;ParametrosPage/&gt;</c> — bandeja P1 (form list) de parámetros
 * globales del sistema (UF-Admin-PR7 §2). Cada fila renderiza el editor
 * adecuado al tipo declarado; el botón "Guardar" se habilita solo
 * cuando el valor cambió (dirty checking local) y el schema dinámico
 * pasa.
 *
 * <para>El backend valida el valor contra el tipo y responde 422 si no
 * parsea — ese caso aterriza como toast con el <c>title</c> del
 * <c>ProblemDetails</c>.</para>
 *
 * <para>Filtro por módulo: <c>"Todos"</c> trae globales y por módulo;
 * <c>"Globales"</c> filtra a los del sistema (Modulo IS NULL); el resto
 * son los nombres de módulos detectados en los items.</para>
 */
export function ParametrosPage() {
  const canEditar = useHasPermission(
    PermisosCanonicos.AdminParametrosEditar,
  );
  const [filtroModulo, setFiltroModulo] = useState<string>(TODOS_LOS_MODULOS);

  const moduloQuery: string | undefined =
    filtroModulo === TODOS_LOS_MODULOS
      ? undefined
      : filtroModulo === SOLO_GLOBALES
        ? ''
        : filtroModulo;

  const query = useParametros(moduloQuery);
  const items = useMemo(() => query.data?.items ?? [], [query.data]);

  // Detecta módulos presentes en la respuesta (cuando viene "todos")
  // para enriquecer el select. Si el filtro ya está acotado, igual
  // calculamos sobre lo recibido — es barato y evita doble query.
  const modulosPresentes = useMemo(() => {
    const set = new Set<string>();
    for (const p of items) {
      if (p.modulo != null && p.modulo.length > 0) {
        set.add(p.modulo);
      }
    }
    return [...set].sort();
  }, [items]);

  return (
    <div className="space-y-4 p-4">
      <header>
        <h1 className="text-xl font-semibold tracking-tight">
          Parámetros del sistema
        </h1>
        <p className="text-xs text-muted-foreground">
          Configuración global. Solo el valor es editable; el tipo y la
          clave son inmutables.
        </p>
      </header>

      <div className="flex flex-wrap items-end gap-3 rounded-md border bg-card p-3">
        <div className="flex flex-col gap-1">
          <label className="text-xs font-medium text-muted-foreground">
            Módulo
          </label>
          <Select value={filtroModulo} onValueChange={setFiltroModulo}>
            <SelectTrigger className="h-9 w-56">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={TODOS_LOS_MODULOS}>Todos</SelectItem>
              <SelectItem value={SOLO_GLOBALES}>Globales</SelectItem>
              {modulosPresentes.map((m) => (
                <SelectItem key={m} value={m}>
                  {m}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
      </div>

      <ParametrosLista
        items={items}
        isLoading={query.isLoading}
        isError={query.isError}
        error={query.error}
        onRetry={() => query.refetch()}
        canEditar={canEditar}
      />
    </div>
  );
}

interface ParametrosListaProps {
  items: readonly ParametroResponse[];
  isLoading: boolean;
  isError: boolean;
  error: unknown;
  onRetry: () => void;
  canEditar: boolean;
}

function ParametrosLista({
  items,
  isLoading,
  isError,
  error,
  onRetry,
  canEditar,
}: ParametrosListaProps) {
  if (isLoading) {
    return (
      <TableSkeleton
        rows={4}
        columns={[{ width: 'w-full' }, { width: 'w-64' }]}
      />
    );
  }

  if (isError) {
    const problem = esApiError(error) ? error.problem : undefined;
    return <ErrorState problem={problem} onRetry={onRetry} />;
  }

  if (items.length === 0) {
    return (
      <EmptyState
        icon={<Settings2 className="h-10 w-10" />}
        title="Sin parámetros configurados."
        description="Cuando se agreguen, aparecerán aquí."
      />
    );
  }

  return (
    <div className="space-y-3">
      {items.map((p) => (
        <ParametroFila key={p.id} parametro={p} canEditar={canEditar} />
      ))}
    </div>
  );
}

interface ParametroFilaProps {
  parametro: ParametroResponse;
  canEditar: boolean;
}

/**
 * Fila editable de un parámetro. Mantiene el valor "borrador" en
 * estado local; on success refresca contra el server response (Version
 * actualizado) y muestra toast.
 */
function ParametroFila({ parametro, canEditar }: ParametroFilaProps) {
  const [valor, setValor] = useState(parametro.valor);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const idempotencyKey = useFormIdempotencyKey();
  const actualizar = useActualizarParametro();

  const dirty = valor !== parametro.valor;
  const schema = useMemo(
    () => actualizarParametroSchema(parametro.tipo),
    [parametro.tipo],
  );

  function guardar() {
    const result = schema.safeParse({ valor });
    if (!result.success) {
      const issue = result.error.issues[0];
      setErrorMsg(issue?.message ?? 'Valor inválido.');
      return;
    }
    setErrorMsg(null);
    actualizar.mutate(
      {
        clave: parametro.clave,
        payload: { valor: result.data.valor },
        idempotencyKey,
      },
      {
        onSuccess: (data) => {
          setValor(data.valor);
          toast.success(`${parametro.clave} actualizado`);
        },
        onError: (err) => {
          if (esApiError(err)) {
            toast.error(err.problem.title, {
              description: err.problem.detail,
            });
          } else {
            toast.error('Error al actualizar el parámetro.');
          }
        },
      },
    );
  }

  return (
    <div
      className="flex flex-col gap-3 rounded-md border bg-card p-4"
      data-testid={`parametro-${parametro.clave}`}
    >
      <header className="flex flex-wrap items-start justify-between gap-2">
        <div>
          <div className="flex items-center gap-2">
            <code className="font-mono text-sm font-semibold">
              {parametro.clave}
            </code>
            <Badge variant="secondary">{TIPO_LABEL[parametro.tipo]}</Badge>
            {parametro.modulo != null && (
              <Badge variant="outline">{parametro.modulo}</Badge>
            )}
          </div>
          {parametro.descripcion.length > 0 && (
            <p className="mt-1 text-xs text-muted-foreground">
              {parametro.descripcion}
            </p>
          )}
        </div>
      </header>

      <div className="flex flex-col gap-2">
        <ParametroEditor
          tipo={parametro.tipo}
          valor={valor}
          disabled={!canEditar || actualizar.isPending}
          onChange={(v) => {
            setValor(v);
            setErrorMsg(null);
          }}
          inputId={`parametro-input-${parametro.clave}`}
        />
        {errorMsg != null && (
          <p className="text-xs text-destructive">{errorMsg}</p>
        )}

        <div className="flex items-center justify-end gap-2">
          {canEditar && (
            <>
              {dirty && (
                <Button
                  type="button"
                  size="sm"
                  variant="ghost"
                  onClick={() => {
                    setValor(parametro.valor);
                    setErrorMsg(null);
                  }}
                  disabled={actualizar.isPending}
                >
                  Revertir
                </Button>
              )}
              <Button
                type="button"
                size="sm"
                onClick={guardar}
                disabled={!dirty || actualizar.isPending}
              >
                {actualizar.isPending ? 'Guardando…' : 'Guardar'}
              </Button>
            </>
          )}
        </div>
      </div>
    </div>
  );
}

interface ParametroEditorProps {
  tipo: TipoParametro;
  valor: string;
  disabled: boolean;
  onChange: (v: string) => void;
  inputId: string;
}

function ParametroEditor({
  tipo,
  valor,
  disabled,
  onChange,
  inputId,
}: ParametroEditorProps) {
  switch (tipo) {
    case TipoParametro.Texto:
      return (
        <Input
          id={inputId}
          type="text"
          value={valor}
          disabled={disabled}
          onChange={(e) => onChange(e.target.value)}
        />
      );
    case TipoParametro.Numero:
      return (
        <Input
          id={inputId}
          type="number"
          step="any"
          value={valor}
          disabled={disabled}
          onChange={(e) => onChange(e.target.value)}
        />
      );
    case TipoParametro.Booleano: {
      // Checkbox nativo (no Radix) — Radix Checkbox no funciona bien
      // bajo jsdom porque depende de Pointer Events; mismo criterio
      // que se aplicó en PR5 al elegir editor de booleanos. El Switch
      // tampoco está disponible en este set de UI.
      const checked = valor === 'true';
      return (
        <label
          htmlFor={inputId}
          className="inline-flex items-center gap-2 text-sm"
        >
          <input
            id={inputId}
            type="checkbox"
            className="h-4 w-4"
            checked={checked}
            disabled={disabled}
            onChange={(e) => onChange(e.target.checked ? 'true' : 'false')}
          />
          <span>{checked ? 'Activado (true)' : 'Desactivado (false)'}</span>
        </label>
      );
    }
    case TipoParametro.Json:
      return (
        <Textarea
          id={inputId}
          rows={6}
          value={valor}
          disabled={disabled}
          onChange={(e) => onChange(e.target.value)}
          className="font-mono text-xs"
        />
      );
  }
}
