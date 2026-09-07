import * as THREE from "three";
import type { ItemState } from "../core/types";
import { PORTRAIT_LIVE_DIST, addPortraitLights, portraitCameraDir } from "../render/portraitFrame";

const SIZE = 72;
const cache = new Map<string, string>();
const pending = new Map<string, Promise<string>>();

let loadVisual: ((item: ItemState) => Promise<THREE.Group>) | null = null;
let renderer: THREE.WebGLRenderer | null = null;
let scene: THREE.Scene | null = null;
let camera: THREE.PerspectiveCamera | null = null;

export function bindItemIconLoader(load: (item: ItemState) => Promise<THREE.Group>): void {
  loadVisual = load;
}

function iconKey(item: ItemState): string {
  return `q4:${item.kind}:${item.model ?? item.shape}:${item.color}`;
}

function ensureRenderer(): { renderer: THREE.WebGLRenderer; scene: THREE.Scene; camera: THREE.PerspectiveCamera } {
  if (renderer && scene && camera) return { renderer, scene, camera };
  const canvas = document.createElement("canvas");
  canvas.width = SIZE;
  canvas.height = SIZE;
  renderer = new THREE.WebGLRenderer({ canvas, alpha: true, antialias: true, powerPreference: "low-power" });
  renderer.setSize(SIZE, SIZE, false);
  renderer.setClearColor(0xfaf3e6, 1);
  renderer.outputColorSpace = THREE.SRGBColorSpace;
  scene = new THREE.Scene();
  addPortraitLights(scene);
  camera = new THREE.PerspectiveCamera(34, 1, 0.08, 20);
  return { renderer, scene, camera };
}

function disposeTree(root: THREE.Object3D): void {
  root.traverse((child) => {
    const mesh = child as THREE.Mesh;
    if (!mesh.isMesh) return;
    const mats = Array.isArray(mesh.material) ? mesh.material : [mesh.material];
    for (const mat of mats) mat.dispose();
  });
}

function fallbackUrl(item: ItemState): string {
  const canvas = document.createElement("canvas");
  canvas.width = SIZE;
  canvas.height = SIZE;
  const ctx = canvas.getContext("2d");
  if (!ctx) return "";
  ctx.fillStyle = "#faf3e6";
  ctx.fillRect(0, 0, SIZE, SIZE);
  ctx.fillStyle = item.color || "#c9a15b";
  ctx.beginPath();
  ctx.arc(SIZE / 2, SIZE / 2, SIZE * 0.28, 0, Math.PI * 2);
  ctx.fill();
  return canvas.toDataURL("image/png");
}

async function snapshot(item: ItemState): Promise<string> {
  if (!loadVisual) return fallbackUrl(item);
  try {
    const visual = await loadVisual(item);
    const ctx = ensureRenderer();
    ctx.scene.add(visual);
    visual.updateWorldMatrix(true, true);
    const box = new THREE.Box3().setFromObject(visual);
    if (!box.isEmpty()) {
      const size = box.getSize(new THREE.Vector3());
      const center = box.getCenter(new THREE.Vector3());
      const radius = Math.max(size.x, size.y, size.z) * 0.5;
      const fov = (ctx.camera.fov * Math.PI) / 180;
      const dist = (radius / Math.tan(fov / 2)) * PORTRAIT_LIVE_DIST;
      ctx.camera.position.copy(center).addScaledVector(portraitCameraDir(), dist);
      ctx.camera.near = Math.max(0.04, dist - radius * 2.4);
      ctx.camera.far = dist + radius * 5;
      ctx.camera.lookAt(center.x, center.y + size.y * 0.02, center.z);
      ctx.camera.updateProjectionMatrix();
    }
    ctx.renderer.render(ctx.scene, ctx.camera);
    const url = ctx.renderer.domElement.toDataURL("image/png");
    ctx.scene.remove(visual);
    disposeTree(visual);
    return url || fallbackUrl(item);
  } catch {
    return fallbackUrl(item);
  }
}

export function itemIconUrl(item: ItemState): Promise<string> {
  const key = iconKey(item);
  const hit = cache.get(key);
  if (hit) return Promise.resolve(hit);
  const inflight = pending.get(key);
  if (inflight) return inflight;
  const job = snapshot(item).then((url) => {
    cache.set(key, url);
    pending.delete(key);
    return url;
  });
  pending.set(key, job);
  return job;
}
