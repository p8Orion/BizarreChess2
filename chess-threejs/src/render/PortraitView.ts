import * as THREE from "three";
import type { ItemState, UnitState } from "../core/types";

export class PortraitView {
  private readonly scene = new THREE.Scene();
  private readonly camera = new THREE.PerspectiveCamera(28, 1, 0.08, 20);
  private readonly renderer: THREE.WebGLRenderer;
  private readonly clock = new THREE.Clock();
  private visual: THREE.Object3D | null = null;
  private token = 0;
  private raf = 0;
  private visible = false;
  private currentKey = "";

  constructor(
    private readonly canvas: HTMLCanvasElement,
    private readonly loadVisual: (unit: UnitState) => Promise<THREE.Group>,
    private readonly loadItemVisual?: (item: ItemState) => Promise<THREE.Group>
  ) {
    this.renderer = new THREE.WebGLRenderer({
      canvas,
      alpha: true,
      antialias: true,
      powerPreference: "low-power",
    });
    this.renderer.setClearColor(0xfaf3e6, 1);
    this.renderer.outputColorSpace = THREE.SRGBColorSpace;
    this.scene.add(new THREE.AmbientLight(0xfff6ea, 0.78));
    this.scene.add(new THREE.HemisphereLight(0xfff8ee, 0xcbb89a, 0.42));
    const key = new THREE.DirectionalLight(0xfff1d6, 1.35);
    key.position.set(1.2, 2.4, 3.4);
    this.scene.add(key);
    const rim = new THREE.DirectionalLight(0xc9a15b, 0.35);
    rim.position.set(-2.2, 1.4, -1.6);
    this.scene.add(rim);
    this.resize();
    window.addEventListener("resize", this.resize);
    this.loop();
  }

  dispose(): void {
    cancelAnimationFrame(this.raf);
    window.removeEventListener("resize", this.resize);
    this.clear();
    this.renderer.dispose();
  }

  clear(): void {
    this.token += 1;
    this.currentKey = "";
    this.visible = false;
    if (!this.visual) return;
    this.scene.remove(this.visual);
    this.visual.traverse((child) => {
      const mesh = child as THREE.Mesh;
      if (!mesh.isMesh) return;
      const mats = Array.isArray(mesh.material) ? mesh.material : [mesh.material];
      for (const mat of mats) mat.dispose();
    });
    this.visual = null;
    this.renderer.clear();
  }

  show(unit: UnitState | null): void {
    if (!unit) {
      this.clear();
      return;
    }
    this.showLoaded(
      `unit:${unit.unitId}:${unit.definitionId}:${unit.ownerId}:${unit.heldItem?.id ?? unit.heldItem?.kind ?? ""}`,
      () => this.loadVisual(unit)
    );
  }

  showItem(item: ItemState | null): void {
    if (!item || !this.loadItemVisual) {
      this.clear();
      return;
    }
    this.showLoaded(`item:${item.id}:${item.kind}:${item.model ?? item.shape}`, () => this.loadItemVisual!(item));
  }

  private showLoaded(key: string, load: () => Promise<THREE.Group>): void {
    if (key === this.currentKey) {
      this.visible = true;
      return;
    }
    this.currentKey = key;
    this.visible = true;
    const token = ++this.token;
    void load().then((visual) => {
      if (token !== this.token) {
        this.disposeTree(visual);
        return;
      }
      if (this.visual) this.disposeTree(this.visual);
      this.visual = visual;
      this.scene.add(visual);
      this.frame(visual);
    });
  }

  private disposeTree(root: THREE.Object3D): void {
    this.scene.remove(root);
    root.traverse((child) => {
      const mesh = child as THREE.Mesh;
      if (!mesh.isMesh) return;
      const mats = Array.isArray(mesh.material) ? mesh.material : [mesh.material];
      for (const mat of mats) mat.dispose();
    });
  }

  private frame(object: THREE.Object3D): void {
    object.updateWorldMatrix(true, true);
    const box = new THREE.Box3().setFromObject(object);
    if (box.isEmpty()) return;
    const size = box.getSize(new THREE.Vector3());
    const center = box.getCenter(new THREE.Vector3());
    const fov = (this.camera.fov * Math.PI) / 180;
    const fitH = size.y / (2 * Math.tan(fov / 2));
    const fitW = size.x / (2 * Math.tan(fov / 2) * this.camera.aspect);
    const dist = Math.max(fitH, fitW, size.z * 0.55) * 1.42;
    this.camera.position.set(center.x, center.y + size.y * 0.06, center.z + dist);
    this.camera.near = Math.max(0.05, dist - size.z - 1);
    this.camera.far = dist + size.z + 4;
    this.camera.lookAt(center.x, center.y + size.y * 0.02, center.z);
    this.camera.updateProjectionMatrix();
  }

  private readonly resize = (): void => {
    const css = Math.max(this.canvas.clientWidth || 88, 64);
    const px = Math.round(css * Math.min(devicePixelRatio, 2));
    this.renderer.setSize(px, px, false);
    this.camera.aspect = 1;
    this.camera.updateProjectionMatrix();
    if (this.visual) this.frame(this.visual);
  };

  private readonly loop = (): void => {
    this.raf = requestAnimationFrame(this.loop);
    if (!this.visible || !this.visual) return;
    this.clock.getDelta();
    this.renderer.render(this.scene, this.camera);
  };
}
