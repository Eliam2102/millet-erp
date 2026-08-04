import { Link } from '@tanstack/react-router';
import { ExternalLink } from 'lucide-react';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import {
  SettingDisplayMode,
  TipoSetting,
  type SettingItem,
} from '@/lib/admin/types';

/**
 * <c>&lt;AutoSettingsForm/&gt;</c> — renderiza la lista de
 * <see cref="SettingItem"/> devuelta por
 * <c>GET /api/v1/{modulo}/settings/schema</c>.
 *
 * <para><b>UF-Admin-PR1 scope</b>: el componente actúa como "índice" del
 * módulo — por cada item muestra una card con etiqueta, descripción y,
 * si el item es <see cref="SettingDisplayMode.Custom"/>, un botón que
 * linkea a <c>rutaCustom</c>. Para items <see cref="SettingDisplayMode.Auto"/>
 * muestra un placeholder neutro indicando que el control inline se
 * implementará cuando el primer módulo lo necesite (UF-Admin-PR2+). La
 * decisión es deliberada: hoy solo Compras expone un setting y es
 * <c>Custom</c>, así que construir el PATCH genérico ahora sería
 * código sin consumidor real (deuda anticipada en lugar de habilitada).</para>
 */
export interface AutoSettingsFormProps {
  /** Items ya filtrados por permiso (el backend descarta los no-leer). */
  items: readonly SettingItem[];
}

export function AutoSettingsForm({ items }: AutoSettingsFormProps) {
  if (items.length === 0) {
    return (
      <div className="rounded-md bg-muted/50 px-4 py-6 text-center text-sm text-muted-foreground">
        Este módulo no expone settings visibles para tu rol.
      </div>
    );
  }

  return (
    <div className="space-y-3">
      {items.map((item) => (
        <SettingRow key={item.clave} item={item} />
      ))}
    </div>
  );
}

function SettingRow({ item }: { item: SettingItem }) {
  const esCustom = item.mostrar === SettingDisplayMode.Custom;
  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-sm">{item.etiqueta}</CardTitle>
        <CardDescription>{item.descripcion}</CardDescription>
      </CardHeader>
      <CardContent className="flex items-center justify-between gap-3">
        <div className="text-xs text-muted-foreground">
          <span className="font-mono">{item.clave}</span>
          <span className="ml-2 rounded bg-muted px-1.5 py-0.5">
            {nombreTipo(item.tipo)}
          </span>
        </div>
        {esCustom && item.rutaCustom ? (
          <Button asChild size="sm" variant="outline">
            <Link to={item.rutaCustom}>
              Abrir configuración
              <ExternalLink className="ml-1.5 h-3.5 w-3.5" />
            </Link>
          </Button>
        ) : (
          <span className="text-xs italic text-muted-foreground">
            Edición inline disponible próximamente
          </span>
        )}
      </CardContent>
    </Card>
  );
}

/**
 * Etiqueta humana para el tipo del setting. Útil hasta que el form
 * inline renderice el control real.
 */
function nombreTipo(tipo: SettingItem['tipo']): string {
  switch (tipo) {
    case TipoSetting.Booleano:
      return 'Booleano';
    case TipoSetting.Entero:
      return 'Entero';
    case TipoSetting.Numerico:
      return 'Decimal';
    case TipoSetting.Texto:
      return 'Texto';
    case TipoSetting.Lista:
      return 'Lista';
    case TipoSetting.Fecha:
      return 'Fecha';
    default:
      return 'Desconocido';
  }
}
