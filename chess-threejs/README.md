# Bizarre Chess — Three.js

Client + server port of the Unity rules core. Rendering and connectivity live here; movement, captures, forcefield, checkmate and stalemate come from `src/core`.

## Run

```bash
cd chess-threejs
npm install
npm start
```

- UI: http://localhost:5173
- WebSocket (validation + rooms): `ws://127.0.0.1:8787/ws`

`Play Offline` is hotseat and does not need the server. `Host` / `Join` do.

## Layout

- `src/core` — board, pieces, items, movement, validator, game state
- `src/render` — Three.js wood board, GLB + clips, forcefield/markers, camera
- `src/net` + `server` — host/join rooms, server-side move and pickup checks

Piece meshes are copied from `Assets/Resources/Pieces/Models`.
