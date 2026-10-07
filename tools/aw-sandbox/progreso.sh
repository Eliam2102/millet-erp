#!/bin/bash
# Barra de progreso de cargar-completo.sh (refresca cada 5 s; Ctrl+C para salir). Uso: tools/aw-sandbox/progreso.sh
cd "$(dirname "$0")"
while :; do
  t=$(wc -l < .tablas); d=$(wc -l < .cargado); f=$(grep -c '^FALLO' carga.log 2>/dev/null)
  p=$(( t ? d*100/t : 0 )); w=$(( p*40/100 ))
  clear
  printf '[%s%s] %d%%  %d/%d tablas  (fallos: %d)\n\n' "$(printf '#%.0s' $(seq $w))" "$(printf '.%.0s' $(seq $((40-w))))" "$p" "$d" "$t" "$f"
  echo "En curso (archivo temporal exportado):"
  docker exec aw-sandbox sh -c 'ls -l /tmp/bcp_*.dat 2>/dev/null' | awk '{printf "  %-28s %6.0f MB\n", substr($9,10), $5/1048576}'
  echo; echo "Últimas cargadas:"; tail -4 carga.log | cut -c1-90 | sed 's/^/  /'
  [ "$d" -ge "$t" ] && { echo; echo "Terminado."; break; }
  sleep 5
done
