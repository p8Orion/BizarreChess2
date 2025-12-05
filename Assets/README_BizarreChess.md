# Bizarre Chess - Setup Guide

## Requisitos

### Packages necesarios (Window > Package Manager)

1. **Netcode for GameObjects**
   - Add package by name: `com.unity.netcode.gameobjects`

2. **Unity Transport**
   - Add package by name: `com.unity.transport`

3. **TextMeshPro** (generalmente ya instalado)
   - Si no está, usar: `com.unity.textmeshpro`

4. **Input System** (para UI)
   - Add package by name: `com.unity.inputsystem`
   - En Player Settings > Active Input Handling > seleccionar "Both" o "Input System Package"

## Setup Rápido

### Opción A: Usar el menú de editor
1. Abrir cualquier escena
2. Ir a menú **Bizarre Chess > Setup Current Scene**
3. Guardar la escena
4. Play!

### Opción B: Abrir la escena existente
1. Abrir `Assets/Scenes/MultiplayerChess.unity`
2. Si los scripts no están enlazados, usar **Bizarre Chess > Setup Current Scene**

## Cómo Jugar

### Modo Offline (Testing)
1. Click "Play Offline" en el menú
2. Click en una pieza para seleccionarla
3. Click en una casilla verde para mover
4. El turno cambia automáticamente

### Multiplayer - Host (Jugador 1)
1. Click "Host Game"
2. Esperar a que se conecte el oponente
3. El host juega como "blancas" (primera fila)

### Multiplayer - Join (Jugador 2)
1. Ingresar la IP del host (o dejar 127.0.0.1 para local)
2. Click "Join Game"
3. El cliente juega como "negras" (última fila)

## Control de Piezas

- Cada jugador SOLO puede controlar sus propias piezas
- El servidor valida todos los movimientos
- Si intentas mover una pieza del oponente, no pasa nada
- Si intentas hacer un movimiento inválido, se rechaza

## Arquitectura

```
Scripts/
├── Core/           # Lógica pura (sin Unity dependencies para el core)
│   ├── Graph/      # Tablero como grafo
│   ├── Units/      # Definiciones y estado de unidades
│   ├── Rules/      # Validación de movimientos
│   ├── Armies/     # Composición de ejércitos
│   └── Factories/  # Creación de assets clásicos
│
├── Networking/     # Netcode for GameObjects
│   ├── GameNetworkManager.cs    # Host/Client/Server
│   └── NetworkedGameState.cs    # Estado sincronizado
│
├── Persistence/    # Perfiles (separado del game server)
│   ├── IProfileService.cs       # Interface abstracta
│   └── MockProfileService.cs    # Local para desarrollo
│
├── Presentation/   # Rendering
│   ├── BoardRenderer.cs
│   ├── TileRenderer.cs
│   ├── UnitRenderer.cs
│   └── GameUI.cs
│
└── GameManager.cs  # Conecta todo
```

## Separación de Servidores

- **Game Server** (Netcode): Maneja la partida en curso
  - Host mode: Un jugador hostea
  - Dedicated: Servidor separado para ranked
  
- **Profile Service** (Cloud): Maneja datos persistentes
  - Perfiles de jugador
  - Progresión de unidades
  - Ejércitos guardados
  - ⚠️ SEPARADO del game server

## Próximos Pasos

1. [ ] Instalar Netcode packages
2. [ ] Configurar WebSocket transport para WebGL
3. [ ] Implementar ProfileService real (PlayFab/Firebase/UGS)
4. [ ] Crear editor visual de grafos
5. [ ] Agregar piezas bizarras custom
6. [ ] Sistema de habilidades
7. [ ] Generación procedural de mapas

## Troubleshooting

### "Scripts not found"
- Verificar que los packages están instalados
- Window > Package Manager > verificar Netcode y Transport

### "Cannot connect"
- Verificar firewall
- Verificar que ambos usan el mismo puerto (default: 7777)
- Para WebGL: necesita WebSocket transport configurado

### "Turn not changing"
- En modo offline, el turno cambia automáticamente después de cada movimiento
- En multiplayer, esperar que ambos jugadores estén conectados

