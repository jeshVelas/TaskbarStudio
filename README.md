# Taskbar Studio · Fase 0

Dos prototipos de escritorio independientes para Windows 11 x64. El primero permite probar y revertir la ocultación automática de la barra del usuario. El segundo muestra un dock flotante de tres accesos.

**El dock B queda aprobado provisionalmente para la Fase 1. La Fase 0 sigue abierta.** Antes de la publicación se recompilaron los tres proyectos en Release x64 con cero errores y advertencias. Pasaron las 13 pruebas automatizadas de ABI y lógica. [Resultados y criterios pendientes](docs/PRUEBAS-FASE-0.md)

**A · Ocultación nativa.** En Windows 11 25H2 (26200.9974) se habían observado 40 ciclos reales de cambio y restauración con la barra abajo, 20 por estado inicial. En la verificación de cierre, con la barra a la izquierda, A bloqueó la escritura. La sonda confirmó la misma posición, área de trabajo y ocultación al salir, sin registro de recuperación pendiente. Quedan la recuperación tras una interrupción real y la validación en otra compilación. [Ensayos de A](docs/PRUEBAS-FASE-0.md)

**B · Dock flotante.** En el cierre completó 50 ciclos con cero procesos residuales y un P95 de 361 ms en los 49 arranques posteriores al primero (objetivo ≤ 750 ms). Una misma instancia con 20 accesos permaneció abierta dos horas y cerró normalmente. Las series anteriores verificaron el aislamiento de la barra con la ocultación activada y desactivada, y cumplieron los límites de CPU y memoria.

Dos pares controlados de GPU dieron **0,0000 y 0,1928 pp** de incremento del escritorio, dentro del límite de **0,500 pp**. La entrada de usuario interrumpió el tercero. Quedan ese par, la traza para explicar el exceso previo de **0,545 pp**, los reinicios de Explorer, la suspensión, los arranques en frío y la matriz de interacción y pantallas. [Mediciones y criterios pendientes](docs/PRUEBAS-FASE-0.md)

## Compilar y ejecutar

En Windows x64 se necesita el SDK .NET 10 x64 para compilar. Para ejecutar los binarios dependientes del framework se necesita el **.NET 10 Desktop Runtime x64**, incluido en el SDK de Windows. El runtime básico por sí solo carece de WinForms. Los proyectos usan las bibliotecas del framework y no declaran paquetes NuGet. [Descargas oficiales de .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0)

Desde la raíz del repositorio, en PowerShell con cuenta estándar:

```powershell
dotnet build TaskbarStudio.slnx -c Release -p:PlatformTarget=x64
.\src\NativeTrial\bin\Release\net10.0-windows\NativeTrial.exe
.\src\FloatingDock\bin\Release\net10.0-windows\FloatingDock.exe
```

Si se usa un SDK portátil, establezca `$env:DOTNET_ROOT` al directorio del SDK y sustituya el comando `dotnet` por `& "$env:DOTNET_ROOT\dotnet.exe"` en esa sesión.

## Usar los prototipos

**A · Prueba nativa.** Consulte el estado, active o desactive la ocultación y observe el efecto en la barra. Cada escritura va seguida de una lectura del Shell. Puede restaurar el estado anterior, conservar la elección o cerrar la prueba activa para restaurarla. La ventana vuelve a consultar al recuperar el foco y mediante **Consultar estado**. La aplicación usa el contrato de mensajes del Shell con una ventana propia. [Consulta de estado](https://learn.microsoft.com/windows/win32/shell/abm-getstate) · [Cambio de estado](https://learn.microsoft.com/windows/win32/shell/abm-setstate)

Antes de solicitar un cambio, A guarda el estado previo en `%LOCALAPPDATA%\TaskbarStudio\NativeTrial\pending.v1`. Un archivo de sesión abierto en exclusiva impide que dos instancias se disputen esa recuperación. Tras una interrupción, volver a abrir A muestra la decisión pendiente. **Restaurar** verifica el estado y **Conservar** descarta el registro sin escribir en el Shell. Cerrar una sesión recuperada deja esa decisión disponible para después. Si Explorer está indisponible, vuelva a consultar cuando se haya recuperado.

**B · Dock.** Abra Bloc de notas, Calculadora o Explorador mediante clic, o recorra los botones con Tab y Mayús+Tab y active el botón enfocado con Espacio o Entrar. **Cerrar** y Escape terminan el proceso. La posición inicial se calcula en el área de trabajo y se recalcula al cambiar la pantalla. El dock usa una ventana normal, puede quedar cubierta por otras ventanas y no reserva espacio. Los iconos son dibujos geométricos propios. Los destinos deben estar disponibles en el equipo.

## Estructura y pruebas

| Ruta | Contenido |
|---|---|
| `TaskbarStudio.slnx`, `Directory.Build.props` | Solución, plataforma x64 y configuración común. |
| `src/NativeTrial/` | Interfaz, acceso al Shell y transacción local de recuperación. |
| `src/FloatingDock/` | Ventana, accesos, dibujos y cálculo de posición. |
| `tests/Phase0.Tests/` | Comprobación del ABI x64 y pruebas de lógica con Shell simulado y directorios temporales. |
| `tests/Test-NativeTrial.ps1`, `tests/Measure-Dock.ps1` | Pruebas de los ejecutables en un escritorio interactivo. |
| `docs/PRUEBAS-FASE-0.md` | Protocolo, mediciones realizadas y verificaciones pendientes. |
| `.gitignore` | Exclusión de binarios y archivos de compilación. |

```powershell
dotnet run --project tests/Phase0.Tests -c Release --no-build
.\tests\Test-NativeTrial.ps1
.\tests\Measure-Dock.ps1
```

El script de A consulta y cierra la ventana. En una sesión de prueba, añadir `-ExerciseChange` solicita cambiar temporalmente el ajuste real, verificarlo y restaurarlo. El script del dock realiza 50 ciclos de apertura y cierre, comprueba los cuatro botones a los DPI de la ventana y registra los handles y objetos GDI y USER de cada proceso. También compara el área de trabajo, la geometría y los indicadores de la barra primaria antes, durante y después. La pasada de reposo mide CPU, memoria privada y GPU frente a una referencia del escritorio. [Protocolo y resultados](docs/PRUEBAS-FASE-0.md)
