// Ameliyathane: masa, hasta (60° sağ lateral dekübit, sol taraf yukarıda), örtüler, trokarlar, monitör
import * as THREE from 'three';
import { V } from './util.js';
import { anteriorSurface, TORSO } from './anatomy.js';

const D2R = Math.PI / 180;

// Transperitoneal sol LPN port yerleşimi (hasta-yerel, Bkz. README):
//  kamera: umbilikus lateral/kraniyali (12 mm), sol el: subkostal midklaviküler (12 mm),
//  sağ el: umbilikus altı lateral rektus kenarı (12 mm), asistan: suprapubik/medial (5 mm)
export const PORTS = {
  camera: { x: 0.032, y: -0.03, d: 12 },
  left: { x: 0.055, y: 0.095, d: 12 },
  right: { x: 0.092, y: -0.115, d: 12 },
  assist: { x: -0.01, y: -0.14, d: 5 },
};

export function buildRoom(scene, anatomy) {
  const R = { };
  const orRoot = new THREE.Group(); orRoot.name = 'orRoot'; scene.add(orRoot);
  R.root = orRoot;

  // Oda (sabit; kalibrasyonda hareket etmez)
  const env = new THREE.Group(); env.name = 'env'; scene.add(env); R.env = env;
  const floor = new THREE.Mesh(new THREE.PlaneGeometry(8, 8), new THREE.MeshStandardMaterial({ color: 0x5f7f86, roughness: 0.8 }));
  floor.rotation.x = -Math.PI / 2; env.add(floor);
  const wallM = new THREE.MeshStandardMaterial({ color: 0x9fbfc2, roughness: 0.9 });
  for (const [x, z, ry] of [[0, -3.2, 0], [-3.5, 0, Math.PI / 2], [3.5, 0, -Math.PI / 2], [0, 3.5, Math.PI]]) {
    const w = new THREE.Mesh(new THREE.PlaneGeometry(8, 3.2), wallM); w.position.set(x, 1.6, z); w.rotation.y = ry; env.add(w);
  }
  const lampM = new THREE.MeshStandardMaterial({ color: 0xffffff, emissive: 0xffffff, emissiveIntensity: 0.8 });
  for (const x of [-1.2, 1.2]) { const l = new THREE.Mesh(new THREE.CylinderGeometry(0.28, 0.32, 0.08, 32), lampM); l.position.set(x, 2.4, -0.6); env.add(l); }

  // Hasta grubu
  const patient = new THREE.Group(); patient.name = 'patient';
  const qBase = new THREE.Quaternion().setFromRotationMatrix(new THREE.Matrix4().makeBasis(V(0, 1, 0), V(-1, 0, 0), V(0, 0, 1)));
  const qRoll = new THREE.Quaternion().setFromAxisAngle(V(1, 0, 0), -30 * D2R);
  patient.quaternion.copy(qRoll).multiply(qBase);
  patient.position.set(0, 1.0, -0.55);
  patient.add(anatomy.root);
  orRoot.add(patient);
  R.patient = patient;
  patient.updateMatrixWorld(true);

  // Masa: hastanın en alt noktasının altına
  let minY = Infinity;
  for (let i = 0; i < 64; i++) {
    const th = i / 64 * Math.PI * 2;
    const p = V((TORSO.a) * Math.sin(th), 0, TORSO.cz + TORSO.b * Math.cos(th));
    patient.localToWorld(p); minY = Math.min(minY, p.y);
  }
  const centerW = patient.localToWorld(V(-0.1, 0, 0.0));
  const tableM = new THREE.MeshStandardMaterial({ color: 0x2b3238, roughness: 0.5, metalness: 0.3 });
  const top = new THREE.Mesh(new THREE.BoxGeometry(2.0, 0.08, 0.55), new THREE.MeshStandardMaterial({ color: 0x1d4f73, roughness: 0.7 }));
  top.position.set(centerW.x - 0.25, minY - 0.04, centerW.z); orRoot.add(top);
  const col = new THREE.Mesh(new THREE.CylinderGeometry(0.12, 0.16, 2.0, 24), tableM); // uzun: kalibrasyonda yere gömülür
  col.position.set(centerW.x - 0.25, minY - 0.08 - 1.0, centerW.z); orRoot.add(col);

  // Örtüler (baş ve bacak tarafı) + gövde örtüsü pencereli
  const drapeM = new THREE.MeshStandardMaterial({ color: 0x2f6fa8, roughness: 0.85, side: THREE.DoubleSide });
  // baş tarafı anestezi örtüsü ve bacaklar (lateral pozisyonda alt bacak fleksiyonda)
  const head = new THREE.Mesh(new THREE.BoxGeometry(0.32, 0.36, 0.42), drapeM);
  head.position.copy(patient.localToWorld(V(-0.04, 0.47, 0.0))); head.position.y = minY + 0.18; orRoot.add(head);
  const screenD = new THREE.Mesh(new THREE.BoxGeometry(0.02, 0.55, 0.6), drapeM); // anestezi perdesi
  screenD.position.copy(patient.localToWorld(V(0, 0.33, 0))); screenD.position.y = minY + 0.3; orRoot.add(screenD);
  const legs = new THREE.Mesh(new THREE.BoxGeometry(0.85, 0.26, 0.5), drapeM);
  legs.position.copy(patient.localToWorld(V(-0.04, -0.74, 0.0))); legs.position.y = minY + 0.13; orRoot.add(legs);
  // Gövde örtüsü: cilt geometrisinin biraz dışı; ameliyat penceresinde açıklık
  const dg = anatomy.meshes.skin.geometry.clone();
  { const p = dg.attributes.position, n = dg.attributes.normal; for (let i = 0; i < p.count; i++) p.setXYZ(i, p.getX(i) + n.getX(i) * 0.004, p.getY(i) + n.getY(i) * 0.004, p.getZ(i) + n.getZ(i) * 0.004); }
  const tdM = drapeM.clone(); tdM.side = THREE.FrontSide;
  tdM.onBeforeCompile = (sh) => {
    sh.vertexShader = sh.vertexShader.replace('#include <common>', '#include <common>\nvarying vec3 vP;').replace('#include <begin_vertex>', '#include <begin_vertex>\nvP = position;');
    sh.fragmentShader = sh.fragmentShader.replace('#include <common>', '#include <common>\nvarying vec3 vP;').replace('#include <clipping_planes_fragment>', `#include <clipping_planes_fragment>
      if (vP.z > 0.02 && vP.x > -0.06 && vP.x < 0.15 && vP.y > -0.19 && vP.y < 0.15) discard;`);
  };
  const torsoDrape = new THREE.Mesh(dg, tdM); anatomy.root.add(torsoDrape);
  R.torsoDrape = torsoDrape;

  // Trokarlar
  const target = V(0.08, -0.01, 0.01);
  R.ports = {};
  const trocM = new THREE.MeshStandardMaterial({ color: 0x1b1b1b, roughness: 0.4, metalness: 0.2 });
  const capM = new THREE.MeshStandardMaterial({ color: 0x3a8fd0, roughness: 0.5 });
  for (const k in PORTS) {
    const P = PORTS[k];
    const { p, n } = anteriorSurface(P.x, P.y);
    const dir = target.clone().sub(p).normalize();
    if (k === 'assist') dir.copy(V(0.06, 0.06, 0).sub(p).normalize());
    const g = new THREE.Group();
    g.position.copy(p);
    g.quaternion.setFromUnitVectors(V(0, 0, 1), dir.clone().negate());
    const r = P.d / 2000 + 0.0012;
    const can = new THREE.Mesh(new THREE.CylinderGeometry(r, r, 0.11, 20, 1, true), trocM);
    can.rotation.x = Math.PI / 2; can.position.z = -0.025; g.add(can);
    const hub = new THREE.Mesh(new THREE.CylinderGeometry(r * 2.6, r * 2.2, 0.035, 24), capM);
    hub.rotation.x = Math.PI / 2; hub.position.z = 0.035; g.add(hub);
    const valve = new THREE.Mesh(new THREE.CylinderGeometry(0.003, 0.003, 0.02, 8), trocM);
    valve.position.set(0, r * 2.6 + 0.006, 0.035); g.add(valve);
    anatomy.root.add(g);
    R.ports[k] = { local: p.clone(), normal: n, dir, obj: g };
  }

  // Monitör (laparoskopik görüntü) — hastanın sırt tarafında, cerrahın karşısında
  const mon = new THREE.Group(); mon.name = 'monitor';
  const frame = new THREE.Mesh(new THREE.BoxGeometry(1.02, 0.6, 0.05), new THREE.MeshStandardMaterial({ color: 0x111111, roughness: 0.4 }));
  mon.add(frame);
  const arm = new THREE.Mesh(new THREE.CylinderGeometry(0.02, 0.02, 1.0, 12), tableM); arm.position.set(0, 0.75, -0.05); mon.add(arm);
  mon.position.set(0.05, 1.55, -1.3);
  orRoot.add(mon);
  R.monitor = mon;

  // Bilgi paneli
  R.panelAnchor = new THREE.Group();
  R.panelAnchor.position.set(-0.9, 1.45, -1.12); R.panelAnchor.rotation.y = 0.45;
  orRoot.add(R.panelAnchor);

  // Ameliyathane ışıkları (yalnız dış görüş: katman 6)
  const hemi = new THREE.HemisphereLight(0xffffff, 0x445566, 1.6); hemi.layers.set(6); scene.add(hemi);
  const dl = new THREE.DirectionalLight(0xffffff, 1.5); dl.position.set(1, 3, 2); dl.layers.set(6); scene.add(dl);
  return R;
}
