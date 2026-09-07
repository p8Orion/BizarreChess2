import * as THREE from "three";

const LIGHT = "#c4c1bb";
const DARK = "#9a9791";

/** Square checkerboard under a portrait piece — baked into the snapshot. */
export function createCheckerGround(tiles = 12, tileSize = 1): THREE.Mesh {
  const px = 48;
  const canvas = document.createElement("canvas");
  canvas.width = canvas.height = tiles * px;
  const ctx = canvas.getContext("2d")!;
  for (let y = 0; y < tiles; y++) {
    for (let x = 0; x < tiles; x++) {
      ctx.fillStyle = (x + y) % 2 === 0 ? LIGHT : DARK;
      ctx.fillRect(x * px, y * px, px, px);
    }
  }
  const map = new THREE.CanvasTexture(canvas);
  map.colorSpace = THREE.SRGBColorSpace;
  map.magFilter = THREE.NearestFilter;
  map.minFilter = THREE.NearestFilter;
  map.generateMipmaps = false;
  const geo = new THREE.PlaneGeometry(tiles * tileSize, tiles * tileSize);
  geo.rotateX(-Math.PI / 2);
  const mat = new THREE.MeshStandardMaterial({
    map,
    color: 0xb8b5b0,
    roughness: 0.94,
    metalness: 0,
  });
  const mesh = new THREE.Mesh(geo, mat);
  mesh.position.y = -0.004;
  mesh.name = "portraitGround";
  return mesh;
}
