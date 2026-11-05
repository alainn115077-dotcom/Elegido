#!/usr/bin/env bash
# Prueba de humo: crea el administrador y abre las páginas principales; falla si alguna no responde 200.
set -u
B="$1"; J=$(mktemp); F=0
tok() { grep -o 'name="__RequestVerificationToken" type="hidden" value="[^"]*"' | head -1 | sed 's/.*value="//;s/"$//'; }
ok() { local r c t; r=$(curl -s -o /tmp/r.html -w '%{http_code} %{time_total}' -b "$J" -c "$J" "$B$1"); c=${r% *}; t=${r#* }; echo "$c  ${t}s  $1"; [ "$c" = "200" ] || { F=1; head -c 1500 /tmp/r.html; echo; }; }

T=$(curl -s -L -b "$J" -c "$J" "$B/Cuenta/Configurar" | tok)
[ -n "$T" ] || { echo "Sin token en Configurar"; exit 1; }
c=$(curl -s -o /tmp/r.html -w '%{http_code}' -b "$J" -c "$J" -X POST "$B/Cuenta/Configurar" \
  --data-urlencode "__RequestVerificationToken=$T" --data-urlencode "NombreCompleto=Alain Prueba" \
  --data-urlencode "Usuario=alain" --data-urlencode "Contrasena=Prueba123" --data-urlencode "ConfirmarContrasena=Prueba123")
echo "$c  POST Configurar"; [ "$c" = "302" ] || { head -c 3000 /tmp/r.html; exit 1; }

# Contratación de prueba
T=$(curl -s -b "$J" -c "$J" "$B/Contrataciones/Crear" | tok)
c=$(curl -s -o /tmp/r.html -w '%{http_code}' -b "$J" -c "$J" -X POST "$B/Contrataciones/Crear" \
  --data-urlencode "__RequestVerificationToken=$T" --data-urlencode "NuevoCliente=true" \
  --data-urlencode "ClienteNombre=Cliente Prueba" --data-urlencode "ClienteTelefono=8991234567" \
  --data-urlencode "Dia=20" --data-urlencode "Mes=12" --data-urlencode "HoraInicio=21:00" --data-urlencode "DuracionHoras=2" \
  --data-urlencode "Direccion=Calle Arte 1545" --data-urlencode "PrecioAcordado=7000" --data-urlencode "Anticipo=500" \
  --data-urlencode "Estado=Confirmada")
echo "$c  POST Contrataciones/Crear"; [ "$c" = "302" ] || { F=1; grep -o 'text-error[^<]*<[^<]*' /tmp/r.html | head; grep -o 'validation-summary[^<]*<ul><li>[^<]*' /tmp/r.html | head; }

for p in / /Agenda "/Agenda/Eventos?start=2026-01-01&end=2027-12-31" "/Agenda/Dia?fecha=2026-12-20" /Agenda/IcsProximos \
         /Contrataciones /Contrataciones/Exportar /Clientes /Consultas /Calculadora /Documentos /Integrantes \
         /Finanzas /Finanzas/PorEvento /Finanzas/Gastos /Usuarios /Usuarios/Crear /Bitacora /Respaldo/Descargar /Cuenta/CambiarContrasena; do
  ok "$p"
done
ID=$(curl -s -b "$J" "$B/Contrataciones" | grep -o '/Contrataciones/Detalle/[0-9]*' | head -1)
[ -n "$ID" ] && ok "$ID" || { echo "No apareció la contratación creada"; F=1; }
ok "/Consultas/Responder?clave=generado-finde"
ok "/Consultas/Responder?clave=fechas-disponibles"
# Archivos propios (Bootstrap, íconos, jQuery) deben servirse desde la app
for p in /lib/bootstrap/bootstrap.min.css /lib/bootstrap/bootstrap.bundle.min.js /lib/bootstrap-icons/bootstrap-icons.min.css \
         /lib/bootstrap-icons/fonts/bootstrap-icons.woff2 /lib/jquery/jquery.min.js /sw.js /manifest.webmanifest; do ok "$p"; done
exit $F
