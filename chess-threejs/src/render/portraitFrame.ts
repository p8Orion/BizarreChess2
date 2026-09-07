import * as THREE from "three";

/** Camera sits 20° to the right of front. */
const CAM_YAW = 20;
const CAM_PITCH = 8;
/** Key light in front, from above, 20° to the left. */
const KEY_YAW = -20;
const KEY_PITCH = 28;

export const PORTRAIT_ICON_DIST = 1.22;
export const PORTRAIT_LIVE_DIST = 2.02;

export function portraitDir(yawDeg: number, pitchDeg: number): THREE.Vector3 {
  const yaw = THREE.MathUtils.degToRad(yawDeg);
  const pitch = THREE.MathUtils.degToRad(pitchDeg);
  return new THREE.Vector3(
    Math.sin(yaw) * Math.cos(pitch),
    Math.sin(pitch),
    Math.cos(yaw) * Math.cos(pitch)
  );
}

export function portraitCameraDir(): THREE.Vector3 {
  return portraitDir(CAM_YAW, CAM_PITCH);
}

export function addPortraitLights(scene: THREE.Scene): void {
  scene.add(new THREE.AmbientLight(0xfff6ea, 0.7));
  scene.add(new THREE.HemisphereLight(0xfff8ee, 0xcbb89a, 0.38));
  const key = new THREE.DirectionalLight(0xfff1d6, 1.45);
  key.position.copy(portraitDir(KEY_YAW, KEY_PITCH).multiplyScalar(6));
  scene.add(key);
  const fill = new THREE.DirectionalLight(0xc9a15b, 0.22);
  fill.position.copy(portraitDir(CAM_YAW + 40, 12).multiplyScalar(5));
  scene.add(fill);
}
