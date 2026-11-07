# Colonia 09

Juego de defensa táctica para iOS y Android, hecho en Unity 6 (6000.6.7f1). Tablero vertical de 7 × 11, 8 módulos, 14 enemigos y 9 oleadas.

## Estructura

- `Assets/Scripts/Simulation`: reglas del juego en C# puro, sin dependencias de Unity.
- `Assets/Scripts/Presentation`: tablero, interfaz y prueba automática de desarrollo.
- `Assets/Editor/ProjectSetup.cs`: configuración del proyecto y compilaciones (menú **Colonia 09**).
- `Tools/Parity`: compara la simulación con el prototipo HTML original de `Tools/Reference`.

## Comandos

Cierra el Editor de Unity antes de ejecutar estos scripts.

```bash
powershell -ExecutionPolicy Bypass -File Tools/run-autotest.ps1
```

Prueba la interfaz en cuatro formatos de pantalla.

```bash
dotnet run -c Release --project Tools/Parity/ParityRunner -- Tools/Parity
```

Compara la simulación con el prototipo. Antes hay que generar las referencias con `Tools/Parity/run-js.ps1`.

```bash
powershell -ExecutionPolicy Bypass -File Tools/publish-ios.ps1
```

Genera el proyecto de Xcode y lo publica como release, lo que inicia la compilación de iOS en GitHub Actions.

## iOS

La compilación corre en `.github/workflows/ios.yml`. Sin los secrets `ASC_KEY_ID`, `ASC_ISSUER_ID`, `ASC_KEY_P8` y `APPLE_TEAM_ID`, solo compila sin firmar. Con ellos, firma con `fastlane match` (rama `certificados`) y sube a TestFlight.
