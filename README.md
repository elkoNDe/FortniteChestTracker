# Fortnite ChestTracker

[![Build and test Windows](https://github.com/elkoNDe/FortniteChestTracker/actions/workflows/build.yml/badge.svg)](https://github.com/elkoNDe/FortniteChestTracker/actions/workflows/build.yml)
![Version](https://img.shields.io/badge/version-0.3.4-blue)
![License](https://img.shields.io/badge/license-MIT-green)

**Contador local de cofres de Fortnite para Windows 10/11.** Analiza los archivos de repetición (`.replay`) al acabar cada partida y ofrece estadísticas personales, cofres observados del equipo y un historial con gráficas en el navegador.

No lee la memoria del juego, no modifica Fortnite, no solicita credenciales de Epic Games y no envía tus estadísticas a un servidor.

> **Estado: v0.3.4.** El proyecto pasa las pruebas automáticas y se compila en GitHub Actions, pero la precisión del conteo y la atribución de compañeros aún dependen del formato y del contenido real de las repeticiones. Las actualizaciones de Fortnite pueden requerir ajustes.

## Qué hace

- **Estadísticas por periodos:** Hoy, Esta semana, Este mes, gráfica diaria/mensual y filtros del historial muestran tanto tus cofres como los observados de la party (incluidos los tuyos).
- **Tus cofres:** aperturas, destruidos, récord, media, estadísticas diarias y mensuales.
- **Cofres de party:** suma tus cofres una sola vez y añade los cofres *observados y atribuibles* al resto de tu equipo.
- **Historial:** fecha de inicio y de fin en dos líneas de una misma columna Fecha, duración grabada, filtros por fechas y posición final cuando está disponible. Paginación de 10 en 10 (10–100 partidas por página).
- **Detección automática:** revisa las repeticiones cada 6 segundos y procesa las partidas al terminar su grabación.
- **Panel HTML local:** `dashboard.html` se actualiza cada 20 segundos y permite examinar las gráficas por periodos.
- **Datos propios:** SQLite (`tracker.db`) y exportación a `matches.csv`. Las partidas se deduplican por sesión.

### Modos admitidos

Se cuentan únicamente estas playlists de **Battle Royale cero construcción** (el unico modo bueno):

- `Playlist_NoBuildBR_Solo`
- `Playlist_NoBuildBR_Duo`
- `Playlist_NoBuildBR_Trio`
- `Playlist_NoBuildBR_Squad`

Los demás modos quedan fuera de las estadísticas, las gráficas, el historial y el CSV. Los datos antiguos de otras playlists se conservan en SQLite, pero no se muestran.

**Duración de la partida:** representa la duración **grabada en el replay**, que puede incluir tiempo en modo espectador o acabar antes de que termine la partida para el resto del equipo. La fecha fin se calcula a partir del inicio más esa duración. La aplicación intenta recuperar automáticamente la duración de partidas antiguas mientras sus archivos `.replay` sigan disponibles. En otros casos muestra «—» sin inventar una hora.

**Nota sobre la party:** el programa identifica el equipo de la partida a partir de `TeamIndex`; no puede comprobar si los jugadores pertenecían a tu grupo de amigos del lobby. Un «—» significa que faltan datos para atribuir aperturas, **no cero cofres**. Por ello, el total del equipo es un mínimo observado.

## Descargar y usar

1. Activa en Fortnite **Ajustes → Juego → Repeticiones → Grabar repeticiones**.
2. Descarga **[FortniteChestTracker.exe](https://github.com/elkoNDe/FortniteChestTracker/releases/latest/download/FortniteChestTracker.exe)** desde la [última release](https://github.com/elkoNDe/FortniteChestTracker/releases/latest): un único archivo, sin ZIP y sin instalar .NET.
3. Ejecuta `FortniteChestTracker.exe`. Se abrirá el panel en el navegador. Mantén la aplicación abierta mientras juegas para importar nuevas partidas.

Si prefieres inspeccionar las dependencias por separado, en [GitHub Actions](https://github.com/elkoNDe/FortniteChestTracker/actions/workflows/build.yml) se sigue generando la distribución `FortniteChestTracker-win-x64-portable` con hashes SHA-256.

Por defecto, lee los replays de `%LOCALAPPDATA%\FortniteGame\Saved\Demos` y guarda los datos en `%LOCALAPPDATA%\FortniteChestTracker`. **Cambiar el EXE no borra tus estadísticas.**

Los ejecutables no están firmados digitalmente. Si Windows muestra una advertencia, comprueba el motivo antes de ejecutarlos; **no desactives Defender ni SmartScreen**.

### Parámetros

Desde PowerShell, en la carpeta del ejecutable:

```powershell
.\FortniteChestTracker.exe                     # Vigilar partidas nuevas
.\FortniteChestTracker.exe --once              # Importar el histórico disponible y salir
.\FortniteChestTracker.exe --no-browser        # No abrir el navegador
.\FortniteChestTracker.exe --demos "D:\Replays" # Otra carpeta de repeticiones
.\FortniteChestTracker.exe --self-test         # Pruebas internas
.\FortniteChestTracker.exe --backfill-squad    # Recuperar equipo/posición de replays antiguos
```

`--backfill-squad` es opcional: ejecútalo con el monitor cerrado y preferiblemente sin jugar. Solo recupera información si los archivos originales todavía existen; no duplica tus partidas.

### Si no aparecen partidas nuevas

Comprueba que Fortnite esté guardando repeticiones y que la consola muestre «Monitor activo». El recuento **no ocurre durante la partida**: comienza cuando el replay se puede leer correctamente. El panel se recarga cada 20 segundos.

Los errores se guardan en `%LOCALAPPDATA%\FortniteChestTracker\errors.log` y los errores de arranque en `startup-errors.log`, en la misma carpeta.

## Compilar desde el código fuente

Necesitas **Git para Windows y el SDK .NET 10**.

```powershell
git clone https://github.com/elkoNDe/FortniteChestTracker.git
cd FortniteChestTracker
powershell -ExecutionPolicy Bypass -File .\scripts\test.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

El EXE se crea en `dist\win-x64\FortniteChestTracker.exe`. Para generar la distribución con dependencias separadas, ejecuta `scripts\build-portable.ps1`.

La compilación descarga código fuente del lector abierto [LED-N/Fortnite-ChestCounter](https://github.com/LED-N/Fortnite-ChestCounter), fijado en `scripts/bootstrap.ps1` al commit `1f387987737cfd606611d5b8d294d5db04af9eab`, y restaura dependencias NuGet. **Compilar requiere Internet; ejecutar el contador no.** Consulta [docs/THIRD_PARTY.md](docs/THIRD_PARTY.md) para las atribuciones y licencias.

## Seguridad y privacidad

- El juego y sus `.replay` se leen en **modo de solo lectura**. No se modifican archivos ni DLL de Fortnite, ni se interactúa con su sistema antitrampas.
- Los nombres e identificadores extraídos del replay se almacenan localmente. **No publiques `tracker.db`, `matches.csv`, `dashboard.html` ni archivos `.replay`**: podrían revelar estadísticas o nombres de jugadores.
- `.gitignore` excluye estos datos generados, junto con los archivos de log, pero siempre conviene revisar `git status` antes de publicar cambios.
- La salida HTML escapa los nombres de jugadores y el CSV incorpora protección ante fórmulas maliciosas de Excel.
- La descarga de dependencias durante la compilación debe tratarse con las precauciones habituales de cualquier proyecto de código abierto.

## Limitaciones

- No funciona como contador en directo dentro del juego ni tiene overlay o instalador.
- Fortnite puede borrar replays antiguos o cambiar su formato. Las partidas ya importadas se conservan en la base de datos.
- Algunos tipos de cofres, posiciones y compañeros pueden no identificarse completamente; cuando no hay datos suficientes se evita inventar cifras.
- El EXE no dispone todavía de firma de código.
- Si vienes de versiones anteriores, haz una copia de `tracker.db` antes de actualizar. Una migración anterior corrigió la interpretación de fechas UTC y conservó un respaldo interno de las marcas originales.

## Estructura del proyecto

| Archivo | Función |
| --- | --- |
| `ReplayProbe.cs` / `ReplayAnalyzer.cs` | Leer cabeceras y detectar eventos del replay |
| `SquadAttribution.cs` | Atribuir cofres observados a jugadores del equipo |
| `ReplayScanner.cs` | Vigilar archivos, esperar a que terminen y reintentar |
| `TrackerStore.cs` | SQLite, deduplicación y migraciones |
| `Dashboard.cs` / `Assets/dashboard.js` | Panel HTML interactivo y CSV |
| `SelfTest.cs` / `scripts/*.cjs` | Pruebas de lógica y navegador |
| `.github/workflows/build.yml` | Validación y creación de artefactos de Windows |

Para informar de un error, [abre un issue](https://github.com/elkoNDe/FortniteChestTracker/issues) con la versión del juego, el modo y el resultado esperado/observado. Evita adjuntar replays con datos personales sin revisarlos.

## Desarrollo con ayuda de IA

**Proyecto personal de elkoNDe**, desarrollado con apoyo de **herramientas de inteligencia artificial generativa** para programación, revisión de código, pruebas y documentación. El autor dirige el desarrollo y decide los cambios que se publican. El uso de IA no garantiza ausencia de errores ni sustituye la validación con partidas reales.

## Licencia y créditos

El código propio está publicado bajo **MIT** (ver [LICENSE](LICENSE)).

Se emplea código y conocimiento técnico de [LED-N/Fortnite-ChestCounter](https://github.com/LED-N/Fortnite-ChestCounter), cuyo lector deriva de [Shiqan/FortniteReplayDecompressor](https://github.com/Shiqan/FortniteReplayDecompressor), ambos con licencia MIT. Deben mantenerse sus licencias y avisos de copyright. Más información en [docs/THIRD_PARTY.md](docs/THIRD_PARTY.md).

Proyecto independiente **no afiliado ni respaldado por Epic Games**. Fortnite es marca de Epic Games, Inc.
