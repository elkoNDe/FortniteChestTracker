# Fortnite ChestTracker

Tracker **de código abierto y ejecución local** para Windows 10/11. Lee archivos `.replay` de Fortnite tras cada partida y registra cofres del jugador que grabó la repetición, con un desglose adicional de aperturas atribuibles a compañeros del mismo equipo. **Solo se contabilizan las partidas NoBuildBR de Cero construcción.** No lee memoria de Fortnite, no modifica el juego, no pide credenciales de Epic y no envía estadísticas por Internet.

> **Estado: v0.3.2 (primera implementación pendiente de validación con replays reales).** El lector y el modelo de datos están implementados; hasta pasar las pruebas de GitHub Actions y analizar repeticiones reales **no se debe dar por validada la precisión del conteo**. Fortnite puede cambiar el formato de los replays tras cada actualización.

## Qué incluye

- Detección de cofres normales, raros, enterrados, especiales y de reliquias mediante eventos de contenedores.
- Asociación de la interacción al **propietario de la repetición** para los totales personales. Además, se intenta atribuir por separado cofres a los jugadores con el mismo **TeamIndex** (escuadra de esa partida), sin contar rivales.
- Cofres abiertos y destruidos; las cajas de munición se registran por separado.
- Lectura en **solo lectura** de `%LOCALAPPDATA%\FortniteGame\Saved\Demos`.
- Vigilancia cada **6 segundos**: reconoce repeticiones finalizadas tras dos lecturas estables; informa en consola de grabaciones activas, claves pendientes y reintentos, con un aviso de actividad cada minuto.
- SQLite en `%LOCALAPPDATA%\FortniteChestTracker\tracker.db`.
- Deduplicación por `GameSessionId`, incluso si una repetición se renombra.
- Panel oscuro `dashboard.html` con histórico completo y paginación por partida (25, 50 o 100 partidas), gráficas interactivas por día y mes, filtros, medias, récord y **filas por cada miembro de la escuadra bajo una única Fecha inicio y Posición**; se actualiza cada 20 s.
- Exportación `matches.csv`, registro `errors.log`, tests internos y workflow de compilación para Windows.

### Filtro permanente de modos y resultados (v0.3.1)

Solo se contabilizan exactamente las siguientes playlists de Cero construcción BR: `Playlist_NoBuildBR_Solo`, `Playlist_NoBuildBR_Duo`, `Playlist_NoBuildBR_Trio` y `Playlist_NoBuildBR_Squad`.

**Cualquier otro modo queda excluido de todos los totales, las gráficas, el historial, los récords, las medias y el CSV.** Los registros de otras playlists ya importados permanecen en SQLite por seguridad, pero no aparecen ni se suman. Las nuevas repeticiones de otros modos quedan marcadas como revisadas para evitar volver a analizarlas cada seis segundos.

La cabecera muestra **Tus cofres** y, a la derecha en tamaño menor, **Cofres de party**: tu total más las aperturas atribuibles a compañeros en cada partida, contando tus cofres una única vez. El total de escuadra es un **mínimo observado**, ya que puede haber compañeros sin eventos atribuibles o partidas antiguas sin desglose; nunca suma cofres de rivales.

La columna **Posición** reemplaza a Modo en la tabla y en `matches.csv`. El dato sale de `PlayerData.Placement` y, si falta, de `TeamStats.Position`. Se guarda con una columna opcional nueva en SQLite, con migración automática y sin borrar el histórico. Si falta la posición, aparece `—`. Para recuperar posiciones o escuadras antiguas cuando aún existan los replays, usa `--backfill-squad` al cerrar el monitor y sin jugar.

### Iconos y limpieza visual (v0.3.2)

- El icono original `Assets/AppIcon.ico` también se incluye como recurso integrado para el favicon de la pestaña y el logotipo junto al título. Ambas imágenes son idénticas al icono del ejecutable y funcionan con el HTML local `file://` sin archivos adicionales ni conexiones externas.
- Se elimina el texto largo sobre recuperación de compañeros, el mensaje «sin límite de 60», la coletilla del modo en la cabecera y se simplifica «Cofres de party». Las opciones técnicas de recuperación `--backfill-squad` siguen disponibles en esta documentación.

### Estadísticas y navegación (v0.2)

- **Hoy, esta semana y este mes**: cofres y número de partidas, calculados según la hora local del ordenador.
- **Diario (14 días), semanal (12 semanas) y mensual (12 meses)**: incluye periodos sin partidas con valor cero.
- Las semanas van de **lunes a domingo**, incluso cuando cambian el mes o el año.
- Pulsa una barra de la gráfica para filtrar el historial a ese periodo.
- Historial sin tope de 60 partidas, con **paginación de 25, 50 o 100**, filtros «todo», «hoy», «esta semana», «este mes», «últimos 7 días», «últimos 30 días» y periodo seleccionado.
- Al actualizarse cada 20 segundos, intenta conservar filtro y página mediante el almacenamiento de sesión del navegador, cuando esté disponible.
- El CSV sigue exportando **todo** el histórico, sin filtros ni recortes, y la base `tracker.db` no cambia.

### Horario de inicio de partidas (v0.2.4)

- **Fecha inicio** y el CSV ahora muestran la hora **local** del ordenador, con la conversión desde la marca de tiempo UTC del replay (en vez de tratarla como local).
- El lector de Fortnite proporciona `UtcTimeStartedMatch` sin `DateTimeKind`; desde esta versión se reconoce explícitamente como UTC. En España, por ejemplo, las 22:45 UTC de noviembre corresponden a las 23:45 de hora local, y las 21:45 UTC de julio corresponden a las 23:45.
- Al arrancar por primera vez v0.2.4, la aplicación **recalcula una sola vez** las horas de partidas importadas con versiones anteriores, considerando la zona horaria actual del PC y el horario de verano/invierno. Es una corrección retrospectiva estimada; no se puede saber con absoluta certeza si cada fecha antigua procedía del campo UTC o de un dato alternativo.
- Antes de actualizar `matches.started_utc`, conserva cada valor anterior en `legacy_start_times_backup`, dentro de la **misma base SQLite**; una entrada en `schema_migrations` impide repetir la conversión. No se alteran los cofres, los eventos ni los identificadores. Por seguridad, haz también una copia de `%LOCALAPPDATA%\FortniteChestTracker\tracker.db` antes de cambiar de versión, especialmente si jugaste usando otro huso horario.
- Las partidas nuevas se guardan correctamente desde el primer análisis y no se vuelven a ajustar.

### Cofres de escuadra (v0.3.0)

- En el historial aparece **una sola Fecha inicio y Posición por partida**. La columna Jugador tiene una fila para ti y otra para cada compañero **identificado en el mismo equipo durante esa partida**.
- Los cofres mostrados para compañeros son **aperturas observadas** en los datos del replay y vinculadas a un personaje. **«—» no significa cero**: quiere decir que faltan datos para atribuir cofres a ese jugador. Un `0` significa que no se observaron cofres para su personaje; no garantiza que no abriera ninguno.
- **«Mismo equipo» no equivale necesariamente a «party» social**: solo se pueden mostrar jugadores presentes en el replay y con el TeamIndex del propietario. Si el replay no incluye nombres, vínculo de personaje o equipo, pueden faltar compañeros.
- **Los récords, medias, gráficas, estadísticas diarias y CSV `matches.csv` siguen contando únicamente tus cofres**, sin sumar los de los demás. Solo el nuevo total de escuadra incluye cofres observados de compañeros; el CSV cambia Modo por Posicion.
- Las partidas nuevas tendrán automáticamente el desglose de escuadra. El histórico anterior sigue intacto y solo incluye tus datos hasta que se vuelva a analizar el replay original, que Fortnite puede haber borrado.
- Para **recuperar opcionalmente las escuadras y posiciones de partidas antiguas** (solo las que aún conservas), cierra primero la aplicación, abre PowerShell en la carpeta del ejecutable y ejecuta:

  ```powershell
  .\FortniteChestTracker.exe --backfill-squad
  ```

  Después puedes abrir la aplicación normalmente. Este proceso no duplica ni altera tus partidas personales, pero puede consumir CPU mientras vuelve a analizar los replays: **hazlo cuando no estés jugando**. Se ejecuta solo al solicitarlo; la vigilancia normal no reprocesa las partidas antiguas.
- La atribución sigue **pendiente de validación con replays reales** de partidas en dúo/trío/escuadra. Puede haber aperturas no registradas, vínculos incompletos o nombres ausentes.

### ¿No se actualizan las partidas nuevas?

1. Mantén **abierta la terminal** mientras juegas; debe indicar «Monitor activo».
2. Comprueba que Fortnite tenga activada la grabación de repeticiones.
3. Al salir de una partida, la consola informa si la repetición sigue en grabación, espera clave, se está analizando o ha fallado.
4. Al importar una partida verás «Informe actualizado». El navegador recarga la página cada 20 s; también puedes usar Ctrl+F5.
5. Los errores de lectura se guardan en `%LOCALAPPDATA%\FortniteChestTracker\errors.log`. Si hay una actualización de Fortnite, el lector puede necesitar compatibilidad adicional.

El monitor no modifica `tracker.db` ni intenta leer la memoria o el proceso de Fortnite.

### Límites conocidos

- **No** cuenta cofres en tiempo real dentro de la partida: Fortnite suele escribir la clave real del `.replay` al terminarla. Las nuevas partidas se incorporan después de su finalización, normalmente tras dos comprobaciones y un breve periodo sin cambios.
- Solo se analiza el historial de archivos todavía presentes. Fortnite elimina repeticiones antiguas.
- No se garantiza que cualquier tipo nuevo de cofre se clasifique correctamente: pueden requerirse cambios en los alias del lector.
- La v0.1 requiere identificar al jugador que grabó la repetición; si el parser no expone ese vínculo, **no inventa un cero**: registra un error y permite reintentar después.
- Para replays incompatibles, el log muestra el error. La copia del replay permanece intacta.
- No se garantiza el conteo completo de todos los compañeros: los eventos grabados pueden ser parciales o faltar la relación jugador-personaje.
- No hay aún overlay sobre el juego, instalación automática ni estadísticas en directo.

## Preparación (Windows)

1. Activa en Fortnite: **Ajustes → Juego → Repeticiones → Grabar repeticiones**.
2. Instala **Git para Windows** y el **SDK .NET 10** (no basta con el runtime).
3. Clona este repositorio o descarga el ZIP del código fuente.
4. Abre PowerShell en la raíz del proyecto y ejecuta:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\scripts\test.ps1
   powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1
   ```

5. Se generará `dist\win-x64\FortniteChestTracker.exe` (si la compilación pasa). Ejecútalo; abrirá el panel en tu navegador.

**Por qué `bootstrap.ps1` descarga código:** para analizar el formato propietario de las repeticiones necesitamos un lector. En lugar de descargar un EXE cerrado, obtenemos **únicamente el código fuente** del proyecto MIT [LED-N/Fortnite-ChestCounter](https://github.com/LED-N/Fortnite-ChestCounter) y lo dejamos fijado al commit `1f387987737cfd606611d5b8d294d5db04af9eab`. El script verifica el hash de commit. Los archivos se colocan en `third_party/` (ignorado por Git). Puedes inspeccionarlos antes de compilarlos. La compilación requiere acceso a NuGet para `Microsoft.Data.Sqlite` y al repositorio público de GitHub para obtener el lector.

### Parámetros

```powershell
.\dist\win-x64\FortniteChestTracker.exe                 # Vigila hasta pulsar Ctrl+C
.\dist\win-x64\FortniteChestTracker.exe --once          # Importa el histórico disponible y sale
.\dist\win-x64\FortniteChestTracker.exe --demos "D:\Replays"  # Carpeta personalizada
.\dist\win-x64\FortniteChestTracker.exe --no-browser     # No abre el navegador
.\dist\win-x64\FortniteChestTracker.exe --self-test      # Tests sin jugar a Fortnite
.\dist\win-x64\FortniteChestTracker.exe --backfill-squad # Reanaliza las escuadras de los replays antiguos
```

Si la ventana del programa se cierra, el contador deja de vigilar. Si aparece un error de inicio, la aplicación muestra su causa y guarda detalles en `%LOCALAPPDATA%\\FortniteChestTracker\\startup-errors.log`. Si sigue cerrándose sin mostrar nada, ejecuta el EXE desde PowerShell para conservar la salida. El historial en SQLite **no** se pierde.

## Arquitectura

```text
src/FortniteChestTracker/
  ReplayProbe.cs     Verificación parcial de cabecera (live/key)
  ReplayAnalyzer.cs  NetFieldExports → cofres del propietario y eventos de la escuadra
  SquadAttribution.cs Vinculación prudente al equipo, con datos no atribuibles
  ReplayScanner.cs   Escaneo, espera de estabilidad y manejo de errores
  TrackerStore.cs    SQLite y deduplicación transaccional
  Dashboard.cs       Informe HTML y CSV 100 % local
  Models.cs          Tipos y clasificación de cofres
  Program.cs         CLI y vigilancia de nuevas partidas
  SelfTest.cs        Tests internos (incluye transición automática de replay activo a terminado)
  Assets/dashboard.js   Interactividad local de gráficas, filtros y paginación
scripts/
  bootstrap.ps1      Descarga código abierto del lector, fijado por commit
  test.ps1           Ejecuta las comprobaciones
  build.ps1          Publica ejecutable x64 autónomo
  smoke.ps1          Comprueba el EXE descargable, SQLite y arranque en modo vigilancia
  dashboard.test.cjs Comprueba semanas, meses y paginación
  browser-smoke.cjs  Abre el HTML con Edge real y verifica gráficas, contadores e historial
.github/workflows/
  build.yml          Tests, compilación y smoke test del EXE aislado y navegador
```

## Descarga bloqueada por antivirus o SmartScreen

**No desactives Defender, SmartScreen ni añadas exclusiones.** El repositorio es abierto, pero una compilación exitosa **no garantiza** que el resultado sea inocuo. Hasta identificar la detección concreta, no se debe asumir que es un falso positivo.

El workflow publica dos tipos de distribución Windows:

- `FortniteChestTracker-win-x64`: ejecutable único de .NET (contiene bibliotecas nativas que se extraen al iniciar).
- `FortniteChestTracker-win-x64-portable`: **distribución estándar de .NET con los archivos visibles por separado**, sin autoextracción de bibliotecas. Es una alternativa para revisión y diagnóstico de seguridad, **no** un mecanismo para eludir el antivirus. Descomprime el paquete entero en una carpeta y ejecuta el EXE desde ella, pero solo una vez revisadas y resueltas las alertas de seguridad.

La distribución estándar incluye `SHA256SUMS.txt` para que puedas comprobar la integridad de los archivos después de descargarlos. También se verifica automáticamente en GitHub Actions.

Para conocer el motivo del bloqueo, abre **Seguridad de Windows → Protección contra virus y amenazas → Historial de protección** y anota el producto, nombre de detección y archivo afectado. Si el aviso aparece en Edge, comprueba la sección Descargas. Una posible clasificación incorrecta puede solicitar revisión en [Microsoft Security Intelligence](https://www.microsoft.com/wdsi/filesubmission), seleccionando **Software developer**. La firma de código Authenticode es la vía apropiada para identificar de forma verificable al editor; la distribución actual no está firmada.

## Seguridad y privacidad

- El programa solo abre los `.replay` con `FileAccess.Read` y `FileShare.ReadWrite | FileShare.Delete`.
- No hay conexión a servicios externos **en tiempo de ejecución**.
- No se manipulan los archivos del juego, el proceso Fortnite, sus DLL ni Easy Anti-Cheat.
- Los datos personales que aparezcan en la repetición (p. ej. nombre de jugador) permanecen en el ordenador. `tracker.db` y `matches.csv` no deben subirse al repositorio.
- El HTML escapa los datos leídos del replay y el CSV protege las celdas frente a fórmulas de Excel.
- El bootstrap **sí** necesita Internet, exclusivamente para descargar código fuente del lector y restaurar paquetes durante la compilación.

## Validación pendiente y colaboración

Para validar el conteo, copia dos o tres archivos `.replay` reales y ejecuta `--once`; compara las interacciones de cofres con el vídeo o con el contador de la partida. No publiques replays privados ni credenciales sin revisarlos: pueden contener nombres e identificadores. Registra en un issue la versión de Fortnite, el número esperado, el número obtenido y una descripción de las discrepancias.

## Licencias

Código original de este proyecto: MIT (ver `LICENSE`).

Adaptación técnica del seguimiento de contenedores y uso del parser: [LED-N/Fortnite-ChestCounter](https://github.com/LED-N/Fortnite-ChestCounter) (MIT, © 2026 LED-N) y su lector, basado en [Shiqan/FortniteReplayDecompressor](https://github.com/Shiqan/FortniteReplayDecompressor) (MIT). Ver `docs/THIRD_PARTY.md`. La descarga del lector conserva sus licencias originales.

Proyecto independiente, sin afiliación con Epic Games. Fortnite es marca de Epic Games, Inc.

## Cambios v0.2.2

- El gráfico ofrece solo **Diario** (por defecto) y **Mensual**. El resumen **Esta semana** y el filtro semanal del historial se mantienen.
- El EXE incorpora `Assets/AppIcon.ico`, creado a partir de la imagen facilitada por el usuario; Windows puede tardar en actualizar su caché de iconos.
